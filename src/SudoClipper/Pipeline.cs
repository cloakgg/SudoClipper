using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace SudoClipper;

public sealed class JobLog
{
    public string At { get; set; } = DateTimeOffset.Now.ToString("HH:mm:ss");
    public string Text { get; set; } = "";
    public bool Error { get; set; }
}

public sealed class Job
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..8];
    public string Url { get; set; } = "";
    public string Source { get; set; } = "";
    public string Title { get; set; } = "";
    public string State { get; set; } = "queued";
    public string Stage { get; set; } = "queued";
    public int Progress { get; set; }
    public string Error { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public List<JobLog> Log { get; set; } = new();
    public List<ClipRecord> Clips { get; set; } = new();
    public bool FollowAudience { get; set; } = true;
    public string Audience { get; set; } = "";
    public string CreatedAt { get; set; } = DateTimeOffset.Now.ToString("O");

    [JsonIgnore] public CancellationTokenSource Cts { get; } = new();
    [JsonIgnore] public string WorkDir { get; set; } = "";
}

public static class Jobs
{
    private static readonly ConcurrentDictionary<string, Job> Active = new();
    private static readonly List<Job> History = new();
    private static readonly object HistoryLock = new();

    // One run at a time: two jobs used to split the bandwidth and both crawled.
    private static readonly SemaphoreSlim RunGate = new(1, 1);

    public static Job Create(string url, string source, Settings settings)
    {
        var job = new Job { Url = url, Source = source, Provider = settings.Provider, Model = settings.Model };
        Active[job.Id] = job;
        return job;
    }

    public static Job? Get(string id) => Active.TryGetValue(id, out var job) ? job : null;

    public static List<Job> Recent()
    {
        lock (HistoryLock) return History.Take(20).ToList();
    }

    public static void Start(Job job, Settings settings, string? localFile)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (RunGate.CurrentCount == 0)
                {
                    job.Stage = "queued";
                    Pipeline.Log(job, "Queued — finishing the run in front of this one first.");
                }
                // Waiting without the token meant a canceled queued run still started
                // (and held the queue) once the run ahead finished.
                await RunGate.WaitAsync(job.Cts.Token);
                try
                {
                    await Pipeline.RunAsync(job, settings, localFile);
                }
                finally { RunGate.Release(); }
            }
            catch (Exception ex)
            {
                if (job.Cts.IsCancellationRequested)
                {
                    job.State = "canceled";
                    Pipeline.Log(job, "Run canceled.");
                }
                else
                {
                    job.State = "error";
                    job.Error = ex.Message;
                    Pipeline.Log(job, ex.Message, error: true);
                }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(job.WorkDir))
                    {
                        var work = job.WorkDir;
                        job.WorkDir = "";
                        Directory.Delete(work, true);
                    }
                }
                catch { }

                lock (HistoryLock)
                {
                    History.Insert(0, job);
                    if (History.Count > 30) History.RemoveRange(30, History.Count - 30);
                }
            }
        });
    }
}

public static class Pipeline
{
    private static readonly SemaphoreSlim RenderGate = new(2, 2);

    public static void Log(Job job, string text, bool error = false)
    {
        lock (job.Log)
        {
            job.Log.Add(new JobLog { Text = text, Error = error });
            if (job.Log.Count > 400) job.Log.RemoveRange(0, job.Log.Count - 400);
        }
        Console.WriteLine($"  [{job.Id}] {text}");
    }

    private static void Stage(Job job, string stage, int progress)
    {
        job.Stage = stage;
        job.Progress = Math.Clamp(progress, 0, 100);
    }

