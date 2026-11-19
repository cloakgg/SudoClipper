using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SudoClipper;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var argv = new List<string>(args);
if (argv.Any(a => a is "--help" or "-h" or "help"))
{
    Console.WriteLine("""
        SUDOCLIPPER — local clipping studio

        USAGE
          sudoclipper [options]

        OPTIONS
          --port <n>     Port for the local UI (default 4319, next free port if taken)
          --no-open      Do not open your browser
          --doctor       Print the state of ffmpeg / yt-dlp and exit
          --mcp          Run as an MCP server on stdio (for Claude Desktop, Cursor…)
          --version      Print the version and exit
          --help         This screen
        """);
    return;
}
if (argv.Any(a => a is "--version" or "-v"))
{
    Console.WriteLine("SUDOCLIPPER 1.0.0");
    return;
}
if (argv.Contains("--mcp"))
{
    await Mcp.RunAsync();
    return;
}
if (argv.Contains("--doctor"))
{
    Console.WriteLine("SUDOCLIPPER doctor");
    Console.WriteLine("  data    " + Paths.DataDir);
    Console.WriteLine("  clips   " + Store.Settings.ResolveOutDir());
    foreach (var tool in await Tooling.StatusAsync(CancellationToken.None))
        Console.WriteLine($"  {tool.Name,-8}{(tool.Found ? "ok  " : "MISS")} {(tool.Found ? tool.Version : tool.Hint)}");
    return;
}

var noOpen = argv.Contains("--no-open");
var port = 4319;
var portFlag = argv.IndexOf("--port");
if (portFlag >= 0 && portFlag + 1 < argv.Count && int.TryParse(argv[portFlag + 1], out var requestedPort) && requestedPort > 0 && requestedPort < 65536)
    port = requestedPort;
port = FindFreePort(port);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = Array.Empty<string>(),
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Logging.SetMinimumLevel(LogLevel.Warning);
// Loopback by default; set SUDOCLIPPER_HOST=0.0.0.0 to let the Android app (android/) reach the studio.
var bindHost = Environment.GetEnvironmentVariable("SUDOCLIPPER_HOST")?.Trim() ?? "";
var bindAddress = IPAddress.Loopback;
if (bindHost is "0.0.0.0" or "*" or "any") bindAddress = IPAddress.Any;
else if (bindHost.Length > 0 && IPAddress.TryParse(bindHost, out var parsedBind)) bindAddress = parsedBind;
builder.WebHost.ConfigureKestrel(options => options.Listen(bindAddress, port));
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 8L * 1024 * 1024 * 1024);

var app = builder.Build();
var webRoot = ResolveWebRoot(app.Environment);

if (webRoot != null)
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(webRoot),
        OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
    });
}

var url = $"http://127.0.0.1:{port}";
var modelCache = new ConcurrentDictionary<string, (DateTime At, List<ProviderModels> Groups)>();

/* ─────────────────────────── status ─────────────────────────── */

app.MapGet("/api/status", async (CancellationToken ct) =>
{
    var settings = Store.Settings;
    var tools = await Tooling.StatusAsync(ct);
    return Results.Ok(new
    {
        version = "1.0.0",
        port,
        url,
        outDir = settings.ResolveOutDir(),
        dataDir = Paths.DataDir,
        bootstrapping = Tooling.Bootstrapping,
        bootstrapLog = Tooling.BootstrapLog,
        tools = tools.Select(t => new { t.Name, t.Id, t.Found, t.Path, t.Version, t.Hint }),
        providers = Providers.All,
        ready = tools.All(t => t.Found),
        uiBuilt = webRoot != null,
    });
});

app.MapPost("/api/bootstrap", async () =>
{
    await Tooling.EnsureAsync(_ => Task.CompletedTask, CancellationToken.None);
    var tools = await Tooling.StatusAsync(CancellationToken.None);
    return Results.Ok(new { tools, log = Tooling.BootstrapLog });
});

/* ─────────────────────────── config ─────────────────────────── */

