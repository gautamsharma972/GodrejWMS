import { StyleSheet, TextInput, View, type TextInputProps } from "react-native";
import { colors, radius, spacing } from "../../theme";
import { AppIcon } from "./AppIcon";

export function SearchField(props: TextInputProps) {
  return (
    <View style={styles.wrap}>
      <AppIcon name="search-outline" size={16} color={colors.textMuted} />
      <TextInput style={styles.input} placeholderTextColor={colors.textMuted} {...props} />
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    flexDirection: "row",
    alignItems: "center",
    backgroundColor: colors.surface,
    borderWidth: 1.5,
    borderColor: colors.border,
    borderRadius: radius.md,
    paddingHorizontal: spacing.md,
    marginBottom: spacing.md,
  },
  input: { flex: 1, marginLeft: spacing.sm, paddingVertical: 11, fontSize: 14.5, color: colors.textPrimary },
});
