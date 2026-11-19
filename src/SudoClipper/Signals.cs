using System.Globalization;
using System.Text.Json;

namespace SudoClipper;

/// <summary>A moment the audience reacted to: chat bursting, or a replayed passage.</summary>
public sealed class Hotspot
{
    public string Kind { get; set; } = "";      // chat | replay
    public double Start { get; set; }
    public double End { get; set; }
    public double Peak { get; set; }            // 0..1
    public double PeakStart { get; set; }        // where the strongest bucket sits
    public int Messages { get; set; }           // chat only
    public int Superchats { get; set; }         // chat only

    public double Center => (Start + End) / 2;

    public string Label => Kind == "chat"
        ? $"chat went wild — {Messages} messages{(Superchats > 0 ? $", {Superchats} paid" : "")}"
        : $"most replayed — {Math.Round(Peak * 100)}% heat";

    public double Boost(double start, double end)
    {
        var overlap = Math.Min(end, End) - Math.Max(start, Start);
        if (overlap <= 0) return 0;
        var span = Math.Max(0.5, End - Start);
        var coverage = Math.Min(1, overlap / span);
        var inside = Center >= start && Center <= end ? 1 : coverage;
        return Math.Round(Math.Max(coverage, inside) * Peak, 3);
    }
}

/// <summary>
/// Audience signals pulled while the video downloads. No view counts needed: the live-chat
/// replay tells us exactly where the room lost it, and YouTube's heatmap shows the replayed parts.
/// </summary>
public sealed class Engagement
{
    public List<Hotspot> Chat { get; set; } = new();
    public List<Hotspot> Replay { get; set; } = new();
    public int ChatMessages { get; set; }
    public int ChatPaid { get; set; }
    public bool ChatPartial { get; set; }
    public string Note { get; set; } = "";

    public bool Any => Chat.Count > 0 || Replay.Count > 0;

    public List<Hotspot> Hotspots => Chat.Concat(Replay).OrderByDescending(h => h.Peak).ToList();

    public double Boost(double start, double end)
    {
        if (!Any) return 0;
        var best = Chat.Select(h => h.Boost(start, end)).DefaultIfEmpty(0).Max();
        var replay = Replay.Select(h => h.Boost(start, end)).DefaultIfEmpty(0).Max() * 0.85;
        return Math.Min(1, Math.Max(best, replay));
    }

    /// <summary>The few lines an LLM can reason about.</summary>
    public string ToPrompt(int max = 14)
    {
        if (!Any) return "No audience data was available for this video.";
        var lines = new List<string>();
        foreach (var hotspot in Hotspots.Take(max))
            lines.Add($"[{Clock(hotspot.Start)}] {hotspot.Label} (peak {Math.Round(hotspot.Peak * 100)}%)");
        return string.Join("\n", lines);
    }

    public IEnumerable<string> LogLines(int max = 5)
    {
        foreach (var hotspot in Chat.OrderByDescending(h => h.Peak).Take(max))
            yield return $"Chat spike at {Clock(hotspot.Start)} — {hotspot.Messages} messages in {Math.Round(hotspot.End - hotspot.Start)}s"
                         + (hotspot.Superchats > 0 ? $" ({hotspot.Superchats} paid)" : "");
        foreach (var hotspot in Replay.OrderByDescending(h => h.Peak).Take(max))
            yield return $"Most replayed at {Clock(hotspot.Start)} — {Math.Round(hotspot.Peak * 100)}% heat";
        if (ChatMessages > 0)
            yield return $"{ChatMessages:N0} chat messages read from the replay.";
    }

    public static string Clock(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
    }

    /* ───────────────────────── fetch ───────────────────────── */

    public static async Task<Engagement> FetchAsync(string url, string workDir, Action<string> log, CancellationToken ct)
    {
        var result = new Engagement();
        if (Tooling.YtDlp == null || string.IsNullOrWhiteSpace(url)) return result;

        await TryReplayAsync(result, url, workDir, log, ct);
        await TryChatAsync(result, url, workDir, log, ct);

        if (result.ChatPartial)
            result.Note = "The chat replay was longer than the read budget, so bursts cover the early part of the stream.";
        else if (!result.Any && result.ChatMessages == 0)
            result.Note = "This video exposes no chat replay or replay heatmap, so moments come from the transcript alone.";
        return result;
    }

