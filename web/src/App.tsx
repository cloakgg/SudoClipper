import { ChatTab } from "@/components/chat";
import { ClipperTab } from "@/components/clipper";
import { ConfigTab } from "@/components/config-tab";
import { McpStrip, mcpHidden, setMcpHidden } from "@/components/mcp-strip";
import { MemoryTab } from "@/components/memory";
import { BrandMark } from "@/components/ui/brand-mark";
import { FloatingDock } from "@/components/ui/floating-dock";
import { ThemeToggle, type Theme } from "@/components/ui/theme-toggle";
import { Pill } from "@/components/ui/bits";
import { api, type Config, type Status } from "@/lib/api";
import { cn } from "@/lib/utils";
import {
  IconBrain,
  IconCircleDot,
  IconMessageChatbot,
  IconPlugConnected,
  IconScissors,
  IconSettings,
  IconTerminal2,
} from "@tabler/icons-react";
import { AnimatePresence, motion } from "motion/react";
import * as React from "react";

type Tab = "clipper" | "chat" | "memory" | "config";
const TABS: Tab[] = ["clipper", "chat", "memory", "config"];

function tabFromHash(): Tab {
  const hash = window.location.hash.replace("#", "");
  return (TABS as string[]).includes(hash) ? (hash as Tab) : "clipper";
}

const THEME_KEY = "sudoclipper:theme";

function startTheme(): Theme {
  try {
    return window.localStorage.getItem(THEME_KEY) === "color" ? "color" : "mono";
  } catch {
    return "mono";
  }
}