    public static async Task RunAsync(Job job, Settings settings, string? localFile)
    {
        var ct = job.Cts.Token;
        job.State = "running";
        job.WorkDir = Paths.EnsureDir(Path.Combine(Paths.WorkDir, job.Id));
        var outDir = settings.ResolveOutDir();

        Stage(job, "tools", 2);
        Log(job, "Checking the local toolchain…");
        var setupNotes = await Tooling.EnsureAsync(line => { Log(job, line); return Task.CompletedTask; }, ct);
        if (Tooling.Ffmpeg == null || Tooling.YtDlp == null)
        {
            throw new InvalidOperationException(
                "SUDOCLIPPER still needs ffmpeg and yt-dlp. " +
                (string.IsNullOrWhiteSpace(setupNotes) ? "Open Config → Diagnostics to retry the download." : setupNotes.Trim()));
        }
        Log(job, $"yt-dlp {Tooling.YtDlp}");

        string sourcePath;
        double duration = 0;
        string title = "";
        string videoId = "";

        if (localFile != null)
        {
            Stage(job, "download", 10);
            Log(job, "Using the file you dropped in.");
            sourcePath = Path.Combine(job.WorkDir, "source" + Path.GetExtension(localFile));
            File.Copy(localFile, sourcePath, true);
            title = Path.GetFileNameWithoutExtension(localFile);
            duration = await ProbeDurationAsync(sourcePath, ct);
        }
        else
        {
            PruneCache();
            Stage(job, "metadata", 5);
            Log(job, "Reading video info…");
            var (metaCode, meta) = await Proc.CaptureAsync(Tooling.YtDlp!,
                new[] { "--no-warnings", "--no-playlist", "--print", "%(title)s\u001f%(duration)s\u001f%(id)s", job.Url },
                job.WorkDir, ct);
            if (metaCode != 0)
                throw new InvalidOperationException("yt-dlp could not read that link. " + FirstLines(Proc.LastError, 3));

            var parts = meta.Split('\u001f');
            if (parts.Length > 0) title = parts[0].Trim();
            if (parts.Length > 1 && double.TryParse(parts[1], out var parsed)) duration = parsed;
            title = string.IsNullOrWhiteSpace(title) ? "Untitled video" : title;
            job.Title = title;
            videoId = parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]) ? Slug(parts[2], 48) : Slug(job.Url, 48);
            Log(job, $"Found: {title}" + (duration > 0 ? $" · {Subtitles.Format(duration)}" : ""));

