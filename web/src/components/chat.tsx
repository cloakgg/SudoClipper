"use client";

import { ModelMenu } from "@/components/model-menu";
import { ProviderIcon } from "@/components/provider-icons";
import { LiquidInput } from "@/components/ui/liquid-input";
import { SplitText, StreamText } from "@/components/ui/split-text";
import { api, type Config, type Status } from "@/lib/api";
import { cn } from "@/lib/utils";
import {
  IconCheck,
  IconCopy,
  IconMessagePlus,
  IconPlayerPlay,
  IconScissors,
  IconSparkles,
} from "@tabler/icons-react";
import { AnimatePresence, motion } from "motion/react";
import * as React from "react";

const CHAT_KEY = "sudoclipper.chat.v1";

type Message = {
  role: "user" | "assistant";
  content: string;
  applied?: Record<string, unknown>;
  jobId?: string | null;
  failed?: boolean;
};

const SUGGESTIONS = [
  { title: "Switch caption style", body: "Use karaoke captions for everything from now on", icon: IconSparkles },
  { title: "Clip a link", body: "Paste a YouTube link and say: clip this in 4 parts", icon: IconScissors },
  { title: "Tune the cut", body: "Make clips between 20 and 45 seconds, 3 per video", icon: IconCheck },
  { title: "Pick a provider", body: "Which free provider should I use for fast clips?", icon: IconMessagePlus },
];

