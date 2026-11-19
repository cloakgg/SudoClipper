using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace SudoClipper;

public sealed class ProcLine
{
    public string Text { get; init; } = "";
    public bool IsError { get; init; }
}

public static class Proc
{
    public static async Task<int> RunAsync(
        string exe,
        IEnumerable<string> args,
        string? workingDir,
        Func<ProcLine, Task>? onLine,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var gate = new SemaphoreSlim(1, 1);

        async Task Emit(string line, bool err)
        {
            if (string.IsNullOrWhiteSpace(line) || onLine == null) return;
            await gate.WaitAsync(CancellationToken.None);
            try { await onLine(new ProcLine { Text = line.TrimEnd(), IsError = err }); } catch { }
            finally { gate.Release(); }
        }

        var stderr = new StringBuilder();

        proc.OutputDataReceived += (_, e) => { if (e.Data != null) _ = Emit(e.Data, false); };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (stderr) { if (stderr.Length < 8000) stderr.AppendLine(e.Data); }
            _ = Emit(e.Data, true);
        };

        if (!proc.Start()) throw new InvalidOperationException($"Could not start {Path.GetFileName(exe)}");
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
        });

        try { await proc.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { return -1; }

        // Flushes the asynchronous output readers before we read the error buffer.
        try { proc.WaitForExit(); } catch { }

        lock (stderr) { LastError = stderr.ToString(); }
        return proc.ExitCode;
    }

    private static volatile string _lastError = "";
    public static string LastError { get => _lastError; private set => _lastError = value; }

    public static async Task<(int code, string output)> CaptureAsync(string exe, IEnumerable<string> args, string? workingDir, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var code = await RunAsync(exe, args, workingDir, line =>
        {
            lock (sb) { if (sb.Length < 400_000) sb.AppendLine(line.Text); }
            return Task.CompletedTask;
        }, ct);
        return (code, sb.ToString());
    }
}

public sealed record ToolState(string Name, string Id, bool Found, string Path, string Version, string Hint);

public static class Tooling
{
    private static string? _ytdlp;
    private static string? _ffmpeg;
    private static string? _ffprobe;
    private static string? _ytdlpVersion;
    private static string? _ffmpegVersion;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly SemaphoreSlim EnsureGate = new(1, 1);
    public static bool Bootstrapping { get; private set; }
    public static string BootstrapLog { get; private set; } = "";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    private const string FfmpegUrl =
        "https://github.com/yt-dlp/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip";
    private const string YtDlpUrl =
        "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string WhisperZipUrl =
        "https://github.com/ggml-org/whisper.cpp/releases/download/b5454/whisper-bin-x64.zip";
    private static string WhisperModelUrlFor(string size) =>
        $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{(size == "base" ? "base" : "tiny")}.en-q5_1.bin";

    public static string? YtDlp => _ytdlp;
    public static string? Ffmpeg => _ffmpeg;
    public static string? Ffprobe => _ffprobe;

    public static string WhisperDir => Paths.EnsureDir(Path.Combine(Paths.BinDir, "whisper"));

    /// <summary>Quantised models: "tiny" is ~32 MB, "base" ~60 MB. Tiny is the default because it is quick.</summary>
    public static string WhisperModelPath(string size)
    {
        var name = size == "base" ? "base" : "tiny";
        return Path.Combine(WhisperDir, $"ggml-{name}.en-q5_1.bin");
    }

    private static string? _whisperCli;

    public static string? WhisperCli => _whisperCli ??= FindWhisperCli();