            var cachedVideo = CachePath(videoId, "mp4");
            if (File.Exists(cachedVideo) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachedVideo) < TimeSpan.FromHours(72))
            {
                Stage(job, "download", 55);
                sourcePath = cachedVideo;
                Log(job, "Reusing the copy downloaded earlier — skipping the download.");
            }
            else
            {
                Stage(job, "download", 15);
                Log(job, "Downloading the source video (multi-connection)…");
                var height = settings.Quality == "720" ? 720 : 1080;

                // Same shape as supoclip's yt-dlp call: cap at 1080p, pull the file over
                // several connections and in 10 MB range requests. YouTube throttles a
                // single plain connection, which is what made this crawl before.
                var args = new List<string>
                {
                    "--no-warnings", "--no-playlist", "--newline", "--no-mtime",
                    "--no-check-certificate",
                    "--socket-timeout", "30",
                    "--retries", "5", "--fragment-retries", "10",
                    "--http-chunk-size", "10M",
                    "-N", "8",
                    "--user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
                    "--progress-template", "download:PROGRESS %(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s",
                    "-f", $"bv*[height<={height}][ext=mp4]+ba[ext=m4a]/b[height<={height}][ext=mp4]/bv*[height<={height}]+ba/b",
                    "--format-sort", "res,fps",
                    "--merge-output-format", "mp4",
                    "-o", "source.%(ext)s",
                    job.Url
                };

                // yt-dlp goes quiet while it negotiates formats, so nudge the bar forward while we wait.
                using var beats = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var heartbeat = Task.Run(async () =>
                {
                    while (!beats.IsCancellationRequested)
                    {
                        try { await Task.Delay(2000, beats.Token); } catch { return; }
                        if (job.Progress < 52) job.Progress += 1;
                    }
                });

                var lastReported = 0;
                var code = await Proc.RunAsync(Tooling.YtDlp!, args, job.WorkDir, line =>
                {
                    var text = line.Text;
                    if (text.StartsWith("PROGRESS ", StringComparison.Ordinal))
                    {
                        var bits = text[9..].Split('|');
                        var percentText = bits.Length > 0 ? bits[0].Trim().TrimEnd('%') : "";
                        if (double.TryParse(percentText, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                        {
                            job.Progress = Math.Max(job.Progress, 15 + (int)(pct * 0.38));
                            if ((int)pct >= lastReported + 10)
                            {
                                lastReported = (int)pct;
                                var speed = bits.Length > 1 ? bits[1].Trim() : "";
                                var eta = bits.Length > 2 ? bits[2].Trim() : "";
                                Log(job, $"Downloading… {lastReported}%"
                                    + (speed.Length > 0 && speed != "N/A" ? $" · {speed}" : "")
                                    + (eta.Length > 0 && eta != "N/A" ? $" · ETA {eta}" : ""));
                            }
                        }
                        return Task.CompletedTask;
                    }
                    if (text.Contains("Merging", StringComparison.OrdinalIgnoreCase)) Log(job, "Merging video and audio…");
                    return Task.CompletedTask;
                }, ct);

                beats.Cancel();
                try { await heartbeat; } catch { }

                if (ct.IsCancellationRequested) { job.State = "canceled"; return; }
                if (code != 0)
                {
                    if (File.Exists(cachedVideo))
                    {
                        sourcePath = cachedVideo;
                        Log(job, "That download failed, so the copy already on disk is being used.", error: true);
                    }
                    else throw new InvalidOperationException("Download failed. " + FirstLines(Proc.LastError, 4));
                }
                else
                {
                    sourcePath = Directory.EnumerateFiles(job.WorkDir, "source.*")
                        .Where(f => !f.EndsWith(".vtt") && !f.EndsWith(".srt") && !f.EndsWith(".ass"))
                        .OrderByDescending(f => new FileInfo(f).Length)
                        .FirstOrDefault() ?? throw new InvalidOperationException("The download finished but no video file was produced.");

                    try { File.Move(sourcePath, cachedVideo, true); sourcePath = cachedVideo; }
                    catch { }
                }
            }

            // Captions are a separate, best-effort pass. A rate-limited subtitle request must never
            // throw away a perfectly good video download.
            var captionCache = CachePath(videoId, "vtt");
            if (!File.Exists(captionCache))
            {
                Stage(job, "download", 58);
                Log(job, "Fetching the caption track…");
                var subArgs = new List<string>
                {
                    "--no-warnings", "--no-playlist", "--skip-download", "--no-mtime",
                    "--write-subs", "--write-auto-subs",
                    "--sub-langs", "en,en-US,en-GB,en-orig",
                    "--convert-subs", "vtt",
                    "-o", "source.%(ext)s",
                    job.Url
                };
                var subCode = await Proc.RunAsync(Tooling.YtDlp!, subArgs, job.WorkDir, _ => Task.CompletedTask, ct);
                var found = Directory.EnumerateFiles(job.WorkDir)
                    .Where(f => f.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .FirstOrDefault();
                if (found != null) { try { File.Copy(found, captionCache, true); } catch { } }
                else if (subCode != 0) Log(job, "This video has no caption track.", error: true);
            }
            else Log(job, "Using the caption track saved earlier.");
        }

        if (duration <= 0) duration = await ProbeDurationAsync(sourcePath, ct);

        Stage(job, "transcript", 62);
        var cues = new List<Cue>();

        if (!string.IsNullOrEmpty(videoId))
        {
            foreach (var cached in new[] { CachePath(videoId, "vtt"), CachePath(videoId, "srt") })
            {
                if (!File.Exists(cached)) continue;
                var parsed = Subtitles.ParseFile(cached);
                if (parsed.Count > cues.Count) cues = parsed;
            }
        }

        if (cues.Count == 0)
        {
            var captionFiles = Directory.EnumerateFiles(job.WorkDir)
                .Where(f => f.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var file in captionFiles.OrderByDescending(f => new FileInfo(f).Length))
            {
                var parsed = Subtitles.ParseFile(file);
                if (parsed.Count > cues.Count) cues = parsed;
            }
        }

        if (cues.Count == 0)
        {
            Log(job, "No captions on this video — transcribing the audio locally instead.", error: true);
            Stage(job, "transcribe", 63);
            var (transcribed, srtPath) = await TranscribeAsync(job, sourcePath, settings.WhisperModel, ct);
            if (transcribed.Count > 0)
            {
                cues = transcribed;
                if (!string.IsNullOrEmpty(videoId) && srtPath != null)
                {
                    try { File.Copy(srtPath, CachePath(videoId, "srt"), true); } catch { }
                }
            }
        }

        if (cues.Count > 0)
        {
            Log(job, $"Transcript ready: {cues.Count} lines.");
            if (duration <= 0) duration = cues[^1].End;
        }
        else
        {
            throw new InvalidOperationException(
                "SUDOCLIPPER could not get a transcript for this video, so it cannot pick moments. " +
                "Local transcription needs the whisper download to succeed — retry, or use a video with captions.");
        }

        if (duration <= 0)
            throw new InvalidOperationException("Could not determine the video length.");

        // Audience signals: no view counts needed. The live-chat replay shows where the room lost
        // it and YouTube's heatmap shows the replayed passages. Both are scored, both are logged.
        var engagement = new Engagement();
        if (job.FollowAudience && localFile == null && !string.IsNullOrEmpty(videoId))
        {
            Stage(job, "analyze", 65);
            Log(job, "Checking where the chat went wild (chat replay + most replayed)…");
            try
            {
                engagement = await Engagement.FetchAsync(job.Url, job.WorkDir, line => Log(job, line), ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Log(job, "Audience signals skipped: " + ex.Message, error: true); }

            foreach (var line in engagement.LogLines()) Log(job, line);
            var spikes = engagement.Chat.Count;
            var replays = engagement.Replay.Count;
            job.Audience = engagement.Any
                ? $"{spikes} chat spike{(spikes == 1 ? "" : "s")} · {replays} replay hotspot{(replays == 1 ? "" : "s")}"
                : "no audience data";
            if (engagement.ChatMessages > 0 && !engagement.Any)
                Log(job, "Chat stayed flat — falling back to the transcript.");
        }

        Stage(job, "analyze", 68);
        var provider = Providers.Find(settings.Provider);
        var modelLabel = provider.Kind == "local" ? "local selector" :
            (string.IsNullOrWhiteSpace(settings.Model) ? provider.DefaultModel : settings.Model);
        job.Provider = provider.Id;
        job.Model = modelLabel;
        Log(job, $"Finding the {settings.ClampedClipCount} most clippable moments via {provider.Name} · {modelLabel}…");

        var ideas = await Llm.PickAsync(cues, settings, duration, ct, engagement);
        if (ct.IsCancellationRequested) { job.State = "canceled"; return; }
        if (ideas.Count == 0)
            throw new InvalidOperationException("No usable moments came back. Try a longer video, another model, or the free local selector.");

        var docTitle = string.IsNullOrWhiteSpace(title) ? "sudoclipper" : title;
        var folder = Paths.EnsureDir(Path.Combine(outDir, Slug(docTitle)));

        // Local uploads live in a temp folder that gets cleaned; keep a copy for the editor.
        var editableSource = sourcePath;
        if (localFile != null)
        {
            try
            {
                var copy = CachePath("upload-" + job.Id, Path.GetExtension(sourcePath).TrimStart('.'));
                if (!File.Exists(copy)) File.Copy(sourcePath, copy);
                editableSource = copy;
            }
            catch { }
        }
        Stage(job, "render", 72);

        var index = 0;
        foreach (var idea in ideas)
        {
            ct.ThrowIfCancellationRequested();
            index++;
            var label = $"Clip {index}/{ideas.Count} · {idea.Title}";
            Log(job, $"Rendering {label}");

            var stem = $"{index:00}-{Slug(idea.Title, 40)}";
            var clipPath = Path.Combine(folder, stem + ".mp4");
            var thumbPath = Path.Combine(folder, stem + ".jpg");

            string? assName = null;
            if (settings.BurnCaptions && cues.Count > 0)
            {
                assName = $"captions-{index:00}.ass";
                var ass = Subtitles.BuildAss(cues, idea.Start, idea.End, settings.CaptionStyle,
                    settings.CaptionScale, settings.CaptionPosition, settings.CaptionUppercase,
                    settings.CaptionFont, settings.CaptionOutline, settings.CaptionWordsPerLine, settings.CaptionWordPop);
                await File.WriteAllTextAsync(Path.Combine(job.WorkDir, assName), ass, ct);
            }

            var clipDuration = Math.Max(1, idea.End - idea.Start);
            var baseProgress = 72 + (int)((index - 1) / (double)ideas.Count * 26);
            var ok = await RenderClipAsync(job, sourcePath, clipPath, idea.Start, clipDuration, assName, baseProgress, ct);
            if (!ok)
            {
                Log(job, $"Skipping {idea.Title} — the encoder failed.", error: true);
                continue;
            }

            await MakeThumbAsync(clipPath, thumbPath, ct);

            var record = new ClipRecord
            {
                Title = idea.Title,
                Hook = idea.Hook,
                Reason = idea.Reason,
                Score = idea.Score,
                Start = idea.Start,
                End = idea.End,
                Duration = Math.Round(clipDuration, 1),
                File = clipPath,
                Thumb = File.Exists(thumbPath) ? thumbPath : "",
                SourceTitle = docTitle,
                SourceUrl = job.Url,
                Provider = provider.Id,
                Model = modelLabel,
                Captioned = assName != null,
                CaptionStyle = settings.CaptionStyle,
                SourcePath = editableSource,
                SourceDuration = duration
            };

            // Keep the transcript and the source around so the clip can be re-cut in the editor.
            try
            {
                var cuesPath = Path.Combine(CacheDir, "cues-" + record.Id + ".srt");
                File.WriteAllText(cuesPath, Subtitles.ToSrt(cues));
                record.CuesPath = cuesPath;
            }
            catch { }

            Store.AddMemory(record);
            lock (job.Clips) job.Clips.Add(record);
            Log(job, $"Saved {Path.GetFileName(clipPath)}");
        }

        if (job.Clips.Count == 0)
            throw new InvalidOperationException("Every render failed. Check the log above for the ffmpeg error.");

        Stage(job, "done", 100);
        job.State = "done";
        Log(job, $"Done. {job.Clips.Count} clip{(job.Clips.Count == 1 ? "" : "s")} in {folder}");

        try { Directory.Delete(job.WorkDir, true); } catch { }
    }

    private static async Task<bool> RenderClipAsync(
        Job job, string source, string output, double start, double duration, string? assName, int progressStart, CancellationToken ct)
    {
        await RenderGate.WaitAsync(ct);
        try
        {
            return await RenderCoreAsync(job.WorkDir, source, output, start, duration, assName, percent =>
            {
                var pct = Math.Clamp((int)percent, 0, 99);
                job.Progress = Math.Max(job.Progress, progressStart + (int)(pct * 0.24 / Math.Max(1, job.Clips.Count + 1)));
            }, ct);
        }
        finally { RenderGate.Release(); }
    }

    /// <summary>Cuts a 9:16 clip — blurred background, centered source, optional burned captions.</summary>
    private static async Task<bool> RenderCoreAsync(
        string workDir, string source, string output, double start, double duration, string? assFile,
        Action<double> onPercent, CancellationToken ct)
    {
        const int W = 1080, H = 1920;
        var graph = new StringBuilder();
        graph.Append("[0:v]split=2[bg][fg];");
        graph.Append($"[bg]scale={W}:{H}:force_original_aspect_ratio=increase,crop={W}:{H},gblur=sigma=34,eq=brightness=-0.12:saturation=1.2[bgv];");
        graph.Append($"[fg]scale={W}:{H}:force_original_aspect_ratio=decrease:flags=lanczos[fgv];");
        graph.Append("[bgv][fgv]overlay=(W-w)/2:(H-h)/2[v0]");
        if (assFile != null) graph.Append($";[v0]subtitles={assFile}[v]");
        else graph.Append(";[v0]null[v]");

        var args = new List<string>
        {
            "-hide_banner", "-y", "-nostats", "-progress", "pipe:1",
            "-ss", start.ToString("0.###", CultureInfo.InvariantCulture),
            "-t", duration.ToString("0.###", CultureInfo.InvariantCulture),
            "-i", source,
            "-filter_complex", graph.ToString(),
            "-map", "[v]", "-map", "0:a?",
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "20",
            "-pix_fmt", "yuv420p", "-profile:v", "high", "-level", "4.1",
            "-c:a", "aac", "-b:a", "160k", "-ar", "48000", "-ac", "2",
            "-movflags", "+faststart",
            output
        };

        var code = await Proc.RunAsync(Tooling.Ffmpeg!, args, workDir, line =>
        {
            if (line.Text.StartsWith("out_time_us=", StringComparison.Ordinal) &&
                long.TryParse(line.Text[12..], out var micros) && micros > 0)
                onPercent(micros / 1_000_000.0 / Math.Max(0.1, duration) * 100);
            return Task.CompletedTask;
        }, ct);

        if (code != 0) Console.Error.WriteLine("ffmpeg: " + FirstLines(Proc.LastError, 6));
        return code == 0 && File.Exists(output);
    }

    /// <summary>Re-cuts an existing clip from the cached source with new captions or new trim points.</summary>
    public static async Task<ClipRecord> RerenderAsync(ClipRecord clip, EditOptions options, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clip.SourcePath) || !File.Exists(clip.SourcePath))
            throw new InvalidOperationException(
                "The source video for this clip is no longer cached, so it cannot be re-cut. Run the clip again and edit that one.");

        if (Tooling.Ffmpeg == null) await Tooling.EnsureAsync(_ => Task.CompletedTask, ct);
        if (Tooling.Ffmpeg == null) throw new InvalidOperationException("ffmpeg is not ready yet — open Config → Diagnostics and retry the download.");

        var cues = string.IsNullOrWhiteSpace(clip.CuesPath) || !File.Exists(clip.CuesPath)
            ? new List<Cue>()
            : Subtitles.ParseFile(clip.CuesPath);

        var start = Math.Max(0, options.Start);
        var end = Math.Max(start + 1, options.End);
        if (clip.SourceDuration > 0) end = Math.Min(end, clip.SourceDuration);
        var duration = Math.Max(1, end - start);

        var dir = Paths.EnsureDir(Path.Combine(Paths.DataDir, "edits"));
        string? assFile = null;
        if (options.Captions && cues.Count > 0)
        {
            var ass = Subtitles.BuildAss(cues, start, end, options.CaptionStyle, options.FontScale, options.PositionDelta,
                options.Uppercase, options.Font, options.OutlineScale, options.WordsPerLine, options.WordPop);
            var assPath = Path.Combine(dir, "edit-" + clip.Id + ".ass");
            await File.WriteAllTextAsync(assPath, ass, ct);
            assFile = Path.GetFileName(assPath);
        }

        var target = clip.File;
        var folder = Path.GetDirectoryName(target);
        if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException("The clip has no folder.");
        var temp = Path.Combine(folder, Path.GetFileNameWithoutExtension(target) + ".edit.mp4");

        var ok = await RenderCoreAsync(dir, clip.SourcePath, temp, start, duration, assFile, _ => { }, ct);
        if (!ok || !File.Exists(temp))
            throw new InvalidOperationException("The re-render failed. The log has the ffmpeg message.");

        try { File.Move(temp, target, true); }
        catch (Exception ex) { throw new InvalidOperationException("Could not replace the clip: " + ex.Message); }

        if (!string.IsNullOrWhiteSpace(clip.Thumb))
        {
            try
            {
                await Proc.CaptureAsync(Tooling.Ffmpeg, new[]
                {
                    "-hide_banner", "-y", "-loglevel", "error",
                    "-ss", "1.5", "-i", target, "-frames:v", "1", "-vf", "scale=540:-2", clip.Thumb
                }, Path.GetDirectoryName(clip.Thumb)!, ct);
            }
            catch { }
        }

        clip.Start = Math.Round(start, 2);
        clip.End = Math.Round(end, 2);
        clip.Duration = Math.Round(duration, 1);
        clip.CaptionStyle = options.CaptionStyle;
        clip.Captioned = assFile != null;
        Store.AddMemory(clip);
        return clip;
    }

    private static async Task MakeThumbAsync(string clipPath, string thumbPath, CancellationToken ct)
    {
        if (Tooling.Ffmpeg == null) return;
        var args = new[]
        {
            "-hide_banner", "-y", "-loglevel", "error",
            "-ss", "1.5", "-i", clipPath, "-frames:v", "1",
            "-vf", "scale=540:-2", thumbPath
        };
        try { await Proc.CaptureAsync(Tooling.Ffmpeg, args, Path.GetDirectoryName(thumbPath), ct); } catch { }
    }

    public sealed class EditOptions
    {
        public double Start { get; set; }
        public double End { get; set; }
        public string CaptionStyle { get; set; } = "punch";
        public bool Captions { get; set; } = true;
        public double FontScale { get; set; } = 1;
        public int PositionDelta { get; set; }
        public bool? Uppercase { get; set; }
        public string Font { get; set; } = "";
        public double OutlineScale { get; set; } = 1;
        public int? WordsPerLine { get; set; }
        public bool? WordPop { get; set; }
    }

    private static async Task<double> ProbeDurationAsync(string file, CancellationToken ct)
    {
        var tool = Tooling.Ffprobe ?? Tooling.Ffmpeg;
        if (tool == null) return 0;
        try
        {
            var args = tool.EndsWith("ffprobe.exe") || tool.EndsWith("ffprobe")
                ? new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", file }
                : new[] { "-hide_banner", "-i", file };
            var (code, output) = await Proc.CaptureAsync(tool, args, null, ct);
            if (code != 0) return 0;
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (double.TryParse(trimmed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                    return seconds;
                var m = System.Text.RegularExpressions.Regex.Match(trimmed, @"Duration:\s*(\d+):(\d+):(\d+\.?\d*)");
                if (m.Success)
                    return int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        catch { }
        return 0;
    }

    private static string FirstLines(string text, int count) =>
        string.Join(" ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(count)).Trim();

    /* ───────────────────────── source cache ───────────────────────── */

    private static string CacheDir => Paths.EnsureDir(Path.Combine(Paths.DataDir, "cache"));

    private static string CachePath(string id, string extension) => Path.Combine(CacheDir, id + "." + extension);

    /// <summary>Retrying the same link should not download it again.</summary>
    private static void PruneCache()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(CacheDir))
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromDays(7))
                    File.Delete(file);
        }
        catch { }
    }

    /* ───────────────────────── local transcription ───────────────────────── */

    private static async Task<(List<Cue> cues, string? srtPath)> TranscribeAsync(
        Job job, string sourcePath, string whisperSize, CancellationToken ct)
    {
        if (Tooling.Ffmpeg == null) return (new(), null);

        var ready = await Tooling.EnsureWhisperAsync(whisperSize, line => { Log(job, line); return Task.CompletedTask; }, ct);
        if (!ready || Tooling.WhisperCli == null) return (new(), null);

        var wav = Path.Combine(job.WorkDir, "audio.wav");
        Log(job, "Extracting the audio track…");
        var (audioCode, _) = await Proc.CaptureAsync(Tooling.Ffmpeg, new[]
        {
            "-hide_banner", "-loglevel", "error", "-y", "-i", sourcePath,
            "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", wav
        }, job.WorkDir, ct);

        if (audioCode != 0 || !File.Exists(wav))
        {
            Log(job, "Could not extract audio for transcription.", error: true);
            return (new(), null);
        }

        var prefix = Path.Combine(job.WorkDir, "whisper");
        var srtPath = prefix + ".srt";
        Log(job, $"Transcribing locally with Whisper ({whisperSize}.en)…");

        var baseProgress = Math.Max(job.Progress, 63);
        var withProgress = await RunWhisperAsync(job, wav, prefix, whisperSize, baseProgress, printProgress: true, ct);

        // Older builds do not know -pp; retry quietly rather than failing the whole run.
        if (!File.Exists(srtPath) && withProgress != 0)
            await RunWhisperAsync(job, wav, prefix, whisperSize, baseProgress, printProgress: false, ct);

        if (!File.Exists(srtPath))
        {
            Log(job, "Transcription failed: " + FirstLines(Proc.LastError, 3), error: true);
            return (new(), null);
        }

        var cues = Subtitles.ParseFile(srtPath);
        if (cues.Count > 0) Log(job, $"Whisper produced {cues.Count} caption lines.");
        return (cues, srtPath);
    }

    private static async Task<int> RunWhisperAsync(
        Job job, string wav, string prefix, string whisperSize, int baseProgress, bool printProgress, CancellationToken ct)
    {
        var args = new List<string>
        {
            "-m", Tooling.WhisperModelPath(whisperSize),
            "-f", wav,
            "-l", "en",
            "-t", Math.Clamp(Environment.ProcessorCount / 2, 2, 8).ToString(),
            "-osrt",
            "-of", prefix
        };
        if (printProgress) args.Add("-pp");

        return await Proc.RunAsync(Tooling.WhisperCli!, args, job.WorkDir, line =>
        {
            var match = System.Text.RegularExpressions.Regex.Match(line.Text, @"(\d{1,3})%");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var pct))
            {
                var target = baseProgress + (int)(pct * 0.04);
                if (target > job.Progress) job.Progress = target;
                if (pct is 25 or 50 or 75) Log(job, $"Transcribing… {pct}%");
            }
            return Task.CompletedTask;
        }, ct);
    }

    public static string Slug(string value, int max = 60)
    {
        var lowered = value.ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (var ch in lowered)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch is ' ' or '-' or '_' or '.' or ',' or '\'') sb.Append('-');
            if (sb.Length >= max) break;
        }
        var slug = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "-{2,}", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "clip" : slug;
    }
}
