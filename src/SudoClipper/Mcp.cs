using System.Text;
using System.Text.Json;

namespace SudoClipper;

/// <summary>
/// MCP server over stdio: `sudoclipper --mcp`.
/// Lets an AI agent (Claude Desktop, Cursor, Cline…) drive the clipper as a tool.
/// </summary>
public static class Mcp
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // One render at a time, same as the UI: two agents clipping at once would fight for ffmpeg.
    private static readonly SemaphoreSlim ClipGate = new(1, 1);

    public static async Task RunAsync()
    {
        // JSON-RPC owns stdout. Everything the pipeline logs goes to stderr instead.
        var stdout = Console.OpenStandardOutput();
        Console.SetOut(Console.Error);

        using var input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
        var output = new StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true };

        while (true)
        {
            var line = await input.ReadLineAsync();
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch { continue; }

            JsonElement id = default;
            var hasId = false;

            try
            {
                var root = doc.RootElement;
                var method = root.TryGetProperty("method", out var m) ? m.GetString() ?? "" : "";
                hasId = root.TryGetProperty("id", out var idElement);
                if (hasId) id = idElement.Clone();

                // notifications/initialized, notifications/cancelled and friends need no reply
                if (!hasId) continue;

                switch (method)
                {
                    case "initialize":
                        Write(output, id, new
                        {
                            protocolVersion = "2024-11-05",
                            capabilities = new { tools = new { listChanged = false } },
                            serverInfo = new { name = "sudoclipper", version = "1.0.0" },
                            instructions =
                                "Local YouTube clipper. sudoclipper_moments reads where the audience reacted (live-chat spikes + replay heatmap), " +
                                "sudoclipper_clip downloads and renders vertical 9:16 clips with captions, sudoclipper_clips lists results."
                        });
                        break;

                    case "ping":
                        Write(output, id, new { });
                        break;

                    case "tools/list":
                        Write(output, id, new { tools = Tools() });
                        break;

                    case "resources/list":
                        Write(output, id, new { resources = Array.Empty<object>() });
                        break;

                    case "resources/templates/list":
                        Write(output, id, new { resourceTemplates = Array.Empty<object>() });
                        break;

                    case "prompts/list":
                        Write(output, id, new { prompts = Array.Empty<object>() });
                        break;

                    case "tools/call":
                    {
                        var parameters = root.TryGetProperty("params", out var p) ? p : default;
                        var name = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("name", out var n)
                            ? n.GetString() ?? ""
                            : "";
                        var args = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("arguments", out var a)
                            ? a
                            : default;

                        // A failed tool is a tool result with isError, not a JSON-RPC error: the client
                        // must get an answer for this id or it waits forever.
                        try
                        {
                            var text = await CallAsync(name, args);
                            Write(output, id, new
                            {
                                content = new object[] { new { type = "text", text } },
                                isError = false
                            });
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[mcp] {name}: {ex.Message}");
                            Write(output, id, new
                            {
                                content = new object[] { new { type = "text", text = ex.Message } },
                                isError = true
                            });
                        }
                        break;
                    }

                    default:
                        WriteError(output, id, -32601, $"Unknown method: {method}");
                        break;
                }
            }
            catch (Exception ex)
            {
                WriteError(output, hasId ? id : default, -32603, ex.Message);
            }
            finally
            {
                doc.Dispose();
            }
        }
    }

    private static object[] Tools() => new object[]
    {
        new
        {
            name = "sudoclipper_moments",
            description = "Read the audience signals for a video: live-chat bursts and most-replayed passages. Use it to choose what to clip before rendering.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    url = new { type = "string", description = "Video URL to inspect" }
                },
                required = new[] { "url" }
            }
        },
        new
        {
            name = "sudoclipper_clip",
            description = "Download a video, find the best moments (audience signals + captions) and render vertical 9:16 clips with captions. Waits until rendering finishes.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    url = new { type = "string", description = "Video URL to clip" },
                    clips = new { type = "integer", description = "How many clips (1-10, default 3)" },
                    min_seconds = new { type = "integer", description = "Shortest clip (default 12)" },
                    max_seconds = new { type = "integer", description = "Longest clip (default 55)" },
                    caption_style = new { type = "string", description = "punch | karaoke | boxed | clean" },
                    follow_chat = new { type = "boolean", description = "Weight moments by chat/replay heat (default true)" }
                },
                required = new[] { "url" }
            }
        },
        new
        {
            name = "sudoclipper_clips",
            description = "List clips that SUDOCLIPPER has rendered, newest first.",
            inputSchema = new
            {
                type = "object",
                properties = new { limit = new { type = "integer", description = "How many clips to return (default 10)" } }
            }
        },
        new
        {
            name = "sudoclipper_config",
            description = "Read the current settings, or change them by passing any of the same keys.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    provider = new { type = "string" },
                    model = new { type = "string" },
                    captionStyle = new { type = "string" },
                    clipCount = new { type = "integer" },
                    minSeconds = new { type = "integer" },
                    maxSeconds = new { type = "integer" },
                    burnCaptions = new { type = "boolean" }
                }
            }
        },
        new
        {
            name = "sudoclipper_status",
            description = "Report whether ffmpeg / yt-dlp / the local transcriber are ready, plus the clips folder.",
            inputSchema = new { type = "object", properties = new { } }
        }
    };

    private static async Task<string> CallAsync(string name, JsonElement args)
    {
        var settings = Store.Settings;

        switch (name)
        {
            case "sudoclipper_config":
            {
                var changed = false;
                if (args.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in args.EnumerateObject())
                    {
                        switch (prop.Name.ToLowerInvariant())
                        {
                            case "provider": settings.Provider = prop.Value.GetString() ?? settings.Provider; changed = true; break;
                            case "model": settings.Model = prop.Value.GetString() ?? settings.Model; changed = true; break;
                            case "captionstyle": settings.CaptionStyle = prop.Value.GetString() ?? settings.CaptionStyle; changed = true; break;
                            case "clipcount": settings.ClipCount = Math.Clamp(prop.Value.GetInt32(), 1, 10); changed = true; break;
                            case "minseconds": settings.MinSeconds = Math.Clamp(prop.Value.GetInt32(), 5, 180); changed = true; break;
                            case "maxseconds": settings.MaxSeconds = Math.Clamp(prop.Value.GetInt32(), 6, 180); changed = true; break;
                            case "burncaptions": settings.BurnCaptions = prop.Value.ValueKind == JsonValueKind.True; changed = true; break;
                        }
                    }
                    if (changed) Store.SaveSettings();
                }

                return JsonSerializer.Serialize(new
                {
                    settings.Provider,
                    settings.Model,
                    settings.CaptionStyle,
                    settings.BurnCaptions,
                    settings.ClipCount,
                    settings.MinSeconds,
                    settings.MaxSeconds,
                    settings.Quality,
                    settings.WhisperModel,
                    outDir = settings.ResolveOutDir()
                }, Json);
            }

            case "sudoclipper_clips":
            {
                var limit = 10;
                if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("limit", out var l) && l.ValueKind == JsonValueKind.Number)
                    limit = Math.Clamp(l.GetInt32(), 1, 100);
                var clips = Store.ReadMemory().Take(limit).Select(c => new
                {
                    c.Id,
                    c.Title,
                    c.File,
                    c.Thumb,
                    start = c.Start,
                    end = c.End,
                    c.Duration,
                    c.CaptionStyle,
                    c.SourceTitle,
                    exists = File.Exists(c.File)
                });
                return JsonSerializer.Serialize(clips, Json);
            }

            case "sudoclipper_status":
            {
                var tools = await Tooling.StatusAsync(CancellationToken.None);
                return JsonSerializer.Serialize(new
                {
                    ready = tools.All(t => t.Found),
                    tools = tools.Select(t => new { t.Id, t.Found, t.Version }),
                    outDir = settings.ResolveOutDir(),
                    dataDir = Paths.DataDir
                }, Json);
            }

            case "sudoclipper_moments":
            {
                var link = Link(args);
                await Tooling.EnsureAsync(line => { Console.Error.WriteLine("  " + line); return Task.CompletedTask; }, CancellationToken.None);
                var dir = Paths.EnsureDir(Path.Combine(Paths.WorkDir, "signal-" + Guid.NewGuid().ToString("n")[..6]));
                try
                {
                    var engagement = await Engagement.FetchAsync(link, dir, line => Console.Error.WriteLine("  " + line), CancellationToken.None);
                    return JsonSerializer.Serialize(new
                    {
                        url = link,
                        chatMessages = engagement.ChatMessages,
                        paidMessages = engagement.ChatPaid,
                        hotspots = engagement.Hotspots.Select(h => new
                        {
                            kind = h.Kind,
                            start = Math.Round(h.Start, 1),
                            end = Math.Round(h.End, 1),
                            at = Engagement.Clock(h.Start),
                            peak = h.Peak,
                            messages = h.Messages
                        }),
                        summary = engagement.Hotspots.Select(h => $"{Engagement.Clock(h.Start)} — {h.Label}").ToArray(),
                        note = engagement.Note
                    }, Json);
                }
                finally { try { Directory.Delete(dir, true); } catch { } }
            }

            case "sudoclipper_clip":
            {
                var link = Link(args);
                var followChat = true;

                if (args.ValueKind == JsonValueKind.Object)
                {
                    if (args.TryGetProperty("clips", out var c) && c.ValueKind == JsonValueKind.Number)
                        settings.ClipCount = Math.Clamp(c.GetInt32(), 1, 10);
                    if (args.TryGetProperty("min_seconds", out var mn) && mn.ValueKind == JsonValueKind.Number)
                        settings.MinSeconds = Math.Clamp(mn.GetInt32(), 5, 180);
                    if (args.TryGetProperty("max_seconds", out var mx) && mx.ValueKind == JsonValueKind.Number)
                        settings.MaxSeconds = Math.Clamp(mx.GetInt32(), 6, 180);
                    if (args.TryGetProperty("caption_style", out var cs) && cs.ValueKind == JsonValueKind.String)
                        settings.CaptionStyle = cs.GetString() ?? settings.CaptionStyle;
                    if (args.TryGetProperty("follow_chat", out var fc))
                        followChat = fc.ValueKind != JsonValueKind.False;
                    Store.SaveSettings();
                }

                await ClipGate.WaitAsync();
                try
                {
                    var job = Jobs.Create(link, link, settings);
                    job.FollowAudience = followChat;
                    await Pipeline.RunAsync(job, settings, null);

                    if (job.State == "error") throw new InvalidOperationException(job.Error);

                    return JsonSerializer.Serialize(new
                    {
                        job = job.Id,
                        state = job.State,
                        title = job.Title,
                        audience = job.Audience,
                        clips = job.Clips.Select(c => new { c.Id, c.Title, c.File, c.Duration, start = c.Start, end = c.End })
                    }, Json);
                }
                finally { ClipGate.Release(); }
            }

            default:
                throw new InvalidOperationException($"Unknown tool: {name}");
        }
    }

    private static string Link(JsonElement args)
    {
        var url = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("url", out var u) ? u.GetString() : null;
        if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("url is required");
        var target = url.Trim();
        if (!target.StartsWith("http", StringComparison.OrdinalIgnoreCase)) target = "https://" + target;
        return target;
    }

    private static void Write(StreamWriter output, JsonElement id, object result)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = id.ValueKind == JsonValueKind.Undefined ? (object?)null : id,
            result
        };
        output.WriteLine(JsonSerializer.Serialize(payload, Json));
    }

    private static void WriteError(StreamWriter output, JsonElement id, int code, string message)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = id.ValueKind == JsonValueKind.Undefined ? (object?)null : id,
            error = new { code, message }
        };
        output.WriteLine(JsonSerializer.Serialize(payload, Json));
    }
}
