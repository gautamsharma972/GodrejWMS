import { StyleSheet, TextInput, View, type StyleProp, type TextInputProps, type ViewStyle } from "react-native";
import { colors, radius, spacing } from "../../theme";
import { AppIcon } from "./AppIcon";

interface Props extends TextInputProps {
  containerStyle?: StyleProp<ViewStyle>;
}

export function SearchField({ containerStyle, ...props }: Props) {
  return (
    <View style={[styles.wrap, containerStyle]}>
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
