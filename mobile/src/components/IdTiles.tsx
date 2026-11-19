import { StyleSheet, Text, View } from "react-native";
import { colors, radius, spacing, typography } from "../theme";

/** Labelled identifiers on a queue card (Vehicle No., LR No.) so a GRN or pick reference is easy to tie to its vehicle. A blank value shows "-". */
export function IdTiles({ items }: { items: { label: string; value?: string | null }[] }) {
  return (
    <View style={styles.row}>
      {items.map((item) => (
        <View key={item.label} style={styles.tile}>
          <Text style={styles.label}>{item.label.toUpperCase()}</Text>
          <Text style={styles.value} numberOfLines={1}>{item.value && item.value.trim() ? item.value : "-"}</Text>
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: "row", gap: spacing.sm, marginTop: spacing.sm },
  tile: { flex: 1, borderWidth: 1, borderColor: colors.border, borderRadius: radius.md, backgroundColor: colors.surfaceAlt, paddingVertical: spacing.sm, paddingHorizontal: spacing.md },
  label: { ...typography.label, fontSize: 9, color: colors.textMuted, letterSpacing: 0.7 },
  value: { ...typography.subtitle, color: colors.textPrimary, fontWeight: "800", marginTop: 2 },
});
