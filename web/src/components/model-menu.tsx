"use client";

import { ProviderIcon } from "@/components/provider-icons";
import { api, type ModelGroup } from "@/lib/api";
import { cn } from "@/lib/utils";
import { IconCheck, IconChevronDown, IconLoader2, IconRefresh, IconSearch } from "@tabler/icons-react";
import { LayoutGroup, motion } from "motion/react";
import * as React from "react";

type Selection = { provider: string; model: string };

let cache: { at: number; groups: ModelGroup[] } | null = null;

async function loadGroups(force = false): Promise<ModelGroup[]> {
  if (!force && cache && Date.now() - cache.at < 60_000) return cache.groups;
  const result = await api.allModels();
  cache = { at: Date.now(), groups: result.groups ?? [] };
  return cache.groups;
}

export function useModelGroups() {
  const [groups, setGroups] = React.useState<ModelGroup[]>(cache?.groups ?? []);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState("");

  const refresh = React.useCallback(async (force = false) => {
    setLoading(true);
    setError("");
    try {
      setGroups(await loadGroups(force));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not list models");
    } finally {
      setLoading(false);
    }
  }, []);

  React.useEffect(() => {
    void refresh(false);
  }, [refresh]);

  return { groups, loading, error, refresh };
}

const spring = { type: "spring" as const, stiffness: 420, damping: 32, mass: 0.6 };

