"use client";

import { cn } from "@/lib/utils";
import { motion, useReducedMotion } from "motion/react";
import * as React from "react";

/** Word-by-word reveal with a soft blur, used for headings. */
export function SplitText({
  text,
  className,
  delay = 0,
  stagger = 0.045,
  sheen = false,
}: {
  text: string;
  className?: string;
  delay?: number;
  stagger?: number;
  sheen?: boolean;
}) {
  const reduce = useReducedMotion();
  const words = text.split(" ");

  // Reduced motion still gets a fade (no travel, no blur) so the copy keeps its rhythm.
  const hidden = reduce
    ? { opacity: 0, y: "0%", filter: "blur(0px)" }
    : { opacity: 0, y: "105%", filter: "blur(5px)" };
  const shown = { opacity: 1, y: "0%", filter: "blur(0px)" };
  const transition = (index: number) => ({
    duration: reduce ? 0.32 : 0.62,
    delay: delay + index * stagger,
    ease: [0.22, 1, 0.36, 1] as const,
  });

  return (
    <span className={cn("inline", className)}>
      {words.map((word, index) => (
        <span key={`${word}-${index}`} className="inline-block overflow-hidden align-bottom">
          <motion.span
            className={cn("inline-block", sheen && "sc-sheen")}
            initial={hidden}
            animate={shown}
            transition={transition(index)}
          >
            {word}
          </motion.span>
          {index < words.length - 1 && <span>&nbsp;</span>}
        </span>
      ))}
    </span>
  );
}

/** Letters that lift on hover — for small labels and the wordmark. */
export function HoverLetters({ text, className }: { text: string; className?: string }) {
  return (
    <span className={cn("inline-flex", className)}>
      {Array.from(text).map((letter, index) => (
        <motion.span
          key={`${letter}-${index}`}
          className="inline-block"
          whileHover={{ y: -2, opacity: 1 }}
          transition={{ type: "spring", stiffness: 500, damping: 24 }}
        >
          {letter === " " ? "\u00A0" : letter}
        </motion.span>
      ))}
    </span>
  );
}

/** Number that counts up when it mounts or changes. */
export function CountUp({
  value,
  format,
  className,
}: {
  value: number;
  format?: (value: number) => string;
  className?: string;
}) {
  const reduce = useReducedMotion();
  const [shown, setShown] = React.useState(reduce ? value : 0);

  React.useEffect(() => {
    if (reduce) {
      setShown(value);
      return;
    }
    const start = performance.now();
    const from = 0;
    let frame = 0;
    const duration = 900;

    const tick = (now: number) => {
      const t = Math.min(1, (now - start) / duration);
      const eased = 1 - Math.pow(1 - t, 3);
      setShown(from + (value - from) * eased);
      if (t < 1) frame = requestAnimationFrame(tick);
    };
    frame = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frame);
  }, [value, reduce]);

  const text = format ? format(shown) : Math.round(shown).toLocaleString();
  return <span className={className}>{text}</span>;
}

export function formatCompact(value: number): string {
  if (!Number.isFinite(value)) return "0";
  if (Math.abs(value) >= 1_000_000_000) return (value / 1_000_000_000).toFixed(1).replace(/\.0$/, "") + "B";
  if (Math.abs(value) >= 1_000_000) return (value / 1_000_000).toFixed(1).replace(/\.0$/, "") + "M";
  if (Math.abs(value) >= 1_000) return (value / 1_000).toFixed(1).replace(/\.0$/, "") + "K";
  return Math.round(value).toLocaleString();
}

/** Newest chat reply fades in word by word, like a stream. */
export function StreamText({
  text,
  animate,
  className,
}: {
  text: string;
  animate: boolean;
  className?: string;
}) {
  const reduce = useReducedMotion();
  if (!animate || reduce) return <span className={className}>{text}</span>;

  const lines = text.split("\n");
  let index = 0;

  return (
    <span className={className}>
      {lines.map((line, lineIndex) => (
        <React.Fragment key={lineIndex}>
          {lineIndex > 0 && <br />}
          {line.split(" ").map((word, wordIndex) => {
            const delay = Math.min(1.1, index++ * 0.014);
            return (
              <motion.span
                key={wordIndex}
                className="inline-block"
                initial={{ opacity: 0, y: 3 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.28, delay, ease: "easeOut" }}
              >
                {word}
                {wordIndex < line.split(" ").length - 1 ? "\u00A0" : ""}
              </motion.span>
            );
          })}
        </React.Fragment>
      ))}
    </span>
  );
}
