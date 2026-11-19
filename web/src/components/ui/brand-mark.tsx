import { cn } from "@/lib/utils";
import * as React from "react";

/**
 * The wordmark's logo: a clip/play glyph filled with the live theme accents.
 * In the monochrome theme it is white; the colour theme lights it up and spins
 * a conic ring behind it.
 */
export function BrandMark({ size = 34, className }: { size?: number; className?: string }) {
  const rawId = React.useId();
  const id = `sc-mark-${rawId.replace(/[^a-zA-Z0-9]/g, "")}`;

  return (
    <span
      className={cn("brand-mark relative grid shrink-0 place-items-center rounded-[11px]", className)}
      style={{ width: size, height: size }}
    >
      <span className="brand-mark-ring" aria-hidden />
      <svg
        viewBox="0 0 32 32"
        width={Math.round(size * 0.62)}
        height={Math.round(size * 0.62)}
        className="relative z-10"
        aria-hidden
      >
        <defs>
          <linearGradient id={id} x1="0" y1="0" x2="1" y2="1">
            <stop offset="0%" stopColor="var(--accent-1)" />
            <stop offset="52%" stopColor="var(--accent-2)" />
            <stop offset="100%" stopColor="var(--accent-3)" />
          </linearGradient>
        </defs>
        <path d="M11.4 4.4 26.4 16 11.4 27.6V4.4Z" fill={`url(#${id})`} />
        <path
          d="M5.6 8.8v14.4"
          stroke={`url(#${id})`}
          strokeWidth="3.4"
          strokeLinecap="round"
        />
      </svg>
    </span>
  );
}

export default BrandMark;
