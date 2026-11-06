import { StyleSheet, Text, View } from "react-native";
import { colors, radius, spacing, typography } from "../../theme";

export type BadgeTone = "success" | "warning" | "danger" | "info" | "neutral";
export type BadgeVariant = "solid" | "soft";

interface Props {
  label: string;
  tone: BadgeTone;
  /** "solid" (default): bold filled chip, white text - fast to scan under bright warehouse light.
   * "soft": the old tinted pill, for dense contexts (e.g. inside a data table) that want subtlety. */
  variant?: BadgeVariant;
}

const softStyles: Record<BadgeTone, { bg: string; fg: string }> = {
  success: { bg: colors.successSoft, fg: colors.successText },
  warning: { bg: colors.warningSoft, fg: colors.warningText },
  danger: { bg: colors.dangerSoft, fg: colors.dangerText },
  info: { bg: colors.infoSoft, fg: colors.infoText },
  neutral: { bg: colors.neutralSoft, fg: colors.neutralText },
};

const solidStyles: Record<BadgeTone, { bg: string; fg: string }> = {
  success: { bg: colors.successSolid, fg: colors.onSolid },
  warning: { bg: colors.warningSolid, fg: colors.onSolid },
  danger: { bg: colors.dangerSolid, fg: colors.onSolid },
  info: { bg: colors.infoSolid, fg: colors.onSolid },
  neutral: { bg: colors.neutralSolid, fg: colors.onSolid },
};

/** The app's one status-pill shape. Pass an allocation/confirmation state mapped to a tone by the
 * caller (see utils/status.ts) so every screen's pills look identical. */
export function StatusBadge({ label, tone, variant = "solid" }: Props) {
  const { bg, fg } = (variant === "solid" ? solidStyles : softStyles)[tone];
  return (
    <View style={[styles.pill, { backgroundColor: bg }]}>
      <Text style={[styles.text, { color: fg }]}>{label}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  pill: {
    paddingHorizontal: spacing.md,
    paddingVertical: 4,
    borderRadius: radius.pill,
    alignSelf: "flex-start",
  },
  text: { ...typography.label },
});