    private static string? FindWhisperCli()
    {
        try
        {
            if (!Directory.Exists(WhisperDir)) return null;
            foreach (var name in new[] { "whisper-cli.exe", "main.exe", "whisper-cli", "main" })
            {
                var direct = Path.Combine(WhisperDir, name);
                if (File.Exists(direct)) return direct;
                var nested = Directory.EnumerateFiles(WhisperDir, name, SearchOption.AllDirectories).FirstOrDefault();
                if (nested != null) return nested;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Fetches whisper.cpp plus a small English model the first time a video needs transcribing.</summary>
    public static async Task<bool> EnsureWhisperAsync(string size, Func<string, Task> log, CancellationToken ct)
    {
        if (!IsWindows)
        {
            await log("Local transcription needs whisper.cpp; install whisper-cli and put it on PATH.");
            return false;
        }

        await EnsureWhisperGate.WaitAsync(ct);
        try
        {
            var model = WhisperModelPath(size);
            if (WhisperCli != null && ModelReady(model, size)) return true;
            if (File.Exists(model) && !ModelReady(model, size))
            {
                await log("The speech model file was incomplete, downloading it again…");
                TryDeleteFile(model);
            }

            if (WhisperCli == null)
            {
                await log("Downloading the local transcriber (whisper.cpp, ~9 MB)…");
                var zip = Path.Combine(WhisperDir, "whisper.zip");
                await DownloadAsync(WhisperZipUrl, zip, _ => { }, ct);
                ExtractAll(zip, WhisperDir);
                TryDeleteFile(zip);
                _whisperCli = FindWhisperCli();
                if (_whisperCli == null) await log("The transcriber archive did not contain a whisper executable.");
            }

            if (!File.Exists(model))
            {
                var megabytes = size == "base" ? 60 : 32;
                await log($"Downloading the speech model ({size}.en, ~{megabytes} MB, one time)…");
                var lastReported = -1;
                await DownloadAsync(WhisperModelUrlFor(size), model, percent =>
                {
                    if (percent / 10 != lastReported / 10)
                    {
                        lastReported = percent;
                        _ = log($"Speech model {percent}%");
                    }
                }, ct);
            }

            if (WhisperCli != null && ModelReady(model, size))
            {
                await log("Local transcriber ready.");
                return true;
            }
            return false;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            await log("Could not set up local transcription: " + ex.Message);
            return false;
        }
        finally { EnsureWhisperGate.Release(); }
    }

    private static readonly SemaphoreSlim EnsureWhisperGate = new(1, 1);

    private static bool ModelReady(string path, string size)
    {
        try
        {
            var minimum = size == "base" ? 50L * 1024 * 1024 : 25L * 1024 * 1024;
            return File.Exists(path) && new FileInfo(path).Length >= minimum;
        }
        catch { return false; }
    }

    private static void ExtractAll(string zip, string destination)
    {
        var root = Path.GetFullPath(destination);
        using var archive = ZipFile.OpenRead(zip);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('\\', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }

    private static bool IsWindows => OperatingSystem.IsWindows();

    private static string? FindOnPath(params string[] names)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var dirs = pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in dirs.Append(Paths.BinDir))
        {
            foreach (var name in names)
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), name);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
        }
        return null;
    }

    private static string? Locate(string tool)
    {
        if (tool == "yt-dlp")
            return _ytdlp ??= FindOnPath(IsWindows ? new[] { "yt-dlp.exe" } : new[] { "yt-dlp", "yt-dlp.exe" });
        return null;
    }

    private static async Task<string?> VersionOf(string exe, string flag, CancellationToken ct)
    {
        try
        {
            var (code, output) = await Proc.CaptureAsync(exe, new[] { flag }, null, ct);
            if (code != 0) return null;
            var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return string.IsNullOrWhiteSpace(line) ? "unknown" : line;
        }
        catch { return null; }
    }

    /// <summary>Locates tools already installed, and looks for the copies SUDOCLIPPER manages itself.</summary>
    public static async Task RefreshAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var ytdlp = Locate("yt-dlp") ?? (File.Exists(Path.Combine(Paths.BinDir, "yt-dlp.exe")) ? Path.Combine(Paths.BinDir, "yt-dlp.exe") : null);
            if (ytdlp != null)
            {
                _ytdlpVersion ??= await VersionOf(ytdlp, "--version", ct);
                if (_ytdlpVersion == null) ytdlp = null;
            }
            _ytdlp = ytdlp;

