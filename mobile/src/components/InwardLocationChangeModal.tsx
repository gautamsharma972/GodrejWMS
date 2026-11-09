import { useEffect, useState } from "react";
import { FlatList, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { getInwardPutawayLocationOptions } from "../api/endpoints";
import { BarcodeScannerModal } from "./BarcodeScannerModal";
import { AppIcon } from "./ui/AppIcon";
import { Button } from "./ui/Button";
import { FullScreenModal } from "./ui/FullScreenModal";
import { LoadingView } from "./ui/LoadingView";
import { TextField } from "./ui/TextField";
import { colors, radius, spacing, typography } from "../theme";
import type { InwardPalletChangeOptionDto } from "../types/api";

interface Props {
  putawayId: number | null;
  token: string;
  onClose: () => void;
  onSubmit: (newPalletPositionId: number, reason: string) => Promise<void>;
}

export function InwardLocationChangeModal({ putawayId, token, onClose, onSubmit }: Props) {
  const [options, setOptions] = useState<InwardPalletChangeOptionDto[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [selected, setSelected] = useState<InwardPalletChangeOptionDto | null>(null);
  const [reason, setReason] = useState("");
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showScanner, setShowScanner] = useState(false);

  useEffect(() => {
    if (!putawayId) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- intentional reset each time the modal opens/closes
      setOptions([]);
      setSelected(null);
      setReason("");
      setError(null);
      return;
    }
    setIsLoading(true);
    getInwardPutawayLocationOptions(token, putawayId)
      .then(setOptions)
      .finally(() => setIsLoading(false));
  }, [putawayId, token]);

  // Scanned codes are matched only against this putaway's own valid candidate locations (the same
  // list the row picker shows) - scanning some other location in the warehouse correctly misses.
  const handleScanLocation = (value: string): boolean => {
    const normalized = value.trim().toUpperCase();
    const match = options.find((option) => option.locationCode.toUpperCase() === normalized);
    if (!match) return false;
    setSelected(match);
    setShowScanner(false);
    return true;
  };

  const handleSubmit = async () => {
    if (!selected || !reason.trim()) return;
    setIsSaving(true);
    setError(null);
    try {
      await onSubmit(selected.id, reason.trim());
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not change location.");
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <FullScreenModal visible={putawayId !== null} title="Change location" onClose={onClose}>
      {isLoading && <LoadingView />}

      {!selected && !isLoading && (
        <>
          <TouchableOpacity style={styles.scanBanner} onPress={() => setShowScanner(true)}>
            <View style={styles.scanIcon}>
              <AppIcon name="scan-outline" size={18} color={colors.textOnPrimary} />
            </View>
            <View style={{ flex: 1 }}>
              <Text style={styles.scanBannerTitle}>Scan a location</Text>
              <Text style={styles.scanBannerSubtitle}>Scan the rack/bin barcode instead of picking from the list</Text>
            </View>
            <AppIcon name="chevron-forward" size={18} color={colors.primary} />
          </TouchableOpacity>
          <FlatList
          data={options}
          keyExtractor={(item) => String(item.id)}
          renderItem={({ item }) => (
            <TouchableOpacity style={styles.row} onPress={() => setSelected(item)}>
              <View style={styles.rowHeader}>
                <Text style={styles.rowLocation}>{item.locationCode}</Text>
                {item.isCurrent && <Text style={styles.currentTag}>Current</Text>}
              </View>
              <Text style={styles.rowMeta}>
                {item.zoneTypeName} · {item.freeBoxes} free of {item.capacityBoxes} boxes
              </Text>
            </TouchableOpacity>
          )}
            ListEmptyComponent={<Text style={styles.empty}>No alternate locations available.</Text>}
          />
        </>
      )}

      {selected && (
        <View style={styles.form}>
          <Text style={styles.selectedLabel}>New location</Text>
          <Text style={styles.selectedValue}>{selected.locationCode}</Text>

          <TextField
            label="Reason"
            value={reason}
            onChangeText={setReason}
            placeholder="Why are you changing this location?"
            multiline
            style={styles.reasonInput}
          />

          {error && <Text style={styles.error}>{error}</Text>}

          <Button title="Pick a different location" variant="ghost" onPress={() => setSelected(null)} />
          <Button title="Apply change" onPress={handleSubmit} loading={isSaving} disabled={!reason.trim()} style={{ marginTop: spacing.xs }} />
        </View>
      )}

      <BarcodeScannerModal visible={showScanner} onClose={() => setShowScanner(false)} onScan={handleScanLocation} />
    </FullScreenModal>
  );
}

const styles = StyleSheet.create({
  scanBanner: {
    flexDirection: "row",
    alignItems: "center",
    backgroundColor: colors.primarySoft,
    borderRadius: radius.lg,
    padding: spacing.md,
    marginBottom: spacing.md,
    gap: spacing.md,
  },
  scanIcon: {
    width: 36,
    height: 36,
    borderRadius: radius.md,
    backgroundColor: colors.primary,
    alignItems: "center",
    justifyContent: "center",
  },
  scanBannerTitle: { ...typography.subtitle, fontSize: 14, color: colors.primaryText },
  scanBannerSubtitle: { ...typography.caption, color: colors.primaryText, marginTop: 2, opacity: 0.85 },
  row: { paddingVertical: spacing.md, borderBottomWidth: 1, borderBottomColor: colors.border },
  rowHeader: { flexDirection: "row", justifyContent: "space-between", alignItems: "center" },
  rowLocation: { ...typography.subtitle, color: colors.textPrimary },
  currentTag: { fontSize: 11, color: colors.primary, fontWeight: "700" },
  rowMeta: { fontSize: 12, color: colors.textSecondary, marginTop: 2 },
  empty: { textAlign: "center", color: colors.textMuted, marginTop: spacing.xxxl },
  form: { paddingTop: spacing.md },
  selectedLabel: { ...typography.caption, color: colors.textSecondary, fontWeight: "700" },
  selectedValue: { ...typography.display, fontSize: 22, color: colors.textPrimary, marginTop: spacing.xs, marginBottom: spacing.lg },
  reasonInput: { minHeight: 80, textAlignVertical: "top", borderRadius: radius.md },
  error: { color: colors.danger, marginBottom: spacing.sm, fontSize: 13, fontWeight: "600" },
});
