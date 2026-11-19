"use client";

import type { Config } from "@/lib/api";
import { cn } from "@/lib/utils";
import { IconCheck, IconTypography } from "@tabler/icons-react";
import { motion } from "motion/react";
import * as React from "react";

export const CAPTION_STYLES = [
  { value: "punch", label: "Punch", hint: "Each word pops" },
  { value: "karaoke", label: "Karaoke", hint: "Spoken word lights up" },
  { value: "neon", label: "Neon", hint: "Glowing colour words" },
  { value: "rave", label: "Rave", hint: "Bouncing rainbow" },
  { value: "chrome", label: "Chrome", hint: "Slow metallic zoom" },
  { value: "boxed", label: "Boxed", hint: "White bar, black text" },
  { value: "clean", label: "Clean", hint: "Quiet fade" },
] as const;

const FONTS = [
  { value: "", label: "Auto (style default)" },
  { value: "Arial Black", label: "Arial Black" },
  { value: "Impact", label: "Impact" },
  { value: "Bahnschrift SemiBold", label: "Bahnschrift" },
  { value: "Segoe UI Semibold", label: "Segoe UI" },
  { value: "Verdana", label: "Verdana" },
  { value: "Georgia", label: "Georgia" },
];

type Look = {
  font: string;
  weight: number;
  boxed: boolean;
  karaoke: boolean;
  upper: boolean;
  pop: boolean;
  palette?: string[];
  glow?: boolean;
  tilt?: number;
};

const LOOKS: Record<string, Look> = {
  punch: { font: "Arial Black", weight: 900, boxed: false, karaoke: false, upper: true, pop: true, tilt: 2 },
  karaoke: {
    font: "Bahnschrift SemiBold",
    weight: 700,
    boxed: false,
    karaoke: true,
    upper: true,
    pop: false,
    palette: ["#22d3ee", "#a78bfa", "#ffffff"],
    glow: true,
  },
  neon: {
    font: "Arial Black",
    weight: 900,
    boxed: false,
    karaoke: false,
    upper: true,
    pop: true,
    palette: ["#22d3ee", "#a78bfa", "#ffffff"],
    glow: true,
    tilt: 1,
  },
  rave: {
    font: "Bahnschrift SemiBold",
    weight: 800,
    boxed: false,
    karaoke: false,
    upper: true,
    pop: true,
    palette: ["#fbbf24", "#22d3ee", "#a78bfa", "#f472b6"],
    glow: true,
    tilt: 4,
  },
  chrome: {
    font: "Impact",
    weight: 700,
    boxed: false,
    karaoke: false,
    upper: false,
    pop: false,
    palette: ["#dce7ff", "#ffffff", "#9fb6d6"],
    glow: true,
  },
  boxed: { font: "Impact", weight: 700, boxed: true, karaoke: false, upper: false, pop: false },
  clean: { font: "Segoe UI Semibold", weight: 600, boxed: false, karaoke: false, upper: false, pop: false },
};

export type CaptionDraft = {
  style: string;
  font: string;
  scale: number;
  position: number;
  outline: number;
  wordsPerLine: number;
  uppercase: boolean | null;
  wordPop: boolean | null;
};

export function draftFromConfig(config?: Partial<Config> | null): CaptionDraft {
  return {
    style: config?.captionStyle && LOOKS[config.captionStyle] ? config.captionStyle : "punch",
    font: config?.captionFont ?? "",
    scale: config?.captionScale ?? 1,
    position: config?.captionPosition ?? 0,
    outline: config?.captionOutline ?? 1,
    wordsPerLine: config?.captionWordsPerLine ?? 3,
    uppercase: config?.captionUppercase ?? null,
    wordPop: config?.captionWordPop ?? null,
  };
}

export function patchFromDraft(draft: CaptionDraft): Record<string, unknown> {
  return {
    captionStyle: draft.style,
    captionFont: draft.font,
    captionScale: draft.scale,
    captionPosition: draft.position,
    captionOutline: draft.outline,
    captionWordsPerLine: draft.wordsPerLine,
    captionUppercase: draft.uppercase,
    captionWordPop: draft.wordPop,
  };
}

