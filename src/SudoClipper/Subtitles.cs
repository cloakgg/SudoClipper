using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SudoClipper;

public sealed record Cue(double Start, double End, string Text);

public static class Subtitles
{
    private static readonly Regex TagRegex = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex TimeLine = new(@"(?<a>\d{1,2}:\d{2}:\d{2}[.,]\d{1,3}|\d{1,2}:\d{2}[.,]\d{1,3})\s*-->\s*(?<b>\d{1,2}:\d{2}:\d{2}[.,]\d{1,3}|\d{1,2}:\d{2}[.,]\d{1,3})", RegexOptions.Compiled);

    public static List<Cue> ParseFile(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            var cues = Parse(text);
            if (cues.Count == 0) cues = ParseAutoCaptions(text);
            return cues;
        }
        catch { return new(); }
    }

    public static List<Cue> Parse(string content)
    {
        var cues = new List<Cue>();
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var buffer = new StringBuilder();
        double start = 0, end = 0;
        var open = false;

        void Flush()
        {
            if (!open) return;
            var text = Clean(buffer.ToString());
            buffer.Clear();
            open = false;
            if (string.IsNullOrWhiteSpace(text) || end <= start) return;
            cues.Add(new Cue(start, end, text));
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var m = TimeLine.Match(line);
            if (m.Success)
            {
                Flush();
                start = ParseTime(m.Groups["a"].Value);
                end = ParseTime(m.Groups["b"].Value);
                open = true;
                continue;
            }
            if (!open) continue;
            if (line.Length == 0) { Flush(); continue; }
            if (line.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase)) { Flush(); continue; }
            if (buffer.Length > 0) buffer.Append(' ');
            buffer.Append(line);
        }
        Flush();

        return Dedupe(cues);
    }

    /// <summary>YouTube auto-captions often repeat the previous line while a new word appears.</summary>
    private static List<Cue> Dedupe(List<Cue> cues)
    {
        var result = new List<Cue>();
        foreach (var cue in cues)
        {
            var text = cue.Text.Trim();
            if (text.Length == 0) continue;
            if (result.Count > 0)
            {
                var prev = result[^1];
                if (string.Equals(prev.Text, text, StringComparison.OrdinalIgnoreCase))
                {
                    result[^1] = prev with { End = Math.Max(prev.End, cue.End) };
                    continue;
                }
                if (prev.Text.EndsWith(text, StringComparison.OrdinalIgnoreCase) && cue.Start - prev.End < 0.6)
                {
                    result[^1] = prev with { End = Math.Max(prev.End, cue.End) };
                    continue;
                }
                if (text.StartsWith(prev.Text, StringComparison.OrdinalIgnoreCase) && cue.Start - prev.End < 0.6)
                {
                    result[^1] = new Cue(prev.Start, Math.Max(prev.End, cue.End), text);
                    continue;
                }
            }
            result.Add(cue);
        }
        return result;
    }

    private static string Clean(string s)
    {
        var text = TagRegex.Replace(s, " ");
        text = text.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&quot;", "\"").Replace("&#39;", "'")
                   .Replace("&lt;", "<").Replace("&gt;", ">").Replace(">>", " ");
        text = Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }

    private static double ParseTime(string value)
    {
        var v = value.Replace(',', '.');
        var parts = v.Split(':');
        double seconds = 0;
        foreach (var part in parts)
            seconds = seconds * 60 + double.Parse(part, CultureInfo.InvariantCulture);
        return seconds;
    }

    /// <summary>Fallback for caption formats where cue timing is embedded in text.</summary>
    private static List<Cue> ParseAutoCaptions(string content)
    {
        var cues = new List<Cue>();
        foreach (var line in content.Split('\n'))
        {
            var m = TimeLine.Match(line);
            if (m.Success)
            {
                var s = ParseTime(m.Groups["a"].Value);
                var e = ParseTime(m.Groups["b"].Value);
                cues.Add(new Cue(s, e, ""));
            }
        }
        return cues.Where(c => c.End > c.Start).ToList();
    }

    public static string TranscriptText(IEnumerable<Cue> cues)
    {
        var sb = new StringBuilder();
        foreach (var cue in cues)
            sb.Append('[').Append(Format(cue.Start)).Append("] ").Append(cue.Text).Append('\n');
        return sb.ToString();
    }

    public static string Format(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.Hours > 0
            ? $"{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes:00}:{ts.Seconds:00}";
    }

    private static string AssTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds / 10:00}";
    }

    /// <summary>
    /// One caption look. Word styles chop every cue into 2–3 word beats and animate the beat
    /// that is being spoken; static styles keep the whole cue on screen with a soft fade.
    /// </summary>
    private sealed record CaptionSpec(
        string Font,
        int Size,
        string Primary,
        string OutlineColor,
        int Outline,
        int Shadow,
        int MarginV,
        int BorderStyle,
        string Active,
        string Inactive,
        bool WordPop,
        bool WordHighlight,
        bool Uppercase,
        int WordsPerLine,
        int MaxChars,
        int Spacing,
        string Tags,
        string[]? Palette = null,
        int PopScale = 116,
        int Tilt = 0,
        bool Glow = false);

    private const string White = "&H00FFFFFF";

    private static CaptionSpec SpecFor(string? style) => (style ?? "").Trim().ToLowerInvariant() switch
    {
        "karaoke" => new("Bahnschrift SemiBold", 88, White, "&H00000000", 7, 3, 340, 1,
            White, "&H00787878", false, true, true, 3, 16, 1, "\\fad(70,60)"),
        "boxed" => new("Impact", 96, "&H00000000", White, 24, 0, 300, 3,
            "&H00000000", "&H00000000", false, false, false, 0, 22, 0, "\\fad(40,40)"),
        "clean" => new("Segoe UI Semibold", 66, White, "&H00101010", 2, 3, 300, 1,
            White, White, false, false, false, 0, 26, 0, "\\fad(150,150)"),
        // Colour styles: each word takes a colour from the palette while it is spoken.
        "neon" => new("Arial Black", 88, White, "&H00000000", 7, 2, 330, 1,
            "&H00EED322", White, true, false, true, 3, 16, 1, "\\fad(40,45)",
            new[] { "&H00EED322", "&H00FA8BA7", White }, 124, 1, true),
        "rave" => new("Bahnschrift SemiBold", 88, White, "&H00000000", 8, 3, 340, 1,
            "&H0024BFFB", White, true, false, true, 2, 14, 1, "\\fad(25,60)",
            new[] { "&H0024BFFB", "&H00EED322", "&H00FA8BA7", "&H00B672F4" }, 132, 4, true),
        "chrome" => new("Impact", 98, White, "&H00202020", 4, 6, 320, 1,
            "&H00FFE7DC", "&H00C8C8C8", false, false, false, 0, 22, 0, "\\fad(140,140)",
            new[] { "&H00FFE7DC", White, "&H00D6E3F5" }, 104, 0, true),
        "impact" => new("Impact", 96, "&H00000000", White, 24, 0, 300, 3,
            "&H00000000", "&H00000000", false, false, false, 0, 22, 0, "\\fad(40,40)"),
        "bold" => new("Arial Black", 84, White, "&H00000000", 9, 4, 330, 1,
            White, White, true, false, true, 3, 16, 1, "\\fad(60,50)", null, 124, 2),
        _ => new("Arial Black", 84, White, "&H00000000", 9, 4, 330, 1,
            White, White, true, false, true, 3, 16, 1, "\\fad(60,50)", null, 124, 2),
    };

    public static string BuildAss(IReadOnlyList<Cue> cues, double from, double to, string style,
        double fontScale = 1, int marginDelta = 0, bool? uppercase = null,
        string? font = null, double outlineScale = 1, int? wordsPerLine = null, bool? wordPop = null)
    {
        var baseSpec = SpecFor(style);
        var spec = baseSpec with
        {
            Font = string.IsNullOrWhiteSpace(font) ? baseSpec.Font : font.Trim(),
            Outline = Math.Clamp((int)Math.Round(baseSpec.Outline * Math.Clamp(outlineScale, 0.2, 2.5)), 0, 44),
            WordsPerLine = Math.Clamp(wordsPerLine ?? baseSpec.WordsPerLine, 0, 8),
            WordPop = wordPop ?? baseSpec.WordPop,
        };
        var size = Math.Clamp((int)Math.Round(spec.Size * fontScale), 24, 220);
        var margin = Math.Clamp(spec.MarginV + marginDelta, 40, 1560);
        var upper = uppercase ?? spec.Uppercase;

        var sb = new StringBuilder();
        sb.AppendLine("[Script Info]");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine("PlayResX: 1080");
        sb.AppendLine("PlayResY: 1920");
        sb.AppendLine("WrapStyle: 2");
        sb.AppendLine("ScaledBorderAndShadow: yes");
        sb.AppendLine();
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        sb.AppendLine($"Style: Clip,{spec.Font},{size},{spec.Primary},{spec.Primary},{spec.OutlineColor},&H96000000,-1,0,0,0,100,100,{spec.Spacing},0,{spec.BorderStyle},{spec.Outline},{spec.Shadow},2,70,70,{margin},1");
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        foreach (var cue in cues)
        {
            if (cue.End <= from || cue.Start >= to) continue;
            var s = Math.Max(cue.Start, from) - from;
            var e = Math.Min(cue.End, to) - from;
            if (e - s < 0.12) continue;

            var words = Words(cue.Text, upper);
            if (words.Count == 0) continue;

            if (spec.WordsPerLine <= 0 || words.Count == 1)
            {
                var text = string.Join("\\N", Lines(words, spec.MaxChars).Select(Escape));
                sb.AppendLine($"Dialogue: 0,{AssTime(s)},{AssTime(e)},Clip,,0,0,0,,{{{spec.Tags}}}{text}");
                continue;
            }

            var totalWeight = words.Sum(w => w.Length + 1.0);
            var cursor = s;
            for (var start = 0; start < words.Count; start += spec.WordsPerLine)
            {
                var group = words.GetRange(start, Math.Min(spec.WordsPerLine, words.Count - start));
                var groupWeight = group.Sum(w => w.Length + 1.0);
                var groupDuration = (e - s) * groupWeight / totalWeight;

                var wordCursor = cursor;
                for (var i = 0; i < group.Count; i++)
                {
                    var word = group[i];
                    var wordStart = wordCursor;
                    var wordEnd = i == group.Count - 1
                        ? cursor + groupDuration
                        : wordCursor + groupDuration * (word.Length + 1.0) / groupWeight;
                    if (wordEnd - wordStart < 0.06) wordEnd = wordStart + 0.06;
                    wordCursor = wordEnd;

                    var text = Compose(group, i, spec);
                    sb.AppendLine($"Dialogue: 0,{AssTime(wordStart)},{AssTime(wordEnd)},Clip,,0,0,0,,{{{spec.Tags}}}{text}");
                }
                cursor += groupDuration;
            }
        }

        return sb.ToString();
    }

    private static string Compose(IReadOnlyList<string> group, int active, CaptionSpec spec)
    {
        var sb = new StringBuilder();
        var lineLength = 0;

        for (var i = 0; i < group.Count; i++)
        {
            var word = group[i];
            if (lineLength > 0 && lineLength + 1 + word.Length > spec.MaxChars)
            {
                sb.Append("\\N");
                lineLength = 0;
            }
            else if (lineLength > 0)
            {
                sb.Append(' ');
                lineLength++;
            }

            var paletteCount = spec.Palette?.Length ?? 0;
            var paletteColor = paletteCount > 0 ? spec.Palette![i % paletteCount] : null;

            if (i == active)
            {
                sb.Append("{\\c").Append(paletteColor ?? spec.Active);
                if (spec.WordPop)
                    sb.Append($"\\t(0,80,\\fscx{spec.PopScale}\\fscy{spec.PopScale})\\t(80,230,\\fscx100\\fscy100)");
                if (spec.Tilt != 0 && spec.WordPop) sb.Append($"\\frz{(i % 2 == 0 ? spec.Tilt : -spec.Tilt)}");
                if (spec.Glow) sb.Append("\\blur2.2");
                sb.Append('}').Append(word)
                  .Append("{\\c").Append(spec.Primary).Append("\\fscx100\\fscy100\\frz0")
                  .Append(spec.Glow ? "\\blur0" : "").Append('}');
            }
            else
            {
                // Color styles keep a per-word palette even when the word is not spoken yet;
                // karaoke dims unspoken words instead.
                var inactive = paletteColor ?? spec.Inactive;
                if (spec.WordHighlight || paletteCount > 0) sb.Append("{\\c").Append(inactive).Append('}');
                sb.Append(word);
                if (spec.WordHighlight || paletteCount > 0) sb.Append("{\\c").Append(spec.Primary).Append('}');
            }
            lineLength += word.Length;
        }

        return sb.ToString();
    }

    private static List<string> Words(string text, bool uppercase)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (uppercase) words = words.Select(w => w.ToUpperInvariant()).ToList();
        return words;
    }

    private static List<string> Lines(IReadOnlyList<string> words, int maxChars)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var word in words)
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > maxChars)
            {
                lines.Add(current.ToString());
                current.Clear();
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(word);
        }
        if (current.Length > 0) lines.Add(current.ToString());
        return lines;
    }

    private static string Escape(string text) => text.Replace("{", "(").Replace("}", ")");

    public static string ToSrt(IEnumerable<Cue> cues)
    {
        var sb = new StringBuilder();
        var index = 1;
        foreach (var cue in cues)
        {
            if (string.IsNullOrWhiteSpace(cue.Text)) continue;
            sb.AppendLine(index++.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine($"{SrtTime(cue.Start)} --> {SrtTime(cue.End)}");
            sb.AppendLine(cue.Text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string SrtTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00},{ts.Milliseconds:000}";
    }
}
