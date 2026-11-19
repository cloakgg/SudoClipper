"use client";

import { cn } from "@/lib/utils";
import { IconLink, IconLoader2, IconScissors } from "@tabler/icons-react";
import { AnimatePresence, motion } from "motion/react";
import * as React from "react";

export interface VideoInputProps {
  placeholder?: string;
  onSubmit?: (value: string) => void;
  busy?: boolean;
  disabled?: boolean;
  className?: string;
}

function hostOf(value: string) {
  try {
    const url = new URL(value.startsWith("http") ? value : `https://${value}`);
    return url.hostname.replace(/^www\./, "");
  } catch {
    return "";
  }
}

/**
 * One box. Paste the link, hit enter. Nothing else.
 */
export function VideoInput({
  placeholder = "Paste a YouTube or video link…",
  onSubmit,
  busy = false,
  disabled = false,
  className,
}: VideoInputProps) {
  const [value, setValue] = React.useState("");
  const [focused, setFocused] = React.useState(false);
  const inputRef = React.useRef<HTMLInputElement>(null);

  const host = hostOf(value);
  const canSubmit = !disabled && !busy && value.trim().length > 0;

  function submit() {
    if (!canSubmit) return;
    onSubmit?.(value.trim());
    setValue("");
  }

  return (
    <div className={cn("relative w-full", className)}>
      <motion.div
        aria-hidden
        className="pointer-events-none absolute -inset-[1px] rounded-[22px]"
        animate={{ opacity: focused ? 0.55 : 0 }}
        transition={{ duration: 0.35, ease: [0.22, 1, 0.36, 1] }}
        style={{
          background:
            "linear-gradient(110deg, transparent 10%, rgba(255,255,255,0.55) 45%, rgba(255,255,255,0.08) 70%, transparent 92%)",
          filter: "blur(6px)",
        }}
      />

      <div
        className={cn(
          "relative flex items-center gap-3 rounded-[22px] border bg-[#0a0a0a] px-4 py-3 transition-colors duration-300",
          focused ? "border-white/35" : "border-white/[0.09] hover:border-white/20",
          disabled && "opacity-60"
        )}
      >
        <motion.span
          animate={{ rotate: focused ? 0 : -6, opacity: focused ? 1 : 0.55 }}
          transition={{ type: "spring", stiffness: 300, damping: 22 }}
          className="grid h-8 w-8 shrink-0 place-items-center rounded-full border border-white/10 text-neutral-300"
        >
          <IconLink size={15} stroke={1.8} />
        </motion.span>

        <input
          ref={inputRef}
          value={value}
          onChange={(event) => setValue(event.target.value)}
          onFocus={() => setFocused(true)}
          onBlur={() => setFocused(false)}
          onKeyDown={(event) => {
            if (event.key === "Enter") {
              event.preventDefault();
              submit();
            }
          }}
          placeholder={placeholder}
          aria-label="Video link"
          spellCheck={false}
          autoComplete="off"
          disabled={disabled}
          className="min-w-0 flex-1 bg-transparent font-mono text-[14px] leading-6 text-[var(--foreground)] outline-none placeholder:font-sans placeholder:text-neutral-600"
        />

        <AnimatePresence initial={false}>
          {host && !busy && (
            <motion.span
              initial={{ opacity: 0, x: 6 }}
              animate={{ opacity: 1, x: 0 }}
              exit={{ opacity: 0, x: 6 }}
              className="hidden shrink-0 rounded-full border border-white/10 px-2.5 py-1 text-[10.5px] font-medium uppercase tracking-wider text-neutral-500 sm:inline-flex"
            >
              {host}
            </motion.span>
          )}
        </AnimatePresence>

        <motion.button
          type="button"
          onClick={submit}
          disabled={!canSubmit}
          aria-label="Make clips"
          whileTap={{ scale: 0.9 }}
          whileHover={canSubmit ? { scale: 1.04 } : undefined}
          transition={{ type: "spring", stiffness: 400, damping: 22 }}
          className={cn(
            "grid h-10 w-10 shrink-0 place-items-center rounded-full transition-colors",
            canSubmit
              ? "bg-white text-black shadow-[0_0_0_1px_rgba(255,255,255,0.4),0_10px_30px_-12px_rgba(255,255,255,0.5)]"
              : "bg-white/[0.06] text-neutral-600"
          )}
        >
          {busy ? <IconLoader2 size={17} className="animate-spin" /> : <IconScissors size={17} stroke={1.9} />}
        </motion.button>
      </div>
    </div>
  );
}

export default VideoInput;
