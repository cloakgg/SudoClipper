import { CaptionStudio } from "@/components/caption-studio";
import { ClipCard, Player } from "@/components/clip";
import { Editor } from "@/components/editor";
import { ModelMenu } from "@/components/model-menu";
import { Card, Pill, Segmented, SectionLabel, Toggle } from "@/components/ui/bits";
import { SplitText } from "@/components/ui/split-text";
import { VideoInput } from "@/components/ui/video-input";
import { api, type Clip, type Config, type Job, type Model, type Status } from "@/lib/api";
import { cn } from "@/lib/utils";
import {
  IconAlertTriangle,
  IconCrop,
  IconKey,
  IconLoader2,
  IconSparkles,
  IconTypography,
  IconVideo,
  IconX,
} from "@tabler/icons-react";
import { AnimatePresence, motion } from "motion/react";
import * as React from "react";

const STEPS = [
  { key: "download", label: "Download", stages: ["tools", "metadata", "download"] },
  { key: "captions", label: "Captions", stages: ["transcript", "transcribe"] },
  { key: "analyze", label: "Pick moments", stages: ["analyze"] },
  { key: "render", label: "Render 9:16", stages: ["render", "done"] },
];

const LENGTH_PRESETS = [
  { value: "short", label: "Short", min: 10, max: 30 },
  { value: "medium", label: "Medium", min: 15, max: 50 },
  { value: "long", label: "Long", min: 25, max: 80 },
];

