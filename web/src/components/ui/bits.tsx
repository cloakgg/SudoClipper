import { cn } from "@/lib/utils";

export function Pill({
  children,
  tone = "default",
  className,
}: {
  children: React.ReactNode;
  tone?: "default" | "live" | "good" | "warn" | "bad";
  className?: string;
}) {
  const tones = {
    default: "border-white/10 bg-white/[0.04] text-neutral-300",
    live: "border-white/30 bg-white/[0.09] text-white",
    good: "border-white/20 bg-white/[0.06] text-neutral-100",
    warn: "border-white/25 bg-white/[0.03] text-neutral-200",
    bad: "border-white bg-white text-black",
  } as const;

  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[11px] font-medium",
        tones[tone],
        className
      )}
    >
      {children}
    </span>
  );
}

export function SectionLabel({ children, hint }: { children: React.ReactNode; hint?: string }) {
  return (
    <div className="mb-3 flex items-baseline justify-between gap-3">
      <h2 className="sc-label text-[11px] font-semibold uppercase tracking-[0.2em] text-neutral-500">{children}</h2>
      {hint && <span className="text-[11.5px] text-neutral-600">{hint}</span>}
    </div>
  );
}

export function Card({ children, className }: { children: React.ReactNode; className?: string }) {
  return (
    <div
      className={cn(
        "rounded-2xl border border-white/[0.08] bg-[#0a0a0a]/85 p-4 shadow-[0_1px_0_rgba(255,255,255,0.04)_inset] transition-colors duration-300 hover:border-white/[0.14]",
        className
      )}
    >
      {children}
    </div>
  );
}

export function Toggle({
  checked,
  onChange,
  label,
  description,
}: {
  checked: boolean;
  onChange: (value: boolean) => void;
  label: string;
  description?: string;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      onClick={() => onChange(!checked)}
      className="flex w-full items-start gap-3 rounded-xl border border-white/[0.06] bg-white/[0.02] p-3 text-left transition-colors hover:bg-white/[0.04]"
    >
      <span
        className={cn(
          "mt-0.5 flex h-5 w-9 shrink-0 items-center rounded-full border p-0.5 transition-colors",
          checked ? "border-white/50 bg-white" : "border-white/10 bg-white/[0.06]"
        )}
      >
        <span
          className={cn(
            "h-3.5 w-3.5 rounded-full transition-transform duration-200",
            checked ? "translate-x-4 bg-black" : "translate-x-0 bg-white"
          )}
        />
      </span>
      <span className="min-w-0">
        <span className="block text-[13px] font-medium text-white">{label}</span>
        {description && <span className="mt-0.5 block text-[11.5px] leading-relaxed text-neutral-500">{description}</span>}
      </span>
    </button>
  );
}

export function Segmented<T extends string>({
  value,
  options,
  onChange,
  size = "md",
}: {
  value: T;
  options: { value: T; label: string; hint?: string }[];
  onChange: (value: T) => void;
  size?: "sm" | "md";
}) {
  return (
    <div className="inline-flex items-center gap-0.5 rounded-full border border-white/[0.08] bg-white/[0.03] p-0.5">
      {options.map((option) => {
        const active = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            onClick={() => onChange(option.value)}
            className={cn(
              "relative rounded-full font-medium transition-all duration-200",
              size === "sm" ? "px-2.5 py-1 text-[11px]" : "px-3 py-1.5 text-[12px]",
              active ? "bg-white text-black" : "text-neutral-400 hover:text-white"
            )}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}

export function Field({
  label,
  hint,
  children,
}: {
  label: string;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <label className="block">
      <span className="mb-1.5 block text-[12px] font-medium text-neutral-300">{label}</span>
      {children}
      {hint && <span className="mt-1 block text-[11px] text-neutral-600">{hint}</span>}
    </label>
  );
}

export const inputClass =
  "h-9 w-full rounded-xl border border-white/[0.08] bg-black/40 px-3 text-[12.5px] text-white outline-none transition-colors placeholder:text-neutral-600 focus:border-white/35 focus:bg-black/60";
