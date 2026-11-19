import { AnimatePresence, motion } from "motion/react";
import { IconContrast, IconPalette } from "@tabler/icons-react";

export type Theme = "mono" | "color";

/** Header switch between the black-and-white look and the coloured one. */
export function ThemeToggle({ theme, onToggle }: { theme: Theme; onToggle: () => void }) {
  const colour = theme === "color";

  return (
    <motion.button
      type="button"
      onClick={onToggle}
      whileHover={{ scale: 1.06 }}
      whileTap={{ scale: 0.92 }}
      aria-label={colour ? "Switch to black and white" : "Switch to colour"}
      aria-pressed={colour}
      title={colour ? "Colour theme on" : "Colour theme off"}
      className="theme-toggle"
    >
      <motion.span
        aria-hidden
        className="theme-toggle-ring"
        animate={{ rotate: 360 }}
        transition={{ repeat: Infinity, duration: 9, ease: "linear" }}
      />
      <AnimatePresence mode="wait" initial={false}>
        <motion.span
          key={theme}
          initial={{ rotate: -80, scale: 0.55, opacity: 0 }}
          animate={{ rotate: 0, scale: 1, opacity: 1 }}
          exit={{ rotate: 80, scale: 0.55, opacity: 0 }}
          transition={{ duration: 0.22, ease: [0.22, 1, 0.36, 1] }}
          className="relative z-10 grid place-items-center"
        >
          {colour ? <IconPalette size={15} stroke={1.9} /> : <IconContrast size={15} stroke={1.9} />}
        </motion.span>
      </AnimatePresence>
    </motion.button>
  );
}

export default ThemeToggle;
