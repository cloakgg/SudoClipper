export type Provider = {
  id: string;
  name: string;
  kind: "openai" | "gemini" | "local";
  baseUrl: string;
  docs: string;
  needsKey: boolean;
  defaultModel: string;
  blurb: string;
};

export type ToolState = {
  name: string;
  id: string;
  found: boolean;
  path: string;
  version: string;
  hint: string;
};

export type Status = {
  version: string;
  port: number;
  url: string;
  outDir: string;
  dataDir: string;
  bootstrapping: boolean;
  bootstrapLog: string;
  tools: ToolState[];
  providers: Provider[];
  ready: boolean;
  uiBuilt: boolean;
};

export type Config = {
  provider: string;
  model: string;
  captionStyle: string;
  captionFont: string;
  captionScale: number;
  captionPosition: number;
  captionOutline: number;
  captionWordsPerLine: number;
  captionUppercase: boolean | null;
  captionWordPop: boolean | null;
  burnCaptions: boolean;
  clipCount: number;
  minSeconds: number;
  maxSeconds: number;
  quality: string;
  whisperModel: string;
  outDir: string;
  customBaseUrl: string;
  customModel: string;
  chatProvider: string;
  chatModel: string;
  baseUrls: Record<string, string>;
  keys: { provider: string; hasKey: boolean }[];
};

export type Model = {
  id: string;
  name: string;
  context: number;
  note: string;
};

export type ModelGroup = {
  provider: string;
  providerName: string;
  kind: string;
  models: Model[];
  error: string;
};

export type Clip = {
  id: string;
  title: string;
  hook: string;
  reason: string;
  score: number;
  start: number;
  end: number;
  duration: number;
  file: string;
  thumb: string;
  sourceTitle: string;
  sourceUrl: string;
  provider: string;
  model: string;
  captioned: boolean;
  captionStyle?: string;
  sourceDuration?: number;
  editable?: boolean;
  createdAt: string;
  exists?: boolean;
};

export type JobLog = { at: string; text: string; error: boolean };

export type Job = {
  id: string;
  url: string;
  source: string;
  title: string;
  state: "queued" | "running" | "done" | "error" | "canceled";
  stage: string;
  progress: number;
  error: string;
  provider: string;
  model: string;
  log: JobLog[];
  clips: Clip[];
  followAudience?: boolean;
  audience?: string;
  createdAt: string;
};

export type ChannelVideo = {
  title: string;
  id: string;
  views: number;
  likes: number;
  comments: number;
  published: string;
};

export type ChannelStats = {
  name: string;
  handle: string;
  subscribers: number;
  views: number;
  videos: number;
  likes: number;
  comments: number;
  estimatedRevenue: number;
  views30d: number;
  currency: string;
  note: string;
  recent: ChannelVideo[];
  updatedAt: string;
};

export type AnalyticsPlatform = {
  id: string;
  name: string;
  scope: string;
  note: string;
  supported: boolean;
  linked: boolean;
  clientId: string;
  hasSecret: boolean;
  redirectUri: string;
  error: string;
  stats: ChannelStats | null;
};

export type McpInfo = {
  command: string;
  exe: string;
  claudeConfig: string;
  cursorConfig: string;
  tools: { name: string; about: string }[];
  snippet: { mcpServers: Record<string, { command: string; args: string[] }> };
  snippetExe?: { mcpServers: Record<string, { command: string; args: string[] }> };
};

export type ChatReply = {
  reply: string;
  applied: Record<string, unknown>;
  jobId?: string | null;
  provider: string;
  model: string;
};

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      ...(init?.body ? { "Content-Type": "application/json" } : {}),
      ...init?.headers,
    },
  });
  const text = await response.text();
  const data = text ? JSON.parse(text) : {};
  if (!response.ok) {
    throw new Error(data?.error ?? `Request failed (${response.status})`);
  }
  return data as T;
}

export const api = {
  status: () => request<Status>("/api/status"),
  bootstrap: () => request<{ log: string; tools: ToolState[] }>("/api/bootstrap", { method: "POST" }),
  config: () => request<Config>("/api/config"),
  saveConfig: (patch: Record<string, unknown>) =>
    request<{ saved: boolean; outDir: string }>("/api/config", {
      method: "POST",
      body: JSON.stringify(patch),
    }),
  models: (provider: string) =>
    request<{ provider: string; models: Model[] }>("/api/models", {
      method: "POST",
      body: JSON.stringify({ provider }),
    }),
  allModels: () =>
    request<{ groups: ModelGroup[]; cached: boolean }>("/api/models", {
      method: "POST",
      body: JSON.stringify({ provider: "all" }),
    }),
  createJob: (payload: { url: string; uploadId?: string }) =>
    request<Job>("/api/jobs", { method: "POST", body: JSON.stringify(payload) }),
  job: (id: string) => request<Job>(`/api/jobs/${id}`),
  cancelJob: (id: string) => request<{ canceled: boolean }>(`/api/jobs/${id}/cancel`, { method: "POST" }),
  chat: (payload: { provider: string; model: string; messages: { role: string; content: string }[] }) =>
    request<ChatReply>("/api/chat", { method: "POST", body: JSON.stringify(payload) }),
  editClip: (id: string, payload: Record<string, unknown>) =>
    request<{ clip: Clip }>(`/api/clips/${id}/edit`, { method: "POST", body: JSON.stringify(payload) }),
  clipCues: (id: string, from: number, to: number) =>
    request<{ cues: { start: number; end: number; text: string }[] }>(
      `/api/clips/${id}/cues?from=${from}&to=${to}`
    ),
  analytics: () => request<{ platforms: AnalyticsPlatform[] }>("/api/analytics"),
  analyticsCredentials: (platform: string, clientId: string, clientSecret: string) =>
    request<{ ok: boolean; redirectUri: string }>(`/api/analytics/${platform}/credentials`, {
      method: "POST",
      body: JSON.stringify({ clientId, clientSecret }),
    }),
  analyticsStart: (platform: string) =>
    request<{ url: string; redirectUri: string }>(`/api/analytics/${platform}/start`, { method: "POST" }),
  analyticsRefresh: (platform: string) =>
    request<{ linked: boolean; error: string; stats: ChannelStats | null }>(`/api/analytics/${platform}/refresh`, {
      method: "POST",
    }),
  analyticsDisconnect: (platform: string) =>
    request<{ ok: boolean }>(`/api/analytics/${platform}`, { method: "DELETE" }),
  mcp: () => request<McpInfo>("/api/mcp"),
  memory: () => request<{ clips: Clip[]; outDir: string }>("/api/memory"),
  deleteClip: (id: string) => request<{ deleted: boolean }>(`/api/memory/${id}`, { method: "DELETE" }),
  open: (path: string) => request<{ opened: boolean }>("/api/open", { method: "POST", body: JSON.stringify({ path }) }),
  upload: async (file: File) => {
    const form = new FormData();
    form.append("file", file);
    const response = await fetch("/api/upload", { method: "POST", body: form });
    const data = await response.json();
    if (!response.ok) throw new Error(data?.error ?? "Upload failed");
    return data as { id: string; name: string; size: number };
  },
};
