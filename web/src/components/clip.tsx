import { cn } from "@/lib/utils";
import {
  IconBrandYoutube,
  IconLoader2,
  IconPencil,
  IconPlayerPlayFilled,
  IconTrash,
  IconFolderOpen,
  IconFlame,
} from "@tabler/icons-react";
import { AnimatePresence, motion } from "motion/react";
import * as React from "react";
import { api, type Clip } from "@/lib/api";
import { formatTime, mediaUrl } from "@/lib/utils";

export function ClipCard({
  clip,
  onPlay,
  onDelete,
  onEdit,
  index = 0,
}: {
  clip: Clip;
  onPlay: (clip: Clip) => void;
  onDelete?: (clip: Clip) => void;
  onEdit?: (clip: Clip) => void;
  index?: number;
}) {
  return (
    <motion.div
      layout
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, scale: 0.97 }}
      transition={{ delay: Math.min(index * 0.04, 0.3), duration: 0.35, ease: [0.22, 1, 0.36, 1] }}
      className="group relative overflow-hidden rounded-2xl border border-white/[0.08] bg-[#0b0b0b] transition-colors duration-300 hover:border-white/25"
    >
      <button
        type="button"
        onClick={() => onPlay(clip)}
        className="relative block w-full overflow-hidden"
        style={{ aspectRatio: "9 / 14" }}
      >
        {clip.thumb ? (
          <img
            src={mediaUrl(clip.thumb)}
            alt={clip.title}
            className="h-full w-full object-cover transition-transform duration-500 group-hover:scale-[1.04]"
            loading="lazy"
          />
        ) : (
          <div className="grid h-full w-full place-items-center bg-[radial-gradient(circle_at_50%_20%,#1b1b22,#0b0b0e)] text-neutral-600">
            <IconBrandYoutube size={30} />
          </div>
        )}
        <div className="absolute inset-0 bg-gradient-to-t from-black/85 via-black/10 to-black/20" />

        <div className="absolute left-2 top-2 flex items-center gap-1 rounded-full bg-black/55 px-2 py-0.5 text-[10px] font-semibold text-white/90 backdrop-blur">
          <IconFlame size={11} className="text-white" />
          {Math.round(clip.score * 100)}
        </div>
        {clip.captioned && (
          <div className="absolute right-2 top-2 rounded-full bg-black/55 px-2 py-0.5 text-[10px] font-medium text-white/80 backdrop-blur">
            CC
          </div>
        )}
        <div className="absolute inset-0 grid place-items-center opacity-0 transition-opacity duration-300 group-hover:opacity-100">
          <span className="grid h-11 w-11 place-items-center rounded-full bg-white/15 text-white ring-1 ring-white/30 backdrop-blur">
            <IconPlayerPlayFilled size={18} />
          </span>
        </div>

        <div className="absolute inset-x-0 bottom-0 p-2.5 text-left">
          <p className="line-clamp-2 text-[12.5px] font-semibold leading-snug text-white">{clip.title}</p>
          <p className="mt-1 font-mono text-[10.5px] text-white/60">
            {formatTime(clip.start)}–{formatTime(clip.end)} · {Math.round(clip.duration)}s
          </p>
        </div>
      </button>

      <div className="flex items-center gap-1 border-t border-white/[0.06] px-2 py-1.5">
        <span className="truncate font-mono text-[10px] text-neutral-500">{clip.provider}</span>
        <div className="ml-auto flex items-center gap-0.5">
          <button
            type="button"
            title="Show in folder"
            onClick={() => void api.open(clip.file).catch(() => {})}
            className="grid h-7 w-7 place-items-center rounded-lg text-neutral-500 transition-colors hover:bg-white/[0.06] hover:text-white"
          >
            <IconFolderOpen size={14} />
          </button>
          {onEdit && (
            <button
              type="button"
              title="Edit clip"
              onClick={() => onEdit(clip)}
              className="grid h-7 w-7 place-items-center rounded-lg text-neutral-500 transition-colors hover:bg-white/10 hover:text-white"
            >
              <IconPencil size={13} />
            </button>
          )}
          {onDelete && (
            <button
              type="button"
              title="Delete clip"
              onClick={() => onDelete(clip)}
              className="grid h-7 w-7 place-items-center rounded-lg text-neutral-500 transition-colors hover:bg-white/10 hover:text-white"
            >
              <IconTrash size={14} />
            </button>
          )}
        </div>
      </div>
    </motion.div>
  );
}

