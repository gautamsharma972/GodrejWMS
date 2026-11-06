import type { ReactNode } from "react";
import { StyleSheet, View, type ViewStyle } from "react-native";
import { SafeAreaView, type Edge } from "react-native-safe-area-context";
import { colors } from "../../theme";

interface Props {
  children: ReactNode;
  edges?: Edge[];
  style?: ViewStyle;
  padded?: boolean;
}

/** Safe-area + background wrapper every screen renders into, so the app has one consistent canvas. */
export function Screen({ children, edges = ["bottom"], style, padded = false }: Props) {
  return (
    <SafeAreaView style={[styles.screen, padded && styles.padded, style]} edges={edges}>
      {children}
    </SafeAreaView>
  );
}

/** Centered loading/empty wrapper for full-screen states. */
export function CenteredView({ children }: { children: ReactNode }) {
  return <View style={styles.center}>{children}</View>;
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.background },
  padded: { padding: 16 },
  center: { flex: 1, alignItems: "center", justifyContent: "center" },
});
