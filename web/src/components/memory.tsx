import { ClipCard, Player } from "@/components/clip";
import { Editor } from "@/components/editor";
import { Card, Pill } from "@/components/ui/bits";
import { api, type Clip } from "@/lib/api";
import { SplitText } from "@/components/ui/split-text";
import { cn, formatDate } from "@/lib/utils";
import {
  IconBrain,
  IconCheck,
  IconFolderOpen,
  IconLoader2,
  IconSearch,
  IconTrash,
  IconX,
} from "@tabler/icons-react";
import { AnimatePresence, motion } from "motion/react";
import * as React from "react";

export function MemoryTab({
  refreshKey,
  notify,
}: {
  refreshKey: number;
  notify: (message: string, tone?: "good" | "bad") => void;
}) {
  const [clips, setClips] = React.useState<Clip[]>([]);
  const [outDir, setOutDir] = React.useState("");
  const [loading, setLoading] = React.useState(true);
  const [query, setQuery] = React.useState("");
  const [playing, setPlaying] = React.useState<Clip | null>(null);
  const [pendingDelete, setPendingDelete] = React.useState<Clip | null>(null);
  const [editing, setEditing] = React.useState<Clip | null>(null);
  const [selected, setSelected] = React.useState<Set<string>>(new Set());
  const [bulkConfirm, setBulkConfirm] = React.useState(false);
  const [bulkBusy, setBulkBusy] = React.useState(false);

  const load = React.useCallback(async () => {
    try {
      const data = await api.memory();
      setClips(data.clips);
      setOutDir(data.outDir);
    } catch (error) {
      notify(error instanceof Error ? error.message : "Could not load memory", "bad");
    } finally {
      setLoading(false);
    }
  }, [notify]);

  React.useEffect(() => {
    void load();
  }, [load, refreshKey]);

  const filtered = React.useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return clips;
    return clips.filter((clip) =>
      [clip.title, clip.sourceTitle, clip.model, clip.provider].some((value) =>
        (value ?? "").toLowerCase().includes(q)
      )
    );
  }, [clips, query]);

  const totalMinutes = Math.round(clips.reduce((sum, clip) => sum + (clip.duration || 0), 0) / 60);

  async function remove(clip: Clip) {
    setPendingDelete(null);
    try {
      await api.deleteClip(clip.id);
      setClips((prev) => prev.filter((item) => item.id !== clip.id));
      setSelected((prev) => {
        const next = new Set(prev);
        next.delete(clip.id);
        return next;
      });
      notify("Clip deleted");
    } catch (error) {
      notify(error instanceof Error ? error.message : "Could not delete", "bad");
    }
  }

  function toggle(id: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function selectAll() {
    if (selected.size === filtered.length && filtered.length > 0) setSelected(new Set());
    else setSelected(new Set(filtered.map((clip) => clip.id)));
  }

  async function removeSelected() {
    const ids = Array.from(selected);
    if (ids.length === 0) return;
    setBulkBusy(true);
    try {
      for (const id of ids) await api.deleteClip(id);
      setClips((prev) => prev.filter((item) => !selected.has(item.id)));
      setSelected(new Set());
      setBulkConfirm(false);
      notify(`${ids.length} clip${ids.length === 1 ? "" : "s"} deleted`);
    } catch (error) {
      notify(error instanceof Error ? error.message : "Could not delete", "bad");
    } finally {
      setBulkBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-[26px] font-semibold tracking-[-0.02em] text-white sm:text-[30px]">
            <SplitText text="Memory" stagger={0.05} />
          </h1>
          <p className="mt-1 text-[13px] text-neutral-500">
            Every clip SUDOCLIPPER has rendered on this machine, in one place.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Pill>{clips.length} clips</Pill>
          {totalMinutes > 0 && <Pill>{totalMinutes} min total</Pill>}
          {selected.size > 0 && <Pill tone="live">{selected.size} selected</Pill>}
          {clips.length > 0 && (
            <button
              type="button"
              onClick={selectAll}
              className="inline-flex items-center gap-1.5 rounded-full border border-white/10 bg-white/[0.04] px-3 py-1.5 text-[11.5px] font-medium text-neutral-200 transition-colors hover:bg-white/[0.08]"
            >
              <IconCheck size={12} />
              {selected.size === filtered.length && filtered.length > 0 ? "Clear" : "Select all"}
            </button>
          )}
          {selected.size > 0 && (
            <button
              type="button"
              onClick={() => setBulkConfirm(true)}
              className="inline-flex items-center gap-1.5 rounded-full bg-white px-3 py-1.5 text-[11.5px] font-semibold text-black transition-opacity hover:opacity-85"
            >
              <IconTrash size={12} /> Delete selected
            </button>
          )}
          <button
            type="button"
            onClick={() => void api.open(outDir).catch(() => notify("Could not open the folder", "bad"))}
            className="inline-flex items-center gap-1.5 rounded-full border border-white/10 bg-white/[0.04] px-3 py-1.5 text-[11.5px] font-medium text-white transition-colors hover:bg-white/[0.08]"
          >
            <IconFolderOpen size={13} /> Open clips folder
          </button>
        </div>
      </div>

      <div className="relative max-w-sm">
        <IconSearch size={15} className="absolute left-3 top-1/2 -translate-y-1/2 text-neutral-600" />
        <input
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="Search clips, sources, models…"
          className="h-9 w-full rounded-xl border border-white/[0.07] bg-white/[0.02] pl-9 pr-3 text-[12.5px] text-white outline-none transition-colors placeholder:text-neutral-600 focus:border-white/35"
        />
      </div>

      {loading ? (
        <div className="grid place-items-center py-24 text-neutral-600">
          <IconLoader2 size={22} className="animate-spin" />
        </div>
      ) : filtered.length === 0 ? (
        <Card className="grid place-items-center gap-3 py-16 text-center">
          <span className="grid h-12 w-12 place-items-center rounded-2xl border border-white/[0.07] bg-white/[0.03] text-neutral-500">
            <IconBrain size={22} />
          </span>
          <div>
            <p className="text-[13.5px] font-medium text-white">{clips.length ? "Nothing matches that" : "No clips yet"}</p>
            <p className="mt-1 text-[12px] text-neutral-500">
              {clips.length ? "Try another search." : "Cut your first video and it will show up here."}
            </p>
          </div>
          {!clips.length && (
            <a
              href="#clipper"
              className="mt-1 inline-flex items-center gap-1.5 rounded-full bg-white px-4 py-1.5 text-[12px] font-semibold text-black"
            >
              Open the clipper
            </a>
          )}
        </Card>
      ) : (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          <AnimatePresence initial={false}>
            {filtered.map((clip, index) => (
              <div key={clip.id} className="group/card relative flex flex-col gap-1.5">
                <button
                  type="button"
                  onClick={() => toggle(clip.id)}
                  aria-label={selected.has(clip.id) ? "Deselect clip" : "Select clip"}
                  className={cn(
                    "absolute left-2 top-2 z-10 grid h-6 w-6 place-items-center rounded-full border backdrop-blur-md transition-all",
                    selected.has(clip.id)
                      ? "border-white bg-white text-black opacity-100"
                      : "border-white/50 bg-black/50 text-transparent opacity-0 group-hover/card:opacity-100"
                  )}
                >
                  <IconCheck size={13} stroke={3} />
                </button>
                <ClipCard
                  clip={clip}
                  onPlay={setPlaying}
                  onDelete={setPendingDelete}
                  onEdit={setEditing}
                  index={index}
                />
                <p className="truncate px-1 text-[10.5px] text-neutral-600" title={clip.sourceTitle}>
                  {clip.sourceTitle || "Local file"} · {formatDate(clip.createdAt)}
                </p>
              </div>
            ))}
          </AnimatePresence>
        </div>
      )}

      <AnimatePresence>
        {pendingDelete && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 z-[125] grid place-items-center bg-black/70 p-4 backdrop-blur-sm"
            onClick={() => setPendingDelete(null)}
          >
            <motion.div
              initial={{ scale: 0.96, y: 10 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.98 }}
              onClick={(event) => event.stopPropagation()}
              className="w-full max-w-sm rounded-2xl border border-white/10 bg-[#101014] p-5 shadow-2xl"
            >
              <div className="flex items-start gap-3">
                <span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-white/10 text-white">
                  <IconTrash size={17} />
                </span>
                <div className="min-w-0">
                  <p className="text-[13.5px] font-semibold text-white">Delete this clip?</p>
                  <p className="mt-1 truncate text-[12px] text-neutral-500">{pendingDelete.title}</p>
                  <p className="mt-2 text-[11.5px] text-neutral-600">The mp4 and thumbnail are removed from disk.</p>
                </div>
              </div>
              <div className="mt-5 flex justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setPendingDelete(null)}
                  className="rounded-xl border border-white/10 px-3.5 py-2 text-[12.5px] font-medium text-neutral-300 hover:bg-white/[0.05]"
                >
                  Keep it
                </button>
                <button
                  type="button"
                  onClick={() => void remove(pendingDelete)}
                  className="inline-flex items-center gap-1.5 rounded-xl bg-white px-3.5 py-2 text-[12.5px] font-semibold text-black hover:opacity-85"
                >
                  <IconX size={14} /> Delete
                </button>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      <AnimatePresence>
        {bulkConfirm && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 z-[125] grid place-items-center bg-black/70 p-4 backdrop-blur-sm"
            onClick={() => setBulkConfirm(false)}
          >
            <motion.div
              initial={{ scale: 0.96, y: 10 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.98 }}
              onClick={(event) => event.stopPropagation()}
              className="w-full max-w-sm rounded-2xl border border-white/10 bg-[#101014] p-5 shadow-2xl"
            >
              <div className="flex items-start gap-3">
                <span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-white/10 text-white">
                  <IconTrash size={17} />
                </span>
                <div className="min-w-0">
                  <p className="text-[13.5px] font-semibold text-white">
                    Delete {selected.size} clip{selected.size === 1 ? "" : "s"}?
                  </p>
                  <p className="mt-2 text-[11.5px] text-neutral-600">
                    The mp4 files and thumbnails are removed from disk.
                  </p>
                </div>
              </div>
              <div className="mt-5 flex justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setBulkConfirm(false)}
                  className="rounded-xl border border-white/10 px-3.5 py-2 text-[12.5px] font-medium text-neutral-300 hover:bg-white/[0.05]"
                >
                  Keep them
                </button>
                <button
                  type="button"
                  onClick={() => void removeSelected()}
                  disabled={bulkBusy}
                  className="inline-flex items-center gap-1.5 rounded-xl bg-white px-3.5 py-2 text-[12.5px] font-semibold text-black hover:opacity-85 disabled:opacity-60"
                >
                  {bulkBusy ? <IconLoader2 size={13} className="animate-spin" /> : <IconTrash size={13} />}
                  Delete
                </button>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      <Editor
        clip={editing}
        open={!!editing}
        onClose={() => setEditing(null)}
        onSaved={(updated) => {
          setClips((prev) => prev.map((item) => (item.id === updated.id ? updated : item)));
          void load();
        }}
        notify={notify}
      />

      <Player clip={playing} onClose={() => setPlaying(null)} />
    </div>
  );
}
