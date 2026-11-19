using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SudoClipper;

public sealed class VideoStat
{
    public string Title { get; set; } = "";
    public string Id { get; set; } = "";
    public long Views { get; set; }
    public long Likes { get; set; }
    public long Comments { get; set; }
    public string Published { get; set; } = "";
}

public sealed class ChannelStats
{
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    public long Subscribers { get; set; }
    public long Views { get; set; }
    public long Videos { get; set; }
    public long Likes { get; set; }
    public long Comments { get; set; }
    public double EstimatedRevenue { get; set; }
    public double Views30d { get; set; }
    public string Currency { get; set; } = "USD";
    public string Note { get; set; } = "";
    public List<VideoStat> Recent { get; set; } = new();
    public string UpdatedAt { get; set; } = DateTimeOffset.Now.ToString("O");
}

public sealed class ChannelLink
{
    public string Platform { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string Scope { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public ChannelStats? Stats { get; set; }
    public string Error { get; set; } = "";

    [JsonIgnore] public bool Linked => !string.IsNullOrWhiteSpace(AccessToken);
}

public sealed record PlatformInfo(string Id, string Name, string Scope, string Note, bool Supported);

/// <summary>Channel analytics: OAuth against YouTube / TikTok, then the numbers, saved locally.</summary>
public static class Analytics
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };
    private static readonly object Gate = new();
    private static Dictionary<string, ChannelLink>? _cache;

    public static readonly List<PlatformInfo> Platforms = new()
    {
        new("youtube", "YouTube", "youtube.readonly · yt-analytics.readonly",
            "Subscribers, views, likes, top videos and estimated revenue.", true),
        new("tiktok", "TikTok", "user.info.basic · user.info.stats · video.list",
            "Followers, likes, videos and the view counts of recent posts.", true),
        new("instagram", "Instagram", "instagram_basic · instagram_manage_insights",
            "Needs a Business account through Meta. Not wired up yet.", false),
        new("x", "X (Twitter)", "tweet.read · users.read",
            "Needs a paid developer app. Not wired up yet.", false),
    };

    private static string FilePath => Path.Combine(Paths.DataDir, "analytics.json");

    private static Dictionary<string, ChannelLink> Links
    {
        get
        {
            lock (Gate)
            {
                if (_cache != null) return _cache;
                try
                {
                    if (File.Exists(FilePath))
                        _cache = JsonSerializer.Deserialize<Dictionary<string, ChannelLink>>(File.ReadAllText(FilePath), Store.Json);
                }
                catch { }
                _cache ??= new Dictionary<string, ChannelLink>(StringComparer.OrdinalIgnoreCase);
                return _cache;
            }
        }
    }

