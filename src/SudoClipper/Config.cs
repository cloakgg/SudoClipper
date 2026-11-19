using System.Text.Json;

namespace SudoClipper;

public static class Paths
{
    public static string DataDir => EnsureDir(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
        "sudoclipper"));

    public static string BinDir => EnsureDir(Path.Combine(DataDir, "bin"));
    public static string WorkDir => EnsureDir(Path.Combine(DataDir, "work"));
    public static string ConfigFile => Path.Combine(DataDir, "config.json");
    public static string MemoryFile => Path.Combine(DataDir, "memory.json");

    public static string DefaultOutDir
    {
        get
        {
            var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrWhiteSpace(videos)) videos = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(videos, "SUDOCLIPPER");
        }
    }

    public static string EnsureDir(string path)
    {
        try { Directory.CreateDirectory(path); } catch { }
        return path;
    }
}

public sealed class Settings
{
    public string Provider { get; set; } = "local";
    public string Model { get; set; } = "sudoclipper-local";
    public string CaptionStyle { get; set; } = "punch";
    public string CaptionFont { get; set; } = "";
    public double CaptionScale { get; set; } = 1;
    public int CaptionPosition { get; set; }
    public double CaptionOutline { get; set; } = 1;
    public int CaptionWordsPerLine { get; set; } = 3;
    public bool? CaptionUppercase { get; set; }
    public bool? CaptionWordPop { get; set; }
    public bool BurnCaptions { get; set; } = true;
    public int ClipCount { get; set; } = 3;
    public int MinSeconds { get; set; } = 12;
    public int MaxSeconds { get; set; } = 55;
    public string Quality { get; set; } = "1080";
    public string WhisperModel { get; set; } = "tiny";
    public string OutDir { get; set; } = "";
    public string CustomBaseUrl { get; set; } = "";
    public string CustomModel { get; set; } = "";
    public string ChatProvider { get; set; } = "";
    public string ChatModel { get; set; } = "";
    public Dictionary<string, string> Keys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> BaseUrls { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string ResolveOutDir()
    {
        var dir = string.IsNullOrWhiteSpace(OutDir) ? Paths.DefaultOutDir : OutDir.Trim();
        try { dir = Environment.ExpandEnvironmentVariables(dir); } catch { }
        return Paths.EnsureDir(dir);
    }

    public string KeyFor(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider)) return "";
        if (Keys.TryGetValue(provider, out var k) && !string.IsNullOrWhiteSpace(k)) return k.Trim();
        var env = Environment.GetEnvironmentVariable($"SUDOCLIPPER_{provider.ToUpperInvariant()}_KEY");
        return string.IsNullOrWhiteSpace(env) ? "" : env.Trim();
    }

    public int ClampedClipCount => Math.Clamp(ClipCount, 1, 10);
    public int ClampedMin => Math.Clamp(MinSeconds, 5, 180);
    public int ClampedMax => Math.Clamp(MaxSeconds, ClampedMin + 3, 180);
}

public static class Store
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static Settings? _settings;

    public static Settings Settings
    {
        get
        {
            if (_settings != null) return _settings;
            try
            {
                if (File.Exists(Paths.ConfigFile))
                    _settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Paths.ConfigFile), Json);
            }
            catch { }
            _settings ??= new Settings();
            _settings.Keys = new Dictionary<string, string>(_settings.Keys ?? new(), StringComparer.OrdinalIgnoreCase);
            return _settings;
        }
    }

    public static void SaveSettings()
    {
        try
        {
            File.WriteAllText(Paths.ConfigFile, JsonSerializer.Serialize(Settings, Json));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Could not save config: " + ex.Message);
        }
    }

    public static List<ClipRecord> ReadMemory()
    {
        try
        {
            if (File.Exists(Paths.MemoryFile))
                return JsonSerializer.Deserialize<List<ClipRecord>>(File.ReadAllText(Paths.MemoryFile), Json) ?? new();
        }
        catch { }
        return new();
    }

    public static readonly object MemoryLock = new();

    public static void AddMemory(ClipRecord clip)
    {
        lock (MemoryLock)
        {
            var list = ReadMemory();
            list.RemoveAll(c => c.Id == clip.Id);
            list.Insert(0, clip);
            try { File.WriteAllText(Paths.MemoryFile, JsonSerializer.Serialize(list, Json)); } catch { }
        }
    }

    public static void RemoveMemory(string id)
    {
        lock (MemoryLock)
        {
            var list = ReadMemory();
            list.RemoveAll(c => c.Id == id);
            try { File.WriteAllText(Paths.MemoryFile, JsonSerializer.Serialize(list, Json)); } catch { }
        }
    }
}

