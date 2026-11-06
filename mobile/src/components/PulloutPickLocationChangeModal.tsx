import { useEffect, useMemo, useState } from "react";
import { FlatList, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { getPulloutPickOptions } from "../api/endpoints";
import { BarcodeScannerModal } from "./BarcodeScannerModal";
import { AppIcon } from "./ui/AppIcon";
import { Button } from "./ui/Button";
import { FullScreenModal } from "./ui/FullScreenModal";
import { LoadingView } from "./ui/LoadingView";
import { TextField } from "./ui/TextField";
import { colors, radius, spacing, typography } from "../theme";
import type { PulloutPickChangeOptionDto } from "../types/api";

export interface ActivePick {
  referenceNumber: string;
  materialNumber: number;
  currentLocationCode: string;
  mfgMonthLabel: string;
  quantityBoxes: number;
}

export interface PickSplit {
  option: PulloutPickChangeOptionDto;
  quantityBoxes: number;
}

/** "move" is the common case - relocate this pick's whole quantity to one other location, fast.
 * "change" is the full editor - split the quantity across several locations, with whatever stays
 * at the original location computed automatically (never lost). Both end up calling onSubmit. */
export type PickChangeMode = "move" | "change";

interface Props {
  pick: ActivePick | null;
  mode: PickChangeMode;
  token: string;
  onClose: () => void;
  /** One pick can now be replaced by several - e.g. 50 of the original 55 boxes stay at the
   * suggested location and the remaining 5 move elsewhere. The server only requires every split's
   * total not to exceed the line's requested quantity - it does not have to equal the original
   * pick's quantity. */
  onSubmit: (splits: PickSplit[], reason: string) => Promise<void>;
}

function optionKey(option: PulloutPickChangeOptionDto): string {
  return `${option.palletPositionId}|${option.mfgMonthLabel}`;
}

export function PulloutPickLocationChangeModal({ pick, mode, token, onClose, onSubmit }: Props) {
  const [options, setOptions] = useState<PulloutPickChangeOptionDto[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  // "change" mode: whatever isn't claimed by extraSplits automatically stays at the original
  // location - it is never a separate thing the user has to remember to re-add.
  const [extraSplits, setExtraSplits] = useState<PickSplit[]>([]);
  // "move" mode: a single destination, full quantity, nothing to split.
  const [moveSplit, setMoveSplit] = useState<PickSplit | null>(null);
  const [isBrowsing, setIsBrowsing] = useState(false);
  const [reason, setReason] = useState("");
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showScanner, setShowScanner] = useState(false);
  const isMove = mode === "move";

  useEffect(() => {
    if (!pick) {
      setOptions([]);
      setExtraSplits([]);
      setMoveSplit(null);
      setIsBrowsing(false);
      setReason("");
      setError(null);
      return;
    }
    setIsLoading(true);
    setExtraSplits([]);
    setMoveSplit(null);
    getPulloutPickOptions(token, pick)
      .then(setOptions)
      .finally(() => setIsLoading(false));
  }, [pick, token]);

  const originalOption = useMemo(() => options.find((option) => option.isCurrent) ?? null, [options]);

  const extraKeys = useMemo(() => new Set(extraSplits.map((split) => optionKey(split.option))), [extraSplits]);
  const browsableOptions = useMemo(
    () => options.filter((option) => !option.isCurrent && !extraKeys.has(optionKey(option))),
    [options, extraKeys],
  );

  const extraTotal = extraSplits.reduce((sum, split) => sum + split.quantityBoxes, 0);
  const originalQty = pick ? Math.max(0, pick.quantityBoxes - extraTotal) : 0;
  const overAllocated = pick ? extraTotal > pick.quantityBoxes : false;

  const addExtraSplit = (option: PulloutPickChangeOptionDto) => {
    if (!pick) return;
    // Default to moving everything still left at the original location to this new one - the
    // common case - the user can dial it back if they only want part of it to go here.
    const defaultQty = Math.max(0, Math.min(originalQty, option.availableBoxes)) || Math.min(1, option.availableBoxes);
    setExtraSplits((prev) => [...prev, { option, quantityBoxes: defaultQty }]);
    setIsBrowsing(false);
  };

  const updateExtraSplitQty = (key: string, text: string) => {
    const value = Number(text);
    setExtraSplits((prev) =>
      prev.map((split) => (optionKey(split.option) === key ? { ...split, quantityBoxes: Number.isFinite(value) ? value : 0 } : split)),
    );
  };

  const removeExtraSplit = (key: string) => {
    setExtraSplits((prev) => prev.filter((split) => optionKey(split.option) !== key));
  };

  const addMoveSplit = (option: PulloutPickChangeOptionDto) => {
    if (!pick) return;
    setMoveSplit({ option, quantityBoxes: pick.quantityBoxes });
    setIsBrowsing(false);
  };

  const moveBrowsableOptions = useMemo(() => options.filter((option) => !option.isCurrent), [options]);

  const handleScanLocation = (value: string): boolean => {
    const normalized = value.trim().toUpperCase();
    const pool = isMove ? moveBrowsableOptions : browsableOptions;
    const match = pool.find((option) => option.locationCode.toUpperCase() === normalized);
    if (!match) return false;
    if (isMove) addMoveSplit(match);
    else addExtraSplit(match);
    setShowScanner(false);
    return true;
  };

  const canSubmit = isMove
    ? moveSplit !== null && moveSplit.quantityBoxes > 0 && reason.trim().length > 0
    : extraSplits.length > 0 && extraSplits.every((split) => split.quantityBoxes > 0) && !overAllocated && reason.trim().length > 0;

  const handleSubmit = async () => {
    if (!canSubmit || !pick) return;
    const finalSplits: PickSplit[] = isMove
      ? moveSplit
        ? [moveSplit]
        : []
      : [...(originalQty > 0 && originalOption ? [{ option: originalOption, quantityBoxes: originalQty }] : []), ...extraSplits];
    if (finalSplits.length === 0) return;

    setIsSaving(true);
    setError(null);
    try {
      await onSubmit(finalSplits, reason.trim());
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not change pick location.");
    } finally {
      setIsSaving(false);
    }
  };

  const showBrowseList = isMove ? isBrowsing || moveSplit === null : isBrowsing;

  return (
    <FullScreenModal visible={pick !== null} title={isMove ? "Move pick location" : "Change pick location"} onClose={onClose}>
      {isLoading && <LoadingView />}

      {!isLoading && pick && (
        <View style={{ flex: 1 }}>
          <View style={styles.summary}>
            <Text style={styles.summaryTitle}>
              {isMove ? `Moving ${pick.quantityBoxes} boxes` : `${pick.quantityBoxes} boxes at ${pick.currentLocationCode}`}
            </Text>
            <Text style={styles.summarySubtitle}>
              Currently at {pick.currentLocationCode} · {pick.mfgMonthLabel}
            </Text>
          </View>

          {isMove ? (
            <>
              {moveSplit && (
                <View style={styles.splitList}>
                  <View style={styles.splitRow}>
                    <View style={{ flex: 1 }}>
                      <Text style={styles.splitLocation}>{moveSplit.option.locationCode}</Text>
                      <Text style={styles.splitMeta}>
                        {moveSplit.option.mfgMonthLabel} · {moveSplit.option.availableBoxes} available
                      </Text>
                    </View>
                    <Text style={styles.splitQtyFixed}>{moveSplit.quantityBoxes} boxes</Text>
                  </View>
                  <Button
                    title="Choose a different location"
                    variant="ghost"
                    onPress={() => setIsBrowsing(true)}
                    style={{ marginTop: spacing.xs }}
                  />
                </View>
              )}

              {showBrowseList && (
                <>
                  {renderScanBanner(() => setShowScanner(true))}
                  {moveSplit && <Button title="Cancel" variant="ghost" onPress={() => setIsBrowsing(false)} style={{ marginBottom: spacing.sm }} />}
                  <FlatList
                    data={moveBrowsableOptions}
                    keyExtractor={(item) => optionKey(item)}
                    renderItem={({ item }) => (
                      <TouchableOpacity style={styles.row} onPress={() => addMoveSplit(item)}>
                        <View style={styles.rowHeader}>
                          <Text style={styles.rowLocation}>{item.locationCode}</Text>
                        </View>
                        <Text style={styles.rowMeta}>
                          {item.mfgMonthLabel} · {item.availableBoxes} available
                        </Text>
                      </TouchableOpacity>
                    )}
                    ListEmptyComponent={<Text style={styles.empty}>No alternate locations available.</Text>}
                  />
                </>
              )}
            </>
          ) : (
            <>
              <View style={styles.splitList}>
                <View style={[styles.splitRow, styles.originalRow]}>
                  <View style={{ flex: 1 }}>
                    <Text style={styles.splitLocation}>{pick.currentLocationCode}</Text>
                    <Text style={styles.splitMeta}>Stays here · remaining after moves below</Text>
                  </View>
                  <Text style={styles.splitQtyFixed}>{originalQty} boxes</Text>
                </View>

                {extraSplits.map((split) => {
                  const key = optionKey(split.option);
                  return (
                    <View key={key} style={styles.splitRow}>
                      <View style={{ flex: 1 }}>
                        <Text style={styles.splitLocation}>{split.option.locationCode}</Text>
                        <Text style={styles.splitMeta}>
                          {split.option.mfgMonthLabel} · {split.option.availableBoxes} available
                        </Text>
                      </View>
                      <TextField
                        value={String(split.quantityBoxes)}
                        onChangeText={(text) => updateExtraSplitQty(key, text)}
                        keyboardType="decimal-pad"
                        style={styles.splitQtyInput}
                      />
                      <TouchableOpacity onPress={() => removeExtraSplit(key)} hitSlop={8} style={styles.removeButton}>
                        <AppIcon name="close" size={16} color={colors.danger} />
                      </TouchableOpacity>
                    </View>
                  );
                })}

                {overAllocated && (
                  <View style={[styles.totalRow, styles.totalRowMismatch]}>
                    <Text style={styles.totalHint}>
                      {extraTotal - pick.quantityBoxes} box(es) over the {pick.quantityBoxes} available here
                    </Text>
                  </View>
                )}

                <Button
                  title="Move some of this to another location"
                  variant="outline"
                  icon={<AppIcon name="add" size={18} color={colors.primary} />}
                  onPress={() => setIsBrowsing(true)}
                  style={{ marginTop: spacing.sm }}
                  disabled={originalQty === 0}
                />
              </View>

              {showBrowseList && (
                <>
                  {renderScanBanner(() => setShowScanner(true))}
                  <Button title="Cancel" variant="ghost" onPress={() => setIsBrowsing(false)} style={{ marginBottom: spacing.sm }} />
                  <FlatList
                    data={browsableOptions}
                    keyExtractor={(item) => optionKey(item)}
                    renderItem={({ item }) => (
                      <TouchableOpacity style={styles.row} onPress={() => addExtraSplit(item)}>
                        <View style={styles.rowHeader}>
                          <Text style={styles.rowLocation}>{item.locationCode}</Text>
                        </View>
                        <Text style={styles.rowMeta}>
                          {item.mfgMonthLabel} · {item.availableBoxes} available
                        </Text>
                      </TouchableOpacity>
                    )}
                    ListEmptyComponent={<Text style={styles.empty}>No alternate locations available.</Text>}
                  />
                </>
              )}
            </>
          )}

          {!showBrowseList && (isMove ? moveSplit !== null : true) && (
            <View style={styles.form}>
              <TextField
                label="Reason"
                value={reason}
                onChangeText={setReason}
                placeholder={isMove ? "Why are you moving this pick?" : "Why are you changing this pick?"}
                multiline
                style={styles.reasonInput}
              />

              {error && <Text style={styles.error}>{error}</Text>}

              <Button
                title={isMove ? "Apply move" : "Apply change"}
                onPress={handleSubmit}
                loading={isSaving}
                disabled={!canSubmit}
                style={{ marginTop: spacing.xs }}
              />
            </View>
          )}
        </View>
      )}

      <BarcodeScannerModal visible={showScanner} onClose={() => setShowScanner(false)} onScan={handleScanLocation} />
    </FullScreenModal>
  );
}

function renderScanBanner(onPress: () => void) {
  return (
    <TouchableOpacity style={styles.scanBanner} onPress={onPress}>
      <View style={styles.scanIcon}>
        <AppIcon name="scan-outline" size={18} color={colors.textOnPrimary} />
      </View>
      <View style={{ flex: 1 }}>
        <Text style={styles.scanBannerTitle}>Scan a location</Text>
        <Text style={styles.scanBannerSubtitle}>Scan the rack/bin barcode instead of picking from the list</Text>
      </View>
      <AppIcon name="chevron-forward" size={18} color={colors.primary} />
    </TouchableOpacity>
  );
}

const styles = StyleSheet.create({
  summary: { marginBottom: spacing.md },
  summaryTitle: { ...typography.title, fontSize: 18, color: colors.textPrimary },
  summarySubtitle: { ...typography.caption, color: colors.textSecondary, marginTop: 2 },
  splitList: { marginBottom: spacing.md },
  splitRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing.sm,
    backgroundColor: colors.surfaceAlt,
    borderRadius: radius.md,
    padding: spacing.sm,
    marginBottom: spacing.sm,
  },
  originalRow: { backgroundColor: colors.primarySoft },
  splitLocation: { ...typography.subtitle, fontSize: 14, color: colors.textPrimary },
  splitMeta: { ...typography.caption, color: colors.textSecondary, marginTop: 2 },
  splitQtyInput: { width: 76, marginBottom: 0 },
  splitQtyFixed: { ...typography.bodyStrong, color: colors.textPrimary },
  removeButton: {
    width: 32,
    height: 32,
    borderRadius: radius.pill,
    backgroundColor: colors.dangerSoft,
    alignItems: "center",
    justifyContent: "center",
  },
  totalRow: { backgroundColor: colors.successSoft, borderRadius: radius.md, padding: spacing.sm, marginTop: spacing.xs },
  totalRowMismatch: { backgroundColor: colors.warningSoft },
  totalText: { ...typography.bodyStrong, color: colors.textPrimary },
  totalHint: { ...typography.caption, color: colors.warningText },
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
  rowMeta: { fontSize: 12, color: colors.textSecondary, marginTop: 2 },
  empty: { textAlign: "center", color: colors.textMuted, marginTop: spacing.xxxl },
  form: { paddingTop: spacing.sm },
  reasonInput: { minHeight: 80, textAlignVertical: "top", borderRadius: radius.md },
  error: { color: colors.danger, marginBottom: spacing.sm, fontSize: 13, fontWeight: "600" },
});
