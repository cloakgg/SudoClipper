"use client";

import { api, type AnalyticsPlatform, type ChannelStats } from "@/lib/api";
import { CountUp, formatCompact } from "@/components/ui/split-text";
import { Card, SectionLabel } from "@/components/ui/bits";
import { cn } from "@/lib/utils";
import {
  IconAlertTriangle,
  IconBrandInstagram,
  IconBrandTiktok,
  IconBrandX,
  IconBrandYoutube,
  IconCheck,
  IconCopy,
  IconExternalLink,
  IconLoader2,
  IconPlugConnected,
  IconRefresh,
  IconTrash,
} from "@tabler/icons-react";
import { AnimatePresence, motion } from "motion/react";
import * as React from "react";

const ICONS: Record<string, React.ComponentType<{ size?: number; className?: string }>> = {
  youtube: IconBrandYoutube,
  tiktok: IconBrandTiktok,
  instagram: IconBrandInstagram,
  x: IconBrandX,
};

function money(value: number, currency: string) {
  try {
    return new Intl.NumberFormat(undefined, {
      style: "currency",
      currency: currency || "USD",
      maximumFractionDigits: 2,
    }).format(value);
  } catch {
    return value.toFixed(2);
  }
}

function Stat({ label, value, hint }: { label: string; value: React.ReactNode; hint?: string }) {
  return (
    <div className="rounded-xl border border-white/[0.07] bg-white/[0.02] px-3 py-2">
      <p className="text-[9.5px] uppercase tracking-[0.14em] text-neutral-600">{label}</p>
      <p className="mt-1 text-[16px] font-semibold leading-none text-white tabular-nums">{value}</p>
      {hint && <p className="mt-1 text-[10px] text-neutral-600">{hint}</p>}
    </div>
  );
}

function Stats({ stats }: { stats: ChannelStats }) {
  const updated = stats.updatedAt ? new Date(stats.updatedAt) : null;
  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-baseline gap-x-2">
        <p className="text-[14px] font-semibold text-white">{stats.name || "Connected channel"}</p>
        {stats.handle && <span className="font-mono text-[11px] text-neutral-500">{stats.handle}</span>}
        {updated && (
          <span className="ml-auto text-[10px] text-neutral-600">
            synced {updated.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}
          </span>
        )}
      </div>

      <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
        <Stat label="followers" value={<CountUp value={stats.subscribers} format={formatCompact} />} />
        <Stat label="total views" value={<CountUp value={stats.views} format={formatCompact} />} />
        <Stat label="likes" value={<CountUp value={stats.likes} format={formatCompact} />} />
        <Stat label="posts" value={<CountUp value={stats.videos} format={formatCompact} />} />
        <Stat
          label="est. income"
          value={stats.estimatedRevenue > 0 ? <CountUp value={stats.estimatedRevenue} format={(v) => money(v, stats.currency)} /> : "—"}
          hint={stats.estimatedRevenue > 0 ? "last 30 days" : "needs a monetised channel"}
        />
        <Stat
          label="views · 30d"
          value={stats.views30d > 0 ? <CountUp value={stats.views30d} format={formatCompact} /> : "—"}
        />
      </div>

      {stats.recent.length > 0 && (
        <div className="rounded-xl border border-white/[0.07] bg-white/[0.015]">
          {stats.recent.slice(0, 5).map((video) => (
            <div key={video.id} className="flex items-center gap-2 border-b border-white/[0.05] px-3 py-2 last:border-0">
              <span className="min-w-0 flex-1 truncate text-[11.5px] text-neutral-300">{video.title || video.id}</span>
              <span className="shrink-0 font-mono text-[10.5px] text-neutral-400">{formatCompact(video.views)} views</span>
              <span className="hidden shrink-0 font-mono text-[10.5px] text-neutral-600 sm:block">
                ♥ {formatCompact(video.likes)}
              </span>
            </div>
          ))}
        </div>
      )}

      {stats.note && <p className="text-[10.5px] leading-relaxed text-neutral-600">{stats.note}</p>}
    </div>
  );
}