            var ffmpeg = FindOnPath(IsWindows ? new[] { "ffmpeg.exe" } : new[] { "ffmpeg", "ffmpeg.exe" })
                         ?? (File.Exists(Path.Combine(Paths.BinDir, "ffmpeg.exe")) ? Path.Combine(Paths.BinDir, "ffmpeg.exe") : null);
            if (ffmpeg != null)
            {
                _ffmpegVersion ??= await VersionOf(ffmpeg, "-version", ct);
                if (_ffmpegVersion == null) ffmpeg = null;
            }
            _ffmpeg = ffmpeg;
            _ffprobe = ffmpeg == null
                ? null
                : Path.Combine(Path.GetDirectoryName(ffmpeg)!, IsWindows ? "ffprobe.exe" : "ffprobe");
            if (_ffprobe != null && !File.Exists(_ffprobe)) _ffprobe = null;
        }
        finally { Gate.Release(); }
    }

    public static async Task<List<ToolState>> StatusAsync(CancellationToken ct)
    {
        await RefreshAsync(ct);
        return new List<ToolState>
        {
            new("yt-dlp", "yt-dlp", _ytdlp != null, _ytdlp ?? "", _ytdlpVersion ?? "", "Used to download the source video and its captions."),
            new("ffmpeg", "ffmpeg", _ffmpeg != null, _ffmpeg ?? "", _ffmpegVersion ?? "", "Used to cut, crop to 9:16 and burn captions."),
        };
    }

    public static async Task<string> EnsureAsync(Func<string, Task> log, CancellationToken ct)
    {
        await EnsureGate.WaitAsync(ct);
        try { return await EnsureCoreAsync(log, ct); }
        finally { EnsureGate.Release(); }
    }

    private static async Task<string> EnsureCoreAsync(Func<string, Task> log, CancellationToken ct)
    {
        var notes = new StringBuilder();
        async Task Say(string s) { notes.AppendLine(s); BootstrapLog = notes.ToString(); await log(s); }

        await RefreshAsync(ct);
        if (_ytdlp != null && _ffmpeg != null) return "";

        // Another window may already be downloading; wait for it instead of fighting over files.
        using var setupLock = await AcquireSetupLockAsync(ct);
        await RefreshAsync(ct);
        if (_ytdlp != null && _ffmpeg != null)
        {
            await Say("Tools are ready.");
            return BootstrapLog;
        }

        Bootstrapping = true;
        try
        {
            if (!IsWindows)
            {
                var missing = (_ytdlp == null ? "yt-dlp " : "") + (_ffmpeg == null ? "ffmpeg" : "");
                await Say($"Missing {missing}. Install them with your package manager (e.g. apt install ffmpeg yt-dlp) and restart SUDOCLIPPER.");
                return BootstrapLog;
            }

            if (_ytdlp == null)
            {
                await Say("Downloading yt-dlp (the downloader)…");
                var dest = Path.Combine(Paths.BinDir, "yt-dlp.exe");
                await DownloadAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", dest, p =>
                {
                    if (p % 20 == 0) BootstrapLog = notes + $"  yt-dlp {p}%";
                }, ct);
                await Say("yt-dlp ready.");
            }

            if (_ffmpeg == null)
            {
                var zip = Path.Combine(Paths.BinDir, "ffmpeg.zip");
                var installed = false;

                for (var attempt = 1; attempt <= 2 && !installed; attempt++)
                {
                    if (!File.Exists(zip))
                    {
                        await Say(attempt == 1
                            ? "Downloading ffmpeg (the video engine, ~90 MB, one time)…"
                            : "Retrying the ffmpeg download…");
                        await DownloadAsync(FfmpegUrl, zip, p =>
                        {
                            if (p % 10 == 0) BootstrapLog = notes + $"  ffmpeg {p}%";
                        }, ct);
                    }

                    try
                    {
                        await Say("Unpacking ffmpeg…");
                        ExtractFfmpeg(zip);
                        installed = true;
                    }
                    catch (Exception ex)
                    {
                        await Say("That download was incomplete (" + ex.Message + "). Starting over…");
                        TryDeleteFile(zip);
                        TryDeleteFile(zip + ".part");
                    }
                }

                if (!installed) throw new IOException("ffmpeg could not be unpacked after two attempts.");
                TryDeleteFile(zip);
                await Say("ffmpeg ready.");
            }

            _ytdlpVersion = null;
            _ffmpegVersion = null;
            await RefreshAsync(ct);

            if (_ytdlp == null || _ffmpeg == null)
                await Say("Automatic setup did not finish. Install ffmpeg and yt-dlp and put them on your PATH.");
            else
                await Say("All tools ready.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            await Say("Setup failed: " + ex.Message + " Retry from Config → Diagnostics.");
        }
        finally
        {
            Bootstrapping = false;
            BootstrapLog = notes.ToString();
        }
        return BootstrapLog;
    }

    /// <summary>
    /// Downloads to <c>dest.part</c> and resumes where a previous attempt stopped, then moves the
    /// finished file into place. Safe to call again after a crash, a timeout or a locked file.
    /// </summary>
    private static async Task DownloadAsync(string url, string dest, Action<int> onPercent, CancellationToken ct)
    {
        var part = dest + ".part";
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var existing = File.Exists(part) ? new FileInfo(part).Length : 0;
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (existing > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);

                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable && existing > 0)
                {
                    File.Move(part, dest, true);
                    return;
                }
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    existing = 0; // server ignored the range, start over
                else if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
                    response.EnsureSuccessStatusCode();

                var length = response.Content.Headers.ContentLength ?? -1;
                var total = length >= 0 ? length + existing : -1;

                await using (var src = await response.Content.ReadAsStreamAsync(ct))
                await using (var file = new FileStream(part, existing > 0 ? FileMode.Append : FileMode.Create,
                                 FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    var buffer = new byte[81920];
                    long read = existing;
                    int last = -1, n;
                    while ((n = await src.ReadAsync(buffer, ct)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, n), ct);
                        read += n;
                        if (total > 0)
                        {
                            var pct = (int)Math.Min(100, read * 100 / total);
                            if (pct != last) { last = pct; onPercent(pct); }
                        }
                    }
                }

                var finalLength = new FileInfo(part).Length;
                if (total > 0 && finalLength < total)
                    throw new IOException($"The connection dropped at {finalLength} of {total} bytes.");

                File.Move(part, dest, true);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastError = ex;
                await Task.Delay(700 * attempt, ct);
            }
        }

        throw new IOException($"Could not download {Path.GetFileName(dest)}: {lastError?.Message}", lastError);
    }

    /// <summary>
    /// One setup at a time across every running SUDOCLIPPER process, so two windows can never
    /// fight over the same archive.
    /// </summary>
    private static async Task<IDisposable?> AcquireSetupLockAsync(CancellationToken ct)
    {
        var path = Path.Combine(Paths.BinDir, ".setup.lock");
        for (var attempt = 0; attempt < 90; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
                                      FileOptions.DeleteOnClose);
            }
            catch (IOException)
            {
                await Task.Delay(1000, ct); // another process is setting up; wait for it
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private static void ExtractFfmpeg(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        var extracted = 0;
        foreach (var entry in archive.Entries)
        {
            var name = Path.GetFileName(entry.FullName);
            if (name is "ffmpeg.exe" or "ffprobe.exe")
            {
                entry.ExtractToFile(Path.Combine(Paths.BinDir, name), true);
                extracted++;
            }
        }
        if (extracted == 0) throw new InvalidDataException("The downloaded archive did not contain ffmpeg.");
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
