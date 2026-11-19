using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SudoClipper;

public sealed class ModelInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Context { get; set; }
    public string Note { get; set; } = "";
}

public sealed record ChatTurn(string Role, string Content);

public sealed class ProviderModels
{
    public string Provider { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public string Kind { get; set; } = "";
    public List<ModelInfo> Models { get; set; } = new();
    public string Error { get; set; } = "";
}

public sealed class ClipIdea
{
    public string Title { get; set; } = "";
    public string Hook { get; set; } = "";
    public string Reason { get; set; } = "";
    public double Start { get; set; }
    public double End { get; set; }
    public double Score { get; set; }
}

public static class Llm
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(180) };

    private static string BaseUrl(ProviderInfo provider, Settings settings)
    {
        if (provider.Id == "custom") return (settings.CustomBaseUrl ?? "").Trim().TrimEnd('/');
        if (settings.BaseUrls != null && settings.BaseUrls.TryGetValue(provider.Id, out var custom) && !string.IsNullOrWhiteSpace(custom))
            return custom.Trim().TrimEnd('/');
        return provider.BaseUrl.TrimEnd('/');
    }

    /// <summary>Every provider that can answer right now, with its model list. Failures stay isolated.</summary>
    public static async Task<List<ProviderModels>> ListAllModelsAsync(Settings settings, CancellationToken ct)
    {
        // Only providers you have actually connected: a saved key, a local server, or the
        // one currently selected. Keyless remote gateways stay hidden until they are picked.
        var usable = Providers.All
            .Where(p => p.Kind == "local"
                        || !string.IsNullOrWhiteSpace(settings.KeyFor(p.Id))
                        || p.Id is "ollama" or "nine"
                        || string.Equals(p.Id, settings.Provider, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var tasks = usable.Select(async provider =>
        {
            var group = new ProviderModels { Provider = provider.Id, ProviderName = provider.Name, Kind = provider.Kind };
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(8));
                group.Models = await ListModelsAsync(provider, settings, cts.Token);
            }
            catch (Exception ex) { group.Error = ex.Message; }
            return group;
        });

        return (await Task.WhenAll(tasks))
            .Where(g => g.Models.Count > 0 || g.Error.Length > 0)
            .ToList();
    }

    public static async Task<List<ModelInfo>> ListModelsAsync(ProviderInfo provider, Settings settings, CancellationToken ct)
    {
        if (provider.Kind == "local")
            return new List<ModelInfo>
            {
                new() { Id = "sudoclipper-local", Name = "SUDOCLIPPER Local Selector", Note = "Free · no key · runs on your machine" }
            };

        var key = settings.KeyFor(provider.Id);
        var baseUrl = BaseUrl(provider, settings);
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Set the endpoint URL for this provider first.");

        using var request = new HttpRequestMessage(HttpMethod.Get,
            provider.Kind == "gemini"
                ? $"{baseUrl}/models?key={Uri.EscapeDataString(key)}&pageSize=200"
                : $"{baseUrl}/models");

        if (provider.Kind != "gemini" && !string.IsNullOrWhiteSpace(key))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(Describe(response, body, provider.Name));

        var models = new List<ModelInfo>();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                var id = Str(item, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                var model = new ModelInfo { Id = id, Name = Str(item, "name") is { Length: > 0 } n ? n : id };
                if (item.TryGetProperty("context_length", out var cl) && cl.ValueKind == JsonValueKind.Number) model.Context = cl.GetInt32();
                if (item.TryGetProperty("pricing", out var pricing) && pricing.ValueKind == JsonValueKind.Object)
                {
                    var prompt = Str(pricing, "prompt");
                    if (prompt == "0" || prompt == "0.0") model.Note = "free";
                }
                models.Add(model);
            }
        }
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("models", out var gem) && gem.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in gem.EnumerateArray())
            {
                var raw = Str(item, "name");
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var id = raw.StartsWith("models/") ? raw[7..] : raw;
                if (item.TryGetProperty("supportedGenerationMethods", out var methods) &&
                    !methods.EnumerateArray().Any(m => m.GetString() == "generateContent"))
                    continue;
                var model = new ModelInfo { Id = id, Name = Str(item, "displayName") is { Length: > 0 } d ? d : id };
                if (item.TryGetProperty("inputTokenLimit", out var limit) && limit.ValueKind == JsonValueKind.Number)
                    model.Context = limit.GetInt32();
                models.Add(model);
            }
        }

        // Only models that can answer a chat: an embeddings, whisper, tts or image model in the
        // picker is a model that fails the moment you use it.
        return models
            .GroupBy(m => m.Id)
            .Select(g => g.First())
            .Where(m => IsChatModel(m.Id))
            .OrderBy(m => m.Id)
            .ToList();
    }

    private static readonly string[] NonChatFragments =
    {
        "embed", "whisper", "tts", "dall-e", "davinci", "babbage", "moderation", "transcribe",
        "audio", "realtime", "-image", "image-", "sora", "rerank", "guard", "ocr", "-vision-preview"
    };

    private static bool IsChatModel(string id)
    {
        var lower = (id ?? "").ToLowerInvariant();
        return lower.Length > 0 && !NonChatFragments.Any(fragment => lower.Contains(fragment));
    }

    public static Task<string> ChatAsync(ProviderInfo provider, Settings settings, string model, string system, string user, CancellationToken ct) =>
        ChatMessagesAsync(provider, settings, model, system, new List<ChatTurn> { new("user", user) }, true, ct);

    public static async Task<string> ChatMessagesAsync(
        ProviderInfo provider, Settings settings, string model, string system,
        IReadOnlyList<ChatTurn> turns, bool json, CancellationToken ct)
    {
        var key = settings.KeyFor(provider.Id);
        var baseUrl = BaseUrl(provider, settings);
        if (provider.Kind != "local" && provider.NeedsKey && string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException($"{provider.Name} needs an API key. Add one in Config.");

        if (provider.Kind == "gemini")
        {
            var url = $"{baseUrl}/models/{model}:generateContent?key={Uri.EscapeDataString(key)}";
            var payload = new
            {
                systemInstruction = new { parts = new[] { new { text = system } } },
                contents = turns.Select(t => new
                {
                    role = t.Role == "assistant" ? "model" : "user",
                    parts = new[] { new { text = t.Content } }
                }).ToArray(),
                generationConfig = json
                    ? (object)new { temperature = 0.6, maxOutputTokens = 8192, responseMimeType = "application/json" }
                    : new { temperature = 0.7, maxOutputTokens = 8192 }
            };
            using var response = await Http.PostAsync(url,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"), ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(Describe(response, body, "Gemini"));
            using var doc = JsonDocument.Parse(body);
            var sb = new StringBuilder();
            if (doc.RootElement.TryGetProperty("candidates", out var cands) && cands.ValueKind == JsonValueKind.Array)
                foreach (var c in cands.EnumerateArray())
                    if (c.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
                        foreach (var p in parts.EnumerateArray())
                            if (p.TryGetProperty("text", out var t)) sb.Append(t.GetString());
            return sb.ToString();
        }

        var messages = new List<object> { new { role = "system", content = system } };
        foreach (var turn in turns)
            messages.Add(new { role = turn.Role == "assistant" ? "assistant" : "user", content = turn.Content });

        var requestBody = new
        {
            model,
            temperature = 0.6,
            max_tokens = 4096,
            messages
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(key))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (provider.Id == "openrouter")
        {
            req.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/sudoclipper");
            req.Headers.TryAddWithoutValidation("X-Title", "SUDOCLIPPER");
        }

        using var res = await Http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException(Describe(res, text, provider.Name));

        using var parsed = JsonDocument.Parse(text);
        if (parsed.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                return content.ValueKind == JsonValueKind.String ? content.GetString() ?? "" : content.ToString();
            if (first.TryGetProperty("text", out var plain)) return plain.GetString() ?? "";
        }
        return "";
    }

    private static string Shorten(string s) => s.Length <= 400 ? s : s[..400] + "…";

    /// <summary>Turns provider HTTP failures into something a person can act on.</summary>
    private static string Describe(HttpResponseMessage response, string body, string providerName)
    {
        var code = (int)response.StatusCode;
        return code switch
        {
            429 => $"{providerName} is rate limiting this key (429). Wait a minute, pick another model, or switch to the free local selector.",
            401 or 403 => $"{providerName} rejected the API key ({(int)response.StatusCode}). Check it in Config.",
            402 => $"{providerName} says this key has no credit left.",
            _ => $"{providerName} {(int)response.StatusCode}: {Shorten(body)}"
        };
    }

    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    /* ───────────────────────── selection ───────────────────────── */

    public static async Task<List<ClipIdea>> PickAsync(
        IReadOnlyList<Cue> cues, Settings settings, double duration, CancellationToken ct,
        Engagement? engagement = null)
    {
        var provider = Providers.Find(settings.Provider);
        var min = settings.ClampedMin;
        var max = settings.ClampedMax;
        var count = settings.ClampedClipCount;

        List<ClipIdea> ideas;
        if (provider.Kind == "local")
        {
            ideas = LocalSelector.Pick(cues, count, min, max, duration, engagement);
        }
        else
        {
            var model = settings.Provider == "custom" && !string.IsNullOrWhiteSpace(settings.CustomModel)
                ? settings.CustomModel
                : string.IsNullOrWhiteSpace(settings.Model) ? provider.DefaultModel : settings.Model;
            ideas = await PickWithModelAsync(provider, settings, model, cues, count, min, max, duration, ct, engagement);
        }

        // Anchors: any hotspot the answer did not cover gets a candidate window of its own, so a
        // chat explosion is never left out because the model preferred a tidy sentence elsewhere.
        if (engagement is { Any: true })
        {
            var anchors = LocalSelector.Pick(cues, count + 2, min, max, duration, engagement);
            foreach (var anchor in anchors)
            {
                if (ideas.Any(idea => Overlap(idea, anchor) > 2)) continue;
                anchor.Score = Math.Clamp(0.55 + 0.45 * engagement.Boost(anchor.Start, anchor.End), 0, 1);
                anchor.Reason = string.IsNullOrWhiteSpace(anchor.Reason) ? anchor.Reason : anchor.Reason;
                ideas.Add(anchor);
            }
        }

        return Sanitize(ideas, count, min, max, duration);
    }

    private static double Overlap(ClipIdea a, ClipIdea b) =>
        Math.Min(a.End, b.End) - Math.Max(a.Start, b.Start);

    private static async Task<List<ClipIdea>> PickWithModelAsync(
        ProviderInfo provider, Settings settings, string model, IReadOnlyList<Cue> cues,
        int count, int min, int max, double duration, CancellationToken ct, Engagement? engagement)
    {
        const string system =
            "You are a short-form video editor who cuts YouTube videos into viral 9:16 clips. " +
            "You find self-contained moments with a strong hook in the first seconds, tension, a payoff, and no need for outside context. " +
            "You always answer with JSON only.";

        var transcript = Subtitles.TranscriptText(cues);
        var chunks = Chunk(transcript, 24000);
        var perChunk = Math.Max(1, (int)Math.Ceiling(count / (double)chunks.Count) + 1);
        var all = new List<ClipIdea>();

        for (var i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var user = $$"""
                Video length: {{Math.Round(duration)}} seconds.
                Find the {{perChunk}} best clips in this part of the transcript (part {{i + 1}} of {{chunks.Count}}).
                Each clip must be between {{min}} and {{max}} seconds long, must start and end on sentence boundaries,
                and must work with zero context. Prefer moments that make a viewer stop scrolling.

                Audience signals — where the live chat exploded and where viewers replayed the video.
                These are the parts people actually reacted to; weight them heavily and cover at least
                half of your clips with one of them:
                {{(engagement == null ? "none" : engagement.ToPrompt())}}

                Transcript with timestamps in [mm:ss]:
                {{chunks[i]}}

                Reply with JSON exactly like:
                {"clips":[{"title":"short punchy title","hook":"the opening line of the clip","start":12.5,"end":44.0,"score":0.87,"reason":"why it can go viral"}]}
                """;

            var raw = await ChatAsync(provider, settings, model, system, user, ct);
            all.AddRange(ParseIdeas(raw));
        }

        if (engagement is { Any: true })
            foreach (var idea in all)
            {
                var heat = engagement.Boost(idea.Start, idea.End);
                if (heat <= 0) continue;
                idea.Score = Math.Min(1, idea.Score + heat * 0.45);
                idea.Reason = string.IsNullOrWhiteSpace(idea.Reason)
                    ? "Audience reacted here."
                    : idea.Reason;
            }

        return all.OrderByDescending(i => i.Score).ToList();
    }

    private static List<string> Chunk(string text, int size)
    {
        if (text.Length <= size) return new List<string> { text };
        var chunks = new List<string>();
        var lines = text.Split('\n');
        var current = new StringBuilder();
        foreach (var line in lines)
        {
            if (current.Length + line.Length + 1 > size && current.Length > 0)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }
            current.AppendLine(line);
        }
        if (current.Length > 0) chunks.Add(current.ToString());
        return chunks;
    }

    public static List<ClipIdea> ParseIdeas(string raw)
    {
        var ideas = new List<ClipIdea>();
        if (string.IsNullOrWhiteSpace(raw)) return ideas;

        var text = Regex.Replace(raw, "```(?:json)?", "");
        var arrayStart = text.IndexOf('[');
        var objectStart = text.IndexOf('{');
        var slice = "";
        if (arrayStart >= 0 && (objectStart < 0 || arrayStart < objectStart))
        {
            var end = text.LastIndexOf(']');
            if (end > arrayStart) slice = text[arrayStart..(end + 1)];
        }
        else if (objectStart >= 0)
        {
            var end = text.LastIndexOf('}');
            if (end > objectStart) slice = text[objectStart..(end + 1)];
        }

        if (slice.Length == 0) return ideas;

        try
        {
            using var doc = JsonDocument.Parse(slice);
            var root = doc.RootElement;
            JsonElement list = root;
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "clips", "moments", "results", "data" })
                {
                    if (root.TryGetProperty(key, out var found) && found.ValueKind == JsonValueKind.Array) { list = found; break; }
                }
                if (list.ValueKind != JsonValueKind.Array) return ideas;
            }
            if (list.ValueKind != JsonValueKind.Array) return ideas;

            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var idea = new ClipIdea
                {
                    Title = FirstString(item, "title", "name", "label"),
                    Hook = FirstString(item, "hook", "opening", "quote"),
                    Reason = FirstString(item, "reason", "why", "explanation"),
                    Start = FirstNumber(item, "start", "start_time", "startTime", "from", "start_seconds"),
                    End = FirstNumber(item, "end", "end_time", "endTime", "to", "end_seconds"),
                    Score = FirstNumber(item, "score", "virality", "rating", "confidence")
                };
                if (idea.End > idea.Start) ideas.Add(idea);
            }
        }
        catch { }

        return ideas;
    }

    private static string FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return (value.GetString() ?? "").Trim();
        return "";
    }

    private static double FirstNumber(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number) return value.GetDouble();
            if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var parsed)) return parsed;
        }
        return 0;
    }

    private static List<ClipIdea> Sanitize(List<ClipIdea> ideas, int count, int min, int max, double duration)
    {
        var output = new List<ClipIdea>();
        var limit = duration > 0 ? duration : double.MaxValue;

        foreach (var idea in ideas.OrderByDescending(i => i.Score))
        {
            var start = Math.Max(0, idea.Start);
            var end = Math.Min(limit, idea.End);
            if (end - start < 4) continue;

            if (end - start > max) end = start + max;
            if (end - start < min) end = Math.Min(start + min, limit);
            if (end - start < 4) continue;

            if (output.Any(o => Math.Min(o.End, end) - Math.Max(o.Start, start) > 3)) continue;

            output.Add(new ClipIdea
            {
                Title = string.IsNullOrWhiteSpace(idea.Title) ? $"Clip {output.Count + 1}" : idea.Title,
                Hook = idea.Hook,
                Reason = idea.Reason,
                Start = Math.Round(start, 2),
                End = Math.Round(end, 2),
                Score = idea.Score <= 0 ? 0.5 : Math.Round(Math.Min(1, idea.Score), 2)
            });

            if (output.Count >= count) break;
        }

        return output;
    }
}

