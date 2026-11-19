"use client";

import { cn } from "@/lib/utils";
import { IconArrowUp, IconLink } from "@tabler/icons-react";
import { AnimatePresence, motion } from "motion/react";
import * as React from "react";

const MATTE_NOISE = `url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='240' height='240'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='2' stitchTiles='stitch'/><feColorMatrix values='0 0 0 0 1 0 0 0 0 1 0 0 0 0 1 0 0 0 0.65 0'/></filter><rect width='100%25' height='100%25' filter='url(%23n)'/></svg>")`;

export interface LiquidInputProps {
  value: string;
  onChange: (value: string) => void;
  onSubmit: () => void;
  placeholder?: string;
  maxHeight?: number;
  busy?: boolean;
  disabled?: boolean;
  /** Left side of the toolbar — the model picker lives here. */
  toolbar?: React.ReactNode;
  /** Small line under the box. */
  hint?: React.ReactNode;
  className?: string;
  inputRef?: React.RefObject<HTMLTextAreaElement | null>;
}

/**
 * The chat box: matte panel, drifting gradient edge, one line per thought.
 * Dropping text or a link into it inserts it instead of doing nothing.
 */
export function LiquidInput({
  value,
  onChange,
  onSubmit,
  placeholder = "Message SUDOCLIPPER…",
  maxHeight = 200,
  busy = false,
  disabled = false,
  toolbar,
  hint,
  className,
  inputRef,
}: LiquidInputProps) {
  const localRef = React.useRef<HTMLTextAreaElement>(null);
  const areaRef = inputRef ?? localRef;
  const [focused, setFocused] = React.useState(false);
  const [dragOver, setDragOver] = React.useState(false);
  const dragDepth = React.useRef(0);

  React.useEffect(() => {
    const area = areaRef.current;
    if (!area) return;
    area.style.height = "auto";
    area.style.height = Math.min(Math.max(area.scrollHeight, 72), maxHeight) + "px";
  }, [value, maxHeight, areaRef]);

  const canSend = value.trim().length > 0 && !busy && !disabled;

  function submit() {
    if (!canSend) return;
    onSubmit();
  }

  function insert(text: string) {
    const clean = text.trim();
    if (!clean) return;
    onChange(value.trim().length > 0 ? `${value.trimEnd()} ${clean}` : clean);
    areaRef.current?.focus();
  }

  function onDragEnter(event: React.DragEvent) {
    event.preventDefault();
    dragDepth.current += 1;
    if (event.dataTransfer.types.includes("text/plain") || event.dataTransfer.types.includes("text/uri-list"))
      setDragOver(true);
  }

  function onDragLeave(event: React.DragEvent) {
    event.preventDefault();
    dragDepth.current -= 1;
    if (dragDepth.current <= 0) {
      dragDepth.current = 0;
      setDragOver(false);
    }
  }

  function onDrop(event: React.DragEvent) {
    event.preventDefault();
    dragDepth.current = 0;
    setDragOver(false);
    const link = event.dataTransfer.getData("text/uri-list");
    const text = event.dataTransfer.getData("text/plain");
    insert(link || text);
  }

  return (
    <div
      className={cn("relative w-full", className)}
      onDragEnter={onDragEnter}
      onDragLeave={onDragLeave}
      onDragOver={(event) => event.preventDefault()}
      onDrop={onDrop}
    >
      <div className="relative flex w-full flex-col items-center">
        {/* Aurora edge that lights up while a link is dragged over the box */}
        <motion.div
          aria-hidden
          className="pointer-events-none absolute -inset-[2px] rounded-[30px] blur-[10px]"
          animate={{ opacity: dragOver ? 0.85 : 0 }}
          transition={{ duration: 0.25, ease: [0.22, 1, 0.36, 1] }}
          style={{
            background:
              "conic-gradient(from 0deg, rgba(255,255,255,0.9), rgba(255,255,255,0.15), rgba(255,255,255,0.75), rgba(255,255,255,0.08), rgba(255,255,255,0.9))",
            willChange: "opacity",
          }}
        />

        <div
          className={cn(
            "group relative isolate flex w-full flex-col gap-2 overflow-hidden rounded-[28px] p-3 transition-all duration-150 ease-in-out",
            "border border-white/[0.08] bg-[radial-gradient(ellipse_90%_70%_at_50%_-10%,#161619_0%,#0a0a0b_72%)]",
            "shadow-[0_1px_2px_rgba(0,0,0,0.4),0_18px_50px_-24px_rgba(0,0,0,0.9)]",
            "hover:border-white/[0.14] focus-within:border-white/[0.18]",
            dragOver && "border-white/40"
          )}
        >
          <div
            aria-hidden
            className="pointer-events-none absolute inset-0 opacity-[0.05] mix-blend-overlay"
            style={{ backgroundImage: MATTE_NOISE, backgroundSize: "240px 240px" }}
          />
          <div
            aria-hidden
            className="pointer-events-none absolute inset-0 bg-[linear-gradient(to_bottom,rgba(255,255,255,0.045)_0%,transparent_28%,transparent_72%,rgba(0,0,0,0.35)_100%)]"
          />

          {/* 1px gradient edge, slowly orbiting */}
          <div
            aria-hidden
            className="lmi-border pointer-events-none absolute inset-0 z-10 rounded-[28px] opacity-40 transition-opacity duration-500 group-focus-within:opacity-100 group-hover:opacity-80"
            style={{
              padding: "1px",
              WebkitMask: "linear-gradient(#fff 0 0) content-box, linear-gradient(#fff 0 0)",
              WebkitMaskComposite: "xor",
              maskComposite: "exclude",
              background:
                "conic-gradient(from var(--lmi-angle, 0deg), transparent 62%, rgba(255,255,255,0.55) 80%, #ffffff 94%, transparent 100%)",
            }}
          />
          <style>{`
            @property --lmi-angle {
              syntax: '<angle>';
              initial-value: 0deg;
              inherits: false;
            }
            .lmi-border { animation: lmi-spin 6s linear infinite; }
            @keyframes lmi-spin { to { --lmi-angle: 360deg; } }
          `}</style>

          <div className="relative z-20 w-full min-w-0">
            <textarea
              ref={areaRef}
              value={value}
              onChange={(event) => onChange(event.target.value)}
              onFocus={() => setFocused(true)}
              onBlur={() => setFocused(false)}
              onKeyDown={(event) => {
                if (event.key === "Enter" && !event.shiftKey) {
                  event.preventDefault();
                  submit();
                }
              }}
              placeholder={placeholder}
              aria-label="Chat input"
              disabled={disabled}
              rows={1}
              className="w-full resize-none bg-transparent px-3 py-1 text-[13.5px] leading-relaxed text-[var(--foreground)] outline-none placeholder:text-neutral-600"
              style={{ minHeight: "72px", maxHeight }}
            />
          </div>

          <div className="relative z-20 mt-auto flex w-full flex-wrap items-center gap-2 px-1">
            <div className="flex min-w-0 items-center gap-1.5">{toolbar}</div>

            <div className="ml-auto flex items-center gap-2">
              <span className="hidden text-[10px] text-neutral-600 sm:inline">
                {focused ? "Enter sends · Shift+Enter breaks the line" : ""}
              </span>
              <motion.button
                type="button"
                onClick={submit}
                disabled={!canSend}
                aria-label="Send message"
                whileTap={{ scale: 0.92 }}
                transition={{ type: "spring", stiffness: 400, damping: 22 }}
                className={cn(
                  "ml-1 grid h-8 w-8 shrink-0 place-items-center rounded-full transition-colors",
                  canSend
                    ? "bg-white text-black shadow-[0_2px_10px_-2px_rgba(255,255,255,0.45)] hover:bg-neutral-200"
                    : "bg-white/[0.07] text-neutral-600"
                )}
                style={{ willChange: "transform" }}
              >
                <IconArrowUp size={16} stroke={2.5} />
              </motion.button>
            </div>
          </div>

          <AnimatePresence>
            {dragOver && (
              <motion.div
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                transition={{ duration: 0.18 }}
                className="pointer-events-none absolute inset-0 z-30 grid place-items-center rounded-[28px] bg-black/50 backdrop-blur-[2px]"
              >
                <div className="flex items-center gap-2 rounded-full border border-white/15 bg-[#161616] px-5 py-2.5 text-[12.5px] font-semibold text-white shadow-xl">
                  <IconLink size={15} />
                  Drop a link or text
                </div>
              </motion.div>
            )}
          </AnimatePresence>
        </div>
      </div>

      {hint && <div className="mt-1.5 px-2">{hint}</div>}
    </div>
  );
}

export default LiquidInput;
