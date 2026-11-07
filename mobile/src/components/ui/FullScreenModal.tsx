import type { ReactNode } from "react";
import { Modal, StyleSheet, Text, TouchableOpacity, View } from "react-native";
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
      <View style={[styles.container, { paddingBottom: spacing.lg + insets.bottom }]}>
        <View style={styles.header}>
          <Text style={styles.title}>{title}</Text>
          <TouchableOpacity onPress={onClose} hitSlop={10}>
            <Text style={styles.close}>Close</Text>
          </TouchableOpacity>
        </View>
        {children}
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.surface, paddingTop: 56, paddingHorizontal: spacing.lg },
  header: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginBottom: spacing.md },
  title: { ...typography.title, color: colors.textPrimary },
  close: { color: colors.primary, fontWeight: "700" },
});
