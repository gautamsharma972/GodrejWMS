import { useEffect, useMemo, useState } from "react";
import { FlatList, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { getPulloutPickOptions } from "../api/endpoints";
import { AppIcon } from "./ui/AppIcon";
import { Button } from "./ui/Button";
import { FullScreenModal } from "./ui/FullScreenModal";
import { LoadingView } from "./ui/LoadingView";
import { TextField } from "./ui/TextField";
import { colors, radius, spacing, typography } from "../theme";
import type { PulloutPickChangeOptionDto } from "../types/api";

export interface PickerActivePick {
  pickId: number;
  referenceNumber: string;
  materialNumber: number;
  currentLocationCode: string;
  mfgMonthLabel: string;
  quantityBoxes: number;
}

interface Props {
  pick: PickerActivePick | null;
  token: string;
  onClose: () => void;
  /** Mirrors the web app's picker Change location: pick one other location holding this material
   * and take as much as is available there (up to this pick's own quantity) - no splitting, no
   * manual quantity entry, same as PulloutPicker/Index.razor's ChangeLocationAsync. */
  onSubmit: (option: PulloutPickChangeOptionDto, quantityBoxes: number, reason: string) => Promise<void>;
}

/** Ditto of the web app's picker "Change location" tab: a flat list of candidate locations (PKM,
 * boxes available), one tap selects it, a reason is required only when it overrides FIFO, then a
 * single "Take from X" button submits. */
export function PulloutPickerChangeLocationModal({ pick, token, onClose, onSubmit }: Props) {
  const [options, setOptions] = useState<PulloutPickChangeOptionDto[] | null>(null);
  const [selected, setSelected] = useState<PulloutPickChangeOptionDto | null>(null);
  const [reason, setReason] = useState("");
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!pick) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setOptions(null);
      setSelected(null);
      setReason("");
      setError(null);
      return;
    }
    setOptions(null);
    setSelected(null);
    setReason("");
    setError(null);
    let cancelled = false;
    getPulloutPickOptions(token, pick)
      .then((value) => !cancelled && setOptions(value))
      .catch((err) => !cancelled && setError(err instanceof Error ? err.message : "Could not load locations."));
    return () => {
      cancelled = true;
    };
  }, [pick, token]);

  const quantity = useMemo(
    () => (pick && selected ? Math.min(pick.quantityBoxes, selected.availableBoxes) : 0),
    [pick, selected],
  );

  if (!pick) return null;

  const canSubmit = selected !== null && !selected.isCurrent && (!selected.breaksFifo || reason.trim().length > 0);

  const submit = async () => {
    if (!canSubmit || !selected) return;
    setIsSaving(true);
    setError(null);
    try {
      await onSubmit(selected, quantity, selected.breaksFifo ? reason.trim() : "");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not change pick location.");
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <FullScreenModal visible title="Change pick location" onClose={onClose}>
      <View style={styles.summary}>
        <Text style={styles.summaryTitle}>{pick.quantityBoxes} boxes at {pick.currentLocationCode}</Text>
        <Text style={styles.summarySubtitle}>Take this pick from another location that holds the material.</Text>
      </View>

      {options === null ? (
        error ? <Text style={styles.error}>{error}</Text> : <LoadingView />
      ) : options.length === 0 ? (
        <View style={styles.empty}>
          <AppIcon name="alert-circle-outline" size={22} color={colors.textMuted} />
          <Text style={styles.emptyText}>No other location holds this material.</Text>
        </View>
      ) : (
        <FlatList
          data={options}
          keyExtractor={(item) => `${item.palletPositionId}|${item.mfgMonthLabel}`}
          contentContainerStyle={{ paddingBottom: spacing.lg }}
          renderItem={({ item }) => {
            const isSelected = selected?.palletPositionId === item.palletPositionId && selected.mfgMonthLabel === item.mfgMonthLabel;
            return (
              <TouchableOpacity
                style={[styles.option, isSelected && styles.optionSelected, item.isCurrent && styles.optionDisabled]}
                disabled={item.isCurrent}
                onPress={() => setSelected(item)}
              >
                <Text style={styles.optionLocation}>{item.locationCode}</Text>
                <Text style={styles.optionMeta}>
                  PKM {item.mfgMonthLabel} · {item.availableBoxes} box(es) available
                  {item.isCurrent ? " · current location" : item.breaksFifo ? " · newer PKM (overrides FIFO)" : ""}
                </Text>
              </TouchableOpacity>
            );
          }}
        />
      )}

      <View style={styles.footer}>
        {selected?.breaksFifo && (
          <TextField
            label="Reason for overriding FIFO"
            value={reason}
            onChangeText={setReason}
            placeholder="A newer PKM is selected - why? e.g. older stock is damaged"
            multiline
            style={styles.reasonInput}
          />
        )}
        {error && <Text style={styles.error}>{error}</Text>}
        <Button
          title={selected ? `Take from ${selected.locationCode}` : "Select a location"}
          onPress={submit}
          loading={isSaving}
          disabled={!canSubmit}
          icon={<AppIcon name="swap-horizontal" size={20} color={colors.textOnPrimary} />}
        />
      </View>
    </FullScreenModal>
  );
}

const styles = StyleSheet.create({
  summary: { marginBottom: spacing.md },
  summaryTitle: { ...typography.title, fontSize: 18, color: colors.textPrimary },
  summarySubtitle: { ...typography.caption, color: colors.textSecondary, marginTop: 2 },
  option: {
    borderWidth: 1.5,
    borderColor: colors.border,
    borderRadius: radius.md,
    padding: spacing.md,
    marginBottom: spacing.sm,
    backgroundColor: colors.surface,
  },
  optionSelected: { borderColor: colors.primary, backgroundColor: colors.primarySoft },
  optionDisabled: { opacity: 0.5 },
  optionLocation: { ...typography.bodyStrong, color: colors.textPrimary },
  optionMeta: { ...typography.caption, color: colors.textSecondary, marginTop: 2 },
  empty: { alignItems: "center", paddingVertical: spacing.xxxl, gap: spacing.sm },
  emptyText: { ...typography.body, color: colors.textMuted, textAlign: "center" },
  footer: { paddingTop: spacing.sm },
  reasonInput: { minHeight: 80, textAlignVertical: "top", borderRadius: radius.md },
  error: { color: colors.danger, marginBottom: spacing.sm, fontSize: 13, fontWeight: "600" },
});
