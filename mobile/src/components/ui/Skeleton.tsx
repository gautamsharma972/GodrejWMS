import { useEffect, useState } from "react";
import { Animated, StyleSheet, View, type ViewStyle } from "react-native";
import { colors, radius, spacing } from "../../theme";

/** A pulsing placeholder block - exported so other screens can shape their own skeletons (e.g. a
 * dashboard stat tile) without duplicating the animation loop. Uses a lazy useState initializer
 * (not useRef().current) so the stricter react-hooks "refs" rule - which flags reading a ref during
 * render, even the common `useRef(init).current` one-liner - doesn't flag this. */
export function Pulse({ style }: { style: ViewStyle }) {
  const [opacity] = useState(() => new Animated.Value(0.4));

  useEffect(() => {
    const loop = Animated.loop(
      Animated.sequence([
        Animated.timing(opacity, { toValue: 1, duration: 650, useNativeDriver: true }),
        Animated.timing(opacity, { toValue: 0.4, duration: 650, useNativeDriver: true }),
      ]),
    );
    loop.start();
    return () => loop.stop();
  }, [opacity]);

  return <Animated.View style={[style, { opacity }]} />;
}

/** One skeleton row shaped like a queue/history list card - swapped in for the initial load instead
 * of a centered spinner, so the list's shape is visible immediately (perceived-performance trick
 * borrowed from field-ops/delivery apps, not just decorative). */
export function SkeletonListRow() {
  return (
    <View style={styles.row}>
      <View style={styles.rowTop}>
        <Pulse style={styles.titleBar} />
        <Pulse style={styles.badge} />
      </View>
      <Pulse style={styles.lineShort} />
      <Pulse style={styles.lineShort} />
    </View>
  );
}

export function SkeletonList({ count = 5 }: { count?: number }) {
  return (
    <View>
      {Array.from({ length: count }).map((_, i) => (
        <SkeletonListRow key={i} />
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.border,
    padding: spacing.lg,
    marginBottom: spacing.md,
  },
  rowTop: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginBottom: spacing.sm },
  titleBar: { width: "55%", height: 16, borderRadius: radius.sm, backgroundColor: colors.surfaceAlt },
  badge: { width: 72, height: 22, borderRadius: radius.pill, backgroundColor: colors.surfaceAlt },
  lineShort: { width: "70%", height: 11, borderRadius: radius.sm, backgroundColor: colors.surfaceAlt, marginTop: spacing.xs },
});
