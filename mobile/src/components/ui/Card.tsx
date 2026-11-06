import type { ReactNode } from "react";
import { useState } from "react";
import { Animated, StyleSheet, TouchableOpacity, View, type ViewStyle } from "react-native";
import { colors, radius, shadow, spacing } from "../../theme";

interface Props {
  children: ReactNode;
  onPress?: () => void;
  style?: ViewStyle;
  disabled?: boolean;
}

/** The app's one card surface: soft elevation, rounded corners, a hairline border for definition on
 * light backgrounds. Pass `onPress` to make it tappable (used for list rows) - tappable cards get a
 * subtle press-scale animation. */
export function Card({ children, onPress, style, disabled }: Props) {
  const [scale] = useState(() => new Animated.Value(1));

  if (onPress) {
    const pressIn = () => Animated.spring(scale, { toValue: 0.98, useNativeDriver: true, speed: 50, bounciness: 0 }).start();
    const pressOut = () => Animated.spring(scale, { toValue: 1, useNativeDriver: true, speed: 30, bounciness: 6 }).start();
    return (
      <Animated.View style={{ transform: [{ scale }] }}>
        <TouchableOpacity
          style={[styles.card, style]}
          onPress={onPress}
          onPressIn={pressIn}
          onPressOut={pressOut}
          disabled={disabled}
          activeOpacity={0.7}
        >
          {children}
        </TouchableOpacity>
      </Animated.View>
    );
  }
  return <View style={[styles.card, style]}>{children}</View>;
}

const styles = StyleSheet.create({
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.border,
    padding: spacing.lg,
    ...shadow.card,
  },
});