public sealed class ClipRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..10];
    public string Title { get; set; } = "";
    public string Hook { get; set; } = "";
    public string Reason { get; set; } = "";
    public double Score { get; set; }
    public double Start { get; set; }
    public double End { get; set; }
    public double Duration { get; set; }
    public string File { get; set; } = "";
    public string Thumb { get; set; } = "";
    public string SourceTitle { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public bool Captioned { get; set; }
    public string CaptionStyle { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string CuesPath { get; set; } = "";
    public double SourceDuration { get; set; }
    public string CreatedAt { get; set; } = DateTimeOffset.Now.ToString("O");
}

public sealed class ProviderInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "openai"; // openai | gemini | local
    public string BaseUrl { get; set; } = "";
    public string Docs { get; set; } = "";
    public bool NeedsKey { get; set; } = true;
    public string DefaultModel { get; set; } = "";
    public string Blurb { get; set; } = "";
}

public static class Providers
{
    public static readonly List<ProviderInfo> All = new()
    {
        new() { Id = "local", Name = "Local (free)", Kind = "local", NeedsKey = false, DefaultModel = "sudoclipper-local",
                Blurb = "No API key, no account. Ranks moments from the transcript on your machine." },
        new() { Id = "openrouter", Name = "OpenRouter", Kind = "openai", BaseUrl = "https://openrouter.ai/api/v1",
                Docs = "https://openrouter.ai/keys", DefaultModel = "anthropic/claude-3.5-sonnet",
                Blurb = "One key, hundreds of models from every major lab." },
        new() { Id = "openai", Name = "OpenAI", Kind = "openai", BaseUrl = "https://api.openai.com/v1",
                Docs = "https://platform.openai.com/api-keys", DefaultModel = "gpt-4o-mini",
                Blurb = "GPT models, direct from OpenAI." },
        new() { Id = "gemini", Name = "Google Gemini", Kind = "gemini", BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
                Docs = "https://aistudio.google.com/apikey", DefaultModel = "gemini-2.5-flash",
                Blurb = "Free tier available from Google AI Studio." },
        new() { Id = "groq", Name = "Groq", Kind = "openai", BaseUrl = "https://api.groq.com/openai/v1",
                Docs = "https://console.groq.com/keys", DefaultModel = "llama-3.3-70b-versatile",
                Blurb = "Very fast open models, generous free tier." },
        new() { Id = "deepseek", Name = "DeepSeek", Kind = "openai", BaseUrl = "https://api.deepseek.com/v1",
                Docs = "https://platform.deepseek.com/api_keys", DefaultModel = "deepseek-chat",
                Blurb = "Cheap, strong reasoning." },
        new() { Id = "mistral", Name = "Mistral", Kind = "openai", BaseUrl = "https://api.mistral.ai/v1",
                Docs = "https://console.mistral.ai/api-keys", DefaultModel = "mistral-large-latest",
                Blurb = "European open-weight models." },
        new() { Id = "ollama", Name = "Ollama (local)", Kind = "openai", BaseUrl = "http://127.0.0.1:11434/v1",
                Docs = "https://ollama.com", NeedsKey = false, DefaultModel = "llama3.2",
                Blurb = "Models already running on your machine. Nothing leaves localhost." },
        new() { Id = "ollamacloud", Name = "Ollama Cloud", Kind = "openai", BaseUrl = "https://ollama.com/v1",
                Docs = "https://ollama.com/settings/keys", DefaultModel = "gpt-oss:120b",
                Blurb = "Ollama's hosted models — same API as local Ollama, no GPU required." },
        new() { Id = "meta", Name = "Meta Llama API", Kind = "openai", BaseUrl = "https://api.llama.com/v1",
                Docs = "https://llama.developer.meta.com/", DefaultModel = "Llama-3.3-70B-Instruct",
                Blurb = "Meta's own Llama endpoint." },
        new() { Id = "aionlabs", Name = "Aion Labs", Kind = "openai", BaseUrl = "https://api.aionlabs.ai/v1",
                Docs = "https://aionlabs.ai", DefaultModel = "aion-labs/aion-1.0-mini",
                Blurb = "Permanent free tier, no card: 15 req/min, 20K tokens a day. Roleplay and storytelling." },
        new() { Id = "axforge", Name = "AxForge", Kind = "openai", BaseUrl = "https://api.axforge.ai/v1",
                Docs = "https://axforge.ai", DefaultModel = "Llama-3.3-70B-Instruct",
                Blurb = "Permanent free tier: 3M tokens every 30 days on EU-hosted open models." },
        new() { Id = "kilo", Name = "Kilo Code", Kind = "openai", BaseUrl = "https://api.kilo.ai/api/gateway",
                Docs = "https://kilocode.ai", NeedsKey = false, DefaultModel = "kilo/auto",
                Blurb = "Free auto-router — no API key or card needed." },
        new() { Id = "sambanova", Name = "SambaNova", Kind = "openai", BaseUrl = "https://api.sambanova.ai/v1",
                Docs = "https://cloud.sambanova.ai/apis", DefaultModel = "Meta-Llama-3.3-70B-Instruct",
                Blurb = "Fast inference with a free tier." },
        new() { Id = "hyperbolic", Name = "Hyperbolic", Kind = "openai", BaseUrl = "https://api.hyperbolic.xyz/v1",
                Docs = "https://app.hyperbolic.xyz/settings", DefaultModel = "meta-llama/Llama-3.3-70B-Instruct",
                Blurb = "Open-source model hosting with a free tier." },
        new() { Id = "novita", Name = "Novita AI", Kind = "openai", BaseUrl = "https://api.novita.ai/v3/openai",
                Docs = "https://novita.ai/settings/key-management", DefaultModel = "meta-llama/llama-3.3-70b-instruct",
                Blurb = "LLM and image APIs with $0.50 of free starting credit." },
        new() { Id = "zai", Name = "Z.ai (GLM)", Kind = "openai", BaseUrl = "https://api.z.ai/api/paas/v4",
                Docs = "https://z.ai/manage-apikey/apikey-list", DefaultModel = "glm-4.7-flash",
                Blurb = "Free access to GLM flash models like 4.7-Flash and 4.6V-Flash." },
        new() { Id = "llm7", Name = "LLM7.io", Kind = "openai", BaseUrl = "https://api.llm7.io/v1",
                Docs = "https://llm7.io", DefaultModel = "gpt-4o-mini",
                Blurb = "Free OpenAI-compatible gateway for a set of open models." },
        new() { Id = "nscale", Name = "Nscale", Kind = "openai", BaseUrl = "https://inference.api.nscale.com/v1",
                Docs = "https://console.nscale.com", DefaultModel = "meta-llama/Llama-3.3-70B-Instruct",
                Blurb = "Open-weight models on European cloud infrastructure, free tier." },
        new() { Id = "nebius", Name = "Nebius AI Studio", Kind = "openai", BaseUrl = "https://api.studio.nebius.ai/v1",
                Docs = "https://studio.nebius.ai", DefaultModel = "meta-llama/Llama-3.3-70B-Instruct",
                Blurb = "Trial credits for Llama, Qwen, DeepSeek and friends." },
        new() { Id = "ovh", Name = "OVHcloud AI Endpoints", Kind = "openai", BaseUrl = "https://oai.endpoints.kepler.ai.cloud.ovh.net/v1",
                Docs = "https://endpoints.ai.cloud.ovh.net", DefaultModel = "Meta-Llama-3_3-70B-Instruct",
                Blurb = "Free prototyping tier for serverless LLMs hosted in Europe." },
        new() { Id = "siliconflow", Name = "SiliconFlow", Kind = "openai", BaseUrl = "https://api.siliconflow.cn/v1",
                Docs = "https://cloud.siliconflow.cn/account/ak", DefaultModel = "Qwen/Qwen2.5-7B-Instruct",
                Blurb = "Generous free tier with permanent access to select light models." },
        new() { Id = "deepinfra", Name = "DeepInfra", Kind = "openai", BaseUrl = "https://api.deepinfra.com/v1/openai",
                Docs = "https://deepinfra.com/dash/api_keys", DefaultModel = "meta-llama/Llama-3.3-70B-Instruct",
                Blurb = "Free signup credits, no card, dozens of open models." },
        new() { Id = "fireworks", Name = "Fireworks AI", Kind = "openai", BaseUrl = "https://api.fireworks.ai/inference/v1",
                Docs = "https://fireworks.ai/account/api-keys", DefaultModel = "accounts/fireworks/models/llama-v3p3-70b-instruct",
                Blurb = "$1 free starter credit for very fast open-model inference." },
        new() { Id = "together", Name = "Together AI", Kind = "openai", BaseUrl = "https://api.together.xyz/v1",
                Docs = "https://api.together.ai/settings/api-keys", DefaultModel = "meta-llama/Llama-3.3-70B-Instruct-Turbo",
                Blurb = "$5 free trial credit across 100+ open models." },
        new() { Id = "ai21", Name = "AI21 Labs", Kind = "openai", BaseUrl = "https://api.ai21.com/studio/v1",
                Docs = "https://studio.ai21.com/account/api-key", DefaultModel = "jamba-large-1.7",
                Blurb = "Jamba hybrid SSM-Transformer models, free trial credits." },
        new() { Id = "alibaba", Name = "Alibaba Cloud (Qwen)", Kind = "openai", BaseUrl = "https://dashscope-intl.aliyun.com/compatible-mode/v1",
                Docs = "https://bailian.console.alibabacloud.com", DefaultModel = "qwen-plus",
                Blurb = "Model Studio gives new accounts a large free token quota for Qwen." },
        new() { Id = "modelscope", Name = "ModelScope", Kind = "openai", BaseUrl = "https://api-inference.modelscope.cn/v1",
                Docs = "https://modelscope.cn/my/myaccesstoken", DefaultModel = "Qwen/Qwen2.5-72B-Instruct",
                Blurb = "Alibaba's model community: free inference for hosted Qwen models." },
        new() { Id = "cohere", Name = "Cohere", Kind = "openai", BaseUrl = "https://api.cohere.ai/compatibility/v1",
                Docs = "https://dashboard.cohere.com/api-keys", DefaultModel = "command-r-plus-08-2024",
                Blurb = "Permanent free trial key: 20 req/min, 1,000 calls a month." },
        new() { Id = "aimlapi", Name = "AIML API", Kind = "openai", BaseUrl = "https://api.aimlapi.com/v1",
                Docs = "https://aimlapi.com/app/keys", DefaultModel = "gpt-4o-mini",
                Blurb = "Unified gateway to 200+ models with a free starter tier." },
        new() { Id = "nvidia", Name = "NVIDIA NIM", Kind = "openai", BaseUrl = "https://integrate.api.nvidia.com/v1",
                Docs = "https://build.nvidia.com", DefaultModel = "meta/llama-3.3-70b-instruct",
                Blurb = "Free prototyping keys for 120+ hosted models (~40 req/min)." },
        new() { Id = "puter", Name = "Puter.js", Kind = "openai", BaseUrl = "https://api.puter.com/puterai/openai/v1",
                Docs = "https://developer.puter.com", NeedsKey = false, DefaultModel = "gpt-4o-mini",
                Blurb = "Puter's free browser AI. Most reliable through its JS SDK; this entry tries the HTTP endpoint." },
        new() { Id = "pollinations", Name = "Pollinations.ai", Kind = "openai", BaseUrl = "https://text.pollinations.ai/openai",
                Docs = "https://pollinations.ai", NeedsKey = false, DefaultModel = "openai",
                Blurb = "Free, unauthenticated text and image endpoints." },
        new() { Id = "nine", Name = "9Router", Kind = "openai", BaseUrl = "http://127.0.0.1:20128/v1",
                Docs = "https://9router.com", DefaultModel = "auto",
                Blurb = "Self-hosted router: one OpenAI endpoint in front of 60+ providers, with fallbacks." },
        new() { Id = "netlify", Name = "Netlify AI Gateway", Kind = "openai", BaseUrl = "",
                Docs = "https://docs.netlify.com/build/ai-gateway/overview/", DefaultModel = "gpt-5.4-mini",
                Blurb = "No provider keys: paste your site's gateway URL (NETLIFY_AI_GATEWAY_URL / OPENAI_BASE_URL) and gateway key. Serves OpenAI, Gemini, Claude and OpenRouter models." },
        new() { Id = "custom", Name = "Custom endpoint", Kind = "openai", BaseUrl = "",
                Docs = "", DefaultModel = "", Blurb = "Any OpenAI-compatible API: LM Studio, vLLM, LiteLLM, Together…" },
    };

    public static ProviderInfo Find(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