export function AnalyticsSection({
  notify,
}: {
  notify: (message: string, tone?: "good" | "bad") => void;
}) {
  const [platforms, setPlatforms] = React.useState<AnalyticsPlatform[]>([]);
  const [loading, setLoading] = React.useState(true);
  const [busy, setBusy] = React.useState<string>("");
  const [waiting, setWaiting] = React.useState<string>("");
  const [editing, setEditing] = React.useState<string>("");
  const [creds, setCreds] = React.useState<Record<string, { id: string; secret: string }>>({});
  const [copied, setCopied] = React.useState("");

  const load = React.useCallback(async () => {
    try {
      const result = await api.analytics();
      setPlatforms(result.platforms);
    } catch (error) {
      notify(error instanceof Error ? error.message : "Could not read analytics", "bad");
    } finally {
      setLoading(false);
    }
  }, [notify]);

  React.useEffect(() => {
    void load();
  }, [load]);

  React.useEffect(() => {
    if (!waiting) return;
    const timer = window.setInterval(async () => {
      try {
        const result = await api.analytics();
        setPlatforms(result.platforms);
        const target = result.platforms.find((platform) => platform.id === waiting);
        if (target?.linked) {
          setWaiting("");
          setEditing("");
          notify(`${target.stats?.name || target.name} connected`);
        }
      } catch {
        /* keep waiting */
      }
    }, 3000);
    return () => window.clearInterval(timer);
  }, [waiting, notify]);

  async function connect(platform: AnalyticsPlatform) {
    const entry = creds[platform.id];
    const clientId = entry?.id || platform.clientId;
    const secret = entry?.secret;
    if (!clientId || (!platform.hasSecret && !secret)) {
      setEditing(platform.id);
      notify("Add the OAuth client id and secret first", "bad");
      return;
    }

    setBusy(platform.id);
    try {
      if (secret) await api.analyticsCredentials(platform.id, clientId, secret);
      const started = await api.analyticsStart(platform.id);
      window.open(started.url, "_blank", "noopener,width=520,height=680");
      setWaiting(platform.id);
      notify(`Approve access in the tab that just opened (${platform.name})`);
      setCreds((prev) => ({ ...prev, [platform.id]: { id: clientId, secret: "" } }));
    } catch (error) {
      notify(error instanceof Error ? error.message : "Could not start the connection", "bad");
    } finally {
      setBusy("");
    }
  }

  async function saveCredentials(platform: AnalyticsPlatform) {
    const entry = creds[platform.id];
    if (!entry?.id || !entry?.secret) {
      notify("Both the client id and the secret are needed", "bad");
      return;
    }
    setBusy(platform.id);
    try {
      await api.analyticsCredentials(platform.id, entry.id, entry.secret);
      setEditing("");
      notify("Credentials saved — now connect");
      await load();
      void connect({ ...platform, clientId: entry.id, hasSecret: true });
    } catch (error) {
      notify(error instanceof Error ? error.message : "Could not save", "bad");
    } finally {
      setBusy("");
    }
  }

  async function refresh(platform: AnalyticsPlatform) {
    setBusy(platform.id);
    try {
      const result = await api.analyticsRefresh(platform.id);
      await load();
      notify(result.error ? result.error : "Stats refreshed", result.error ? "bad" : "good");
    } catch (error) {
      notify(error instanceof Error ? error.message : "Could not refresh", "bad");
    } finally {
      setBusy("");
    }
  }

  async function disconnect(platform: AnalyticsPlatform) {
    setBusy(platform.id);
    try {
      await api.analyticsDisconnect(platform.id);
      await load();
      notify(`${platform.name} disconnected`);
    } catch (error) {
      notify(error instanceof Error ? error.message : "Could not disconnect", "bad");
    } finally {
      setBusy("");
    }
  }

  async function copy(text: string, key: string) {
    await navigator.clipboard?.writeText(text);
    setCopied(key);
    window.setTimeout(() => setCopied(""), 1400);
  }

  return (
    <section>
      <SectionLabel hint="OAuth — tokens stay on this computer">Analytics</SectionLabel>

      <div className="flex flex-col gap-3">
        {loading && (
          <Card className="grid place-items-center py-8">
            <IconLoader2 size={16} className="animate-spin text-neutral-500" />
          </Card>
        )}

        {!loading &&
          platforms.map((platform) => {
            const Icon = ICONS[platform.id] ?? IconPlugConnected;
            const entry = creds[platform.id] ?? { id: platform.clientId ?? "", secret: "" };
            const stats = platform.stats;

            return (
              <Card key={platform.id} className="flex flex-col gap-3">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="grid h-8 w-8 place-items-center rounded-xl border border-white/10 text-white">
                    <Icon size={16} />
                  </span>
                  <div className="min-w-0">
                    <p className="flex items-center gap-2 text-[13px] font-semibold text-white">
                      {platform.name}
                      {platform.linked && (
                        <span className="rounded-full border border-white/20 bg-white/[0.06] px-1.5 py-0.5 text-[9px] uppercase tracking-[0.12em] text-neutral-300">
                          linked
                        </span>
                      )}
                    </p>
                    <p className="font-mono text-[10px] text-neutral-600">{platform.scope}</p>
                  </div>

                  <div className="ml-auto flex items-center gap-1.5">
                    {!platform.linked && platform.supported && (
                      <button
                        type="button"
                        onClick={() => void connect(platform)}
                        disabled={busy === platform.id}
                        className="inline-flex h-8 items-center gap-1.5 rounded-xl bg-white px-3 text-[11.5px] font-semibold text-black transition-opacity hover:opacity-85 disabled:opacity-60"
                      >
                        {busy === platform.id ? (
                          <IconLoader2 size={13} className="animate-spin" />
                        ) : (
                          <IconPlugConnected size={13} />
                        )}
                        {entry.id || platform.clientId ? "Connect" : "Set up"}
                      </button>
                    )}
                    {platform.linked && (
                      <>
                        <button
                          type="button"
                          onClick={() => void refresh(platform)}
                          disabled={busy === platform.id}
                          className="grid h-8 w-8 place-items-center rounded-xl border border-white/10 text-neutral-300 hover:bg-white/[0.05]"
                          title="Refresh stats"
                        >
                          {busy === platform.id ? (
                            <IconLoader2 size={13} className="animate-spin" />
                          ) : (
                            <IconRefresh size={13} />
                          )}
                        </button>
                        <button
                          type="button"
                          onClick={() => void disconnect(platform)}
                          className="grid h-8 w-8 place-items-center rounded-xl border border-white/10 text-neutral-400 hover:bg-white/[0.05] hover:text-white"
                          title="Disconnect"
                        >
                          <IconTrash size={13} />
                        </button>
                      </>
                    )}
                  </div>
                </div>

                {!platform.supported && (
                  <p className="rounded-xl border border-white/[0.07] bg-white/[0.02] px-3 py-2 text-[11px] leading-relaxed text-neutral-500">
                    {platform.note}
                  </p>
                )}

                {platform.error && platform.supported && (
                  <p className="flex items-start gap-2 rounded-xl border border-white/20 bg-white/[0.04] px-3 py-2 text-[11px] leading-relaxed text-neutral-300">
                    <IconAlertTriangle size={13} className="mt-0.5 shrink-0" />
                    {platform.error}
                  </p>
                )}

                {platform.linked && stats && <Stats stats={stats} />}

                {platform.linked && !stats && (
                  <p className="text-[11px] text-neutral-500">Connected — refresh to pull the numbers.</p>
                )}

                {waiting === platform.id && (
                  <p className="flex items-center gap-2 rounded-xl border border-white/15 bg-white/[0.03] px-3 py-2 text-[11px] text-neutral-300">
                    <IconLoader2 size={12} className="animate-spin" />
                    Waiting for you to approve access in the other tab…
                  </p>
                )}

                {platform.supported && (!platform.linked || editing === platform.id) && (
                  <div className="flex flex-col gap-2 rounded-xl border border-white/[0.07] bg-white/[0.02] p-3">
                    <p className="text-[10.5px] leading-relaxed text-neutral-500">
                      Create an OAuth app and add this exact redirect URL:
                    </p>
                    <button
                      type="button"
                      onClick={() => void copy(platform.redirectUri, platform.id + "-url")}
                      className="flex items-center gap-2 break-all rounded-lg border border-white/[0.08] bg-black/40 px-2 py-1.5 text-left font-mono text-[10px] text-neutral-300 hover:border-white/20"
                    >
                      <span className="min-w-0 flex-1">{platform.redirectUri}</span>
                      {copied === platform.id + "-url" ? <IconCheck size={11} /> : <IconCopy size={11} />}
                    </button>

                    <div className="grid gap-2 sm:grid-cols-2">
                      <input
                        value={entry.id}
                        onChange={(event) =>
                          setCreds((prev) => ({
                            ...prev,
                            [platform.id]: { id: event.target.value, secret: prev[platform.id]?.secret ?? "" },
                          }))
                        }
                        placeholder="Client ID"
                        className="h-8 rounded-lg border border-white/[0.08] bg-black/40 px-2 font-mono text-[11px] text-white outline-none placeholder:text-neutral-600 focus:border-white/25"
                      />
                      <input
                        value={entry.secret}
                        type="password"
                        onChange={(event) =>
                          setCreds((prev) => ({
                            ...prev,
                            [platform.id]: { id: prev[platform.id]?.id ?? platform.clientId ?? "", secret: event.target.value },
                          }))
                        }
                        placeholder={platform.hasSecret ? "Secret saved — leave blank to keep" : "Client secret"}
                        className="h-8 rounded-lg border border-white/[0.08] bg-black/40 px-2 font-mono text-[11px] text-white outline-none placeholder:text-neutral-600 focus:border-white/25"
                      />
                    </div>

                    <div className="flex flex-wrap items-center gap-2">
                      <button
                        type="button"
                        onClick={() => void saveCredentials(platform)}
                        disabled={busy === platform.id}
                        className="inline-flex h-8 items-center gap-1.5 rounded-lg bg-white px-3 text-[11px] font-semibold text-black hover:opacity-85 disabled:opacity-60"
                      >
                        {busy === platform.id ? <IconLoader2 size={12} className="animate-spin" /> : <IconCheck size={12} />}
                        Save and connect
                      </button>
                      <span className="text-[10px] text-neutral-600">Stored in analytics.json on this machine.</span>
                      <span className="ml-auto inline-flex items-center gap-1 text-[10px] text-neutral-600">
                        <IconExternalLink size={10} /> opens {platform.name} consent
                      </span>
                    </div>
                  </div>
                )}

                {platform.supported && !platform.linked && (
                  <p className="text-[10.5px] leading-relaxed text-neutral-600">{platform.note}</p>
                )}
              </Card>
            );
          })}

        <AnimatePresence>
          <motion.p
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            className="px-1 text-[10.5px] leading-relaxed text-neutral-600"
          >
            Income is the platform's own estimate (YouTube Analytics for the last 30 days). TikTok does not expose
            creator revenue through its API, so it stays blank there. Tokens never leave this computer.
          </motion.p>
        </AnimatePresence>
      </div>
    </section>
  );
}

export default AnalyticsSection;
