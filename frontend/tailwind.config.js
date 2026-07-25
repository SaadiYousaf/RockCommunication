/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        // Primary brand — muted teal / petrol. A calm, vintage-retro hue that
        // reads professional against warm neutrals (the mid-century "teal on
        // cream" pairing) without the brightness of a modern sky-blue.
        brand: {
          50:  "#eef3f2",
          100: "#d7e5e2",
          200: "#b3ccc7",
          300: "#85aca5",
          400: "#588b83",
          500: "#3c7269",
          600: "#2f5a53",
          700: "#284a45",
          800: "#233c39",
          900: "#1f332f",
          950: "#0f1d1b",
        },
        // Accent — muted ochre / retro gold. Warm counterpoint to the teal,
        // used sparingly for highlights. Desaturated so it never shouts.
        accent: {
          50:  "#faf6ec",
          100: "#f2e8cf",
          200: "#e6d2a0",
          300: "#d6b76c",
          400: "#c69f47",
          500: "#b28535",
          600: "#986d2c",
          700: "#7c5528",
          800: "#674627",
          900: "#583c24",
          950: "#331f10",
        },
        // Neutrals — warm greige. A softly warmed grey (leaning aged-paper /
        // taupe) that gives the whole UI a vintage, un-clinical feel while
        // keeping text contrast high.
        ink: {
          50:  "#f8f6f2",
          100: "#f0ede6",
          200: "#e2ddd2",
          300: "#cdc5b6",
          400: "#a89e8c",
          500: "#7d7463",
          600: "#5d5648",
          700: "#453f35",
          800: "#2c2823",
          900: "#1a1712",
          950: "#0e0c09",
        },
      },
      // Type scale — tighter line heights at large sizes (display feel),
      // generous at body sizes for legibility on dense tables/forms.
      fontSize: {
        "display-xl": ["48px", { lineHeight: "1.05", letterSpacing: "-0.025em", fontWeight: "700" }],
        "display-lg": ["36px", { lineHeight: "1.1",  letterSpacing: "-0.02em",  fontWeight: "700" }],
        "display-md": ["28px", { lineHeight: "1.15", letterSpacing: "-0.02em",  fontWeight: "600" }],
        "display-sm": ["22px", { lineHeight: "1.2",  letterSpacing: "-0.015em", fontWeight: "600" }],
      },
      boxShadow: {
        // Refined depth scale — softer near surface, longer on float
        xs:        "0 1px 1.5px 0 rgba(15,23,42,0.04)",
        card:      "0 1px 2px 0 rgba(15,23,42,0.04), 0 1px 3px 0 rgba(15,23,42,0.06)",
        "card-hover": "0 8px 24px -8px rgba(15,23,42,0.12), 0 2px 6px -2px rgba(15,23,42,0.06)",
        pop:       "0 16px 40px -12px rgba(15,23,42,0.22), 0 4px 14px -4px rgba(15,23,42,0.10)",
        float:     "0 24px 64px -16px rgba(15,23,42,0.28), 0 8px 24px -8px rgba(15,23,42,0.10)",
        ring:      "0 0 0 4px rgba(60,114,105,0.22)",
        "ring-accent": "0 0 0 4px rgba(178,133,53,0.22)",
        glow:      "0 8px 28px -8px rgba(60,114,105,0.50)",
        "glow-accent": "0 8px 28px -8px rgba(178,133,53,0.50)",
        inset:     "inset 0 1px 0 rgba(255,255,255,0.08)",
        "inset-line": "inset 0 0 0 1px rgba(15,23,42,0.04)",
      },
      borderRadius: {
        xl: "0.875rem",
        "2xl": "1.125rem",
        "3xl": "1.5rem",
        "4xl": "1.875rem",
      },
      fontFamily: {
        sans: ['Inter', 'ui-sans-serif', 'system-ui', '-apple-system', 'Segoe UI', 'Roboto', 'sans-serif'],
        mono: ['ui-monospace', 'SFMono-Regular', 'Menlo', 'Consolas', 'Liberation Mono', 'monospace'],
      },
      backgroundImage: {
        "brand-gradient": "linear-gradient(135deg, #588b83 0%, #3c7269 40%, #284a45 100%)",
        "brand-soft":     "linear-gradient(135deg, #eef3f2 0%, #faf6ec 100%)",
        "rock-gunmetal":  "linear-gradient(180deg, #2c2823 0%, #1a1712 55%, #0e0c09 100%)",
        "ink-gradient":   "linear-gradient(180deg, #0e0c09 0%, #1a1712 55%, #1f332f 140%)",
        "shine":          "linear-gradient(180deg, rgba(255,255,255,0.10) 0%, rgba(255,255,255,0) 60%)",
        // New: subtle light gradient for surface chrome (cards, headers)
        "surface-sheen":  "linear-gradient(180deg, rgba(255,255,255,0.6) 0%, rgba(255,255,255,0) 35%)",
        "header-sheen":   "linear-gradient(180deg, rgba(255,255,255,0.65) 0%, rgba(255,255,255,0) 100%)",
      },
      transitionTimingFunction: {
        "spring-soft":  "cubic-bezier(0.32, 0.72, 0.32, 1)",
        "spring-snap":  "cubic-bezier(0.5, 1.4, 0.4, 1)",
        "ease-out-quint": "cubic-bezier(0.22, 1, 0.36, 1)",
      },
      keyframes: {
        "fade-in":   { "0%": { opacity: "0", transform: "translateY(4px)" },  "100%": { opacity: "1", transform: "translateY(0)" } },
        "slide-in":  { "0%": { opacity: "0", transform: "translateX(16px)" }, "100%": { opacity: "1", transform: "translateX(0)" } },
        "slide-up":  { "0%": { opacity: "0", transform: "translateY(12px)" }, "100%": { opacity: "1", transform: "translateY(0)" } },
        "scale-in":  { "0%": { opacity: "0", transform: "scale(0.96)" },      "100%": { opacity: "1", transform: "scale(1)" } },
        shimmer:     { "0%": { backgroundPosition: "-400px 0" }, "100%": { backgroundPosition: "400px 0" } },
        "pulse-ring":{ "0%": { boxShadow: "0 0 0 0 rgba(60,114,105,0.50)" }, "70%": { boxShadow: "0 0 0 10px rgba(60,114,105,0)" }, "100%": { boxShadow: "0 0 0 0 rgba(60,114,105,0)" } },
        "fade-up":   { "0%": { opacity: "0", transform: "translateY(8px)" },  "100%": { opacity: "1", transform: "translateY(0)" } },
        "rise":      { "0%": { opacity: "0", transform: "translateY(16px) scale(0.98)" }, "100%": { opacity: "1", transform: "translateY(0) scale(1)" } },
      },
      animation: {
        "fade-in":  "fade-in 0.18s ease-out",
        "fade-up":  "fade-up 0.22s cubic-bezier(0.22, 1, 0.36, 1) both",
        "rise":     "rise 0.28s cubic-bezier(0.22, 1, 0.36, 1) both",
        "slide-in": "slide-in 0.22s cubic-bezier(0.32,0.72,0.32,1)",
        "slide-up": "slide-up 0.22s cubic-bezier(0.22, 1, 0.36, 1)",
        "scale-in": "scale-in 0.16s ease-out",
        shimmer:    "shimmer 1.4s linear infinite",
        "pulse-ring": "pulse-ring 2s cubic-bezier(0.66,0,0,1) infinite",
      },
    },
  },
  plugins: [],
}
