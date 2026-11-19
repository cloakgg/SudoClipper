"use client";

import { api, type McpInfo } from "@/lib/api";
import { Card, SectionLabel } from "@/components/ui/bits";
import {
  IconCheck,
  IconCopy,
  IconLoader2,
  IconPlugConnected,
  IconTerminal2,
} from "@tabler/icons-react";
import * as React from "react";

export function McpSection({ notify }: { notify: (message: string, tone?: "good" | "bad") => void }) {
  const [info, setInfo] = React.useState<McpInfo | null>(null);
  const [copied, setCopied] = React.useState("");

  React.useEffect(() => {
    api
      .mcp()
      .then(setInfo)
      .catch(() => notify("Could not read the MCP settings", "bad"));
  }, [notify]);

  const snippet = info ? JSON.stringify(info.snippet, null, 2) : "";

  async function copy(text: string, key: string) {
    await navigator.clipboard?.writeText(text);
    setCopied(key);
    window.setTimeout(() => setCopied(""), 1400);
  }

  return (
    <section>
      <SectionLabel hint="drive SUDOCLIPPER from Claude Desktop, Cursor, Cline…">Connect MCP</SectionLabel>
      <Card className="flex flex-col gap-3">
        <div className="flex flex-wrap items-start gap-2">
          <span className="grid h-8 w-8 place-items-center rounded-xl border border-white/10 text-white">
            <IconPlugConnected size={15} />
          </span>
          <div className="min-w-0 flex-1">
            <p className="text-[13px] font-semibold text-white">An MCP server ships inside this app</p>
            <p className="mt-0.5 text-[11px] leading-relaxed text-neutral-500">
              Point your agent at <span className="font-mono text-neutral-300">sudoclipper --mcp</span> and it can clip
              links, list results and change settings for you.
            </p>
          </div>
        </div>

        {!info ? (
          <div className="flex items-center gap-2 text-[11px] text-neutral-500">
            <IconLoader2 size={13} className="animate-spin" /> reading the manifest…
          </div>
        ) : (
          <>
            <button
              type="button"
              onClick={() => void copy(info.command, "command")}
              className="flex items-center gap-2 rounded-xl border border-white/[0.08] bg-black/40 px-3 py-2 text-left font-mono text-[11px] text-neutral-200 hover:border-white/20"
            >
              <IconTerminal2 size={12} />
              <span className="min-w-0 flex-1 truncate">{info.command}</span>
              {copied === "command" ? <IconCheck size={11} /> : <IconCopy size={11} />}
            </button>

            <div className="relative">
              <pre className="sc-scrollbar-thin max-h-[190px] overflow-auto rounded-xl border border-white/[0.08] bg-black/50 p-3 font-mono text-[10.5px] leading-relaxed text-neutral-300">
                {snippet}
              </pre>
              <button
                type="button"
                onClick={() => void copy(snippet, "snippet")}
                className="absolute right-2 top-2 inline-flex items-center gap-1 rounded-lg border border-white/10 bg-black/70 px-2 py-1 text-[10px] text-neutral-300 hover:text-white"
              >
                {copied === "snippet" ? <IconCheck size={10} /> : <IconCopy size={10} />}
                {copied === "snippet" ? "copied" : "copy"}
              </button>
            </div>

            <div className="grid gap-1.5 sm:grid-cols-2">
              {info.tools.map((tool) => (
                <div
                  key={tool.name}
                  className="rounded-lg border border-white/[0.06] bg-white/[0.02] px-2.5 py-1.5"
                >
                  <p className="font-mono text-[10.5px] text-neutral-200">{tool.name}</p>
                  <p className="mt-0.5 text-[10px] leading-relaxed text-neutral-600">{tool.about}</p>
                </div>
              ))}
            </div>

            <p className="text-[10px] leading-relaxed text-neutral-600">
              Paste that JSON into <span className="font-mono">claude_desktop_config.json</span> or{" "}
              <span className="font-mono">~/.cursor/mcp.json</span>, then restart the agent. If{" "}
              <span className="font-mono">sudoclipper</span> is not on its PATH, use{" "}
              <span className="font-mono break-all">{info.exe}</span> as the command.
            </p>
          </>
        )}
      </Card>
    </section>
  );
}

export default McpSection;
