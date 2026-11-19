import type { ReactNode } from "react";
import { KeyboardAvoidingView, Modal, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { colors, spacing, typography } from "../../theme";

interface Props {
  visible: boolean;
  title: string;
  onClose: () => void;
  children: ReactNode;
}

/** Full-screen take-over modal for flows with their own search/list/form (material picker,
 * location-change flow, ...) - shares one header treatment across the app. */
export function FullScreenModal({ visible, title, onClose, children }: Props) {
  // Android's Modal renders edge-to-edge, so on a device with the classic 3-button nav bar
  // (near-zero inset on gesture-nav devices) the bottom of the content - a submit button, the
  // last row of a list - would otherwise sit underneath it.
  const insets = useSafeAreaInsets();

  return (
    <Modal visible={visible} animationType="slide" onRequestClose={onClose}>
      {/* RN's Modal is its own native window on Android, so it doesn't inherit the Activity's
          windowSoftInputMode="adjustResize" - without this, the keyboard just overlays the bottom
          of the sheet (a fixed footer button, the last list row) instead of making room for it. */}
      {/* "padding" on both platforms: Android's "height" mode left the sheet squeezed after the keyboard closed (footer floating
          mid-screen and jumping between two positions while a field was focused). */}
      <KeyboardAvoidingView style={styles.flex} behavior="padding">
        <View style={[styles.container, { paddingBottom: spacing.lg + insets.bottom }]}>
          <View style={styles.header}>
            <Text style={styles.title}>{title}</Text>
            <TouchableOpacity onPress={onClose} hitSlop={10}>
              <Text style={styles.close}>Close</Text>
            </TouchableOpacity>
          </View>
          {children}
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  container: { flex: 1, backgroundColor: colors.surface, paddingTop: 56, paddingHorizontal: spacing.lg },
  header: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginBottom: spacing.md },
  title: { ...typography.title, color: colors.textPrimary },
  close: { color: colors.primary, fontWeight: "700" },
});