export function ChatTab({
  status,
  config,
  onChooseModel,
  onApplied,
  notify,
}: {
  status: Status | null;
  config: Config | null;
  onChooseModel: (provider: string, model: string) => Promise<void>;
  onApplied: () => Promise<void>;
  notify: (message: string, tone?: "good" | "bad") => void;
}) {
  const [messages, setMessages] = React.useState<Message[]>(() => {
    try {
      const raw = window.localStorage.getItem(CHAT_KEY);
      const parsed = raw ? JSON.parse(raw) : [];
      return Array.isArray(parsed) ? (parsed as Message[]) : [];
    } catch {
      return [];
    }
  });
  const [input, setInput] = React.useState("");
  const [busy, setBusy] = React.useState(false);
  const [copied, setCopied] = React.useState<number | null>(null);
  const scroller = React.useRef<HTMLDivElement>(null);
  const textarea = React.useRef<HTMLTextAreaElement>(null);

  const provider = config?.chatProvider || config?.provider || "openai";
  const model = config?.chatModel || config?.model || "";
  const info = status?.providers.find((item) => item.id === provider);
  const providerName = info?.name ?? provider;
  const needsChatModel = info?.kind === "local";

  React.useEffect(() => {
    scroller.current?.scrollTo({ top: scroller.current.scrollHeight, behavior: "smooth" });
  }, [messages, busy]);

  // The conversation is remembered between visits.
  React.useEffect(() => {
    try {
      window.localStorage.setItem(CHAT_KEY, JSON.stringify(messages.slice(-60)));
    } catch {
      /* storage full or blocked */
    }
  }, [messages]);

  async function send(text: string) {
    const trimmed = text.trim();
    if (!trimmed || busy) return;

    const next: Message[] = [...messages, { role: "user", content: trimmed }];
    setMessages(next);
    setInput("");
    setBusy(true);

    try {
      const result = await api.chat({
        provider,
        model,
        messages: next.map((message) => ({ role: message.role, content: message.content })),
      });
      setMessages([
        ...next,
        {
          role: "assistant",
          content: result.reply || "(the model sent an empty reply)",
          applied: result.applied,
          jobId: result.jobId,
        },
      ]);
      if (result.applied && Object.keys(result.applied).length > 0) await onApplied();
      if (result.jobId) notify("A clipping run just started");
    } catch (error) {
      const message = error instanceof Error ? error.message : "The model did not answer";
      setMessages([...next, { role: "assistant", content: message, failed: true }]);
    } finally {
      setBusy(false);
      textarea.current?.focus();
    }
  }

  function copy(text: string, index: number) {
    void navigator.clipboard?.writeText(text).then(() => {
      setCopied(index);
      window.setTimeout(() => setCopied(null), 1400);
    });
  }

  return (
    <div className="mx-auto flex h-[calc(100vh-13.5rem)] min-h-[420px] w-full max-w-3xl flex-col">
      <div className="flex flex-wrap items-center gap-2 pb-3">
        <h1 className="text-[19px] font-semibold tracking-[-0.01em] text-white">Chat</h1>
        <span className="rounded-full border border-white/10 bg-white/[0.03] px-2 py-0.5 text-[10px] text-neutral-500">
          local session
        </span>
        <button
          type="button"
          onClick={() => setMessages([])}
          disabled={messages.length === 0}
          className="ml-auto inline-flex h-7 items-center gap-1.5 rounded-full border border-white/10 px-2.5 text-[11px] text-neutral-400 transition-colors hover:bg-white/[0.06] hover:text-white disabled:opacity-30"
          title="New chat"
        >
          <IconMessagePlus size={13} />
          New chat
        </button>
      </div>

      <div ref={scroller} className="sc-scrollbar-thin min-h-0 flex-1 overflow-y-auto">
        {messages.length === 0 ? (
          <div className="flex h-full flex-col items-center justify-center gap-6 py-6 text-center">
            <span className="grid h-12 w-12 place-items-center rounded-2xl bg-white text-black">
              <IconScissors size={22} />
            </span>
            <div>
              <p className="text-[22px] font-semibold tracking-[-0.02em] text-white">
                <SplitText text="What are we clipping today?" stagger={0.035} />
              </p>
              <p className="mt-1 text-[12.5px] text-neutral-500">
                {providerName} is on the other end. Ask for a style, a length, or paste a link.
              </p>
            </div>
            <div className="grid w-full gap-2 sm:grid-cols-2">
              {SUGGESTIONS.map((suggestion) => (
                <motion.button
                  key={suggestion.title}
                  type="button"
                  whileHover={{ y: -2 }}
                  onClick={() => void send(suggestion.body)}
                  className="sc-lift flex items-start gap-2.5 rounded-2xl border border-white/[0.09] bg-white/[0.02] p-3 text-left"
                >
                  <span className="mt-0.5 grid h-7 w-7 shrink-0 place-items-center rounded-lg border border-white/10 text-neutral-300">
                    <suggestion.icon size={13} />
                  </span>
                  <span className="min-w-0">
                    <span className="block text-[12px] font-semibold text-white">{suggestion.title}</span>
                    <span className="mt-0.5 block text-[11px] leading-relaxed text-neutral-500">{suggestion.body}</span>
                  </span>
                </motion.button>
              ))}
            </div>
          </div>
        ) : (
          <div className="flex flex-col gap-1 pb-4">
            <AnimatePresence initial={false}>
              {messages.map((message, index) =>
                message.role === "user" ? (
                  <motion.div
                    key={index}
                    initial={{ opacity: 0, y: 8 }}
                    animate={{ opacity: 1, y: 0 }}
                    className="flex justify-end py-2"
                  >
                    <div className="max-w-[85%] rounded-3xl rounded-br-lg bg-white/[0.08] px-4 py-2.5 text-[13px] leading-relaxed text-neutral-100">
                      <p className="whitespace-pre-wrap break-words">{message.content}</p>
                    </div>
                  </motion.div>
                ) : (
                  <motion.div
                    key={index}
                    initial={{ opacity: 0, y: 8 }}
                    animate={{ opacity: 1, y: 0 }}
                    className="group flex gap-3 py-3"
                  >
                    <span
                      className={cn(
                        "mt-0.5 grid h-7 w-7 shrink-0 place-items-center rounded-full",
                        message.failed ? "border border-white/25 text-neutral-300" : "bg-white text-black"
                      )}
                    >
                      <IconScissors size={13} />
                    </span>
                    <div className="min-w-0 flex-1">
                      <p
                        className={cn(
                          "whitespace-pre-wrap break-words text-[13.5px] leading-[1.65]",
                          message.failed ? "text-neutral-400" : "text-neutral-100"
                        )}
                      >
                        <StreamText
                          text={message.content}
                          animate={index === messages.length - 1 && !message.failed}
                        />
                      </p>

                      {message.applied && Object.keys(message.applied).length > 0 && (
                        <div className="mt-2 flex flex-wrap gap-1.5">
                          {Object.entries(message.applied).map(([key, value]) => (
                            <span
                              key={key}
                              className="rounded-full border border-white/15 bg-white/[0.04] px-2 py-0.5 font-mono text-[10px] text-neutral-300"
                            >
                              {key}: {String(value)}
                            </span>
                          ))}
                        </div>
                      )}

                      {message.jobId && (
                        <a
                          href="#clipper"
                          className="mt-2 inline-flex items-center gap-1.5 rounded-full bg-white px-2.5 py-1 text-[11px] font-semibold text-black"
                        >
                          <IconPlayerPlay size={11} /> Watch it in Clipper
                        </a>
                      )}

                      <div className="mt-1.5 flex items-center gap-1 opacity-0 transition-opacity group-hover:opacity-100">
                        <button
                          type="button"
                          onClick={() => copy(message.content, index)}
                          className="inline-flex items-center gap-1 rounded-md px-1.5 py-0.5 text-[10.5px] text-neutral-500 hover:bg-white/[0.06] hover:text-white"
                        >
                          {copied === index ? <IconCheck size={11} /> : <IconCopy size={11} />}
                          {copied === index ? "copied" : "copy"}
                        </button>
                      </div>
                    </div>
                  </motion.div>
                )
              )}
            </AnimatePresence>

            {busy && (
              <div className="flex items-center gap-3 py-3">
                <span className="grid h-7 w-7 shrink-0 place-items-center rounded-full bg-white text-black">
                  <IconScissors size={13} />
                </span>
                <div className="flex items-center gap-1">
                  {[0, 1, 2].map((dot) => (
                    <motion.span
                      key={dot}
                      className="h-1.5 w-1.5 rounded-full bg-neutral-500"
                      animate={{ opacity: [0.25, 1, 0.25], y: [0, -2, 0] }}
                      transition={{ duration: 1.1, repeat: Infinity, delay: dot * 0.15 }}
                    />
                  ))}
                </div>
              </div>
            )}
          </div>
        )}
      </div>

      <div className="pt-2">
        <LiquidInput
          value={input}
          onChange={setInput}
          onSubmit={() => void send(input)}
          busy={busy}
          inputRef={textarea}
          placeholder="Message SUDOCLIPPER… drop a link and ask for clips"
          toolbar={
            <ModelMenu
              compact
              value={{ provider, model }}
              onChange={(next) => void onChooseModel(next.provider, next.model)}
            />
          }
          hint={
            <p className="flex flex-wrap items-center gap-1.5 text-[10.5px] text-neutral-600">
              <ProviderIcon id={provider} size={11} />
              {providerName} · {model || "no model"}
              {needsChatModel && (
                <a href="#config" className="text-neutral-300 underline underline-offset-2">
                  the free local selector can't chat — pick a hosted model
                </a>
              )}
            </p>
          }
        />
      </div>
    </div>
  );
}

export default ChatTab;