    private static async Task TryReplayAsync(Engagement result, string url, string workDir, Action<string> log, CancellationToken ct)
    {
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(45));
            var code = 1;
            try
            {
                (code, _) = await Proc.CaptureAsync(Tooling.YtDlp!, new[]
                {
                    "--no-warnings", "--no-playlist", "--skip-download", "--no-mtime",
                    "--write-info-json", "-o", "engage.%(ext)s", url
                }, workDir, budget.Token);
            }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested) throw;
                log("The replay heatmap took too long to read.");
                return;
            }

            var info = Directory.EnumerateFiles(workDir, "engage*.info.json").FirstOrDefault();
            if (info == null) return;

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(info, ct));
            if (!doc.RootElement.TryGetProperty("heatmap", out var heatmap) || heatmap.ValueKind != JsonValueKind.Array)
            {
                if (code != 0) log("No replay heatmap on this video.");
                return;
            }

            var points = new List<Hotspot>();
            foreach (var item in heatmap.EnumerateArray())
            {
                var start = Number(item, "start_time");
                var end = Number(item, "end_time");
                var value = Number(item, "value");
                if (end <= start || value <= 0) continue;
                points.Add(new Hotspot { Kind = "replay", Start = start, End = end, Peak = Math.Clamp(value, 0, 1) });
            }
            if (points.Count == 0) return;

            // The heatmap is relative, so a quiet video would otherwise produce no hotspot at all.
            var top = points.Max(p => p.Peak);
            if (top > 0 && top < 0.95)
                foreach (var point in points) point.Peak = Math.Clamp(point.Peak / top, 0, 1);

            result.Replay = Merge(points, threshold: 0.6, mergeGap: 2.5, max: 8);

            // A hot region can be minutes long; a clip is not. Anchor it on the hottest slice.
            foreach (var hotspot in result.Replay.Where(h => h.End - h.Start > 45))
            {
                var anchor = hotspot.PeakStart > 0 ? hotspot.PeakStart : hotspot.Start;
                hotspot.Start = Math.Max(0, anchor - 8);
                hotspot.End = hotspot.Start + 40;
            }

            log($"Replay heatmap: {points.Count} points, {result.Replay.Count} hot spots.");
        }
        catch (OperationCanceledException) { throw; }
        catch { /* the heatmap is a nice-to-have */ }
    }

    private static async Task TryChatAsync(Engagement result, string url, string workDir, Action<string> log, CancellationToken ct)
    {
        try
        {
            // A busy stream has hundreds of MB of chat. Read what fits in the budget, then use
            // the partial file: 90 seconds still covers tens of thousands of messages.
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(90));
            try
            {
                await Proc.CaptureAsync(Tooling.YtDlp!, new[]
                {
                    "--no-warnings", "--no-playlist", "--skip-download", "--no-mtime",
                    "--write-subs", "--sub-langs", "live_chat", "--sub-format", "json",
                    "-o", "chat.%(ext)s", url
                }, workDir, budget.Token);
            }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested) throw;
                result.ChatPartial = true;
            }

            var candidates = Directory.EnumerateFiles(workDir, "chat*.live_chat.json*")
                .OrderByDescending(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(f => new FileInfo(f).Length)
                .ToList();
            var chatFile = candidates.FirstOrDefault();
            if (chatFile == null)
            {
                log("No live-chat replay on this video.");
                return;
            }

            const double bucket = 5;
            var counts = new Dictionary<long, int>();
            var paid = new Dictionary<long, int>();
            var total = 0;
            var paidTotal = 0;

            using var stream = new StreamReader(chatFile);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var line = await stream.ReadLineAsync(ct);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (!doc.RootElement.TryGetProperty("replayChatItemAction", out var replay)) continue;
                    if (!TryMilliseconds(replay, out var ms)) continue;

                    var textMessage = HasKey(replay, "liveChatTextMessageRenderer");
                    var paidMessage = HasKey(replay, "liveChatPaidMessageRenderer") || HasKey(replay, "liveChatPaidStickerRenderer");
                    if (!textMessage && !paidMessage) continue;

                    var key = (long)(ms / 1000 / bucket);
                    counts[key] = counts.TryGetValue(key, out var seen) ? seen + 1 : 1;
                    total++;
                    if (paidMessage)
                    {
                        paid[key] = paid.TryGetValue(key, out var cash) ? cash + 1 : 1;
                        paidTotal++;
                    }
                }
                catch { /* one broken line should not stop the count */ }
            }

            result.ChatMessages = total;
            result.ChatPaid = paidTotal;
            if (counts.Count == 0)
            {
                log("Chat replay had no messages.");
                return;
            }

            var values = counts.Values.OrderBy(v => v).ToList();
            var p90 = values[(int)Math.Min(values.Count - 1, values.Count * 0.9)];
            var loudest = values[^1];

            // "Wild" is relative: in a chat that averages 40 messages a window, 90 is a burst.
            var floor = Math.Max(4, (int)Math.Ceiling(p90 * 0.9));

            var buckets = counts
                .Where(pair => pair.Value >= floor)
                .Select(pair => new Hotspot
                {
                    Kind = "chat",
                    Start = pair.Key * bucket,
                    End = pair.Key * bucket + bucket,
                    Peak = Math.Clamp(pair.Value / (double)Math.Max(1, loudest), 0.15, 1),
                    Messages = pair.Value,
                    Superchats = paid.TryGetValue(pair.Key, out var cash) ? cash : 0
                })
                .ToList();

            result.Chat = Merge(buckets, threshold: 0.25, mergeGap: 4, max: 10);
            if (result.Chat.Count == 0)
                log($"Chat replay is flat ({total:N0} messages) — no burst to follow.");
            else
                log($"Chat replay{(result.ChatPartial ? " (partial)" : "")}: {total:N0} messages, {paidTotal:N0} paid — {result.Chat.Count} spikes.");
        }
        catch (OperationCanceledException) { throw; }
        catch { /* chat replay is optional */ }
    }

    /// <summary>Joins neighbouring hot buckets into one window and keeps the strongest ones.</summary>
    private static List<Hotspot> Merge(List<Hotspot> points, double threshold, double mergeGap, int max)
    {
        var ordered = points.Where(p => p.Peak >= threshold || p.Messages >= 6).OrderBy(p => p.Start).ToList();
        var merged = new List<Hotspot>();

        foreach (var point in ordered)
        {
            var last = merged.LastOrDefault();
            if (last != null && point.Start - last.End <= mergeGap)
            {
                last.End = Math.Max(last.End, point.End);
                last.Messages += point.Messages;
                last.Superchats += point.Superchats;
                if (point.Peak > last.Peak)
                {
                    last.Peak = point.Peak;
                    last.PeakStart = point.Start;
                }
                continue;
            }
            merged.Add(new Hotspot
            {
                Kind = point.Kind,
                Start = point.Start,
                End = point.End,
                Peak = point.Peak,
                PeakStart = point.Start,
                Messages = point.Messages,
                Superchats = point.Superchats
            });
        }

        return merged.OrderByDescending(h => h.Peak + h.Messages / 100.0).Take(max).OrderBy(h => h.Start).ToList();
    }

    private static bool TryMilliseconds(JsonElement element, out long ms)
    {
        ms = 0;
        if (element.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals("videoOffsetTimeMsec"))
            {
                var raw = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetRawText(),
                    _ => null
                };
                return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out ms);
            }
            if (property.Value.ValueKind == JsonValueKind.Object && TryMilliseconds(property.Value, out ms)) return true;
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in property.Value.EnumerateArray())
                    if (child.ValueKind == JsonValueKind.Object && TryMilliseconds(child, out ms)) return true;
            }
        }
        return false;
    }

    private static bool HasKey(JsonElement element, string key)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(key)) return true;
            if (property.Value.ValueKind == JsonValueKind.Object && HasKey(property.Value, key)) return true;
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in property.Value.EnumerateArray())
                    if (child.ValueKind == JsonValueKind.Object && HasKey(child, key)) return true;
            }
        }
        return false;
    }

    private static double Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
}
