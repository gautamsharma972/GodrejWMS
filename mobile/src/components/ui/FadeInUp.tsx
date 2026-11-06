import type { ReactNode } from "react";
import { useEffect, useState } from "react";
import { Animated, type ViewStyle } from "react-native";

/** Fades + slides a section up on mount. Built on RN's built-in `Animated` only (no
 * react-native-reanimated) - used to stagger Home's header/stats/actions and Login's form sheet. */
export function FadeInUp({ children, delay = 0, style }: { children: ReactNode; delay?: number; style?: ViewStyle }) {
  const [anim] = useState(() => new Animated.Value(0));

  useEffect(() => {
    const animation = Animated.timing(anim, {
      toValue: 1,
      duration: 420,
      delay,
      useNativeDriver: true,
    });
    animation.start();
    return () => animation.stop();
  }, [anim, delay]);

  return (
    <Animated.View
      style={[
        style,
        {
          opacity: anim,
          transform: [{ translateY: anim.interpolate({ inputRange: [0, 1], outputRange: [16, 0] }) }],
        },
      ]}
    >
      {children}
    </Animated.View>
  );
}