app.MapGet("/api/config", () =>
{
    var s = Store.Settings;
    return Results.Ok(new
    {
        provider = s.Provider,
        model = s.Model,
        captionStyle = s.CaptionStyle,
        captionFont = s.CaptionFont,
        captionScale = s.CaptionScale,
        captionPosition = s.CaptionPosition,
        captionOutline = s.CaptionOutline,
        captionWordsPerLine = s.CaptionWordsPerLine,
        captionUppercase = s.CaptionUppercase,
        captionWordPop = s.CaptionWordPop,
        burnCaptions = s.BurnCaptions,
        clipCount = s.ClipCount,
        minSeconds = s.MinSeconds,
        maxSeconds = s.MaxSeconds,
        quality = s.Quality,
        whisperModel = s.WhisperModel,
        outDir = s.ResolveOutDir(),
        customBaseUrl = s.CustomBaseUrl,
        customModel = s.CustomModel,
        chatProvider = s.ChatProvider,
        chatModel = s.ChatModel,
        baseUrls = s.BaseUrls,
        keys = Providers.All.Select(p => new { provider = p.Id, hasKey = !string.IsNullOrWhiteSpace(s.KeyFor(p.Id)) })
    });
});

app.MapPost("/api/config", (SettingsPatch patch) =>
{
    var s = Store.Settings;

    if (!string.IsNullOrWhiteSpace(patch.Provider)) s.Provider = patch.Provider.Trim();
    if (patch.Model != null) s.Model = patch.Model.Trim();
    if (!string.IsNullOrWhiteSpace(patch.CaptionStyle)) s.CaptionStyle = patch.CaptionStyle.Trim();
    if (patch.CaptionFont != null) s.CaptionFont = patch.CaptionFont.Trim();
    if (patch.CaptionScale.HasValue) s.CaptionScale = Math.Clamp(patch.CaptionScale.Value, 0.7, 1.6);
    if (patch.CaptionPosition.HasValue) s.CaptionPosition = Math.Clamp(patch.CaptionPosition.Value, -260, 320);
    if (patch.CaptionOutline.HasValue) s.CaptionOutline = Math.Clamp(patch.CaptionOutline.Value, 0.2, 2.5);
    if (patch.CaptionWordsPerLine.HasValue) s.CaptionWordsPerLine = Math.Clamp(patch.CaptionWordsPerLine.Value, 0, 8);
    if (patch.CaptionUppercase.HasValue) s.CaptionUppercase = patch.CaptionUppercase.Value;
    if (patch.CaptionWordPop.HasValue) s.CaptionWordPop = patch.CaptionWordPop.Value;
    if (patch.BurnCaptions.HasValue) s.BurnCaptions = patch.BurnCaptions.Value;
    if (patch.ClipCount.HasValue) s.ClipCount = Math.Clamp(patch.ClipCount.Value, 1, 10);
    if (patch.MinSeconds.HasValue) s.MinSeconds = Math.Clamp(patch.MinSeconds.Value, 5, 180);
    if (patch.MaxSeconds.HasValue) s.MaxSeconds = Math.Clamp(patch.MaxSeconds.Value, 6, 180);
    if (!string.IsNullOrWhiteSpace(patch.Quality)) s.Quality = patch.Quality == "720" ? "720" : "1080";
    if (!string.IsNullOrWhiteSpace(patch.WhisperModel)) s.WhisperModel = patch.WhisperModel == "base" ? "base" : "tiny";
    if (patch.OutDir != null) s.OutDir = patch.OutDir.Trim();
    if (patch.CustomBaseUrl != null) s.CustomBaseUrl = patch.CustomBaseUrl.Trim();
    if (patch.CustomModel != null) s.CustomModel = patch.CustomModel.Trim();
    if (patch.ChatProvider != null) s.ChatProvider = patch.ChatProvider.Trim();
    if (patch.ChatModel != null) s.ChatModel = patch.ChatModel.Trim();

    if (patch.BaseUrls != null)
        foreach (var pair in patch.BaseUrls)
        {
            if (string.IsNullOrWhiteSpace(pair.Value)) s.BaseUrls.Remove(pair.Key);
            else s.BaseUrls[pair.Key] = pair.Value.Trim();
        }

    if (patch.Keys != null)
        foreach (var pair in patch.Keys)
        {
            if (string.IsNullOrWhiteSpace(pair.Value)) s.Keys.Remove(pair.Key);
            else s.Keys[pair.Key] = pair.Value.Trim();
        }

    if (patch.ClearKeys != null)
        foreach (var key in patch.ClearKeys) s.Keys.Remove(key);

    Store.SaveSettings();
    return Results.Ok(new { saved = true, outDir = s.ResolveOutDir() });
});

