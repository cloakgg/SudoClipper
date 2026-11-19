import type { CSSProperties } from "react";
import { PROVIDER_PATHS } from "@/components/provider-paths";
import { cn } from "@/lib/utils";
import { IconBolt, IconPlugConnected, IconSparkles } from "@tabler/icons-react";

// Marks we could not pull from Simple Icons: drawn shapes or a letter badge.
const DRAWN: Record<string, string[]> = {
  groq: [
    "M12 2.4a9.6 9.6 0 1 0 0 19.2 9.6 9.6 0 0 0 0-19.2Zm0 3.1a6.5 6.5 0 1 1 0 13 6.5 6.5 0 0 1 0-13Z",
    "M15.4 15.4 21 21l-1.2-5.6-4.4 0Z",
  ],
  sambanova: ["M12 2 3 7v10l9 5 9-5V7l-9-5Zm0 3.4 6 3.4v6.4l-6 3.4-6-3.4V8.8l6-3.4Z"],
  hyperbolic: ["M4 3h3v7.6L16 3h4l-8.4 8.6L20.5 21h-4.2l-6.3-7.2L7 16.4V21H4V3Z"],
  novita: ["M12 2 4 6.6v10.8L12 22l8-4.6V6.6L12 2Zm0 3.4 5 2.9v5.4l-5 2.9-5-2.9V8.3l5-2.9Z"],
  zai: ["M4 4h16v3.2L9.6 17H20v3H4v-3.2L14.4 7H4V4Z"],
  kilocode: ["M3 5h4l5 7-5 7H3l5-7-5-7Zm9 0h4l5 7-5 7h-4l5-7-5-7Z"],
  aionlabs: ["M12 2 2.5 21h4.6L12 9.4 16.9 21h4.6L12 2Z"],
  axforge: ["M12 2 3 7v10l9 5 9-5V7l-9-5Zm0 4 5.5 3v6L12 18l-5.5-3V9L12 6Z"],
};

const BRAND: Record<string, string> = {
  openai: "#10a37f",
  gemini: "#4285f4",
  openrouter: "#8b7bf7",
  groq: "#f55036",
  mistral: "#fa520f",
  deepseek: "#4d6bfe",
  ollama: "#ffffff",
  ollamacloud: "#ffffff",
  meta: "#0866ff",
  nvidia: "#76b900",
  alibaba: "#ff6a00",
  modelscope: "#6c5ce7",
  deepinfra: "#5b8def",
  together: "#0f6fff",
  fireworks: "#f95822",
  cohere: "#39a37a",
  nebius: "#4b7bec",
  sambanova: "#ef5b2b",
  hyperbolic: "#6a4cff",
  novita: "#23c6b0",
  zai: "#4f8cff",
  ai21: "#e0644a",
  siliconflow: "#7c5cff",
  aimlapi: "#ef4444",
  nscale: "#ff5c8a",
  ovh: "#5b8def",
  kilo: "#f59e0b",
  kilocode: "#f59e0b",
  aionlabs: "#7dd3fc",
  axforge: "#34d399",
  llm7: "#a3e635",
  puter: "#5eead4",
  pollinations: "#fb923c",
  nine: "#c084fc",
  netlify: "#00c7b7",
  local: "#fbbf24",
  custom: "#22d3ee",
};

// Every provider gets a visible brand colour, even the ones without a known logo.
const FALLBACK_BRAND = ["#f87171", "#fbbf24", "#34d399", "#22d3ee", "#a78bfa", "#f472b6", "#fb923c", "#84cc16", "#38bdf8"];

function brandColor(id: string): string {
  if (BRAND[id]) return BRAND[id];
  let hash = 7;
  for (let i = 0; i < id.length; i++) hash = (hash * 31 + id.charCodeAt(i)) % 9973;
  return FALLBACK_BRAND[hash % FALLBACK_BRAND.length];
}

const MONOGRAM: Record<string, string> = {
  ollamacloud: "O",
  sambanova: "S",
  hyperbolic: "H",
  novita: "N",
  zai: "Z",
  alibaba: "Q",
  kilocode: "K",
  aionlabs: "A",
  axforge: "X",
  kilo: "K",
  meta: "M",
  groq: "G",
  llm7: "7",
  nscale: "N",
  nebius: "N",
  ovh: "O",
  siliconflow: "S",
  deepinfra: "D",
  fireworks: "F",
  together: "T",
  ai21: "A",
  cohere: "C",
  aimlapi: "A",
  puter: "P",
  pollinations: "P",
  nine: "9",
};

export function ProviderIcon({
  id,
  className,
  size = 17,
}: {
  id: string;
  className?: string;
  size?: number;
}) {
  const brandStyle = { "--brand": brandColor(id) } as CSSProperties;

  if (id === "local")
    return (
      <span className={cn("prov-wrap", className)} data-provider={id} style={brandStyle}>
        <IconSparkles className="prov-logo" size={size} stroke={1.7} />
      </span>
    );
  if (id === "custom")
    return (
      <span className={cn("prov-wrap", className)} data-provider={id} style={brandStyle}>
        <IconPlugConnected className="prov-logo" size={size} stroke={1.7} />
      </span>
    );

  // Ollama Cloud uses the same mark as local Ollama.
  const key = id === "ollamacloud" ? "ollama" : id;
  const paths = PROVIDER_PATHS[key] ?? DRAWN[key];
  if (paths) {
    return (
      <svg
        viewBox="0 0 24 24"
        fill="currentColor"
        aria-hidden
        width={size}
        height={size}
        data-provider={id}
        style={brandStyle}
        className={cn("prov-logo shrink-0", className)}
      >
        {paths.map((d, index) => (
          <path key={index} d={d} />
        ))}
      </svg>
    );
  }

  const letter = MONOGRAM[id] ?? (id.slice(0, 1).toUpperCase() || "?");
  if (!letter) return <IconBolt className={className} size={size} stroke={1.7} />;

  return (
    <svg viewBox="0 0 24 24" aria-hidden width={size} height={size} data-provider={id} style={brandStyle} className={cn("prov-logo shrink-0", className)}>
      <rect x="1.5" y="1.5" width="21" height="21" rx="6" fill="none" stroke="currentColor" strokeWidth="1.6" />
      <text
        x="12"
        y="16.4"
        textAnchor="middle"
        fontSize="11"
        fontWeight="700"
        fill="currentColor"
        fontFamily="Inter, ui-sans-serif, system-ui, sans-serif"
      >
        {letter}
      </text>
    </svg>
  );
}