export default function App() {
  const [tab, setTab] = React.useState<Tab>(tabFromHash);
  const [status, setStatus] = React.useState<Status | null>(null);
  const [config, setConfig] = React.useState<Config | null>(null);
  const [toast, setToast] = React.useState<{ message: string; tone: "good" | "bad" } | null>(null);
  const [refreshKey, setRefreshKey] = React.useState(0);
  const [bootError, setBootError] = React.useState("");
  const [showMcp, setShowMcp] = React.useState(() => !mcpHidden());
  const [theme, setTheme] = React.useState<Theme>(startTheme);

  React.useEffect(() => {
    document.documentElement.dataset.theme = theme;
    try {
      window.localStorage.setItem(THEME_KEY, theme);
    } catch {
      /* private mode */
    }
  }, [theme]);

  // The bar hides itself through a DOM event so the strip stays a leaf component.
  React.useEffect(() => {
    const hide = () => setShowMcp(false);
    window.addEventListener("sudoclipper:mcp-hidden", hide);
    return () => window.removeEventListener("sudoclipper:mcp-hidden", hide);
  }, []);

  const notify = React.useCallback((message: string, tone: "good" | "bad" = "good") => {
    setToast({ message, tone });
    window.setTimeout(() => setToast(null), 3400);
  }, []);

  const loadStatus = React.useCallback(async () => {
    try {
      const next = await api.status();
      setStatus(next);
      setBootError("");
    } catch (error) {
      setBootError(error instanceof Error ? error.message : "Cannot reach the local server.");
    }
  }, []);

  const loadConfig = React.useCallback(async () => {
    try {
      setConfig(await api.config());
    } catch {
      /* surfaced by status */
    }
  }, []);

  React.useEffect(() => {
    void loadStatus();
    void loadConfig();
  }, [loadStatus, loadConfig]);

  React.useEffect(() => {
    if (!status || status.ready) return;
    const timer = window.setInterval(() => void loadStatus(), 2500);
    return () => window.clearInterval(timer);
  }, [status, loadStatus]);

  React.useEffect(() => {
    const onHash = () => setTab(tabFromHash());
    window.addEventListener("hashchange", onHash);
    if (!window.location.hash) window.location.hash = "clipper";
    return () => window.removeEventListener("hashchange", onHash);
  }, []);

  const updateConfig = React.useCallback(
    async (patch: Record<string, unknown>) => {
      setConfig((prev) => (prev ? { ...prev, ...(patch as Partial<Config>) } : prev));
      try {
        const saved = await api.saveConfig(patch);
        setConfig((prev) => (prev ? { ...prev, outDir: saved.outDir } : prev));
      } catch (error) {
        notify(error instanceof Error ? error.message : "Could not save", "bad");
        void loadConfig();
      }
    },
    [loadConfig, notify]
  );

  const items = [
    {
      title: "Clipper",
      icon: (
        <span className="nav-glyph" data-tab="clipper">
          <IconScissors className="h-full w-full" />
        </span>
      ),
      href: "#clipper",
      active: tab === "clipper",
    },
    {
      title: "Chat",
      icon: (
        <span className="nav-glyph" data-tab="chat">
          <IconMessageChatbot className="h-full w-full" />
        </span>
      ),
      href: "#chat",
      active: tab === "chat",
    },
    {
      title: "Memory",
      icon: (
        <span className="nav-glyph" data-tab="memory">
          <IconBrain className="h-full w-full" />
        </span>
      ),
      href: "#memory",
      active: tab === "memory",
    },
    {
      title: "Config",
      icon: (
        <span className="nav-glyph" data-tab="config">
          <IconSettings className="h-full w-full" />
        </span>
      ),
      href: "#config",
      active: tab === "config",
    },
  ];

  const missing = status ? status.tools.filter((tool) => !tool.found) : [];

  return (
    <div className="relative flex min-h-full flex-col">
      <header className="sticky top-0 z-40 border-b border-white/[0.07] bg-[#050505]/85 backdrop-blur-xl">
        <div className="mx-auto flex h-16 max-w-6xl items-center gap-3 px-4 sm:px-6">
          <div className="flex items-center gap-2.5">
            <BrandMark size={34} />
            <div className="leading-none">
              <div className="sc-brand text-[15px] font-bold tracking-[0.14em]">SUDOCLIPPER</div>
              <div className="mt-1 hidden text-[9.5px] font-medium uppercase tracking-[0.24em] text-neutral-500 sm:block">
                local clipping studio
              </div>
            </div>
          </div>

          <div className="ml-auto flex items-center gap-2">
            <ThemeToggle
              theme={theme}
              onToggle={() => setTheme((current) => (current === "color" ? "mono" : "color"))}
            />
            {status?.bootstrapping ? (
              <Pill tone="warn">
                <IconCircleDot size={11} className="animate-pulse" /> setting up tools
              </Pill>
            ) : missing.length ? (
              <Pill tone="bad">{missing.map((t) => t.name).join(" + ")} missing</Pill>
            ) : (
              <Pill tone="good">
                <IconCircleDot size={11} /> ready
              </Pill>
            )}
            <button
              type="button"
              onClick={() => {
                setMcpHidden(false);
                setShowMcp(true);
              }}
              className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[11px] font-medium transition-colors ${
                showMcp
                  ? "border-white/25 bg-white/[0.07] text-white"
                  : "border-white/10 bg-white/[0.03] text-neutral-400 hover:text-white"
              }`}
              title="MCP server for your AI agent"
            >
              <IconPlugConnected size={11} /> MCP
            </button>
            <Pill tone="default" className="hidden sm:inline-flex">
              <IconTerminal2 size={11} /> 127.0.0.1
            </Pill>
          </div>
        </div>
      </header>

      <main className="relative z-10 mx-auto w-full max-w-6xl flex-1 px-4 pb-40 pt-8 sm:px-6 sm:pt-10">
        {showMcp && <McpStrip notify={notify} />}

        {bootError && (
          <div className="mb-6 rounded-2xl border border-white/25 bg-white/[0.06] p-4 text-[13px] text-neutral-200">
            {bootError}
          </div>
        )}

        {missing.length > 0 && !status?.bootstrapping && (
          <div className="mb-6 flex flex-wrap items-center gap-3 rounded-2xl border border-white/20 bg-white/[0.04] px-4 py-3 text-[12.5px] text-neutral-200">
            <span>
              SUDOCLIPPER needs {missing.map((t) => t.name).join(" and ")} to download and cut video.
            </span>
            <button
              type="button"
              onClick={async () => {
                notify("Fetching the toolchain…");
                try {
                  await api.bootstrap();
                  await loadStatus();
                  notify("Toolchain ready");
                } catch (error) {
                  notify(error instanceof Error ? error.message : "Setup failed", "bad");
                }
              }}
              className="ml-auto rounded-full bg-white px-3 py-1 text-[11.5px] font-semibold text-black transition-opacity hover:opacity-80"
            >
              Set up now
            </button>
          </div>
        )}

        {/* No exit waiting: mode="wait" left the old tab on screen when its exit
            animation did not report back, which made the dock feel dead. */}
        <motion.div
          key={tab}
          initial={{ opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.22, ease: [0.22, 1, 0.36, 1] }}
        >
            {tab === "clipper" && (
              <ClipperTab
                status={status}
                config={config}
                onConfigChange={updateConfig}
                notify={notify}
                onFinished={() => setRefreshKey((key) => key + 1)}
              />
            )}
            {tab === "chat" && (
              <ChatTab
                status={status}
                config={config}
                onChooseModel={(provider, model) => updateConfig({ chatProvider: provider, chatModel: model, provider, model })}
                onApplied={loadConfig}
                notify={notify}
              />
            )}
            {tab === "memory" && <MemoryTab refreshKey={refreshKey} notify={notify} />}
            {tab === "config" && (
              <ConfigTab
                status={status}
                config={config}
                onSaved={loadConfig}
                onStatusRefresh={loadStatus}
                notify={notify}
              />
            )}
        </motion.div>
      </main>

      <div className="pointer-events-none fixed inset-x-0 bottom-5 z-50 flex justify-center px-4">
        <div className="pointer-events-auto">
          <FloatingDock items={items} />
        </div>
      </div>

      <AnimatePresence>
        {toast && (
          <motion.div
            initial={{ opacity: 0, y: 14, scale: 0.97 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 10, scale: 0.98 }}
            className={cn(
              "fixed bottom-24 right-4 z-[130] max-w-xs rounded-xl border px-4 py-2.5 text-[12.5px] shadow-2xl backdrop-blur-xl sm:right-6",
              toast.tone === "good"
                ? "border-white/12 bg-[#111]/95 text-neutral-200"
                : "border-white bg-white text-black"
            )}
          >
            {toast.message}
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