/** The 9:16 phone used both in the studio and in Config. */
export function CaptionPhone({
  draft,
  text,
  className,
  compact = false,
}: {
  draft: CaptionDraft;
  text: string;
  className?: string;
  compact?: boolean;
}) {
  const look = LOOKS[draft.style] ?? LOOKS.punch;
  const font = draft.font || look.font;
  const upper = draft.uppercase ?? look.upper;
  const pop = draft.wordPop ?? look.pop;

  const words = React.useMemo(() => {
    const list = text.trim().split(/\s+/).filter(Boolean);
    const limit = draft.wordsPerLine > 0 ? Math.max(1, draft.wordsPerLine) : Math.max(3, list.length);
    const sliced = list.slice(0, Math.min(limit, 8));
    return (upper ? sliced.map((word) => word.toUpperCase()) : sliced).map((word) => word);
  }, [text, draft.wordsPerLine, upper]);

  const [active, setActive] = React.useState(0);

  React.useEffect(() => {
    setActive(0);
  }, [words.length, draft.style]);

  React.useEffect(() => {
    if (!(look.karaoke || pop) || words.length < 2) return;
    const timer = window.setInterval(() => setActive((index) => (index + 1) % words.length), 560);
    return () => window.clearInterval(timer);
  }, [words.length, look.karaoke, pop]);

  const fontSize = (compact ? 17 : 22) * Math.min(1.6, Math.max(0.7, draft.scale));
  const bottom = Math.max(7, 20 - draft.position / 22);
  const outline = Math.max(0, 1.7 * draft.outline);
  const palette = look.palette ?? [];
  const glow = (index: number) => (look.glow ? `0 0 10px ${palette[index % Math.max(1, palette.length)] ?? "#22d3ee"}, 0 0 20px ${palette[index % Math.max(1, palette.length)] ?? "#22d3ee"}` : "");

  return (
    <div
      className={cn(
        "caption-phone relative mx-auto aspect-[9/16] w-full overflow-hidden rounded-[24px] border border-white/10 bg-black",
        compact ? "max-w-[150px]" : "max-w-[220px]",
        className
      )}
    >
      <div className="caption-preview-bg absolute inset-0" />
      <div className="absolute inset-0 bg-gradient-to-b from-black/25 via-transparent to-black/55" />
      <div className="absolute inset-x-0 top-0 flex items-center justify-between px-2.5 pt-2 text-[8px] uppercase tracking-[0.18em] text-white/45">
        <span>9:16</span>
        <span>preview</span>
      </div>

      <div className="absolute inset-x-2.5 flex justify-center" style={{ bottom: `${bottom}%` }}>
        <div
          className={cn(
            "flex max-w-full flex-wrap items-baseline justify-center gap-x-1.5 gap-y-0.5 px-1.5 py-0.5 text-center",
            look.boxed && "bg-white"
          )}
          style={{
            fontFamily: font,
            fontWeight: look.weight,
            fontSize,
            lineHeight: 1.06,
            letterSpacing: "0.01em",
            color: look.boxed ? "#000" : "#fff",
            textShadow: look.boxed
              ? "none"
              : `-${outline}px 0 #000, ${outline}px 0 #000, 0 -${outline}px #000, 0 ${outline}px #000, ${glow(0)}`.trim(),
          }}
        >
          {words.map((word, index) => {
            const isActive = index === active;
            const tint = palette.length ? palette[index % palette.length] : "#fff";
            const tilt = isActive && pop && look.tilt ? (index % 2 === 0 ? look.tilt : -look.tilt) : 0;
            return (
              <span
                key={`${word}-${index}`}
                style={{
                  display: "inline-block",
                  color: look.boxed ? "#000" : look.karaoke && !isActive ? "#8a8a8a" : palette.length && !look.karaoke ? tint : look.karaoke && !isActive ? "#8a8a8a" : tint,
                  textShadow: look.boxed ? "none" : glow(index) || "none",
                  transform: isActive && pop ? `scale(1.18) rotate(${tilt}deg)` : "scale(1)",
                  transition: "transform 160ms ease, color 200ms ease, text-shadow 200ms ease",
                }}
              >
                {word}
              </span>
            );
          })}
        </div>
      </div>
    </div>
  );
}

