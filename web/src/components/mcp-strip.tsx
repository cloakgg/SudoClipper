"use client";

import { api, type McpInfo } from "@/lib/api";
import { cn } from "@/lib/utils";
import { IconCheck, IconCopy, IconPlugConnected, IconX } from "@tabler/icons-react";
import { motion } from "motion/react";
import * as React from "react";

const HIDE_KEY = "sudoclipper.mcp.strip.v1";

export function mcpHidden() {
  try {
    return window.localStorage.getItem(HIDE_KEY) === "hidden";
  } catch {
    return false;
  }
}

export function setMcpHidden(hidden: boolean) {
  try {
    if (hidden) window.localStorage.setItem(HIDE_KEY, "hidden");
    else window.localStorage.removeItem(HIDE_KEY);
  } catch {
    /* private mode */
  }
}

/**
 * The MCP line, right on the first screen: the command to paste into an agent and a
 * one-click copy of the JSON block. Lives above the tabs so it is visible on load.
 */
export function McpStrip({ notify }: { notify: (message: string, tone?: "good" | "bad") => void }) {
  const [info, setInfo] = React.useState<McpInfo | null>(null);
  const [copied, setCopied] = React.useState("");
  const [open, setOpen] = React.useState(false);

  React.useEffect(() => {
    api
      .mcp()
      .then(setInfo)
      .catch(() => notify("Could not read the MCP link", "bad"));
  }, [notify]);

  async function copy(text: string, key: string) {
    try {
      await navigator.clipboard?.writeText(text);
      setCopied(key);
      window.setTimeout(() => setCopied(""), 1600);
    } catch {
      notify("Clipboard is blocked in this browser", "bad");
    }
  }

  const snippet = info ? JSON.stringify(info.snippetExe ?? info.snippet, null, 2) : "";

  return (
    <motion.div
      initial={{ opacity: 0, y: -6 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.3, ease: [0.22, 1, 0.36, 1] }}
      className="mb-5 overflow-hidden rounded-2xl border border-white/[0.09] bg-[radial-gradient(ellipse_80%_120%_at_0%_-20%,rgba(255,255,255,0.07),transparent_60%)]"
    >
      <div className="flex flex-wrap items-center gap-2 px-3.5 py-2.5">
        <span className="grid h-7 w-7 shrink-0 place-items-center rounded-lg border border-white/12 text-white">
          <IconPlugConnected size={14} />
        </span>
        <div className="min-w-0 flex-1">
          <p className="text-[12px] font-semibold text-white">
            Connect MCP <span className="ml-1 font-normal text-neutral-500">— use SUDOCLIPPER from Claude, Cursor or any agent</span>
          </p>
          <p className="mt-0.5 truncate font-mono text-[10.5px] text-neutral-400">
            {info?.exe ? `${info.exe} --mcp` : "sudoclipper --mcp"}
          </p>
        </div>

        <button
          type="button"
          onClick={() => void copy(info?.exe ? `${info.exe} --mcp` : "sudoclipper --mcp", "cmd")}
          className="inline-flex h-7 items-center gap-1.5 rounded-full border border-white/12 px-2.5 text-[11px] font-medium text-neutral-200 transition-colors hover:border-white/30 hover:text-white"
        >
          {copied === "cmd" ? <IconCheck size={12} /> : <IconCopy size={12} />}
          {copied === "cmd" ? "copied" : "copy command"}
        </button>

        <button
          type="button"
          onClick={() => void copy(snippet, "json")}
          disabled={!info}
          className={cn(
            "inline-flex h-7 items-center gap-1.5 rounded-full px-3 text-[11px] font-semibold transition-all",
            info ? "bg-white text-black hover:opacity-85" : "bg-white/[0.06] text-neutral-500"
          )}
        >
          {copied === "json" ? <IconCheck size={12} /> : <IconCopy size={12} />}
          {copied === "json" ? "copied" : "copy config"}
        </button>

        <button
          type="button"
          onClick={() => setOpen((value) => !value)}
          className="inline-flex h-7 items-center rounded-full px-2 text-[11px] text-neutral-400 hover:text-white"
        >
          {open ? "hide setup" : "setup"}
        </button>

        <button
          type="button"
          aria-label="Hide the MCP bar"
          onClick={() => {
            setMcpHidden(true);
            window.dispatchEvent(new CustomEvent("sudoclipper:mcp-hidden"));
          }}
          className="grid h-7 w-7 place-items-center rounded-full text-neutral-500 hover:bg-white/[0.06] hover:text-white"
        >
          <IconX size={13} />
        </button>
      </div>

      {open && (
        <div className="grid gap-2 border-t border-white/[0.07] px-3.5 py-3">
          <pre className="sc-scrollbar-thin max-h-44 overflow-auto rounded-xl border border-white/[0.08] bg-black/60 p-3 font-mono text-[10.5px] leading-relaxed text-neutral-300">
            {snippet}
          </pre>
          <p className="text-[10.5px] leading-relaxed text-neutral-500">
            Paste it into <span className="font-mono text-neutral-300">{info?.claudeConfig ?? "claude_desktop_config.json"}</span> or{" "}
            <span className="font-mono text-neutral-300">{info?.cursorConfig ?? "~/.cursor/mcp.json"}</span>, then restart the
            agent. Tools: {info?.tools.map((tool) => tool.name).join(", ") || "reading…"}.
          </p>
        </div>
      )}
    </motion.div>
  );
}

export default McpStrip;