export function ClipperTab({
  status,
  config,
  onConfigChange,
  notify,
  onFinished,
}: {
  status: Status | null;
  config: Config | null;
  onConfigChange: (patch: Record<string, unknown>) => Promise<void>;
  notify: (message: string, tone?: "good" | "bad") => void;
  onFinished: () => void;
}) {
  const [jobs, setJobs] = React.useState<Job[]>([]);
  const [activeJobId, setActiveJobId] = React.useState<string | null>(null);
  const [starting, setStarting] = React.useState(false);
  const [playing, setPlaying] = React.useState<Clip | null>(null);
  const [editing, setEditing] = React.useState<Clip | null>(null);

  const activeJob = jobs.find((job) => job.id === activeJobId) ?? jobs[0] ?? null;
  const busy = activeJob?.state === "running" || activeJob?.state === "queued" || starting;

  const provider = React.useMemo(
    () => status?.providers.find((p) => p.id === config?.provider) ?? status?.providers[0],
    [status, config?.provider]
  );

  const pollJob = React.useCallback(
    async (id: string) => {
      let stopped = false;
      let timer: number | undefined;

      const tick = async () => {
        try {
          const job = await api.job(id);
          if (stopped) return;
          setJobs((prev) => [job, ...prev.filter((item) => item.id !== job.id)]);
          if (job.state === "running" || job.state === "queued") {
            timer = window.setTimeout(tick, 900);
          } else {
            setStarting(false);
            onFinished();
            if (job.state === "done") {
              notify(`${job.clips.length} clip${job.clips.length === 1 ? "" : "s"} ready — opening the editor`);
              if (job.clips[0]) setEditing(job.clips[0]);
            }
            if (job.state === "error") notify(job.error || "That run failed", "bad");
          }
        } catch {
          if (!stopped) timer = window.setTimeout(tick, 1600);
        }
      };

      void tick();
      return () => {
        stopped = true;
        if (timer) window.clearTimeout(timer);
      };
    },
    [notify, onFinished]
  );

  React.useEffect(() => {
    if (!activeJobId) return;
    let cleanup: (() => void) | undefined;
    void pollJob(activeJobId).then((fn) => {
      cleanup = fn;
    });
    return () => cleanup?.();
  }, [activeJobId, pollJob]);

  async function start(url: string) {
    setStarting(true);
    try {
      const job = await api.createJob({ url });
      setJobs((prev) => [job, ...prev]);
      setActiveJobId(job.id);
    } catch (error) {
      setStarting(false);
      notify(error instanceof Error ? error.message : "Could not start", "bad");
    }
  }

  const lengthValue =
    LENGTH_PRESETS.find((p) => config && p.min === config.minSeconds && p.max === config.maxSeconds)?.value ?? "medium";

  const logLines = activeJob?.log.slice(-7) ?? [];

  return (
    <div className="flex flex-col gap-8">
      <section>
        <div className="mb-6 flex flex-col items-center text-center">
          <Pill tone="live" className="mb-4">
            <IconSparkles size={11} /> fully local · your key, your machine
          </Pill>
          <h1 className="max-w-2xl text-balance text-[30px] font-semibold leading-[1.08] tracking-[-0.03em] text-white sm:text-[42px]">
            <SplitText text="Paste a video. Get the moments" stagger={0.032} />{" "}
            <span className="sc-underline inline-block">
              <SplitText text="people share." delay={0.2} stagger={0.06} sheen />
            </span>
          </h1>
          <p className="mt-3 max-w-lg text-[13.5px] leading-relaxed text-neutral-500">
            SUDOCLIPPER downloads the source, reads the captions, finds the hooks and renders vertical
            9:16 clips with burned-in subtitles — all on this computer.
          </p>
        </div>

        <div className="mx-auto w-full max-w-3xl">
          <VideoInput
            onSubmit={start}
            busy={busy}
            placeholder="Paste a YouTube link…"
          />

          <div className="mt-2 flex flex-wrap items-center justify-center gap-2 text-[11px] text-neutral-500">
            <ModelMenu
              value={{
                provider: config?.provider ?? provider?.id ?? "local",
                model: config?.model || provider?.defaultModel || "",
              }}
              onChange={(next) => void onConfigChange({ provider: next.provider, model: next.model })}
            />
            <span className="inline-flex items-center gap-1.5 rounded-full border border-white/[0.07] bg-white/[0.02] px-2.5 py-1">
              <IconTypography size={11} /> {config?.captionStyle || "punch"} captions
            </span>
          </div>

          <div className="mt-3 flex flex-wrap items-center justify-center gap-2 sm:gap-3">
            <div className="flex items-center gap-1.5 rounded-full border border-white/[0.07] bg-white/[0.02] px-1 py-1">
              <span className="pl-2 pr-1 text-[10.5px] uppercase tracking-wider text-neutral-500">Clips</span>
              <Segmented
                size="sm"
                value={String(config?.clipCount ?? 3)}
                options={[1, 2, 3, 5].map((n) => ({ value: String(n), label: String(n) }))}
                onChange={(value) => void onConfigChange({ clipCount: Number(value) })}
              />
            </div>

            <div className="flex items-center gap-1.5 rounded-full border border-white/[0.07] bg-white/[0.02] px-1 py-1">
              <span className="pl-2 pr-1 text-[10.5px] uppercase tracking-wider text-neutral-500">Length</span>
              <Segmented
                size="sm"
                value={lengthValue}
                options={LENGTH_PRESETS.map((p) => ({ value: p.value, label: p.label }))}
                onChange={(value) => {
                  const preset = LENGTH_PRESETS.find((p) => p.value === value);
                  if (preset) void onConfigChange({ minSeconds: preset.min, maxSeconds: preset.max });
                }}
              />
            </div>

            <button
              type="button"
              onClick={() => void onConfigChange({ burnCaptions: !config?.burnCaptions })}
              className={cn(
                "inline-flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-[11.5px] font-medium transition-colors",
                config?.burnCaptions
                  ? "border-white/30 bg-white/[0.09] text-white"
                  : "border-white/[0.07] bg-white/[0.02] text-neutral-400"
              )}
            >
              <IconVideo size={12} /> Captions {config?.burnCaptions ? "on" : "off"}
            </button>

            <span className="inline-flex items-center gap-1.5 rounded-full border border-white/[0.07] bg-white/[0.02] px-3 py-1.5 text-[11.5px] text-neutral-400">
              <IconCrop size={12} /> 9:16 · 1080×1920
            </span>
          </div>

          {provider?.needsKey && !config?.keys.find((entry) => entry.provider === provider.id)?.hasKey && (
            <p className="mt-3 flex items-center justify-center gap-1.5 text-[11.5px] text-neutral-600">
              <IconKey size={12} /> {provider.name} selected — add your key in
              <a href="#config" className="text-white underline underline-offset-2">
                Config
              </a>
            </p>
          )}

          <div className="mt-5">
            <CaptionStudio config={config} onChange={onConfigChange} />
          </div>
        </div>
      </section>

      <AnimatePresence>
        {activeJob && (
          <motion.section
            key={activeJob.id}
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0 }}
            className="mx-auto w-full max-w-3xl"
          >
            <Card className="p-0">
              <div className="flex items-center gap-3 border-b border-white/[0.06] px-4 py-3">
                <span
                  className={cn(
                    "grid h-7 w-7 place-items-center rounded-full",
                    activeJob.state === "done"
                      ? "bg-white text-black"
                      : activeJob.state === "error"
                        ? "bg-white/[0.1] text-white"
                        : "bg-white/[0.06] text-white"
                  )}
                >
                  {activeJob.state === "running" || activeJob.state === "queued" ? (
                    <IconLoader2 size={15} className="animate-spin" />
                  ) : activeJob.state === "done" ? (
                    <IconSparkles size={15} />
                  ) : (
                    <IconAlertTriangle size={15} />
                  )}
                </span>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-[12.5px] font-medium text-white">
                    {activeJob.title || activeJob.source || "Working…"}
                  </p>
                  <p className="truncate font-mono text-[10.5px] text-neutral-500">
                    {activeJob.state === "done"
                      ? `${activeJob.clips.length} clips · ${activeJob.provider}`
                      : activeJob.state === "error"
                        ? activeJob.error
                        : activeJob.state === "queued"
                          ? "Queued — waiting for the run ahead (cancel it with the ×)"
                          : `${STEPS.find((s) => s.stages.includes(activeJob.stage))?.label ?? activeJob.stage} · ${activeJob.progress}%`}
                    {activeJob.audience && activeJob.audience !== "no audience data" && (
                      <span className="text-neutral-400"> · {activeJob.audience}</span>
                    )}
                  </p>
                </div>
                {(activeJob.state === "running" || activeJob.state === "queued") && (
                  <button
                    type="button"
                    onClick={() => void api.cancelJob(activeJob.id)}
                    className="grid h-7 w-7 place-items-center rounded-lg text-neutral-500 hover:bg-white/[0.06] hover:text-white"
                    title="Cancel"
                  >
                    <IconX size={14} />
                  </button>
                )}
              </div>

              <div className="px-4 py-3">
                <div className="mb-3 flex items-center gap-1.5">
                  {STEPS.map((step, index) => {
                    const currentIndex = STEPS.findIndex((s) => s.stages.includes(activeJob.stage));
                    const done = activeJob.state === "done" || index < currentIndex;
                    const current = index === currentIndex && activeJob.state !== "done";
                    return (
                      <div key={step.key} className="flex flex-1 items-center gap-1.5">
                        <div
                          className={cn(
                            "h-1 flex-1 rounded-full transition-colors duration-500",
                            done ? "bg-white/80" : current ? "sc-shimmer" : "bg-white/[0.08]"
                          )}
                        />
                        <span
                          className={cn(
                            "text-[10px] font-medium",
                            done ? "text-neutral-300" : current ? "text-white" : "text-neutral-600"
                          )}
                        >
                          {step.label}
                        </span>
                      </div>
                    );
                  })}
                </div>

                <div className="h-1.5 w-full overflow-hidden rounded-full bg-white/[0.06]">
                  <motion.div
                    className={cn(
                      "h-full rounded-full",
                      activeJob.state === "queued"
                        ? "sc-shimmer"
                        : "bg-gradient-to-r from-white/50 via-white to-white/70"
                    )}
                    animate={{
                      width:
                        activeJob.state === "queued"
                          ? "100%"
                          : `${activeJob.state === "error" ? 100 : activeJob.progress}%`,
                    }}
                    transition={{ type: "spring", stiffness: 120, damping: 20 }}
                  />
                </div>

                <div className="mt-3 max-h-32 overflow-y-auto rounded-xl border border-white/[0.05] bg-black/50 p-2.5 font-mono text-[10.5px] leading-relaxed">
                  {logLines.length === 0 && <p className="text-neutral-600">Starting the worker…</p>}
                  {logLines.map((line, index) => (
                    <p key={index} className={cn("truncate", line.error ? "text-chili-400/90" : "text-neutral-500")}>
                      <span className="text-neutral-700">{line.at} </span>
                      {line.text}
                    </p>
                  ))}
                </div>
              </div>

              {activeJob.clips.length > 0 && (
                <div className="grid grid-cols-2 gap-3 border-t border-white/[0.06] p-4 sm:grid-cols-3 md:grid-cols-4">
                  {activeJob.clips.map((clip, index) => (
                    <ClipCard key={clip.id} clip={clip} onPlay={setPlaying} index={index} />
                  ))}
                </div>
              )}
            </Card>
          </motion.section>
        )}
      </AnimatePresence>

      {!activeJob && (
        <section className="mx-auto grid w-full max-w-3xl gap-3 sm:grid-cols-3">
          {[
            { title: "No uploads to anyone", body: "Everything is downloaded and rendered by ffmpeg on this machine." },
            { title: "Your key, your model", body: "OpenRouter, OpenAI, Gemini, Groq, Ollama — or the free local picker." },
            { title: "Clips stay yours", body: "Each export lands in a plain folder you can open from Memory." },
          ].map((item) => (
            <div key={item.title} className="rounded-2xl border border-white/[0.06] bg-white/[0.015] p-4">
              <p className="text-[12.5px] font-semibold text-white">{item.title}</p>
              <p className="mt-1 text-[11.5px] leading-relaxed text-neutral-500">{item.body}</p>
            </div>
          ))}
        </section>
      )}

      <Editor
        clip={editing}
        open={!!editing}
        onClose={() => setEditing(null)}
        onSaved={(updated) => {
          setJobs((prev) =>
            prev.map((job) => ({
              ...job,
              clips: job.clips.map((item) => (item.id === updated.id ? updated : item)),
            }))
          );
          onFinished();
        }}
        notify={notify}
      />

      <Player clip={playing} onClose={() => setPlaying(null)} />
    </div>
  );
}