function Slider({
  label,
  value,
  display,
  min,
  max,
  step,
  onChange,
}: {
  label: string;
  value: number;
  display: string;
  min: number;
  max: number;
  step: number;
  onChange: (value: number) => void;
}) {
  return (
    <label className="block">
      <span className="mb-1 flex items-center justify-between text-[10.5px] uppercase tracking-wider text-neutral-600">
        {label} <span className="font-mono normal-case tracking-normal text-neutral-400">{display}</span>
      </span>
      <input
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        onChange={(event) => onChange(Number(event.target.value))}
        className="h-1.5 w-full appearance-none rounded-full bg-white/10"
        style={{ accentColor: "var(--accent-2)" }}
      />
    </label>
  );
}

export function CaptionStudio({
  config,
  onChange,
}: {
  config: Config | null;
  onChange: (patch: Record<string, unknown>) => Promise<void>;
}) {
  const [draft, setDraft] = React.useState<CaptionDraft>(() => draftFromConfig(config));
  const [text, setText] = React.useState("this is how your captions will look");
  const savedRef = React.useRef(JSON.stringify(draftFromConfig(config)));

  // Config saves round-trip through the server; only adopt values we did not send.
  React.useEffect(() => {
    const next = draftFromConfig(config);
    const serialized = JSON.stringify(next);
    if (serialized !== savedRef.current) {
      savedRef.current = serialized;
      setDraft(next);
    }
  }, [
    config?.captionStyle,
    config?.captionFont,
    config?.captionScale,
    config?.captionPosition,
    config?.captionOutline,
    config?.captionWordsPerLine,
    config?.captionUppercase,
    config?.captionWordPop,
  ]);

  const serializedDraft = JSON.stringify(draft);
  React.useEffect(() => {
    if (serializedDraft === savedRef.current) return;
    const timer = window.setTimeout(() => {
      savedRef.current = serializedDraft;
      void onChange(patchFromDraft(draft));
    }, 420);
    return () => window.clearTimeout(timer);
  }, [serializedDraft]);

  const look = LOOKS[draft.style] ?? LOOKS.punch;
  const upper = draft.uppercase ?? look.upper;
  const pop = draft.wordPop ?? look.pop;

  return (
    <section className="mx-auto w-full max-w-3xl">
      <div className="rounded-3xl border border-white/[0.08] bg-white/[0.015] p-4 sm:p-5">
        <div className="mb-4 flex flex-wrap items-center gap-2">
          <span className="inline-flex items-center gap-1.5 text-[12.5px] font-semibold text-white">
            <IconTypography size={14} /> Caption studio
          </span>
          <span className="rounded-full border border-white/10 bg-white/[0.03] px-2 py-0.5 text-[10.5px] text-neutral-400">
            {CAPTION_STYLES.find((style) => style.value === draft.style)?.label} ·{" "}
            {Math.round(draft.scale * 100)}% · {draft.wordsPerLine > 0 ? `${draft.wordsPerLine} words` : "full lines"}
          </span>
          <span className="ml-auto text-[10.5px] text-neutral-600">saved automatically</span>
        </div>

        <div className="grid gap-4 sm:grid-cols-[200px_minmax(0,1fr)]">
          <div>
            <CaptionPhone draft={draft} text={text} />
            <input
              value={text}
              maxLength={90}
              onChange={(event) => setText(event.target.value)}
              placeholder="Preview text"
              className="mt-2 h-8 w-full rounded-lg border border-white/[0.08] bg-black/40 px-2 text-[11px] text-neutral-300 outline-none placeholder:text-neutral-600 focus:border-white/25"
            />
          </div>

          <div className="flex flex-col gap-3">
            <div className="grid grid-cols-2 gap-2">
              {CAPTION_STYLES.map((style) => {
                const styleLook = LOOKS[style.value];
                const selected = draft.style === style.value;
                return (
                  <button
                    key={style.value}
                    type="button"
                    onClick={() => setDraft((prev) => ({ ...prev, style: style.value }))}
                    className={cn(
                      "relative flex flex-col items-start gap-1 rounded-xl border p-2 text-left transition-colors",
                      selected
                        ? "border-white/40 bg-white/[0.08]"
                        : "border-white/[0.07] bg-white/[0.015] hover:border-white/20 hover:bg-white/[0.04]"
                    )}
                  >
                    <span className="flex w-full items-center gap-1.5">
                      <span className="text-[11px] font-semibold text-white">{style.label}</span>
                      {selected && (
                        <motion.span
                          layoutId="caption-style-check"
                          className="ml-auto text-white"
                          initial={{ scale: 0.4, opacity: 0 }}
                          animate={{ scale: 1, opacity: 1 }}
                        >
                          <IconCheck size={11} stroke={3} />
                        </motion.span>
                      )}
                    </span>
                    <span className="text-[9.5px] leading-tight text-neutral-500">{style.hint}</span>
                    <span
                      className={cn(
                        "mt-0.5 truncate rounded px-1 py-0.5 text-[9px]",
                        styleLook.boxed ? "bg-white text-black" : "text-white"
                      )}
                      style={{ fontFamily: styleLook.font, fontWeight: styleLook.weight }}
                    >
                      {styleLook.upper ? "BIG WORDS" : "Big words"}
                    </span>
                  </button>
                );
              })}
            </div>

            <div className="grid gap-3 sm:grid-cols-2">
              <label className="block">
                <span className="mb-1 block text-[10.5px] uppercase tracking-wider text-neutral-600">Font</span>
                <select
                  value={draft.font}
                  onChange={(event) => setDraft((prev) => ({ ...prev, font: event.target.value }))}
                  className="h-8 w-full rounded-lg border border-white/[0.08] bg-black/50 px-2 text-[11.5px] text-white outline-none"
                >
                  {FONTS.map((font) => (
                    <option key={font.value} value={font.value} className="bg-[#111]">
                      {font.label}
                    </option>
                  ))}
                </select>
              </label>
              <Slider
                label="Size"
                value={draft.scale}
                display={`${draft.scale.toFixed(2)}×`}
                min={0.7}
                max={1.6}
                step={0.05}
                onChange={(value) => setDraft((prev) => ({ ...prev, scale: value }))}
              />
              <Slider
                label="Height"
                value={draft.position}
                display={`${draft.position > 0 ? "+" : ""}${draft.position}`}
                min={-260}
                max={320}
                step={10}
                onChange={(value) => setDraft((prev) => ({ ...prev, position: value }))}
              />
              <Slider
                label="Outline"
                value={draft.outline}
                display={`${draft.outline.toFixed(1)}×`}
                min={0.3}
                max={2}
                step={0.1}
                onChange={(value) => setDraft((prev) => ({ ...prev, outline: value }))}
              />
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <span className="text-[10.5px] uppercase tracking-wider text-neutral-600">Per line</span>
              <div className="inline-flex items-center gap-0.5 rounded-full border border-white/[0.08] bg-white/[0.03] p-0.5">
                {[2, 3, 4, 5, 6].map((count) => (
                  <button
                    key={count}
                    type="button"
                    onClick={() => setDraft((prev) => ({ ...prev, wordsPerLine: count }))}
                    className={cn(
                      "rounded-full px-2 py-0.5 text-[10.5px] font-medium transition-colors",
                      draft.wordsPerLine === count ? "bg-white text-black" : "text-neutral-400 hover:text-white"
                    )}
                  >
                    {count}
                  </button>
                ))}
              </div>

              <button
                type="button"
                onClick={() =>
                  setDraft((prev) => ({
                    ...prev,
                    uppercase: upper ? false : true,
                  }))
                }
                className={cn(
                  "rounded-full border px-2.5 py-1 text-[10.5px] font-medium transition-colors",
                  upper ? "border-white/30 bg-white/[0.08] text-white" : "border-white/10 text-neutral-400"
                )}
              >
                UPPERCASE
              </button>

              <button
                type="button"
                onClick={() => setDraft((prev) => ({ ...prev, wordPop: !pop }))}
                className={cn(
                  "rounded-full border px-2.5 py-1 text-[10.5px] font-medium transition-colors",
                  pop ? "border-white/30 bg-white/[0.08] text-white" : "border-white/10 text-neutral-400"
                )}
              >
                Word pop
              </button>

              {(draft.uppercase !== null || draft.wordPop !== null) && (
                <button
                  type="button"
                  onClick={() => setDraft((prev) => ({ ...prev, uppercase: look.upper, wordPop: look.pop }))}
                  className="text-[10.5px] text-neutral-600 underline underline-offset-2 hover:text-neutral-300"
                >
                  reset to style
                </button>
              )}
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}

export default CaptionStudio;
