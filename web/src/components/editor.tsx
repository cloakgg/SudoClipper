"use client";

import { api, type Clip } from "@/lib/api";
import { cn, formatTime, mediaUrl } from "@/lib/utils";
import {
  IconAlertTriangle,
  IconCheck,
  IconLoader2,
  IconPlayerPlayFilled,
  IconRefresh,
  IconScissors,
  IconTag,
  IconTypography,
  IconX,
} from "@tabler/icons-react";
import { AnimatePresence, motion } from "motion/react";
import * as React from "react";

const STYLES = [
  { value: "punch", label: "Punch" },
  { value: "karaoke", label: "Karaoke" },
  { value: "neon", label: "Neon" },
  { value: "rave", label: "Rave" },
  { value: "chrome", label: "Chrome" },
  { value: "boxed", label: "Boxed" },
  { value: "clean", label: "Clean" },
];

const FONTS = [
  { value: "", label: "Auto" },
  { value: "Arial Black", label: "Arial Black" },
  { value: "Impact", label: "Impact" },
  { value: "Bahnschrift SemiBold", label: "Bahnschrift" },
  { value: "Segoe UI Semibold", label: "Segoe UI" },
  { value: "Verdana", label: "Verdana" },
  { value: "Georgia", label: "Georgia" },
];

const PREVIEW_FALLBACK = ["this", "is", "how", "your", "captions", "will", "look"];

type Cue = { start: number; end: number; text: string };

function styleSpec(style: string, font: string) {
  const base =
    {
      punch: { font: "Arial Black", upper: true, weight: 900, color: "#fff", boxed: false, karaoke: false, palette: [] as string[] },
      karaoke: { font: "Bahnschrift SemiBold", upper: true, weight: 700, color: "#fff", boxed: false, karaoke: true, palette: ["#22d3ee", "#a78bfa", "#fff"] },
      neon: { font: "Arial Black", upper: true, weight: 900, color: "#fff", boxed: false, karaoke: false, palette: ["#22d3ee", "#a78bfa", "#fff"] },
      rave: { font: "Bahnschrift SemiBold", upper: true, weight: 800, color: "#fff", boxed: false, karaoke: false, palette: ["#fbbf24", "#22d3ee", "#a78bfa", "#f472b6"] },
      chrome: { font: "Impact", upper: false, weight: 700, color: "#fff", boxed: false, karaoke: false, palette: ["#dce7ff", "#fff", "#9fb6d6"] },
      boxed: { font: "Impact", upper: false, weight: 700, color: "#000", boxed: true, karaoke: false, palette: [] as string[] },
      clean: { font: "Segoe UI Semibold", upper: false, weight: 600, color: "#fff", boxed: false, karaoke: false, palette: [] as string[] },
    }[style] ?? { font: "Arial Black", upper: true, weight: 900, color: "#fff", boxed: false, karaoke: false, palette: [] as string[] };
  return { ...base, font: font || base.font };
}

