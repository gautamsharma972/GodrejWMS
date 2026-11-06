import type { ReactNode } from "react";
import { useState } from "react";
import { ActivityIndicator, Animated, StyleSheet, Text, TouchableOpacity, type ViewStyle } from "react-native";
import { colors, radius, spacing, typography } from "../../theme";
import { lightTap } from "../../utils/haptics";

type Variant = "primary" | "secondary" | "outline" | "danger" | "ghost";
type Size = "md" | "lg";

interface Props {
  title: string;
  onPress: () => void;
  variant?: Variant;
  size?: Size;
  loading?: boolean;
  disabled?: boolean;
  icon?: ReactNode;
  style?: ViewStyle;
  fullWidth?: boolean;
  /** Light haptic tap on press. Defaults on for primary/danger (the "committing" actions); off for
   * secondary/outline/ghost, which are usually navigational rather than destructive/confirming. */
  haptic?: boolean;
}

export function Button({
  title,
  onPress,
  variant = "primary",
  size = "md",
  loading = false,
  disabled = false,
  icon,
  style,
  fullWidth = true,
  haptic,
}: Props) {
  const isDisabled = disabled || loading;
  const hapticEnabled = haptic ?? (variant === "primary" || variant === "danger");
  const [scale] = useState(() => new Animated.Value(1));

  const handlePress = () => {
    if (hapticEnabled) lightTap();
    onPress();
  };
  const pressIn = () => {
    Animated.spring(scale, { toValue: 0.97, useNativeDriver: true, speed: 50, bounciness: 0 }).start();
  };
  const pressOut = () => {
    Animated.spring(scale, { toValue: 1, useNativeDriver: true, speed: 30, bounciness: 6 }).start();
  };

  return (
    // `style` (incl. things like `flex: 1` call sites pass to sit inside a row) lives on this outer
    // wrapper - the scale transform - so it actually governs the button's box; the inner
    // TouchableOpacity just fills whatever box that is.
    <Animated.View style={[fullWidth && styles.fullWidth, style, { transform: [{ scale }] }]}>
      <TouchableOpacity
        style={[styles.base, size === "lg" ? styles.lg : styles.md, variantStyles[variant], isDisabled && styles.disabled]}
        onPress={handlePress}
        onPressIn={pressIn}
        onPressOut={pressOut}
        disabled={isDisabled}
        activeOpacity={0.8}
      >
        {loading ? (
          <ActivityIndicator color={variant === "outline" || variant === "ghost" ? colors.primary : "#fff"} />
        ) : (
          <>
            {icon}
            <Text style={[styles.text, textVariantStyles[variant], icon ? { marginLeft: spacing.sm } : null]}>{title}</Text>
          </>
        )}
      </TouchableOpacity>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  base: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    borderRadius: radius.md,
  },
  md: { paddingVertical: 12, paddingHorizontal: spacing.lg },
  lg: { paddingVertical: 16, paddingHorizontal: spacing.xl },
  fullWidth: { width: "100%" },
  disabled: { opacity: 0.5 },
  text: { ...typography.subtitle },
});

const variantStyles: Record<Variant, ViewStyle> = {
  primary: { backgroundColor: colors.primary },
  secondary: { backgroundColor: colors.primarySoft },
  outline: { backgroundColor: "transparent", borderWidth: 1.5, borderColor: colors.primary },
  danger: { backgroundColor: colors.danger },
  ghost: { backgroundColor: "transparent" },
};

const textVariantStyles: Record<Variant, { color: string }> = {
  primary: { color: colors.textOnPrimary },
  secondary: { color: colors.primaryText },
  outline: { color: colors.primary },
  danger: { color: colors.textOnPrimary },
  ghost: { color: colors.primary },
};
