// Shared design tokens for the Pickers app. Every screen/component should pull its colors,
// spacing, radii and type sizes from here instead of hard-coding its own StyleSheet values, so the
// whole app reads as one consistent, modern system rather than a pile of ad hoc screens.

export const colors = {
  // Brand
  primary: "#4f46e5",
  primaryDark: "#4338ca",
  primarySoft: "#eef2ff",
  primaryText: "#3730a3",
  accent2: "#06b6d4",
  accent2Soft: "#cffafe",

  // Ops-console chrome (dark header / tab bar) - a fixed design element, not a theme switch.
  ink: "#0b1120",
  inkElevated: "#121b2e",
  inkBorder: "#1f2a40",

  // Surfaces
  background: "#f4f6fb",
  surface: "#ffffff",
  surfaceAlt: "#eef1f8",
  border: "#e2e6f0",
  borderStrong: "#d1d5db",
  overlay: "rgba(15, 23, 42, 0.45)",

  // Text
  textPrimary: "#0f172a",
  textSecondary: "#475569",
  textMuted: "#94a3b8",
  textOnPrimary: "#ffffff",
  textOnDarkPrimary: "#f8fafc",
  textOnDarkSecondary: "#cbd5e1",
  textOnDarkMuted: "#64748b",

  // Semantic - soft tints (subtle contexts) ...
  success: "#16a34a",
  successSoft: "#dcfce7",
  successText: "#166534",
  warning: "#d97706",
  warningSoft: "#fef3c7",
  warningText: "#92400e",
  danger: "#dc2626",
  dangerSoft: "#fee2e2",
  dangerText: "#991b1b",
  info: "#0284c7",
  infoSoft: "#e0f2fe",
  infoText: "#075985",
  neutralSoft: "#f1f5f9",
  neutralText: "#475569",

  // ... and bold/filled versions, white text on top - the new default for status chips.
  successSolid: "#10b981",
  warningSolid: "#f59e0b",
  dangerSolid: "#ef4444",
  infoSolid: "#0ea5e9",
  neutralSolid: "#64748b",
  onSolid: "#ffffff",
} as const;

// Rich, saturated gradient stops for hero chrome and data-driven summary cards (info/success/
// warning/danger). Kept deep/dark, not pastel, so white text stays legible on every stop.
export const gradients = {
  hero: ["#0b1120", "#1e1b4b"],
  primary: ["#4f46e5", "#4338ca"],
  info: ["#0ea5e9", "#0369a1"],
  success: ["#10b981", "#047857"],
  warning: ["#f59e0b", "#b45309"],
  danger: ["#f87171", "#b91c1c"],
} as const;

export const spacing = {
  xs: 4,
  sm: 8,
  md: 12,
  lg: 16,
  xl: 20,
  xxl: 28,
  xxxl: 36,
} as const;

export const radius = {
  sm: 8,
  md: 12,
  lg: 16,
  xl: 20,
  pill: 999,
} as const;

export const typography = {
  statNumber: { fontSize: 34, fontWeight: "800" as const },
  display: { fontSize: 26, fontWeight: "800" as const },
  title: { fontSize: 20, fontWeight: "700" as const },
  subtitle: { fontSize: 15, fontWeight: "600" as const },
  body: { fontSize: 14, fontWeight: "400" as const },
  bodyStrong: { fontSize: 14, fontWeight: "700" as const },
  caption: { fontSize: 12, fontWeight: "500" as const },
  label: { fontSize: 12, fontWeight: "700" as const },
};

export const shadow = {
  card: {
    shadowColor: "#0f172a",
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 0.06,
    shadowRadius: 10,
    elevation: 2,
  },
  raised: {
    shadowColor: "#0f172a",
    shadowOffset: { width: 0, height: 8 },
    shadowOpacity: 0.12,
    shadowRadius: 18,
    elevation: 6,
  },
} as const;

export const theme = { colors, gradients, spacing, radius, typography, shadow };
export default theme;