export function Editor({
  clip,
  open,
  onClose,
  onSaved,
  notify,
}: {
  clip: Clip | null;
  open: boolean;
  onClose: () => void;
  onSaved: (clip: Clip) => void;
  notify: (message: string, tone?: "good" | "bad") => void;
}) {
  const [start, setStart] = React.useState(0);
  const [end, setEnd] = React.useState(0);
  const [style, setStyle] = React.useState("punch");
  const [captions, setCaptions] = React.useState(true);
  const [scale, setScale] = React.useState(1);
  const [position, setPosition] = React.useState(0);
  const [uppercase, setUppercase] = React.useState(true);
  const [font, setFont] = React.useState("");
  const [outline, setOutline] = React.useState(1);
  const [wordsPerLine, setWordsPerLine] = React.useState(3);
  const [wordPop, setWordPop] = React.useState(true);
  const [saving, setSaving] = React.useState(false);
  const [version, setVersion] = React.useState(0);
  const [cues, setCues] = React.useState<Cue[]>([]);
  const [activeWord, setActiveWord] = React.useState(0);
  const [dirty, setDirty] = React.useState(false);

  React.useEffect(() => {
    if (!clip) return;
    setStart(clip.start);
    setEnd(clip.end > clip.start ? clip.end : clip.start + clip.duration);
    setStyle(clip.captionStyle || "punch");
    setCaptions(clip.captioned);
    setScale(1);
    setPosition(0);
    setUppercase(true);
    setFont("");
    setOutline(1);
    setWordsPerLine(3);
    setWordPop(true);
    setDirty(false);
    setVersion((value) => value + 1);
  }, [clip?.id]);

  React.useEffect(() => {
    if (!clip || !open) return;
    let cancelled = false;
    api
      .clipCues(clip.id, start, end)
      .then((result) => {
        if (!cancelled) setCues(result.cues ?? []);
      })
      .catch(() => {
        if (!cancelled) setCues([]);
      });
    return () => {
      cancelled = true;
    };
  }, [clip?.id, open, start, end]);

  const previewWords = React.useMemo(() => {
    const text = cues.length > 0 ? cues[0].text : PREVIEW_FALLBACK.join(" ");
    const words = text.split(/\s+/).filter(Boolean);
    const bounded = words.slice(0, Math.max(2, wordsPerLine));
    return styleSpec(style, font).upper ? bounded.map((word) => word.toUpperCase()) : bounded;
  }, [cues, wordsPerLine, style, font]);

  React.useEffect(() => {
    const timer = window.setInterval(() => {
      setActiveWord((index) => (previewWords.length ? (index + 1) % previewWords.length : 0));
    }, 620);
    return () => window.clearInterval(timer);
  }, [previewWords.length]);

  React.useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (event.key === "Escape" && open) onClose();
    }
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  if (!clip) return null;

  const max = Math.max(clip.sourceDuration || 0, end + 5, 30);
  const src = `${mediaUrl(clip.file)}${mediaUrl(clip.file).includes("?") ? "&" : "?"}v=${version}`;
  const seconds = Math.max(1, end - start);
  const spec = styleSpec(style, font);
  const fontSize = 30 * scale;
  const bottom = Math.max(6, 16 - position / 24);
  const outlineWidth = Math.max(0, 2.4 * outline);

  function touch() {
    setDirty(true);
  }

  function reset() {
    if (!clip) return;
    setStart(clip.start);
    setEnd(clip.end);
    setStyle(clip.captionStyle || "punch");
    setCaptions(clip.captioned);
    setScale(1);
    setPosition(0);
    setUppercase(true);
    setFont("");
    setOutline(1);
    setWordsPerLine(3);
    setWordPop(true);
    setDirty(false);
  }

  async function save() {
    if (!clip) return;
    setSaving(true);
    try {
      const result = await api.editClip(clip.id, {
        start,
        end,
        captionStyle: style,
        captioned: captions,
        fontScale: scale,
        positionDelta: position,
        uppercase,
        font,
        outlineScale: outline,
        wordsPerLine,
        wordPop,
      });
      setVersion((value) => value + 1);
      setDirty(false);
      onSaved(result.clip);
      notify("Changes saved — clip re-rendered");
    } catch (error) {
      notify(error instanceof Error ? error.message : "Could not re-render", "bad");
    } finally {
      setSaving(false);
    }
  }

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          className="fixed inset-0 z-[135] grid place-items-center bg-black/85 p-2 backdrop-blur-sm sm:p-6"
          onClick={onClose}
        >
          <motion.div
            initial={{ scale: 0.97, y: 14, opacity: 0 }}
            animate={{ scale: 1, y: 0, opacity: 1 }}
            exit={{ scale: 0.98, y: 8, opacity: 0 }}
            transition={{ type: "spring", stiffness: 300, damping: 28 }}
            onClick={(event) => event.stopPropagation()}
            className="sc-scrollbar-thin flex max-h-[94vh] w-full max-w-6xl flex-col overflow-y-auto rounded-3xl border border-white/[0.1] bg-[#080808] lg:flex-row"
          >
            {/* preview */}
            <div className="flex shrink-0 flex-col gap-3 border-b border-white/[0.06] p-4 lg:w-[400px] lg:border-b-0 lg:border-r">
              <div className="flex items-center justify-between">
                <p className="text-[10.5px] uppercase tracking-[0.2em] text-neutral-500">Preview</p>
                <span className="rounded-full border border-white/10 px-2 py-0.5 text-[10px] text-neutral-500">
                  9:16 live
                </span>
              </div>

              <div className="relative mx-auto aspect-[9/16] w-full max-w-[300px] overflow-hidden rounded-2xl border border-white/10 bg-black">
                {clip.thumb ? (
                  <img
                    src={`${mediaUrl(clip.thumb)}&v=${version}`}
                    alt=""
                    className="absolute inset-0 h-full w-full scale-110 object-cover opacity-70 blur-[6px]"
                  />
                ) : (
                  <div className="absolute inset-0 bg-[radial-gradient(circle_at_50%_20%,#1b1b1b,#080808)]" />
                )}
                {clip.thumb && (
                  <img
                    src={`${mediaUrl(clip.thumb)}&v=${version}`}
                    alt=""
                    className="absolute inset-0 h-full w-full object-contain"
                  />
                )}
                <div className="absolute inset-0 bg-black/10" />

                {captions ? (
                  <div
                    className="absolute inset-x-2 flex justify-center text-center"
                    style={{ bottom: `${bottom}%` }}
                  >
                    <div
                      className={cn(
                        "flex flex-wrap items-center justify-center gap-x-1.5 gap-y-0.5 px-2 py-1",
                        spec.boxed && "bg-white"
                      )}
                      style={{
                        fontFamily: spec.font,
                        fontWeight: spec.weight,
                        fontSize,
                        lineHeight: 1.06,
                        color: spec.color,
                        letterSpacing: "0.01em",
                        textShadow: spec.boxed
                          ? "none"
                          : `-${outlineWidth}px 0 #000, ${outlineWidth}px 0 #000, 0 -${outlineWidth}px #000, 0 ${outlineWidth}px #000, 0 ${outlineWidth * 1.6}px ${outlineWidth * 1.6}px rgba(0,0,0,0.85)`,
                      }}
                    >
                      {previewWords.map((word, index) => {
                        const active = index === activeWord;
                        return (
                          <span
                            key={`${word}-${index}`}
                            style={{
                              display: "inline-block",
                              color: spec.boxed
                                ? "#000"
                                : spec.palette.length
                                  ? spec.karaoke && !active
                                    ? "#8a8a8a"
                                    : spec.palette[index % spec.palette.length]
                                  : "#fff",
                              textShadow:
                                spec.boxed || !spec.palette.length
                                  ? undefined
                                  : `0 0 12px ${spec.palette[index % spec.palette.length]}`,
                              transform: active && wordPop ? "scale(1.18)" : "scale(1)",
                              transition: "transform 160ms ease, color 160ms ease",
                            }}
                          >
                            {word}
                          </span>
                        );
                      })}
                    </div>
                  </div>
                ) : (
                  <span className="absolute inset-x-0 bottom-8 text-center text-[11px] text-neutral-500">
                    captions off
                  </span>
                )}
              </div>

              <video
                key={src}
                src={src}
                controls
                playsInline
                className="max-h-[210px] w-full rounded-xl bg-black object-contain"
              />
            </div>

            {/* controls */}
            <div className="flex min-w-0 flex-1 flex-col gap-4 p-4 sm:p-5">
              <div className="flex items-start gap-3">
                <div className="min-w-0">
                  <p className="text-[10.5px] uppercase tracking-[0.2em] text-neutral-500">Editor</p>
                  <h2 className="mt-1 line-clamp-2 text-[17px] font-semibold leading-tight text-white">
                    {clip.title}
                  </h2>
                  <p className="mt-1 font-mono text-[11px] text-neutral-500">
                    {formatTime(start)}–{formatTime(end)} · {Math.round(seconds)}s · 1080×1920
                  </p>
                </div>
                <button
                  type="button"
                  onClick={onClose}
                  className="ml-auto grid h-8 w-8 shrink-0 place-items-center rounded-full border border-white/10 text-neutral-400 hover:bg-white/[0.06] hover:text-white"
                  aria-label="Close editor"
                >
                  <IconX size={15} />
                </button>
              </div>

              {!clip.editable && (
                <p className="flex items-start gap-2 rounded-xl border border-white/20 bg-white/[0.05] p-3 text-[11.5px] leading-relaxed text-neutral-300">
                  <IconAlertTriangle size={14} className="mt-0.5 shrink-0" />
                  The source for this clip is no longer cached, so it can only be played back. Run the link again to edit.
                </p>
              )}

              <section>
                <div className="mb-2 flex items-center justify-between text-[11.5px]">
                  <span className="inline-flex items-center gap-1.5 font-medium text-neutral-300">
                    <IconScissors size={13} /> Trim
                  </span>
                  <span className="font-mono text-neutral-500">
                    {formatTime(start)} → {formatTime(end)}
                  </span>
                </div>
                <label className="mb-1 block text-[10.5px] uppercase tracking-wider text-neutral-600">Start</label>
                <input
                  type="range"
                  min={0}
                  max={Math.max(1, max - 2)}
                  step={0.1}
                  value={start}
                  onChange={(event) => {
                    const next = Number(event.target.value);
                    setStart(next);
                    if (end - next < 2) setEnd(Math.min(max, next + 2));
                    touch();
                  }}
                  className="h-1.5 w-full appearance-none rounded-full bg-white/10 accent-white"
                />
                <label className="mb-1 mt-3 block text-[10.5px] uppercase tracking-wider text-neutral-600">End</label>
                <input
                  type="range"
                  min={1}
                  max={max}
                  step={0.1}
                  value={end}
                  onChange={(event) => {
                    const next = Number(event.target.value);
                    setEnd(next);
                    if (next - start < 2) setStart(Math.max(0, next - 2));
                    touch();
                  }}
                  className="h-1.5 w-full appearance-none rounded-full bg-white/10 accent-white"
                />
              </section>

              <section className="rounded-2xl border border-white/[0.08] bg-white/[0.015] p-3.5">
                <div className="mb-2 flex items-center gap-2">
                  <span className="inline-flex items-center gap-1.5 text-[12px] font-medium text-neutral-200">
                    <IconTypography size={13} /> Captions
                  </span>
                  <button
                    type="button"
                    role="switch"
                    aria-checked={captions}
                    onClick={() => {
                      setCaptions((value) => !value);
                      touch();
                    }}
                    className={cn(
                      "ml-auto flex h-5 w-9 items-center rounded-full border p-0.5 transition-colors",
                      captions ? "border-white/50 bg-white" : "border-white/10 bg-white/[0.06]"
                    )}
                  >
                    <span
                      className={cn(
                        "h-3.5 w-3.5 rounded-full transition-transform",
                        captions ? "translate-x-4 bg-black" : "translate-x-0 bg-white"
                      )}
                    />
                  </button>
                </div>

                <div className="inline-flex flex-wrap items-center gap-0.5 rounded-full border border-white/[0.08] bg-white/[0.03] p-0.5">
                  {STYLES.map((option) => (
                    <button
                      key={option.value}
                      type="button"
                      onClick={() => {
                        setStyle(option.value);
                        setUppercase(option.value === "punch" || option.value === "karaoke");
                        touch();
                      }}
                      className={cn(
                        "rounded-full px-2.5 py-1 text-[11px] font-medium transition-colors",
                        style === option.value ? "bg-white text-black" : "text-neutral-400 hover:text-white"
                      )}
                    >
                      {option.label}
                    </button>
                  ))}
                </div>

                <div className="mt-3 grid gap-3 sm:grid-cols-2">
                  <label className="block">
                    <span className="mb-1 block text-[10.5px] uppercase tracking-wider text-neutral-600">Font</span>
                    <select
                      value={font}
                      onChange={(event) => {
                        setFont(event.target.value);
                        touch();
                      }}
                      className="h-8 w-full rounded-lg border border-white/[0.08] bg-black/50 px-2 text-[11.5px] text-white outline-none"
                    >
                      {FONTS.map((option) => (
                        <option key={option.value} value={option.value} className="bg-[#111]">
                          {option.label}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label className="block">
                    <span className="mb-1 flex justify-between text-[10.5px] uppercase tracking-wider text-neutral-600">
                      Size <span className="font-mono text-neutral-500">{scale.toFixed(2)}×</span>
                    </span>
                    <input
                      type="range"
                      min={0.75}
                      max={1.45}
                      step={0.05}
                      value={scale}
                      onChange={(event) => {
                        setScale(Number(event.target.value));
                        touch();
                      }}
                      className="h-1.5 w-full appearance-none rounded-full bg-white/10 accent-white"
                    />
                  </label>
                  <label className="block">
                    <span className="mb-1 flex justify-between text-[10.5px] uppercase tracking-wider text-neutral-600">
                      Height <span className="font-mono text-neutral-500">{position > 0 ? "+" : ""}{position}</span>
                    </span>
                    <input
                      type="range"
                      min={-220}
                      max={300}
                      step={10}
                      value={position}
                      onChange={(event) => {
                        setPosition(Number(event.target.value));
                        touch();
                      }}
                      className="h-1.5 w-full appearance-none rounded-full bg-white/10 accent-white"
                    />
                  </label>
                  <label className="block">
                    <span className="mb-1 flex justify-between text-[10.5px] uppercase tracking-wider text-neutral-600">
                      Outline <span className="font-mono text-neutral-500">{outline.toFixed(1)}×</span>
                    </span>
                    <input
                      type="range"
                      min={0.3}
                      max={2}
                      step={0.1}
                      value={outline}
                      onChange={(event) => {
                        setOutline(Number(event.target.value));
                        touch();
                      }}
                      className="h-1.5 w-full appearance-none rounded-full bg-white/10 accent-white"
                    />
                  </label>
                </div>

                <div className="mt-3 flex flex-wrap items-center gap-2">
                  <span className="text-[10.5px] uppercase tracking-wider text-neutral-600">Per line</span>
                  <div className="inline-flex items-center gap-0.5 rounded-full border border-white/[0.08] bg-white/[0.03] p-0.5">
                    {[2, 3, 4, 5, 6].map((count) => (
                      <button
                        key={count}
                        type="button"
                        onClick={() => {
                          setWordsPerLine(count);
                          setActiveWord(0);
                          touch();
                        }}
                        className={cn(
                          "rounded-full px-2 py-0.5 text-[10.5px] font-medium transition-colors",
                          wordsPerLine === count ? "bg-white text-black" : "text-neutral-400 hover:text-white"
                        )}
                      >
                        {count}
                      </button>
                    ))}
                  </div>
                  <button
                    type="button"
                    onClick={() => {
                      setWordPop((value) => !value);
                      touch();
                    }}
                    className={cn(
                      "rounded-full border px-2.5 py-1 text-[10.5px] font-medium transition-colors",
                      wordPop ? "border-white/30 bg-white/[0.08] text-white" : "border-white/10 text-neutral-400"
                    )}
                  >
                    Word pop
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      setUppercase((value) => !value);
                      touch();
                    }}
                    className={cn(
                      "inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[10.5px] font-medium transition-colors",
                      uppercase ? "border-white/30 bg-white/[0.08] text-white" : "border-white/10 text-neutral-400"
                    )}
                  >
                    <IconTag size={11} /> UPPERCASE
                  </button>
                </div>
              </section>

              <div className="mt-auto flex flex-wrap items-center gap-2">
                <button
                  type="button"
                  onClick={() => void save()}
                  disabled={saving || !clip.editable}
                  className="inline-flex h-10 items-center gap-2 rounded-full bg-white px-5 text-[12.5px] font-semibold text-black transition-opacity hover:opacity-85 disabled:bg-white/[0.08] disabled:text-neutral-500"
                >
                  {saving ? <IconLoader2 size={14} className="animate-spin" /> : <IconCheck size={14} />}
                  {saving ? "Saving…" : dirty ? "Save changes" : "Saved"}
                </button>
                <button
                  type="button"
                  onClick={reset}
                  disabled={saving}
                  className="inline-flex h-10 items-center gap-1.5 rounded-full border border-white/12 px-4 text-[12px] text-neutral-300 hover:bg-white/[0.05]"
                >
                  <IconRefresh size={13} /> Reset
                </button>
                <button
                  type="button"
                  onClick={() => void api.open(clip.file).catch(() => {})}
                  className="inline-flex h-10 items-center gap-1.5 rounded-full border border-white/12 px-4 text-[12px] text-neutral-300 hover:bg-white/[0.05]"
                >
                  <IconPlayerPlayFilled size={11} /> Show file
                </button>
                <p className="text-[11px] text-neutral-600">
                  Preview is approximate — the ffmpeg render is the source of truth.
                </p>
              </div>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}

export default Editor;