    public static void Save()
    {
        lock (Gate)
        {
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Links, Store.Json)); } catch { }
        }
    }

    public static List<ChannelLink> All()
    {
        lock (Gate)
        {
            return Platforms.Select(p => Links.TryGetValue(p.Id, out var link)
                ? link
                : new ChannelLink { Platform = p.Id }).ToList();
        }
    }

    public static ChannelLink Get(string platform)
    {
        lock (Gate)
        {
            if (Links.TryGetValue(platform, out var link)) return link;
            link = new ChannelLink { Platform = platform };
            Links[platform] = link;
            return link;
        }
    }

    public static void Remove(string platform)
    {
        lock (Gate) Links.Remove(platform);
        Save();
    }

    public static string RedirectUri(int port, string platform) => $"http://127.0.0.1:{port}/api/oauth/{platform}/callback";

    public static string AuthorizeUrl(string platform, ChannelLink link, int port, string state)
    {
        var redirect = Uri.EscapeDataString(RedirectUri(port, platform));
        return platform switch
        {
            "youtube" =>
                "https://accounts.google.com/o/oauth2/v2/auth" +
                $"?client_id={Uri.EscapeDataString(link.ClientId)}&redirect_uri={redirect}&response_type=code" +
                "&scope=https://www.googleapis.com/auth/youtube.readonly%20https://www.googleapis.com/auth/yt-analytics.readonly" +
                $"&access_type=offline&prompt=consent&include_granted_scopes=true&state={state}",
            "tiktok" =>
                "https://www.tiktok.com/v2/auth/authorize/" +
                $"?client_key={Uri.EscapeDataString(link.ClientId)}&scope=user.info.basic,user.info.stats,video.list" +
                $"&response_type=code&redirect_uri={redirect}&state={state}",
            _ => ""
        };
    }

    public static async Task<ChannelLink> ExchangeAsync(string platform, string code, int port, CancellationToken ct)
    {
        var link = Get(platform);
        if (string.IsNullOrWhiteSpace(link.ClientId)) throw new InvalidOperationException("Add the OAuth client id for this platform first.");
        var redirect = RedirectUri(port, platform);

        var form = platform == "youtube"
            ? new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = link.ClientId,
                ["client_secret"] = link.ClientSecret,
                ["redirect_uri"] = redirect,
                ["grant_type"] = "authorization_code",
            }
            : new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_key"] = link.ClientId,
                ["client_secret"] = link.ClientSecret,
                ["redirect_uri"] = redirect,
                ["grant_type"] = "authorization_code",
            };

        var endpoint = platform == "youtube"
            ? "https://oauth2.googleapis.com/token"
            : "https://open.tiktokapis.com/v2/oauth/token/";

        using var response = await Http.PostAsync(endpoint, new FormUrlEncodedContent(form), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{PlatformName(platform)} rejected the token exchange ({(int)response.StatusCode}): {Trim(body)}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        link.AccessToken = Str(root, "access_token");
        link.RefreshToken = Str(root, "refresh_token");
        var expires = Num(root, "expires_in");
        link.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expires > 0 ? expires : 3600);
        link.Scope = Str(root, "scope");
        link.Error = "";
        Save();

        return await FetchStatsAsync(link, ct);
    }

    public static async Task<ChannelLink> RefreshIfNeededAsync(ChannelLink link, CancellationToken ct)
    {
        if (!link.Linked) return link;
        if (link.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return link;
        if (string.IsNullOrWhiteSpace(link.RefreshToken)) return link;

        var form = link.Platform == "youtube"
            ? new Dictionary<string, string>
            {
                ["client_id"] = link.ClientId,
                ["client_secret"] = link.ClientSecret,
                ["refresh_token"] = link.RefreshToken,
                ["grant_type"] = "refresh_token",
            }
            : new Dictionary<string, string>
            {
                ["client_key"] = link.ClientId,
                ["client_secret"] = link.ClientSecret,
                ["refresh_token"] = link.RefreshToken,
                ["grant_type"] = "refresh_token",
            };

        var endpoint = link.Platform == "youtube"
            ? "https://oauth2.googleapis.com/token"
            : "https://open.tiktokapis.com/v2/oauth/token/";

        try
        {
            using var response = await Http.PostAsync(endpoint, new FormUrlEncodedContent(form), ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) return link;
            using var doc = JsonDocument.Parse(body);
            var token = Str(doc.RootElement, "access_token");
            if (token.Length == 0) return link;
            link.AccessToken = token;
            var fresh = Str(doc.RootElement, "refresh_token");
            if (fresh.Length > 0) link.RefreshToken = fresh;
            var expires = Num(doc.RootElement, "expires_in");
            link.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expires > 0 ? expires : 3600);
            Save();
        }
        catch { }
        return link;
    }

    public static async Task<ChannelLink> FetchStatsAsync(ChannelLink link, CancellationToken ct)
    {
        try
        {
            await RefreshIfNeededAsync(link, ct);
            var stats = link.Platform == "youtube"
                ? await FetchYouTubeAsync(link, ct)
                : await FetchTikTokAsync(link, ct);
            link.Stats = stats;
            link.Error = "";
        }
        catch (Exception ex)
        {
            link.Error = ex.Message;
        }
        Save();
        return link;
    }

    private static async Task<ChannelStats> FetchYouTubeAsync(ChannelLink link, CancellationToken ct)
    {
        var stats = new ChannelStats { Currency = "USD" };

        using var channels = await GetAsync(
            "https://www.googleapis.com/youtube/v3/channels?part=snippet,statistics,contentDetails&mine=true", link.AccessToken, ct);
        var channelRoot = channels.RootElement;
        if (!channelRoot.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
            throw new InvalidOperationException("This Google account has no YouTube channel.");

        var item = items[0];
        var snippet = item.GetProperty("snippet");
        stats.Name = Str(snippet, "title");
        stats.Handle = Str(snippet, "customUrl");
        var statistics = item.GetProperty("statistics");
        stats.Subscribers = (long)Num(statistics, "subscriberCount");
        stats.Views = (long)Num(statistics, "viewCount");
        stats.Videos = (long)Num(statistics, "videoCount");

        var uploads = "";
        if (item.TryGetProperty("contentDetails", out var details) &&
            details.TryGetProperty("relatedPlaylists", out var lists))
            uploads = Str(lists, "uploads");

        if (uploads.Length > 0)
        {
            using var playlist = await GetAsync(
                $"https://www.googleapis.com/youtube/v3/playlistItems?part=contentDetails&maxResults=8&playlistId={uploads}", link.AccessToken, ct);
            var ids = new List<string>();
            if (playlist.RootElement.TryGetProperty("items", out var vids))
                foreach (var vid in vids.EnumerateArray())
                    if (vid.TryGetProperty("contentDetails", out var cd) && cd.TryGetProperty("videoId", out var id))
                        ids.Add(id.GetString() ?? "");

            if (ids.Count > 0)
            {
                using var videos = await GetAsync(
                    $"https://www.googleapis.com/youtube/v3/videos?part=snippet,statistics&id={Uri.EscapeDataString(string.Join(",", ids))}", link.AccessToken, ct);
                if (videos.RootElement.TryGetProperty("items", out var vlist))
                    foreach (var video in vlist.EnumerateArray())
                    {
                        var vsnip = video.GetProperty("snippet");
                        var vstat = video.GetProperty("statistics");
                        stats.Recent.Add(new VideoStat
                        {
                            Id = Str(video, "id"),
                            Title = Str(vsnip, "title"),
                            Published = Str(vsnip, "publishedAt"),
                            Views = (long)Num(vstat, "viewCount"),
                            Likes = (long)Num(vstat, "likeCount"),
                            Comments = (long)Num(vstat, "commentCount"),
                        });
                    }
            }
        }

        // Revenue + last-30-day views need the YouTube Analytics API and a monetised channel.
        try
        {
            var end = DateTime.UtcNow.Date;
            var start = end.AddDays(-30);
            using var report = await GetAsync(
                "https://youtubeanalytics.googleapis.com/v2/reports?ids=channel==MINE" +
                $"&startDate={start:yyyy-MM-dd}&endDate={end:yyyy-MM-dd}&metrics=estimatedRevenue,views", link.AccessToken, ct);
            if (report.RootElement.TryGetProperty("rows", out var rows) && rows.GetArrayLength() > 0)
            {
                var row = rows[0];
                if (row.GetArrayLength() > 0) stats.EstimatedRevenue = row[0].GetDouble();
                if (row.GetArrayLength() > 1) stats.Views30d = row[1].GetDouble();
            }
        }
        catch (Exception ex)
        {
            stats.Note = "Revenue unavailable: " + ex.Message;
        }

        stats.UpdatedAt = DateTimeOffset.Now.ToString("O");
        return stats;
    }

    private static async Task<ChannelStats> FetchTikTokAsync(ChannelLink link, CancellationToken ct)
    {
        var stats = new ChannelStats();

        using var info = await GetAsync(
            "https://open.tiktokapis.com/v2/user/info/?fields=display_name,username,follower_count,likes_count,video_count", link.AccessToken, ct);
        if (!info.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("user", out var user))
            throw new InvalidOperationException("TikTok returned no user data.");

        stats.Name = Str(user, "display_name");
        stats.Handle = Str(user, "username");
        stats.Subscribers = (long)Num(user, "follower_count");
        stats.Likes = (long)Num(user, "likes_count");
        stats.Videos = (long)Num(user, "video_count");
        stats.Note = "TikTok does not expose creator income through this API.";

        try
        {
            using var videos = await PostJsonAsync(
                "https://open.tiktokapis.com/v2/video/list/?fields=id,title,view_count,like_count,comment_count,create_time", link.AccessToken, "{}", ct);
            if (videos.RootElement.TryGetProperty("data", out var vdata) && vdata.TryGetProperty("videos", out var list))
                foreach (var video in list.EnumerateArray())
                {
                    var views = Num(video, "view_count");
                    stats.Views += (long)views;
                    stats.Recent.Add(new VideoStat
                    {
                        Id = Str(video, "id"),
                        Title = Str(video, "title"),
                        Views = (long)views,
                        Likes = (long)Num(video, "like_count"),
                        Comments = (long)Num(video, "comment_count"),
                        Published = DateTimeOffset.FromUnixTimeSeconds((long)Num(video, "create_time")).ToString("O"),
                    });
                }
        }
        catch { }

        stats.UpdatedAt = DateTimeOffset.Now.ToString("O");
        return stats;
    }

    private static async Task<JsonDocument> GetAsync(string url, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {Trim(body)}");
        return JsonDocument.Parse(body);
    }

    private static async Task<JsonDocument> PostJsonAsync(string url, string token, string json, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {Trim(body)}");
        return JsonDocument.Parse(body);
    }

    public static string PlatformName(string id) =>
        Platforms.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name ?? id;

    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static double Num(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number) return value.GetDouble();
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var parsed)) return parsed;
        return 0;
    }

    private static string Trim(string body) => body.Length <= 300 ? body.Replace('\n', ' ') : body[..300].Replace('\n', ' ') + "…";
}