/// <summary>Zero-dependency moment picker: scores transcript windows for hook-shaped language.</summary>
public static class LocalSelector
{
    private static readonly string[] StrongMarkers =
    {
        "the truth", "nobody tells you", "nobody talks about", "here's why", "here is why", "the secret",
        "what nobody", "mistake", "biggest", "never do", "stop doing", "most people", "the problem is",
        "the reason", "actually", "surprising", "shocking", "insane", "crazy", "wild", "listen",
        "let me tell you", "i learned", "i realized", "the key", "the trick", "hack", "instantly",
        "guarantee", "never again", "changed my life", "wasted", "regret", "warning", "truth is",
        "you should", "you need to", "do this", "don't do", "million", "thousand", "percent", "dollars",
        "first time", "worst", "best", "hardest", "secret sauce", "if you want"
    };

    public static List<ClipIdea> Pick(
        IReadOnlyList<Cue> cues, int count, int min, int max, double duration, Engagement? engagement = null)
    {
        var ideas = new List<ClipIdea>();
        if (cues.Count == 0) return ideas;

        var target = Math.Clamp(min + 10, min, max);

        for (var i = 0; i < cues.Count; i++)
        {
            var start = cues[i].Start;
            var end = start;
            var j = i;
            while (j < cues.Count && end - start < target) { end = cues[j].End; j++; }
            if (end - start < min) end = Math.Min(start + min, Math.Max(duration, cues[^1].End));
            if (end - start < min * 0.7) continue;

            var text = string.Join(" ", cues.Skip(i).Take(j - i).Select(c => c.Text));
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (words < 18) continue;

            var score = 0.0;
            var lower = text.ToLowerInvariant();
            foreach (var marker in StrongMarkers)
            {
                var index = 0;
                while ((index = lower.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
                {
                    score += marker.Length > 8 ? 1.6 : 1.0;
                    index += marker.Length;
                }
            }
            score += Math.Min(6, Regex.Matches(text, @"\?").Count) * 0.8;
            score += Math.Min(4, Regex.Matches(text, @"!").Count) * 0.4;
            score += Math.Min(10, Regex.Matches(lower, @"\b(i|you|we)\b").Count) * 0.25;
            score += Math.Min(6, Regex.Matches(text, @"\b\d+([.,]\d+)?\b").Count) * 0.5;
            score += Math.Min(4, Regex.Matches(lower, @"\b(but|however|because|so|then|until)\b").Count) * 0.3;

            // Sentence boundary bonus and juicy opening line.
            if (Regex.IsMatch(cues[i].Text, @"^(so|and|but|look|listen|here|now|when|the|i|you|what|why|how)\b", RegexOptions.IgnoreCase))
                score += 0.8;

            var seconds = Math.Max(1, end - start);
            var density = words / seconds;
            score += Math.Min(3, density * 1.8);
            if (start < 12) score -= 1.2;
            if (duration > 30 && end > duration - 8) score -= 1.2;
            score += (target - Math.Abs((end - start) - target)) * 0.02;

            // Chat spikes and replayed passages outrank wordy prose.
            var heat = engagement?.Boost(start, end) ?? 0;
            if (heat > 0) score += heat * 9;

            var sentences = Regex.Split(text, @"(?<=[.!?])\s+");
            var hook = sentences.OrderByDescending(s => s.Length).FirstOrDefault() ?? text;
            if (hook.Length > 150) hook = hook[..150];

            ideas.Add(new ClipIdea
            {
                Title = Title(text),
                Hook = hook.Trim(),
                Reason = heat > 0
                    ? "Local scoring: the audience reacted here (chat/replay) plus hook language and pacing."
                    : "Local scoring: hook language, question density, numbers and pacing.",
                Start = Math.Round(start, 2),
                End = Math.Round(end, 2),
                Score = Math.Round(score, 2)
            });
        }

        var picked = new List<ClipIdea>();
        foreach (var idea in ideas.OrderByDescending(i => i.Score))
        {
            if (picked.Any(p => Math.Min(p.End, idea.End) - Math.Max(p.Start, idea.Start) > 2)) continue;
            picked.Add(idea);
            if (picked.Count >= count) break;
        }
        picked.Sort((a, b) => a.Start.CompareTo(b.Start));
        return picked;
    }

    private static string Title(string text)
    {
        var words = Regex.Replace(text, @"\[[^\]]*\]", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var title = string.Join(" ", words.Take(9)).TrimEnd(',', '.', '?', '!', ':', ';');
        if (title.Length > 60) title = title[..60].TrimEnd() + "…";
        return string.IsNullOrWhiteSpace(title) ? "Untitled moment" : char.ToUpperInvariant(title[0]) + title[1..];
    }
}