app.MapPost("/api/models", async (ModelRequest request, CancellationToken ct) =>
{
    var settings = Store.Settings;
    var wanted = string.IsNullOrWhiteSpace(request.Provider) ? settings.Provider : request.Provider;

    // "all" = every provider that can answer right now, grouped, so the picker can show them together.
    if (string.Equals(wanted, "all", StringComparison.OrdinalIgnoreCase))
    {
        var signature = string.Join("|", Providers.All.Select(p => p.Id + ":" + (string.IsNullOrWhiteSpace(settings.KeyFor(p.Id)) ? "0" : "1")))
            + "#" + string.Join("|", (settings.BaseUrls ?? new()).Select(pair => pair.Key + "=" + pair.Value));

        if (modelCache.TryGetValue(signature, out var cached) && DateTime.UtcNow - cached.At < TimeSpan.FromMinutes(5))
            return Results.Ok(new { groups = cached.Groups, cached = true });

        var groups = await Llm.ListAllModelsAsync(settings, ct);
        modelCache[signature] = (DateTime.UtcNow, groups);
        return Results.Ok(new { groups, cached = false });
    }

    var provider = Providers.Find(wanted);
    try
    {
        var models = await Llm.ListModelsAsync(provider, settings, ct);
        return Results.Ok(new { provider = provider.Id, models });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message, provider = provider.Id });
    }
});

/* ─────────────────────────── chat ─────────────────────────── */

app.MapPost("/api/chat", async (ChatRequest request, CancellationToken ct) =>
{
    var settings = Store.Settings;
    var provider = Providers.Find(string.IsNullOrWhiteSpace(request.Provider) ? settings.Provider : request.Provider);
    var model = string.IsNullOrWhiteSpace(request.Model)
        ? (string.IsNullOrWhiteSpace(settings.Model) ? provider.DefaultModel : settings.Model)
        : request.Model;

    if (provider.Kind == "local")
        return Results.BadRequest(new { error = "The free local selector ranks moments; it cannot chat. Pick a hosted model for the assistant." });

    var turns = (request.Messages ?? new List<ChatMessage>())
        .Where(m => !string.IsNullOrWhiteSpace(m.Content))
        .TakeLast(24)
        .Select(m => new ChatTurn(string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user", m.Content ?? ""))
        .ToList();
    if (turns.Count == 0) return Results.BadRequest(new { error = "Say something first." });

    var system = $$"""
        You are the SUDOCLIPPER assistant, running inside a local clipping app on the user's own machine.
        Current settings: provider={{settings.Provider}}, model={{settings.Model}}, captionStyle={{settings.CaptionStyle}},
        burnCaptions={{settings.BurnCaptions}}, clipCount={{settings.ClampedClipCount}}, minSeconds={{settings.ClampedMin}},
        maxSeconds={{settings.ClampedMax}}, quality={{settings.Quality}}p, whisperModel={{settings.WhisperModel}}.
        Caption styles: punch (each spoken word pops), karaoke (spoken word lights up), boxed (white bar, black text), clean (quiet fade), neon (glowing colour-word pops), rave (bouncing rainbow words), chrome (slow metallic zoom).
        Be brief and concrete. When the user wants a change, apply it with an ACTION line as the very last line:
        ACTION {"captionStyle":"karaoke","clipCount":4}
        Allowed keys: provider, model, captionStyle, burnCaptions, clipCount, minSeconds, maxSeconds, quality, whisperModel.
        To start a clipping run for a link the user pasted, use: ACTION {"clip":"https://..."}
        Only include the keys that actually change.
        """;

    try
    {
        var reply = await Llm.ChatMessagesAsync(provider, settings, model, system, turns, false, ct);
        var applied = new Dictionary<string, object>();
        string? jobId = null;

        // The JSON may be wrapped across lines by some models, so match to the closing brace.
        var action = Regex.Match(reply ?? "", @"ACTION\s*(\{[\s\S]*?\})", RegexOptions.IgnoreCase);
        if (action.Success)
        {
            try
            {
                using var doc = JsonDocument.Parse(action.Groups[1].Value);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    switch (prop.Name.ToLowerInvariant())
                    {
                        case "provider":
                            settings.Provider = prop.Value.GetString() ?? settings.Provider;
                            applied["provider"] = settings.Provider;
                            break;
                        case "model":
                            settings.Model = prop.Value.GetString() ?? settings.Model;
                            applied["model"] = settings.Model;
                            break;
                        case "captionstyle":
                            settings.CaptionStyle = NormalizeCaptionStyle(prop.Value.GetString());
                            applied["captionStyle"] = settings.CaptionStyle;
                            break;
                        case "burncaptions":
                            settings.BurnCaptions = prop.Value.ValueKind == JsonValueKind.True;
                            applied["burnCaptions"] = settings.BurnCaptions;
                            break;
                        case "clipcount" when prop.Value.ValueKind == JsonValueKind.Number:
                            settings.ClipCount = Math.Clamp(prop.Value.GetInt32(), 1, 10);
                            applied["clipCount"] = settings.ClipCount;
                            break;
                        case "minseconds" when prop.Value.ValueKind == JsonValueKind.Number:
                            settings.MinSeconds = Math.Clamp(prop.Value.GetInt32(), 5, 180);
                            applied["minSeconds"] = settings.MinSeconds;
                            break;
                        case "maxseconds" when prop.Value.ValueKind == JsonValueKind.Number:
                            settings.MaxSeconds = Math.Clamp(prop.Value.GetInt32(), 6, 180);
                            applied["maxSeconds"] = settings.MaxSeconds;
                            break;
                        case "quality":
                            settings.Quality = prop.Value.GetString() == "720" ? "720" : "1080";
                            applied["quality"] = settings.Quality;
                            break;
                        case "whispermodel":
                            settings.WhisperModel = prop.Value.GetString() == "base" ? "base" : "tiny";
                            applied["whisperModel"] = settings.WhisperModel;
                            break;
                        case "clip":
                            var link = (prop.Value.GetString() ?? "").Trim();
                            if (link.Length == 0) break;
                            if (!link.StartsWith("http", StringComparison.OrdinalIgnoreCase)) link = "https://" + link;
                            var job = Jobs.Create(link, link, settings);
                            Jobs.Start(job, settings, null);
                            jobId = job.Id;
                            applied["clip"] = link;
                            break;
                    }
                }
                if (applied.Count > 0) Store.SaveSettings();
                reply = Regex.Replace(reply ?? "", @"ACTION\s*\{[\s\S]*?\}", "").Trim();
            }
            catch { }
        }

        return Results.Ok(new { reply, applied, jobId, provider = provider.Id, model });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message, provider = provider.Id });
    }
});

