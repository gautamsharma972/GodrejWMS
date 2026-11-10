import { CameraView, useCameraPermissions, type BarcodeScanningResult } from "expo-camera";
import { useEffect, useRef, useState } from "react";
import { Modal, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { AppIcon } from "./ui/AppIcon";
import { Button } from "./ui/Button";
import { colors, radius, spacing, typography } from "../theme";
import { errorPulse, lightTap } from "../utils/haptics";

interface Props {
  visible: boolean;
  title?: string;
  onClose: () => void;
  /** Called with the raw scanned text. Return true if it matched something and the scanner should
   * close; return false to keep scanning and flash an inline "not recognized" hint instead. */
  onScan: (value: string) => boolean;
}

// Warehouse rack/location labels aren't guaranteed to be one symbology - cover the common ones a
// label printer would produce instead of assuming QR only.
const BARCODE_TYPES = ["qr", "code128", "code39", "code93", "ean13", "ean8", "upc_a", "codabar", "itf14"] as const;

/** Full-screen camera scanner used to read a location code off a rack/bin label instead of typing
 * or searching for it. Shared by any flow that needs "scan to pick a location". */
export function BarcodeScannerModal({ visible, title = "Scan location", onClose, onScan }: Props) {
  const [permission, requestPermission] = useCameraPermissions();
  const [notRecognized, setNotRecognized] = useState<string | null>(null);
  const lockedRef = useRef(false);

  useEffect(() => {
    if (visible) {
      lockedRef.current = false;
      // eslint-disable-next-line react-hooks/set-state-in-effect -- intentional reset each time the modal opens/closes
      setNotRecognized(null);
      if (!permission?.granted) requestPermission();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [visible]);

  if (!visible) return null;

  const handleScanned = (result: BarcodeScanningResult) => {
    if (lockedRef.current) return;
    const value = result.data?.trim();
    if (!value) return;
    lockedRef.current = true;

    const matched = onScan(value);
    if (matched) {
      lightTap();
    } else {
      errorPulse();
      setNotRecognized(value);
      // Allow re-scanning after a short cooldown rather than requiring a manual retry tap.
      setTimeout(() => {
        lockedRef.current = false;
      }, 1200);
    }
  };

  return (
    <Modal visible={visible} animationType="slide" onRequestClose={onClose}>
      <View style={styles.container}>
        {permission?.granted ? (
          <>
            <CameraView
              style={StyleSheet.absoluteFill}
              facing="back"
              barcodeScannerSettings={{ barcodeTypes: [...BARCODE_TYPES] }}
              onBarcodeScanned={handleScanned}
            />
            <View style={styles.overlay} pointerEvents="box-none">
              <View style={styles.topBar}>
                <TouchableOpacity onPress={onClose} hitSlop={10} style={styles.closeButton}>
                  <AppIcon name="close" size={22} color={colors.textOnPrimary} />
                </TouchableOpacity>
                <Text style={styles.title}>{title}</Text>
                <View style={styles.closeButton} />
              </View>

              <View style={styles.frameWrap}>
                <View style={styles.frame} />
                <Text style={styles.hint}>Align the location&rsquo;s barcode or QR code inside the frame</Text>
                {notRecognized && (
                  <View style={styles.badge}>
                    <Text style={styles.badgeText} numberOfLines={1}>
                      &ldquo;{notRecognized}&rdquo; is not a known location &mdash; try again
                    </Text>
                  </View>
                )}
              </View>
            </View>
          </>
        ) : (
          <View style={styles.permissionWrap}>
            <AppIcon name="camera-outline" size={40} color={colors.textMuted} />
            <Text style={styles.permissionTitle}>Camera access needed</Text>
            <Text style={styles.permissionBody}>
              Allow camera access to scan a location&rsquo;s barcode instead of typing it in.
            </Text>
            <Button title="Allow camera" onPress={() => requestPermission()} style={{ marginTop: spacing.lg }} />
            <Button title="Cancel" variant="ghost" onPress={onClose} style={{ marginTop: spacing.sm }} />
          </View>
        )}
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.ink },
  overlay: { flex: 1, justifyContent: "space-between" },
  topBar: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingTop: 56,
    paddingHorizontal: spacing.lg,
  },
  closeButton: {
    width: 40,
    height: 40,
    borderRadius: radius.pill,
    backgroundColor: "rgba(15,23,42,0.55)",
    alignItems: "center",
    justifyContent: "center",
  },
  title: { ...typography.subtitle, color: colors.textOnPrimary },
  frameWrap: { alignItems: "center", paddingBottom: spacing.xxxl, paddingHorizontal: spacing.xl },
  frame: {
    width: 260,
    height: 180,
    borderRadius: radius.lg,
    borderWidth: 3,
    borderColor: colors.accent2,
    alignSelf: "center",
  },
  hint: {
    ...typography.body,
    color: colors.textOnDarkSecondary,
    textAlign: "center",
    marginTop: spacing.lg,
  },
  badge: {
    marginTop: spacing.md,
    backgroundColor: colors.dangerSolid,
    borderRadius: radius.pill,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.sm,
    maxWidth: "100%",
  },
  badgeText: { color: colors.onSolid, fontWeight: "700", fontSize: 12.5 },
  permissionWrap: { flex: 1, alignItems: "center", justifyContent: "center", padding: spacing.xxl },
  permissionTitle: { ...typography.title, color: colors.textOnPrimary, marginTop: spacing.lg, textAlign: "center" },
  permissionBody: {
    ...typography.body,
    color: colors.textOnDarkSecondary,
    textAlign: "center",
    marginTop: spacing.sm,
  },
});