export function Player({ clip, onClose }: { clip: Clip | null; onClose: () => void }) {
  React.useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    if (clip) document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [clip, onClose]);

  return (
    <AnimatePresence>
      {clip && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          className="fixed inset-0 z-[120] grid place-items-center bg-black/80 p-4 backdrop-blur-sm"
          onClick={onClose}
        >
          <motion.div
            initial={{ scale: 0.96, y: 12, opacity: 0 }}
            animate={{ scale: 1, y: 0, opacity: 1 }}
            exit={{ scale: 0.98, y: 8, opacity: 0 }}
            transition={{ type: "spring", stiffness: 320, damping: 28 }}
            className="relative flex max-h-[90vh] w-full max-w-3xl gap-4 overflow-hidden rounded-3xl border border-white/10 bg-[#0d0d11] p-3 shadow-2xl"
            onClick={(e) => e.stopPropagation()}
          >
            <video
              key={clip.file}
              src={mediaUrl(clip.file)}
              controls
              autoPlay
              playsInline
              className="max-h-[84vh] rounded-2xl bg-black"
              style={{ aspectRatio: "9 / 16" }}
            />
            <div className="flex min-w-0 flex-1 flex-col gap-3 p-2">
              <div>
                <p className="text-[11px] uppercase tracking-[0.18em] text-neutral-500">Clip</p>
                <h3 className="mt-1 text-lg font-semibold leading-tight text-white">{clip.title}</h3>
              </div>
              {clip.hook && (
                <div className="rounded-xl border border-white/[0.07] bg-white/[0.03] p-3">
                  <p className="text-[11px] uppercase tracking-wider text-neutral-500">Hook</p>
                  <p className="mt-1 text-[13px] leading-relaxed text-neutral-300">“{clip.hook}”</p>
                </div>
              )}
              {clip.reason && (
                <p className="text-[12px] leading-relaxed text-neutral-500">{clip.reason}</p>
              )}
              <dl className="mt-auto grid grid-cols-2 gap-2 text-[11.5px]">
                <Fact label="Source" value={clip.sourceTitle} />
                <Fact label="Model" value={clip.model} />
                <Fact label="Length" value={`${Math.round(clip.duration)}s`} />
                <Fact label="Virality" value={`${Math.round(clip.score * 100)} / 100`} />
              </dl>
              <div className="flex gap-2">
                <button
                  type="button"
                  onClick={() => void api.open(clip.file).catch(() => {})}
                  className="inline-flex flex-1 items-center justify-center gap-1.5 rounded-xl border border-white/10 bg-white/[0.04] px-3 py-2 text-[12.5px] font-medium text-white transition-colors hover:bg-white/[0.08]"
                >
                  <IconFolderOpen size={15} /> Show file
                </button>
                <button
                  type="button"
                  onClick={onClose}
                  className="inline-flex items-center justify-center rounded-xl bg-white px-4 py-2 text-[12.5px] font-semibold text-black transition-opacity hover:opacity-90"
                >
                  Close
                </button>
              </div>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0">
      <dt className="text-[10px] uppercase tracking-wider text-neutral-600">{label}</dt>
      <dd className="truncate text-neutral-300">{value || "—"}</dd>
    </div>
  );
}

export function Spinner({ className }: { className?: string }) {
  return <IconLoader2 className={cn("animate-spin", className)} size={16} />;
}