export function ModelMenu({
  value,
  onChange,
  className,
  compact = false,
}: {
  value: Selection;
  onChange: (next: Selection) => void;
  className?: string;
  compact?: boolean;
}) {
  const { groups, loading, error, refresh } = useModelGroups();
  const [open, setOpen] = React.useState(false);
  const [query, setQuery] = React.useState("");
  const [hovered, setHovered] = React.useState<string | null>(null);
  const [pulse, setPulse] = React.useState(0);
  const [offset, setOffset] = React.useState(0);
  const wrapRef = React.useRef<HTMLDivElement>(null);

  // Keep the panel on screen: in the chat header the trigger sits near the right edge,
  // so a plain left-0 panel used to open outside the viewport.
  React.useLayoutEffect(() => {
    if (!open) return;
    function place() {
      const rect = wrapRef.current?.getBoundingClientRect();
      if (!rect) return;
      const width = Math.min(380, window.innerWidth - 24);
      const left = Math.max(12, Math.min(rect.left, window.innerWidth - width - 12));
      setOffset(left - rect.left);
    }
    place();
    window.addEventListener("resize", place);
    window.addEventListener("scroll", place, true);
    return () => {
      window.removeEventListener("resize", place);
      window.removeEventListener("scroll", place, true);
    };
  }, [open]);

  const providerName = React.useMemo(() => {
    for (const group of groups) if (group.provider === value.provider) return group.providerName;
    return value.provider;
  }, [groups, value.provider]);

  // Keys change in Config; reopening the menu should reflect what is usable right now.
  React.useEffect(() => {
    if (!open) return;
    const stale = !cache || Date.now() - cache.at > 20_000;
    if (stale) void refresh(true);
  }, [open, refresh]);

  React.useEffect(() => {
    if (!open) {
      setHovered(null);
      setQuery("");
      return;
    }
    function onDoc(event: MouseEvent) {
      if (!wrapRef.current?.contains(event.target as Node)) setOpen(false);
    }
    function onKey(event: KeyboardEvent) {
      if (event.key === "Escape") setOpen(false);
    }
    document.addEventListener("mousedown", onDoc);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDoc);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  const q = query.trim().toLowerCase();
  const filtered = groups
    .map((group) => ({
      ...group,
      models: q
        ? group.models.filter(
            (model) =>
              model.id.toLowerCase().includes(q) ||
              model.name.toLowerCase().includes(q) ||
              group.providerName.toLowerCase().includes(q)
          )
        : group.models,
    }))
    .filter((group) => group.models.length > 0);

  const total = groups.reduce((sum, group) => sum + group.models.length, 0);
  const key = `${value.provider}:${value.model}`;
  const hidden = groups.filter((group) => group.models.length === 0 && group.error.trim().length > 0);
  const selectedMissing =
    groups.some((group) => group.models.length > 0) &&
    !groups.some((group) => group.provider === value.provider && group.models.some((model) => model.id === value.model));

  function pick(next: Selection) {
    onChange(next);
    setOpen(false);
    setPulse((count) => count + 1);
  }

  return (
    <div ref={wrapRef} className={cn("relative inline-flex", className)}>
      <motion.button
        type="button"
        onClick={() => setOpen((prev) => !prev)}
        aria-haspopup="listbox"
        aria-expanded={open}
        whileTap={{ scale: 0.975 }}
        className={cn(
          "relative inline-flex max-w-[330px] items-center gap-2 overflow-hidden rounded-full border border-white/[0.09] bg-white/[0.03] px-3 text-[11.5px] text-neutral-300 transition-colors hover:border-white/25 hover:text-white",
          compact ? "h-7" : "h-8",
          open && "border-white/30 text-white"
        )}
      >
        {pulse > 0 && (
          <motion.span
            key={pulse}
            className="pointer-events-none absolute inset-0 rounded-full bg-white"
            initial={{ opacity: 0.26 }}
            animate={{ opacity: 0 }}
            transition={{ duration: 0.55, ease: "easeOut" }}
          />
        )}

        <motion.span
          key={value.provider}
          initial={{ scale: 0.6, opacity: 0, rotate: -20 }}
          animate={{ scale: 1, opacity: 1, rotate: 0 }}
          transition={spring}
          className="relative z-10 inline-flex shrink-0 text-white"
        >
          <ProviderIcon id={value.provider} size={14} />
        </motion.span>

        <motion.span
          key={`label-${providerName}`}
          initial={{ opacity: 0, y: 6 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.18 }}
          className="relative z-10 shrink-0 truncate font-medium"
        >
          {providerName}
        </motion.span>

        <span className="relative z-10 min-w-0 flex-1 overflow-hidden text-left">
          <motion.span
            key={key}
            initial={{ y: 9, opacity: 0 }}
            animate={{ y: 0, opacity: 1 }}
            transition={{ duration: 0.22, ease: [0.22, 1, 0.36, 1] }}
            className="block truncate font-mono text-[11px] text-neutral-500"
          >
            {value.model || "no model"}
          </motion.span>
        </span>

        {selectedMissing && (
          <span
            className="relative z-10 h-1.5 w-1.5 shrink-0 rounded-full bg-neutral-400"
            title="This model is not in the active list — pick one below"
          />
        )}

        <motion.span animate={{ rotate: open ? 180 : 0 }} transition={spring} className="relative z-10 inline-flex opacity-60">
          <IconChevronDown size={13} />
        </motion.span>
      </motion.button>

      {open && (
        <motion.div
            initial={{ opacity: 0, y: -8, scale: 0.97, filter: "blur(6px)" }}
            animate={{ opacity: 1, y: 0, scale: 1, filter: "blur(0px)" }}
            transition={spring}
            style={{ left: offset }}
            className="absolute top-[calc(100%+8px)] z-[120] w-[min(380px,calc(100vw-24px))] overflow-hidden rounded-2xl border border-white/[0.1] bg-[#0b0b0b] shadow-[0_28px_70px_-24px_rgba(0,0,0,0.95)]"
            role="listbox"
          >
            <div className="flex items-center gap-2 border-b border-white/[0.07] px-3 py-2">
              <IconSearch size={13} className="text-neutral-600" />
              <input
                autoFocus
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                placeholder={`Search ${total} models…`}
                className="h-6 w-full bg-transparent text-[12px] text-white outline-none placeholder:text-neutral-600"
              />
              <button
                type="button"
                onClick={() => void refresh(true)}
                className="grid h-6 w-6 place-items-center rounded-md text-neutral-500 hover:bg-white/[0.06] hover:text-white"
                aria-label="Refresh models"
              >
                {loading ? <IconLoader2 size={12} className="animate-spin" /> : <IconRefresh size={12} />}
              </button>
            </div>

            <motion.div
              className="sc-scrollbar-thin max-h-[340px] overflow-y-auto p-1.5"
              initial="hidden"
              animate="show"
              variants={{ show: { transition: { staggerChildren: 0.012 } } }}
            >
              {loading && total === 0 && (
                <div className="grid place-items-center py-8 text-neutral-600">
                  <IconLoader2 size={16} className="animate-spin" />
                </div>
              )}
              {!loading && total === 0 && (
                <p className="px-2 py-6 text-center text-[11.5px] leading-relaxed text-neutral-600">
                  {error || "No usable models yet. Add a key in Config — only connected providers are listed here."}
                </p>
              )}

              <LayoutGroup id="model-menu">
                {filtered.map((group) => (
                  <div key={group.provider} className="mb-1">
                    <div className="flex items-center gap-1.5 px-2 py-1.5 text-[10px] font-semibold uppercase tracking-[0.14em] text-neutral-500">
                      <ProviderIcon id={group.provider} size={12} />
                      {group.providerName}
                      <span className="ml-auto font-mono text-[9.5px] normal-case tracking-normal text-neutral-600">
                        {group.models.length}
                      </span>
                    </div>

                    {group.models.map((model) => {
                      const rowKey = `${group.provider}:${model.id}`;
                      const selected = rowKey === key;
                      const highlighted = hovered === rowKey || (hovered === null && selected);
                      return (
                        <motion.button
                          key={rowKey}
                          type="button"
                          role="option"
                          aria-selected={selected}
                          variants={{
                            hidden: { opacity: 0, y: 4 },
                            show: { opacity: 1, y: 0, transition: { duration: 0.18 } },
                          }}
                          onMouseEnter={() => setHovered(rowKey)}
                          onFocus={() => setHovered(rowKey)}
                          onClick={() => pick({ provider: group.provider, model: model.id })}
                          className="relative flex w-full items-center gap-2 rounded-lg px-2 py-1.5 text-left transition-colors"
                        >
                          {highlighted && (
                            <motion.span
                              layoutId="model-menu-highlight"
                              className="absolute inset-0 rounded-lg bg-white/[0.07] ring-1 ring-white/[0.06]"
                              transition={spring}
                              aria-hidden
                            />
                          )}
                          {selected && (
                            <motion.span
                              layoutId="model-menu-selected"
                              className="absolute inset-0 rounded-lg bg-white/[0.12]"
                              transition={spring}
                              aria-hidden
                            />
                          )}

                          <motion.span
                            layout
                            className={cn(
                              "relative z-10 h-1.5 w-1.5 shrink-0 rounded-full",
                              selected ? "bg-white" : "bg-neutral-700"
                            )}
                          />

                          <span className="relative z-10 min-w-0 flex-1 truncate font-mono text-[11.5px] text-neutral-200">
                            {model.id}
                          </span>

                          {model.note && (
                            <span className="relative z-10 shrink-0 rounded-full border border-white/15 px-1.5 text-[9px] text-neutral-300">
                              {model.note}
                            </span>
                          )}

                          {selected && (
                            <motion.span
                              initial={{ scale: 0.4, opacity: 0, rotate: -30 }}
                              animate={{ scale: 1, opacity: 1, rotate: 0 }}
                              transition={{ type: "spring", stiffness: 500, damping: 24 }}
                              className="relative z-10 inline-flex text-white"
                            >
                              <IconCheck size={12} stroke={3} />
                            </motion.span>
                          )}
                        </motion.button>
                      );
                    })}
                  </div>
                ))}
              </LayoutGroup>

              {groups
                .filter((group) => group.error && group.models.length > 0)
                .map((group) => (
                  <p key={group.provider + "-error"} className="px-2 py-1 text-[10.5px] text-neutral-600">
                    {group.providerName}: {group.error}
                  </p>
                ))}

              {hidden.length > 0 && (
                <p className="px-2 pb-1 pt-2 text-[10px] leading-relaxed text-neutral-600">
                  {hidden.length} provider{hidden.length === 1 ? "" : "s"} hidden — no key or not reachable:{" "}
                  {hidden.map((group) => group.providerName).join(", ")}
                </p>
              )}
            </motion.div>
        </motion.div>
      )}
    </div>
  );
}

export default ModelMenu;