app.MapGet("/api/clips/{id}/cues", (string id, double? from, double? to) =>
{
    var clip = Store.ReadMemory().FirstOrDefault(c => c.Id == id);
    if (clip == null) return Results.NotFound(new { error = "Unknown clip" });
    if (string.IsNullOrWhiteSpace(clip.CuesPath) || !File.Exists(clip.CuesPath))
        return Results.Ok(new { cues = Array.Empty<object>() });

    var start = from ?? clip.Start;
    var end = to ?? (clip.End > clip.Start ? clip.End : clip.Start + 10);
    var cues = Subtitles.ParseFile(clip.CuesPath)
        .Where(c => c.End > start && c.Start < end)
        .Take(80)
        .Select(c => new { c.Start, c.End, c.Text })
        .ToList();
    return Results.Ok(new { cues });
});

app.MapPost("/api/clips/{id}/edit", async (string id, EditRequest request, CancellationToken ct) =>
{
    var clip = Store.ReadMemory().FirstOrDefault(c => c.Id == id);
    if (clip == null) return Results.NotFound(new { error = "Unknown clip" });

    try
    {
        var updated = await Pipeline.RerenderAsync(clip, new Pipeline.EditOptions
        {
            Start = request.Start ?? clip.Start,
            End = request.End ?? (clip.End > clip.Start ? clip.End : clip.Start + Math.Max(6, clip.Duration)),
            CaptionStyle = NormalizeCaptionStyle(request.CaptionStyle ?? clip.CaptionStyle),
            Captions = request.Captioned ?? true,
            FontScale = Math.Clamp(request.FontScale ?? 1, 0.7, 1.6),
            PositionDelta = Math.Clamp(request.PositionDelta ?? 0, -260, 320),
            Uppercase = request.Uppercase,
            Font = request.Font ?? "",
            OutlineScale = Math.Clamp(request.OutlineScale ?? 1, 0.2, 2.5),
            WordsPerLine = request.WordsPerLine,
            WordPop = request.WordPop
        }, ct);
        return Results.Ok(new { clip = updated });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

/* ─────────────────────────── jobs ─────────────────────────── */

app.MapPost("/api/jobs", (JobRequest request) =>
{
    var settings = Store.Settings;
    string? localFile = null;
    var source = "";

    if (!string.IsNullOrWhiteSpace(request.UploadId))
    {
        var uploads = Path.Combine(Paths.DataDir, "uploads");
        var match = Directory.Exists(uploads)
            ? Directory.EnumerateFiles(uploads, request.UploadId + ".*").FirstOrDefault()
            : null;
        if (match == null) return Results.BadRequest(new { error = "That upload expired. Drop the file again." });
        localFile = match;
        source = Path.GetFileName(match);
        if (string.IsNullOrWhiteSpace(request.Url)) source = "local file";
    }
    else
    {
        var target = (request.Url ?? "").Trim();
        if (target.Length == 0) return Results.BadRequest(new { error = "Paste a YouTube link first." });
        if (!target.StartsWith("http://") && !target.StartsWith("https://")) target = "https://" + target;
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            return Results.BadRequest(new { error = "That does not look like a link." });
        source = target;
        request = request with { Url = target };
    }

    var job = Jobs.Create(request.Url ?? "", source, settings);
    Jobs.Start(job, settings, localFile);
    return Results.Ok(job);
});

app.MapGet("/api/jobs/{id}", (string id) =>
{
    var job = Jobs.Get(id);
    return job == null ? Results.NotFound(new { error = "Unknown job" }) : Results.Ok(job);
});

app.MapPost("/api/jobs/{id}/cancel", (string id) =>
{
    var job = Jobs.Get(id);
    if (job == null) return Results.NotFound(new { error = "Unknown job" });
    try { job.Cts.Cancel(); } catch { }
    return Results.Ok(new { canceled = true });
});

app.MapGet("/api/jobs", () => Results.Ok(Jobs.Recent()));

/* ─────────────────────────── memory ─────────────────────────── */

app.MapGet("/api/memory", () =>
{
    var clips = Store.ReadMemory()
        .Select(c => new
        {
            c.Id, c.Title, c.Hook, c.Reason, c.Score, c.Start, c.End, c.Duration,
            c.File, c.Thumb, c.SourceTitle, c.SourceUrl, c.Provider, c.Model, c.Captioned, c.CreatedAt,
            c.CaptionStyle, sourceDuration = c.SourceDuration,
            editable = !string.IsNullOrWhiteSpace(c.SourcePath) && File.Exists(c.SourcePath),
            exists = File.Exists(c.File)
        })
        .ToList();
    return Results.Ok(new { clips, outDir = Store.Settings.ResolveOutDir() });
});

app.MapDelete("/api/memory/{id}", (string id) =>
{
    var clip = Store.ReadMemory().FirstOrDefault(c => c.Id == id);
    if (clip == null) return Results.NotFound(new { error = "Unknown clip" });
    TryDelete(clip.File);
    TryDelete(clip.Thumb);
    Store.RemoveMemory(id);
    return Results.Ok(new { deleted = true });
});

/* ─────────────────────────── files ─────────────────────────── */

app.MapGet("/api/media", (string p) =>
{
    if (string.IsNullOrWhiteSpace(p) || !File.Exists(p)) return Results.NotFound();
    if (!IsInside(Store.Settings.ResolveOutDir(), p) && !IsInside(Paths.DataDir, p)) return Results.NotFound();
    var ext = Path.GetExtension(p).ToLowerInvariant();
    var type = ext switch
    {
        ".mp4" => "video/mp4",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webm" => "video/webm",
        _ => "application/octet-stream"
    };
    return Results.File(p, type, enableRangeProcessing: true);
});

app.MapPost("/api/open", (OpenRequest request) =>
{
    var path = request.Path;
    if (!Directory.Exists(path) && !File.Exists(path))
        path = Store.Settings.ResolveOutDir();
    if (!IsInside(Store.Settings.ResolveOutDir(), path) && !IsInside(Paths.DataDir, path))
        return Results.BadRequest(new { error = "Outside the SUDOCLIPPER folder." });
    try
    {
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo("explorer.exe", File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"") { UseShellExecute = true });
        else if (OperatingSystem.IsMacOS())
            Process.Start(new ProcessStartInfo("open", File.Exists(path) ? $"-R \"{path}\"" : $"\"{path}\"") { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo("xdg-open", File.Exists(path) ? Path.GetDirectoryName(path)! : path) { UseShellExecute = true });
        return Results.Ok(new { opened = true });
    }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/upload", async (HttpRequest request) =>
{
    if (!request.HasFormContentType) return Results.BadRequest(new { error = "Expected a file upload." });
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
    if (file == null || file.Length == 0) return Results.BadRequest(new { error = "No file received." });

    var dir = Paths.EnsureDir(Path.Combine(Paths.DataDir, "uploads"));
    foreach (var stale in Directory.EnumerateFiles(dir))
        if (File.GetLastWriteTimeUtc(stale) < DateTime.UtcNow.AddHours(-12)) TryDelete(stale);

    var id = Guid.NewGuid().ToString("n")[..10];
    var ext = Path.GetExtension(file.FileName);
    if (string.IsNullOrWhiteSpace(ext) || ext.Length > 6) ext = ".mp4";
    var path = Path.Combine(dir, id + ext);
    await using (var stream = File.Create(path)) await file.CopyToAsync(stream);
    return Results.Ok(new { id, name = file.FileName, size = file.Length });
});

/* ─────────────────────────── analytics ─────────────────────────── */

var oauthStates = new ConcurrentDictionary<string, string>();

app.MapGet("/api/analytics", () =>
{
    var links = Analytics.All();
    return Results.Ok(new
    {
        platforms = Analytics.Platforms.Select(p =>
        {
            var link = links.FirstOrDefault(l => l.Platform == p.Id) ?? new ChannelLink { Platform = p.Id };
            return new
            {
                p.Id,
                p.Name,
                p.Scope,
                p.Note,
                p.Supported,
                linked = link.Linked,
                clientId = link.ClientId,
                hasSecret = !string.IsNullOrWhiteSpace(link.ClientSecret),
                redirectUri = Analytics.RedirectUri(port, p.Id),
                error = link.Error,
                stats = link.Stats,
            };
        }),
    });
});

app.MapPost("/api/analytics/{platform}/credentials", (string platform, AnalyticsCredsRequest req) =>
{
    var info = Analytics.Platforms.FirstOrDefault(p => p.Id == platform);
    if (info == null) return Results.NotFound(new { error = "Unknown platform" });
    var link = Analytics.Get(platform);
    if (!string.IsNullOrWhiteSpace(req.ClientId)) link.ClientId = req.ClientId.Trim();
    if (!string.IsNullOrWhiteSpace(req.ClientSecret)) link.ClientSecret = req.ClientSecret.Trim();
    Analytics.Save();
    return Results.Ok(new
    {
        ok = true,
        clientId = link.ClientId,
        hasSecret = !string.IsNullOrWhiteSpace(link.ClientSecret),
        redirectUri = Analytics.RedirectUri(port, platform),
    });
});

app.MapPost("/api/analytics/{platform}/start", (string platform) =>
{
    var info = Analytics.Platforms.FirstOrDefault(p => p.Id == platform);
    if (info is not { Supported: true })
        return Results.BadRequest(new { error = "This platform is not wired up yet." });

    var link = Analytics.Get(platform);
    if (string.IsNullOrWhiteSpace(link.ClientId) || string.IsNullOrWhiteSpace(link.ClientSecret))
        return Results.BadRequest(new { error = "Add the OAuth client id and secret first." });

    var state = Guid.NewGuid().ToString("n");
    oauthStates[state] = platform;
    return Results.Ok(new
    {
        url = Analytics.AuthorizeUrl(platform, link, port, state),
        redirectUri = Analytics.RedirectUri(port, platform),
    });
});

app.MapGet("/api/oauth/{platform}/callback", async (string platform, string? code, string? state, string? error, CancellationToken ct) =>
{
    var page = (bool ok, string title, string detail) =>
    {
        var html =
            "<!doctype html><meta charset=\"utf-8\"><title>" + title + "</title>" +
            "<style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#080808;color:#fff;" +
            "font:15px/1.6 'Segoe UI',system-ui,sans-serif}" +
            "div{max-width:440px;padding:28px;border:1px solid #ffffff1a;border-radius:20px;text-align:center}" +
            "h1{font-size:17px;margin:0 0 10px}p{color:#8a8a8a;margin:0 0 18px;font-size:13px}" +
            "a{color:#fff}</style>" +
            "<div><h1>" + title + "</h1><p>" + detail + "</p><p><a href=\"" + url + "\">Back to SUDOCLIPPER</a></p></div>";
        return Results.Text(html, "text/html");
    };

    if (!string.IsNullOrWhiteSpace(error))
        return page(false, "Not connected", WebUtility.HtmlEncode(error));

    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state) ||
        !oauthStates.TryRemove(state, out var expected) ||
        !string.Equals(expected, platform, StringComparison.OrdinalIgnoreCase))
        return page(false, "Not connected", "The authorization code was missing or the state check failed. Start again from Config → Analytics.");

    try
    {
        var link = await Analytics.ExchangeAsync(platform, code, port, ct);
        var name = WebUtility.HtmlEncode(link.Stats?.Name ?? Analytics.PlatformName(platform));
        return page(true, $"{name} connected", $"<span>{(link.Stats?.Subscribers ?? 0):N0}</span> followers are now on your dashboard. You can close this tab.");
    }
    catch (Exception ex)
    {
        var current = Analytics.Get(platform);
        current.Error = ex.Message;
        Analytics.Save();
        return page(false, "The exchange failed", WebUtility.HtmlEncode(ex.Message));
    }
});

app.MapPost("/api/analytics/{platform}/refresh", async (string platform, CancellationToken ct) =>
{
    var link = Analytics.Get(platform);
    if (!link.Linked) return Results.BadRequest(new { error = "Connect this channel first." });
    await Analytics.FetchStatsAsync(link, ct);
    return Results.Ok(new { linked = link.Linked, error = link.Error, stats = link.Stats });
});

app.MapDelete("/api/analytics/{platform}", (string platform) =>
{
    Analytics.Remove(platform);
    return Results.Ok(new { ok = true });
});

/* ─────────────────────────── mcp ─────────────────────────── */

app.MapGet("/api/mcp", () =>
{
    var exe = Environment.ProcessPath ?? "sudoclipper";
    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    var claudeConfig = string.IsNullOrWhiteSpace(appData)
        ? "claude_desktop_config.json"
        : Path.Combine(appData, "Claude", "claude_desktop_config.json");
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var cursorConfig = Path.Combine(home, ".cursor", "mcp.json");

    return Results.Ok(new
    {
        command = "sudoclipper --mcp",
        exe,
        // A globally installed `sudoclipper` is the friendliest command; the full path is the fallback.
        claudeConfig,
        cursorConfig,
        tools = new[]
        {
            new { name = "sudoclipper_moments", about = "Read where the chat went wild and which parts got replayed" },
            new { name = "sudoclipper_clip", about = "Download a link and render captioned 9:16 clips" },
            new { name = "sudoclipper_clips", about = "List rendered clips with paths" },
            new { name = "sudoclipper_config", about = "Read or change the clipping settings" },
            new { name = "sudoclipper_status", about = "Report the local toolchain and folders" },
        },
        snippet = new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["sudoclipper"] = new { command = "sudoclipper", args = new[] { "--mcp" } },
            }
        },
        snippetExe = new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["sudoclipper"] = new { command = exe, args = new[] { "--mcp" } },
            }
        },
    });
});

