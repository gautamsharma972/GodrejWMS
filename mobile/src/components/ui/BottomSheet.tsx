import type { ReactNode } from "react";
import { Modal, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { colors, radius, spacing, typography } from "../../theme";

interface Props {
  visible: boolean;
  title: string;
  onClose: () => void;
  children: ReactNode;
  maxHeightPercent?: number;
}

/** The app's one bottom-sheet shape: dim backdrop, rounded top sheet, title + close row. Used for
 * every "pick one from a list" flow (warehouse, material, location, ...). */
export function BottomSheet({ visible, title, onClose, children, maxHeightPercent = 70 }: Props) {
  // Android's Modal renders edge-to-edge, so on a device with the classic 3-button nav bar (as
  // opposed to gesture navigation, which has a near-zero inset) the last row of content would
  // otherwise sit underneath it - this padding keeps it clear on every device, not just ones
  // tested without an on-screen nav bar.
  const insets = useSafeAreaInsets();

  return (
    <Modal visible={visible} animationType="slide" transparent onRequestClose={onClose}>
      <TouchableOpacity style={styles.backdrop} activeOpacity={1} onPress={onClose}>
        <TouchableOpacity
          activeOpacity={1}
          style={[styles.sheet, { maxHeight: `${maxHeightPercent}%`, paddingBottom: spacing.xl + insets.bottom }]}
        >
          <View style={styles.header}>
            <Text style={styles.title}>{title}</Text>
            <TouchableOpacity onPress={onClose} hitSlop={10}>
              <Text style={styles.close}>Close</Text>
            </TouchableOpacity>
          </View>
          {children}
        </TouchableOpacity>
      </TouchableOpacity>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: { flex: 1, backgroundColor: colors.overlay, justifyContent: "flex-end" },
  sheet: {
    backgroundColor: colors.surface,
    borderTopLeftRadius: radius.xl,
    borderTopRightRadius: radius.xl,
    padding: spacing.xl,
  },
  header: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginBottom: spacing.md },
  title: { ...typography.title, color: colors.textPrimary },
  close: { color: colors.primary, fontWeight: "700" },
});
