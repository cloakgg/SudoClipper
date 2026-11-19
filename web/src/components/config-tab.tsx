import { AnalyticsSection } from "@/components/analytics";
import { CAPTION_STYLES, CaptionPhone, draftFromConfig } from "@/components/caption-studio";
import { McpSection } from "@/components/mcp-card";
import { SplitText } from "@/components/ui/split-text";
import { ProviderIcon } from "@/components/provider-icons";
import { Card, Field, Pill, SectionLabel, Segmented, Toggle, inputClass } from "@/components/ui/bits";
import { api, type Config, type Model, type Status } from "@/lib/api";
import { cn } from "@/lib/utils";
import {
  IconAlertTriangle,
  IconCheck,
  IconCircleCheck,
  IconExternalLink,
  IconEye,
  IconEyeOff,
  IconFolderOpen,
  IconKey,
  IconLoader2,
  IconRefresh,
  IconSearch,
  IconTerminal2,
} from "@tabler/icons-react";
import { motion } from "motion/react";
import * as React from "react";

type Draft = Omit<Config, "keys">;

export function ConfigTab({
  status,
  config,
  onSaved,
  onStatusRefresh,
  notify,
}: {
  status: Status | null;
  config: Config | null;
  onSaved: () => Promise<void>;
  onStatusRefresh: () => Promise<void>;
  notify: (message: string, tone?: "good" | "bad") => void;
}) {
  const [draft, setDraft] = React.useState<Draft | null>(null);
  const [keys, setKeys] = React.useState<Record<string, string>>({});
  const [reveal, setReveal] = React.useState<Record<string, boolean>>({});
  const [models, setModels] = React.useState<Model[]>([]);
  const [modelQuery, setModelQuery] = React.useState("");
  const [modelsLoading, setModelsLoading] = React.useState(false);
  const [modelsError, setModelsError] = React.useState("");
  const [saving, setSaving] = React.useState(false);
  const [setting, setSetting] = React.useState(false);

  React.useEffect(() => {
    if (config && !draft) setDraft({ ...config });
  }, [config, draft]);

  const provider = status?.providers.find((p) => p.id === draft?.provider);
  const dirty = React.useMemo(() => {
    if (!config || !draft) return false;
    const { keys: _storedKeys, ...rest } = config;
    return JSON.stringify(draft) !== JSON.stringify(rest) || Object.values(keys).some((value) => value.trim().length > 0);
  }, [config, draft, keys]);

  const loadModels = React.useCallback(
    async (providerId?: string) => {
      if (!draft) return;
      setModelsLoading(true);
      setModelsError("");
      try {
        const result = await api.models(providerId ?? draft.provider);
        setModels(result.models);
      } catch (error) {
        setModels([]);
        setModelsError(error instanceof Error ? error.message : "Could not list models");
      } finally {
        setModelsLoading(false);
      }
    },
    [draft]
  );

  React.useEffect(() => {
    if (!draft) return;
    setModels([]);
    void loadModels(draft.provider);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [draft?.provider]);

  if (!draft || !config) {
    return (
      <div className="grid place-items-center py-24 text-neutral-600">
        <IconLoader2 size={22} className="animate-spin" />
      </div>
    );
  }

  const patch = (values: Partial<Draft>) => setDraft((prev) => (prev ? { ...prev, ...values } : prev));

  async function save() {
    setSaving(true);
    try {
      const payload: Record<string, unknown> = {
        provider: draft!.provider,
        model: draft!.model,
        captionStyle: draft!.captionStyle,
        captionFont: draft!.captionFont,
        captionScale: draft!.captionScale,
        captionPosition: draft!.captionPosition,
        captionOutline: draft!.captionOutline,
        captionWordsPerLine: draft!.captionWordsPerLine,
        captionUppercase: draft!.captionUppercase,
        captionWordPop: draft!.captionWordPop,
        burnCaptions: draft!.burnCaptions,
        clipCount: draft!.clipCount,
        minSeconds: draft!.minSeconds,
        maxSeconds: draft!.maxSeconds,
        quality: draft!.quality,
        whisperModel: draft!.whisperModel,
        outDir: draft!.outDir,
        customBaseUrl: draft!.customBaseUrl,
        customModel: draft!.customModel,
        chatProvider: draft!.chatProvider,
        chatModel: draft!.chatModel,
        baseUrls: draft!.baseUrls,
      };
      const typed = Object.fromEntries(Object.entries(keys).filter(([, value]) => value.trim().length > 0));
      if (Object.keys(typed).length > 0) payload.keys = typed;

      await api.saveConfig(payload);
      setKeys({});
      await onSaved();
      const fresh = await api.config();
      setDraft({ ...fresh });
      notify("Configuration saved");
      void loadModels(draft!.provider);
    } catch (error) {
      notify(error instanceof Error ? error.message : "Could not save", "bad");
    } finally {
      setSaving(false);
    }
  }

  const visibleModels = models.filter((model) => {
    const q = modelQuery.trim().toLowerCase();
    return !q || model.id.toLowerCase().includes(q) || model.name.toLowerCase().includes(q);
  });

  const hasKey = (id: string) => config.keys.find((entry) => entry.provider === id)?.hasKey;

  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-[26px] font-semibold tracking-[-0.02em] text-white sm:text-[30px]">
            <SplitText text="Config" stagger={0.05} />
          </h1>
          <p className="mt-1 text-[13px] text-neutral-500">
            Keys never leave this machine. They are written to{" "}
            <span className="font-mono text-[11.5px] text-neutral-400">{status?.dataDir ?? "…"}</span>.
          </p>
        </div>
        <button
          type="button"
          onClick={() => void save()}
          disabled={saving || !dirty}
          className={cn(
            "inline-flex h-9 items-center gap-2 rounded-full px-4 text-[12.5px] font-semibold transition-all",
            dirty ? "bg-white text-black hover:opacity-90" : "bg-white/[0.06] text-neutral-500"
          )}
        >
          {saving ? <IconLoader2 size={14} className="animate-spin" /> : <IconCheck size={14} />}
          {dirty ? "Save changes" : "Saved"}
        </button>
      </div>

      {/* providers */}
      <section>
        <SectionLabel hint="pick the brain that finds your moments">Model provider</SectionLabel>
        <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
          {status?.providers.map((item) => {
            const selected = draft.provider === item.id;
            return (
              <button
                key={item.id}
                type="button"
                onClick={() => patch({ provider: item.id, model: item.defaultModel || draft.model })}
                className={cn(
                  "sc-lift relative flex flex-col items-start gap-1 rounded-2xl border p-3.5 text-left",
                  selected
                    ? "border-white/35 bg-white/[0.07] shadow-[0_0_0_1px_rgba(255,255,255,0.12)]"
                    : "border-white/[0.07] bg-white/[0.015] hover:border-white/[0.16] hover:bg-white/[0.035]"
                )}
              >
                <div className="flex w-full items-center gap-2">
                  <ProviderIcon
                    id={item.id}
                    className={cn("transition-colors", selected ? "text-white" : "text-neutral-400")}
                  />
                  <span className="text-[12.5px] font-semibold text-white">{item.name}</span>
                  {item.kind === "local" && <Pill tone="good" className="px-1.5 py-0 text-[9.5px]">free</Pill>}
                  {item.id !== "local" && hasKey(item.id) && (
                    <span className="ml-auto text-neutral-300">
                      <IconCircleCheck size={14} />
                    </span>
                  )}
                  {selected && (
                    <motion.span
                      layoutId="provider-dot"
                      className="ml-auto h-1.5 w-1.5 rounded-full bg-white"
                    />
                  )}
                </div>
                <p className="text-[11px] leading-relaxed text-neutral-500">{item.blurb}</p>
                <span className="mt-0.5 max-w-full truncate font-mono text-[9.5px] text-neutral-700">{item.baseUrl}</span>
              </button>
            );
          })}
        </div>
      </section>

      {/* keys */}
      <section>
        <SectionLabel hint="stored locally · masked">API keys</SectionLabel>
        <Card className="divide-y divide-white/[0.05] p-0">
          {status?.providers
            .filter((item) => item.needsKey)
            .map((item) => (
              <div key={item.id} className="flex flex-wrap items-center gap-2 px-3.5 py-2.5">
                <span className="flex w-28 shrink-0 items-center gap-2 text-[12px] font-medium text-neutral-300">
                  <ProviderIcon id={item.id} className="text-neutral-500" />
                  {item.name}
                </span>
                <div className="relative min-w-0 flex-1">
                  <IconKey size={13} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-neutral-600" />
                  <input
                    type={reveal[item.id] ? "text" : "password"}
                    value={keys[item.id] ?? ""}
                    onChange={(event) => setKeys((prev) => ({ ...prev, [item.id]: event.target.value }))}
                    placeholder={hasKey(item.id) ? "••••••••••• saved — type to replace" : "paste your API key"}
                    spellCheck={false}
                    className={cn(inputClass, "h-8 pl-8 pr-8 font-mono text-[11.5px]")}
                  />
                  <button
                    type="button"
                    onClick={() => setReveal((prev) => ({ ...prev, [item.id]: !prev[item.id] }))}
                    className="absolute right-2 top-1/2 -translate-y-1/2 text-neutral-600 hover:text-white"
                    aria-label="Toggle visibility"
                  >
                    {reveal[item.id] ? <IconEyeOff size={13} /> : <IconEye size={13} />}
                  </button>
                </div>
                {item.docs && (
                  <a
                    href={item.docs}
                    target="_blank"
                    rel="noreferrer"
                    className="inline-flex items-center gap-1 text-[11px] text-neutral-500 hover:text-white"
                  >
                    get a key <IconExternalLink size={11} />
                  </a>
                )}
              </div>
            ))}
          {draft.provider === "custom" && (
            <div className="grid gap-3 px-3.5 py-3 sm:grid-cols-2">
              <Field label="Endpoint URL" hint="OpenAI-compatible base, e.g. http://127.0.0.1:1234/v1">
                <input
                  value={draft.customBaseUrl}
                  onChange={(event) => patch({ customBaseUrl: event.target.value })}
                  placeholder="http://127.0.0.1:1234/v1"
                  className={cn(inputClass, "font-mono text-[11.5px]")}
                />
              </Field>
              <Field label="Default model id">
                <input
                  value={draft.customModel}
                  onChange={(event) => patch({ customModel: event.target.value })}
                  placeholder="qwen2.5-7b-instruct"
                  className={cn(inputClass, "font-mono text-[11.5px]")}
                />
              </Field>
            </div>
          )}
        </Card>
      </section>

      {/* models */}
      <section>
        <SectionLabel hint={`${models.length} available`}>Model</SectionLabel>
        <Card className="p-3">
          <div className="flex flex-wrap items-center gap-2">
            <div className="relative min-w-0 flex-1">
              <IconSearch size={13} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-neutral-600" />
              <input
                value={modelQuery}
                onChange={(event) => setModelQuery(event.target.value)}
                placeholder="Filter the model list…"
                className={cn(inputClass, "h-8 pl-8 font-mono text-[11.5px]")}
              />
            </div>
            <button
              type="button"
              onClick={() => void loadModels()}
              className="inline-flex h-8 items-center gap-1.5 rounded-xl border border-white/10 px-3 text-[11.5px] font-medium text-neutral-300 hover:bg-white/[0.05]"
            >
              {modelsLoading ? <IconLoader2 size={13} className="animate-spin" /> : <IconRefresh size={13} />}
              Refresh
            </button>
            <span className="rounded-xl border border-white/[0.08] bg-black/40 px-2.5 py-1.5 font-mono text-[11px] text-white">
              {draft.model || "none"}
            </span>
          </div>

          <div className="sc-scrollbar-thin mt-2 max-h-64 overflow-y-auto rounded-xl border border-white/[0.05] bg-black/30 p-1">
            {modelsLoading && (
              <div className="grid place-items-center py-10 text-neutral-600">
                <IconLoader2 size={18} className="animate-spin" />
              </div>
            )}
            {!modelsLoading && modelsError && (
              <div className="flex items-start gap-2 px-2 py-3 text-[11.5px] text-neutral-300">
                <IconAlertTriangle size={14} className="mt-0.5 shrink-0" />
                <span>{modelsError}</span>
              </div>
            )}
            {!modelsLoading && !modelsError && visibleModels.length === 0 && (
              <div className="px-2 py-6 text-center text-[11.5px] text-neutral-600">
                No models to show. Add a key above, then refresh.
              </div>
            )}
            {!modelsLoading &&
              visibleModels.map((model) => {
                const selected = draft.model === model.id;
                return (
                  <button
                    key={model.id}
                    type="button"
                    onClick={() => patch({ model: model.id })}
                    className={cn(
                      "flex w-full items-center gap-2 rounded-lg px-2 py-1.5 text-left transition-colors",
                      selected ? "bg-white/[0.09]" : "hover:bg-white/[0.04]"
                    )}
                  >
                    <span
                      className={cn(
                        "h-1.5 w-1.5 shrink-0 rounded-full",
                        selected ? "bg-white" : "bg-neutral-700"
                      )}
                    />
                    <span className="truncate font-mono text-[11.5px] text-neutral-200">{model.id}</span>
                    {model.context > 0 && (
                      <span className="ml-auto shrink-0 text-[10px] text-neutral-600">
                        {Math.round(model.context / 1000)}k
                      </span>
                    )}
                    {model.note && (
                      <span className="shrink-0 rounded-full bg-emerald-500/10 px-1.5 text-[9.5px] text-emerald-400">
                        {model.note}
                      </span>
                    )}
                  </button>
                );
              })}
          </div>
          <div className="mt-2 flex items-center gap-2">
            <input
              value={draft.model}
              onChange={(event) => patch({ model: event.target.value })}
              placeholder="or type a model id"
              className={cn(inputClass, "h-8 flex-1 font-mono text-[11.5px]")}
            />
          </div>

          <div className="mt-3 border-t border-white/[0.06] pt-3">
            <Field
              label={`${provider?.name ?? "Provider"} base URL`}
              hint={`Built in: ${provider?.baseUrl || "none"} — set your own only if you proxy or self-host.`}
            >
              <input
                value={draft.baseUrls?.[draft.provider] ?? ""}
                onChange={(event) =>
                  patch({ baseUrls: { ...(draft.baseUrls ?? {}), [draft.provider]: event.target.value } })
                }
                placeholder={provider?.baseUrl || "https://api.example.com/v1"}
                spellCheck={false}
                className={cn(inputClass, "font-mono text-[11.5px]")}
              />
            </Field>
          </div>
        </Card>
      </section>

      {/* defaults */}
      <section>
        <SectionLabel hint="used for every new run">Clipping defaults</SectionLabel>
        <div className="grid gap-3 lg:grid-cols-2">
          <Card className="flex flex-col gap-4">
            <div>
              <div className="mb-2 flex items-center justify-between">
                <span className="text-[12px] font-medium text-neutral-300">Clips per video</span>
                <span className="font-mono text-[12px] text-white">{draft.clipCount}</span>
              </div>
              <input
                type="range"
                min={1}
                max={10}
                value={draft.clipCount}
                onChange={(event) => patch({ clipCount: Number(event.target.value) })}
                className="h-1.5 w-full appearance-none rounded-full bg-white/10 accent-chili-500"
              />
            </div>

            <div className="grid grid-cols-2 gap-3">
              <Field label="Min seconds">
                <input
                  type="number"
                  min={5}
                  max={170}
                  value={draft.minSeconds}
                  onChange={(event) => patch({ minSeconds: Number(event.target.value) })}
                  className={cn(inputClass, "font-mono")}
                />
              </Field>
              <Field label="Max seconds">
                <input
                  type="number"
                  min={6}
                  max={180}
                  value={draft.maxSeconds}
                  onChange={(event) => patch({ maxSeconds: Number(event.target.value) })}
                  className={cn(inputClass, "font-mono")}
                />
              </Field>
            </div>

            <Field label="Source quality">
              <Segmented
                value={draft.quality}
                options={[
                  { value: "1080", label: "1080p" },
                  { value: "720", label: "720p" },
                ]}
                onChange={(value) => patch({ quality: value })}
              />
            </Field>
          </Card>

          <Card className="flex flex-col gap-3">
            <Toggle
              checked={draft.burnCaptions}
              onChange={(value) => patch({ burnCaptions: value })}
              label="Burn captions into the video"
              description="Uses the source's own subtitle track. Word-timed captions are styled for vertical."
            />
            <div>
              <span className="mb-2 block text-[12px] font-medium text-neutral-300">Caption style</span>
              <Segmented
                size="sm"
                value={CAPTION_STYLES.some((style) => style.value === draft.captionStyle) ? draft.captionStyle : "punch"}
                options={CAPTION_STYLES.map((style) => ({ value: style.value, label: style.label }))}
                onChange={(value) => patch({ captionStyle: value })}
              />
              <p className="mt-2 text-[11px] leading-relaxed text-neutral-600">
                Punch pops each word · Karaoke lights the word being spoken · Boxed sits on a solid white bar · Clean is a quiet fade.
              </p>
              <div className="mt-3 overflow-hidden rounded-2xl border border-white/[0.06] bg-black/25 p-3">
                <CaptionPhone
                  draft={draftFromConfig(draft)}
                  text="this is how your captions will look"
                  compact
                />
                <p className="mt-2 text-center text-[10px] leading-relaxed text-neutral-600">
                  Font, size, height, outline, words per line and word pop live in the Clipper tab’s caption studio.
                </p>
              </div>
            </div>
            <Field
              label="Speech model"
              hint="When a video ships no captions, the audio is transcribed locally. The model downloads once."
            >
              <Segmented
                size="sm"
                value={draft.whisperModel}
                options={[
                  { value: "tiny", label: "Fast · tiny.en (32 MB)" },
                  { value: "base", label: "Better · base.en (60 MB)" },
                ]}
                onChange={(value) => patch({ whisperModel: value })}
              />
            </Field>
            <Field label="Clips folder">
              <div className="flex gap-2">
                <input
                  value={draft.outDir}
                  onChange={(event) => patch({ outDir: event.target.value })}
                  className={cn(inputClass, "font-mono text-[11px]")}
                />
                <button
                  type="button"
                  onClick={() => void api.open(draft.outDir).catch(() => notify("Could not open", "bad"))}
                  className="grid h-9 w-9 shrink-0 place-items-center rounded-xl border border-white/10 text-neutral-300 hover:bg-white/[0.05]"
                  title="Open folder"
                >
                  <IconFolderOpen size={15} />
                </button>
              </div>
            </Field>
          </Card>
        </div>
      </section>

      {/* diagnostics */}
      <section>
        <SectionLabel hint="ffmpeg + yt-dlp run locally">Diagnostics</SectionLabel>
        <Card className="flex flex-col gap-3">
          {status?.tools.map((tool) => (
            <div key={tool.id} className="flex flex-wrap items-center gap-2">
              <span className={cn("grid h-6 w-6 place-items-center rounded-lg", tool.found ? "bg-white/10 text-white" : "bg-white/[0.06] text-neutral-400")}>
                {tool.found ? <IconCircleCheck size={14} /> : <IconAlertTriangle size={14} />}
              </span>
              <span className="w-16 text-[12px] font-medium text-white">{tool.name}</span>
              <span className="min-w-0 flex-1 truncate font-mono text-[10.5px] text-neutral-500">
                {tool.found ? tool.version || tool.path : tool.hint}
              </span>
            </div>
          ))}

          <div className="flex flex-wrap items-center gap-2 border-t border-white/[0.06] pt-3">
            <span className="inline-flex items-center gap-1.5 font-mono text-[10.5px] text-neutral-600">
              <IconTerminal2 size={12} /> {status?.dataDir}
            </span>
            <button
              type="button"
              onClick={async () => {
                setSetting(true);
                try {
                  await api.bootstrap();
                  await onStatusRefresh();
                  notify("Toolchain checked");
                } catch (error) {
                  notify(error instanceof Error ? error.message : "Setup failed", "bad");
                } finally {
                  setSetting(false);
                }
              }}
              className="ml-auto inline-flex h-8 items-center gap-1.5 rounded-xl border border-white/10 px-3 text-[11.5px] font-medium text-neutral-200 hover:bg-white/[0.05]"
            >
              {setting ? <IconLoader2 size={13} className="animate-spin" /> : <IconRefresh size={13} />}
              Re-check tools
            </button>
          </div>
        </Card>
      </section>

      <McpSection notify={notify} />

      <AnalyticsSection notify={notify} />
    </div>
  );
}