app.MapGet("/", () => webRoot == null
    ? Results.Text(UiMissingPage.Value, "text/html")
    : Results.File(Path.Combine(webRoot, "index.html"), "text/html"));

app.MapFallback((HttpContext ctx) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api")) return Results.NotFound();
    return webRoot == null
        ? Results.Text(UiMissingPage.Value, "text/html")
        : Results.File(Path.Combine(webRoot, "index.html"), "text/html");
});

/* ─────────────────────────── run ─────────────────────────── */

Banner(port, Store.Settings.ResolveOutDir());

app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        try
        {
            var tools = await Tooling.StatusAsync(CancellationToken.None);
            if (tools.All(t => t.Found)) return;
            Console.WriteLine("  First run: fetching ffmpeg + yt-dlp once (~100 MB)…");
            await Tooling.EnsureAsync(line => { Console.WriteLine("  " + line); return Task.CompletedTask; }, CancellationToken.None);
            Console.WriteLine("  Toolchain ready.");
        }
        catch (Exception ex) { Console.WriteLine("  Tool setup skipped: " + ex.Message); }
    });

    if (!noOpen) OpenBrowser(url);
});

try
{
    await app.RunAsync();
}
catch (IOException ex)
{
    Console.Error.WriteLine("Could not start the local server: " + ex.Message);
}

/* ─────────────────────────── helpers ─────────────────────────── */

static string? ResolveWebRoot(IWebHostEnvironment env)
{
    foreach (var candidate in new[]
    {
        Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        Path.Combine(env.ContentRootPath, "wwwroot"),
        Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
    })
    {
        if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "index.html")))
            return candidate;
    }
    return null;
}

static int FindFreePort(int start)
{
    for (var candidate = start; candidate < start + 60; candidate++)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, candidate);
            listener.Start();
            listener.Stop();
            return candidate;
        }
        catch (SocketException) { }
    }
    return start;
}

static void OpenBrowser(string target)
{
    try
    {
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        else if (OperatingSystem.IsMacOS())
            Process.Start("open", target);
        else
            Process.Start("xdg-open", target);
    }
    catch (Exception ex) { Console.Error.WriteLine("Open " + target + " manually (" + ex.Message + ")"); }
}

static void TryDelete(string path)
{
    try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
}

static bool IsInside(string root, string path)
{
    try
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        return full.StartsWith(fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
    catch { return false; }
}

static string NormalizeCaptionStyle(string? style) => (style ?? "").Trim().ToLowerInvariant() switch
{
    "karaoke" => "karaoke",
    "neon" => "neon",
    "rave" => "rave",
    "chrome" => "chrome",
    "boxed" or "impact" => "boxed",
    "clean" => "clean",
    "punch" or "bold" => "punch",
    _ => "punch"
};

static void Banner(int port, string outDir)
{
    Console.WriteLine();
    Console.WriteLine("  ╭──────────────────────────────────────────────╮");
    Console.WriteLine("  │  S U D O C L I P P E R                       │");
    Console.WriteLine("  │  local clipping studio · 9:16 · captions     │");
    Console.WriteLine("  ╰──────────────────────────────────────────────╯");
    Console.WriteLine($"   ▸ UI       http://127.0.0.1:{port}");
    Console.WriteLine($"   ▸ Clips    {outDir}");
    Console.WriteLine("   ▸ Stop     Ctrl+C");
    Console.WriteLine();
}

public sealed record ModelRequest(string? Provider);

public sealed record JobRequest(string? Url, string? UploadId);

public sealed record ChatMessage(string? Role, string? Content);

public sealed record ChatRequest(string? Provider, string? Model, List<ChatMessage>? Messages);

public sealed record EditRequest
{
    public double? Start { get; init; }
    public double? End { get; init; }
    public string? CaptionStyle { get; init; }
    public bool? Captioned { get; init; }
    public double? FontScale { get; init; }
    public int? PositionDelta { get; init; }
    public bool? Uppercase { get; init; }
    public string? Font { get; init; }
    public double? OutlineScale { get; init; }
    public int? WordsPerLine { get; init; }
    public bool? WordPop { get; init; }
}


public sealed record OpenRequest(string? Path);

public sealed record AnalyticsCredsRequest(string? ClientId, string? ClientSecret);

public sealed record SettingsPatch
{
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? CaptionStyle { get; init; }
    public string? CaptionFont { get; init; }
    public double? CaptionScale { get; init; }
    public int? CaptionPosition { get; init; }
    public double? CaptionOutline { get; init; }
    public int? CaptionWordsPerLine { get; init; }
    public bool? CaptionUppercase { get; init; }
    public bool? CaptionWordPop { get; init; }
    public bool? BurnCaptions { get; init; }
    public int? ClipCount { get; init; }
    public int? MinSeconds { get; init; }
    public int? MaxSeconds { get; init; }
    public string? Quality { get; init; }
    public string? WhisperModel { get; init; }
    public string? OutDir { get; init; }
    public string? CustomBaseUrl { get; init; }
    public string? CustomModel { get; init; }
    public string? ChatProvider { get; init; }
    public string? ChatModel { get; init; }
    public Dictionary<string, string>? BaseUrls { get; init; }
    public Dictionary<string, string>? Keys { get; init; }
    public List<string>? ClearKeys { get; init; }
}

public static class UiMissingPage
{
    public const string Value = """
        <!doctype html><html><head><meta charset="utf-8"><title>SUDOCLIPPER</title>
        <style>body{background:#0a0a0b;color:#eee;font-family:ui-monospace,monospace;display:grid;place-items:center;height:100vh;margin:0}
        code{background:#1a1a1f;padding:2px 6px;border-radius:4px;color:#ffa896}</style></head>
        <body><div><h1>SUDOCLIPPER</h1><p>The UI has not been built yet.</p>
        <p>Run <code>npm install &amp;&amp; npm run build</code> in the <code>web/</code> folder, then restart.</p></div></body></html>
        """;
}
