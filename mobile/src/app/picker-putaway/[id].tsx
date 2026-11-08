import { useLocalSearchParams, useRouter } from "expo-router";
import { useCallback, useEffect, useMemo, useState } from "react";
import { FlatList, ScrollView, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { ApiError } from "../../api/client";
import { getInwardDetail, getPickerLocationOptions, placePickerPutaway } from "../../api/endpoints";
import { useAuth } from "../../auth/AuthContext";
import { BarcodeScannerModal } from "../../components/BarcodeScannerModal";
import {
  buildPickerLocationOptions,
  GIT_OPTION_ID,
  NO_RACK_OPTION_ID,
  PickerLocationPickerModal,
  type PickerLocationOption,
} from "../../components/PickerLocationPickerModal";
import { Button } from "../../components/ui/Button";
import { AppIcon } from "../../components/ui/AppIcon";
import { LoadingView } from "../../components/ui/LoadingView";
import { OptionListSheet } from "../../components/ui/OptionListSheet";
import { Screen } from "../../components/ui/Screen";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { TextField } from "../../components/ui/TextField";
import { colors, radius, spacing, typography } from "../../theme";
import type { AopLocationOptionDto, InwardDetailDto, InwardDetailLineDto, InwardPutawayDto } from "../../types/api";
import { errorPulse, successPulse } from "../../utils/haptics";

const MONTH_LABELS = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];
const CURRENT_YEAR = new Date().getFullYear();
const YEAR_OPTIONS = Array.from({ length: 5 }, (_, i) => CURRENT_YEAR - 3 + i);

interface Row {
  line: InwardDetailLineDto;
  putaway: InwardPutawayDto;
  done: boolean;
}

function buildRows(detail: InwardDetailDto | null): Row[] {
  if (!detail) return [];
  const rows: Row[] = [];
  for (const line of detail.lines) {
    for (const putaway of line.putaways) {
      // On a GRN posted at upload the stock is already at the suggested location: a row is done once the picker has submitted it.
      rows.push({ line, putaway, done: putaway.isPickerPlaced || (!detail.postedAt && putaway.isConfirmed) });
    }
  }
  return rows.sort((a, b) => {
    if (a.done !== b.done) return a.done ? 1 : -1;
    return a.putaway.locationCode.localeCompare(b.putaway.locationCode);
  });
}

function parseMfgMonthLabel(label: string): { year: number; month: number } | null {
  const [monthText, yearText] = label.split("|");
  const monthIndex = MONTH_LABELS.indexOf(monthText?.toUpperCase() ?? "");
  const year = Number(yearText);
  if (monthIndex < 0 || !Number.isFinite(year)) return null;
  return { year, month: monthIndex + 1 };
}

export default function PickerPutawayDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { token } = useAuth();
  const router = useRouter();

  const [detail, setDetail] = useState<InwardDetailDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [locationOptions, setLocationOptions] = useState<AopLocationOptionDto[]>([]);
  const [selectedPutawayId, setSelectedPutawayId] = useState<number | null>(null);

  const [qtyText, setQtyText] = useState("");
  const [pkmYear, setPkmYear] = useState(0);
  const [pkmMonth, setPkmMonth] = useState(0);
  const [newLocationId, setNewLocationId] = useState(0);
  const [newLocationCode, setNewLocationCode] = useState("");
  const [warning, setWarning] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [showLocationPicker, setShowLocationPicker] = useState(false);
  const [showScanner, setShowScanner] = useState(false);
  const [showYearPicker, setShowYearPicker] = useState(false);
  const [showMonthPicker, setShowMonthPicker] = useState(false);

  const load = useCallback(async () => {
    if (!token || !id) return;
    setIsLoading(true);
    try {
      setDetail(await getInwardDetail(token, Number(id)));
    } finally {
      setIsLoading(false);
    }
  }, [token, id]);

  useEffect(() => {
    // Initial route hydration is intentionally driven by the selected GRN id.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    load();
  }, [load]);

  useEffect(() => {
    if (!token) return;
    getPickerLocationOptions(token).then(setLocationOptions);
  }, [token]);

  const rows = useMemo(() => buildRows(detail), [detail]);
  const selectedRow = selectedPutawayId != null ? rows.find((r) => r.putaway.id === selectedPutawayId) ?? null : null;
  const pickerLocationOptions: PickerLocationOption[] = useMemo(() => buildPickerLocationOptions(locationOptions), [locationOptions]);

  const canSubmit =
    Number(qtyText) > 0 && newLocationId !== 0 && (newLocationId < 0 || (pkmYear > 0 && pkmMonth > 0));

  if (!token) return null;

  const selectRow = (row: Row) => {
    setSelectedPutawayId(row.putaway.id);
    setQtyText(String(row.putaway.quantityBoxes));
    setWarning(null);
    setSubmitError(null);
    const locationId =
      row.putaway.excludedLocation === "GIT" ? GIT_OPTION_ID : row.putaway.excludedLocation === "NO RACK" ? NO_RACK_OPTION_ID : row.putaway.palletPositionId;
    setNewLocationId(locationId);
    setNewLocationCode(row.putaway.excludedLocation ?? row.putaway.locationCode);
    const parsed = parseMfgMonthLabel(row.line.mfgMonthLabel);
    setPkmYear(parsed?.year ?? 0);
    setPkmMonth(parsed?.month ?? 0);
  };

  const backToList = () => {
    setSelectedPutawayId(null);
    setWarning(null);
    setSubmitError(null);
  };

  // A scanned barcode/QR is matched against the same option list the search picker uses - exact
  // location code, case/space-insensitive, so "c-19-03" or a trailing newline from the scanner
  // still resolves.
  const handleScanLocation = (value: string): boolean => {
    const normalized = value.trim().toUpperCase();
    const match = pickerLocationOptions.find((option) => option.locationCode.toUpperCase() === normalized);
    if (!match) return false;
    setNewLocationId(match.id);
    setNewLocationCode(match.locationCode);
    setWarning(null);
    setShowScanner(false);
    return true;
  };

  const submit = async (acknowledgeWarning = false) => {
    if (!selectedRow || !canSubmit) return;
    const excluded = newLocationId === GIT_OPTION_ID ? "GIT" : newLocationId === NO_RACK_OPTION_ID ? "NO RACK" : null;
    const mfgMonthText = excluded === null && pkmYear > 0 && pkmMonth > 0 ? `${MONTH_LABELS[pkmMonth - 1]}|${pkmYear}` : null;

    setIsSubmitting(true);
    setSubmitError(null);
    try {
      const result = await placePickerPutaway(token, selectedRow.putaway.id, {
        quantityBoxes: Number(qtyText),
        mfgMonthText,
        newPalletPositionId: excluded === null ? newLocationId : null,
        excludedLocation: excluded,
        acknowledgeWarning,
      });

      if (!result.saved) {
        setWarning(result.warning ?? "This location may need attention.");
        return;
      }

      successPulse();
      setWarning(null);
      setSelectedPutawayId(null);
      const refreshed = await getInwardDetail(token, Number(id));
      setDetail(refreshed);
      if (buildRows(refreshed).every((r) => r.done)) {
        router.back();
      }
    } catch (err) {
      errorPulse();
      setSubmitError(err instanceof ApiError ? err.message : "Could not save this put-away.");
    } finally {
      setIsSubmitting(false);
    }
  };

  if (isLoading || !detail) {
    return (
      <Screen>
        <LoadingView label="Loading GRN details..." />
      </Screen>
    );
  }

  return (
    <Screen padded>
      {!selectedRow ? (
        <>
          <View style={styles.summaryCard}>
            <View style={styles.summaryIcon}><AppIcon name="navigate-outline" size={19} color={colors.primary} /></View>
            <View style={{ flex: 1 }}>
              <Text style={styles.summaryTitle}>{detail.referenceNumber}</Text>
              <Text style={styles.summary}>Tap a location to confirm or change it.</Text>
              <View style={styles.progressTrack}>
                <View style={[styles.progressFill, { width: `${rows.length > 0 ? (rows.filter((r) => r.done).length / rows.length) * 100 : 0}%` }]} />
              </View>
            </View>
            <View style={styles.progressPill}><Text style={styles.summaryStrong}>{rows.filter((r) => r.done).length}/{rows.length}</Text></View>
          </View>
          <FlatList
            data={rows}
            keyExtractor={(row) => String(row.putaway.id)}
            contentContainerStyle={{ paddingBottom: 24 }}
            renderItem={({ item }) => (
              <TouchableOpacity
                style={[styles.row, item.done && styles.rowDone]}
                disabled={!detail?.postedAt && item.putaway.isConfirmed && !(item.putaway.excludedLocation && !item.putaway.isExcludedLocked)}
                onPress={() => selectRow(item)}
              >
                <View style={[styles.locationIcon, item.done && styles.locationIconDone]}>
                  <AppIcon name={item.done ? "checkmark" : "location-outline"} size={17} color={item.done ? colors.success : colors.primary} />
                </View>
                <View style={{ flex: 1 }}>
                  <Text style={[styles.rowLocation, item.done && styles.rowLocationDone]}>{item.putaway.locationCode}</Text>
                  <Text style={styles.rowMeta}>{item.line.materialNumber} · {item.line.mfgMonthLabel}</Text>
                </View>
                <View style={styles.rowStatus}>
                  <Text style={styles.rowQty}>{item.putaway.quantityBoxes} boxes</Text>
                  <StatusBadge
                    label={item.putaway.excludedLocation ?? (item.putaway.isPickerPlaced ? "Submitted" : item.putaway.isConfirmed && !detail?.postedAt ? "Confirmed" : "To do")}
                    tone={item.done ? "success" : "warning"}
                    variant="soft"
                  />
                </View>
              </TouchableOpacity>
            )}
          />
        </>
      ) : (
        <ScrollView
          showsVerticalScrollIndicator={false}
          keyboardShouldPersistTaps="handled"
          contentContainerStyle={styles.editScroll}
        >
          <TouchableOpacity style={styles.backLink} onPress={backToList} activeOpacity={0.72}>
            <AppIcon name="arrow-back" size={17} color={colors.primary} />
            <Text style={styles.backLinkText}>All locations</Text>
          </TouchableOpacity>

          <View style={styles.taskCard}>
            <View style={styles.taskTopRow}>
              <View style={styles.locationMark}>
                <AppIcon name="location-outline" size={20} color={colors.primary} />
              </View>
              <View style={styles.taskLocationCopy}>
                <Text style={styles.editLabelTop}>SUGGESTED LOCATION</Text>
                <Text style={styles.editValue}>{selectedRow.putaway.locationCode}</Text>
              </View>
              <View style={styles.qtyBadge}>
                <Text style={styles.qtyBadgeValue}>{selectedRow.putaway.quantityBoxes}</Text>
                <Text style={styles.qtyBadgeUnit}>boxes</Text>
              </View>
            </View>

            <View style={styles.divider} />

            <Text style={styles.editLabel}>MATERIAL</Text>
            <Text style={styles.materialCode}>{selectedRow.line.materialNumber}</Text>
            <Text style={styles.materialDescription}>{selectedRow.line.materialDescription}</Text>
          </View>

          <View style={styles.formCard}>
            <View style={styles.sectionHeadingRow}>
              <Text style={styles.sectionHeading}>Put-away details</Text>
              <Text style={styles.requiredHint}>All fields required</Text>
            </View>

            <TextField
              label="Quantity (boxes)"
              value={qtyText}
              onChangeText={setQtyText}
              keyboardType="decimal-pad"
              selectTextOnFocus
              style={styles.quantityInput}
            />

            <Text style={styles.editLabel}>PKM</Text>
            <View style={styles.pkmRow}>
              <TouchableOpacity style={styles.pkmButton} onPress={() => setShowYearPicker(true)} activeOpacity={0.72}>
                <View>
                  <Text style={styles.controlHint}>YEAR</Text>
                  <Text style={styles.pkmButtonText}>{pkmYear > 0 ? pkmYear : "Select"}</Text>
                </View>
                <AppIcon name="chevron-down" size={17} color={colors.textSecondary} />
              </TouchableOpacity>
              <TouchableOpacity style={styles.pkmButton} onPress={() => setShowMonthPicker(true)} activeOpacity={0.72}>
                <View>
                  <Text style={styles.controlHint}>MONTH</Text>
                  <Text style={styles.pkmButtonText}>{pkmMonth > 0 ? MONTH_LABELS[pkmMonth - 1] : "Select"}</Text>
                </View>
                <AppIcon name="chevron-down" size={17} color={colors.textSecondary} />
              </TouchableOpacity>
            </View>

            <Text style={styles.editLabel}>DESTINATION LOCATION</Text>
            <View style={styles.locationRow}>
              <TouchableOpacity style={styles.locationButton} onPress={() => setShowLocationPicker(true)} activeOpacity={0.72}>
                <View style={styles.locationButtonIcon}>
                  <AppIcon name="location-outline" size={18} color={colors.primary} />
                </View>
                <View style={styles.locationButtonCopy}>
                  <Text style={styles.controlHint}>MOVE TO</Text>
                  <Text style={styles.locationButtonText} numberOfLines={1}>{newLocationCode || "Search location, GIT or NO RACK"}</Text>
                </View>
                <AppIcon name="chevron-forward" size={18} color={colors.textMuted} />
              </TouchableOpacity>
              <TouchableOpacity style={styles.scanButton} onPress={() => setShowScanner(true)} hitSlop={6} activeOpacity={0.78}>
                <AppIcon name="scan-outline" size={22} color={colors.primary} />
              </TouchableOpacity>
            </View>

            {submitError && (
              <View style={[styles.message, styles.errorMessage]}>
                <AppIcon name="alert-circle-outline" size={17} color={colors.danger} />
                <Text style={styles.error}>{submitError}</Text>
              </View>
            )}
            {warning && (
              <View style={[styles.message, styles.warningMessage]}>
                <AppIcon name="warning-outline" size={17} color={colors.warning} />
                <Text style={styles.warning}>{warning}</Text>
              </View>
            )}

            <Button
              title={isSubmitting ? "Submitting..." : warning ? "Submit anyway" : "Confirm put-away"}
              icon={<AppIcon name="checkmark-circle-outline" size={19} color={colors.textOnPrimary} />}
              onPress={() => submit(warning !== null)}
              loading={isSubmitting}
              disabled={!canSubmit}
              style={styles.submitButton}
            />
          </View>
        </ScrollView>
      )}

      <PickerLocationPickerModal
        visible={showLocationPicker}
        options={pickerLocationOptions}
        onClose={() => setShowLocationPicker(false)}
        onSelect={(option) => {
          setNewLocationId(option.id);
          setNewLocationCode(option.locationCode);
          setWarning(null);
          setShowLocationPicker(false);
        }}
        onScanPress={() => setShowScanner(true)}
      />

      <BarcodeScannerModal visible={showScanner} onClose={() => setShowScanner(false)} onScan={handleScanLocation} />

      <OptionListSheet
        visible={showYearPicker}
        title="PKM year"
        options={YEAR_OPTIONS.map((year) => ({ value: year, label: String(year) }))}
        onClose={() => setShowYearPicker(false)}
        onSelect={(value) => {
          setPkmYear(value);
          setShowYearPicker(false);
        }}
      />

      <OptionListSheet
        visible={showMonthPicker}
        title="PKM month"
        options={MONTH_LABELS.map((label, index) => ({ value: index + 1, label }))}
        onClose={() => setShowMonthPicker(false)}
        onSelect={(value) => {
          setPkmMonth(value);
          setShowMonthPicker(false);
        }}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  summaryCard: { flexDirection: "row", alignItems: "center", backgroundColor: colors.surface, borderWidth: 1, borderColor: colors.border, borderRadius: radius.lg, padding: spacing.md, marginBottom: spacing.md },
  summaryIcon: { width: 38, height: 38, borderRadius: 12, backgroundColor: colors.primarySoft, alignItems: "center", justifyContent: "center", marginRight: spacing.md },
  summaryTitle: { ...typography.subtitle, color: colors.textPrimary },
  summary: { ...typography.caption, color: colors.textSecondary, marginTop: 2 },
  progressTrack: { height: 4, backgroundColor: colors.surfaceAlt, borderRadius: radius.pill, marginTop: spacing.sm, overflow: "hidden" },
  progressFill: { height: "100%", backgroundColor: colors.primary, borderRadius: radius.pill },
  progressPill: { minWidth: 42, borderRadius: radius.pill, backgroundColor: colors.primarySoft, paddingHorizontal: spacing.sm, paddingVertical: 7, alignItems: "center" },
  summaryStrong: { fontWeight: "800", color: colors.textPrimary },
  row: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.lg,
    padding: spacing.md,
    marginBottom: spacing.sm,
    gap: spacing.sm,
  },
  rowDone: { backgroundColor: colors.successSoft, borderColor: "#bbf7d0" },
  locationIcon: { width: 32, height: 32, borderRadius: 10, backgroundColor: colors.primarySoft, alignItems: "center", justifyContent: "center" },
  locationIconDone: { backgroundColor: colors.surface },
  rowLocation: { ...typography.subtitle, color: colors.textPrimary },
  rowLocationDone: { color: colors.textPrimary },
  rowMeta: { fontSize: 12, color: colors.textSecondary, marginTop: 4 },
  rowStatus: { alignItems: "flex-end", gap: 5 },
  rowQty: { ...typography.caption, color: colors.textPrimary, fontWeight: "800" },
  editScroll: { paddingBottom: spacing.xxl },
  backLink: {
    alignSelf: "flex-start",
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
    paddingVertical: 7,
    paddingRight: spacing.sm,
    marginBottom: spacing.sm,
  },
  backLinkText: { ...typography.bodyStrong, color: colors.primary },
  taskCard: {
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.lg,
    padding: spacing.md,
    marginBottom: spacing.md,
  },
  taskTopRow: { flexDirection: "row", alignItems: "center" },
  locationMark: { width: 42, height: 42, borderRadius: 13, backgroundColor: colors.primarySoft, alignItems: "center", justifyContent: "center", marginRight: spacing.sm },
  taskLocationCopy: { flex: 1 },
  editLabelTop: { ...typography.label, fontSize: 10, color: colors.primary, letterSpacing: 0.8 },
  qtyBadge: { flexDirection: "row", alignItems: "baseline", backgroundColor: colors.neutralSoft, borderRadius: radius.pill, paddingHorizontal: spacing.md, paddingVertical: 8, gap: 4 },
  qtyBadgeValue: { fontSize: 16, lineHeight: 19, color: colors.textPrimary, fontWeight: "800" },
  qtyBadgeUnit: { ...typography.caption, color: colors.textSecondary },
  divider: { height: 1, backgroundColor: colors.border, marginVertical: spacing.md },
  editLabel: { ...typography.label, fontSize: 11, color: colors.textSecondary, letterSpacing: 0.35, marginBottom: 6 },
  editValue: { fontSize: 23, lineHeight: 28, color: colors.textPrimary, fontWeight: "800", marginTop: 1 },
  materialCode: { ...typography.subtitle, color: colors.textPrimary, fontWeight: "800", marginBottom: 3 },
  materialDescription: { ...typography.body, color: colors.textSecondary, lineHeight: 19 },
  formCard: {
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.lg,
    padding: spacing.md,
  },
  sectionHeadingRow: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginBottom: spacing.md },
  sectionHeading: { ...typography.subtitle, color: colors.textPrimary, fontWeight: "800" },
  requiredHint: { ...typography.caption, color: colors.textMuted },
  quantityInput: { backgroundColor: colors.surfaceAlt, fontWeight: "700" },
  pkmRow: { flexDirection: "row", gap: spacing.sm, marginBottom: spacing.sm },
  pkmButton: {
    flex: 1,
    minHeight: 58,
    flexDirection: "row",
    justifyContent: "space-between",
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    paddingVertical: 10,
    paddingHorizontal: spacing.md,
    alignItems: "center",
    backgroundColor: colors.surfaceAlt,
  },
  controlHint: { fontSize: 9, lineHeight: 12, fontWeight: "800", color: colors.textMuted, letterSpacing: 0.7, marginBottom: 2 },
  pkmButtonText: { ...typography.subtitle, color: colors.textPrimary, fontWeight: "800" },
  locationButton: {
    flex: 1,
    minWidth: 0,
    minHeight: 58,
    flexDirection: "row",
    alignItems: "center",
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    paddingVertical: 8,
    paddingHorizontal: spacing.sm,
    backgroundColor: colors.surfaceAlt,
  },
  locationButtonIcon: { width: 34, height: 34, borderRadius: 10, backgroundColor: colors.surface, alignItems: "center", justifyContent: "center", marginRight: spacing.sm },
  locationButtonCopy: { flex: 1, minWidth: 0 },
  locationButtonText: { ...typography.subtitle, color: colors.textPrimary, fontWeight: "700" },
  locationRow: { flexDirection: "row", alignItems: "stretch", gap: spacing.sm },
  scanButton: {
    width: 52,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.primarySoft,
    alignItems: "center",
    justifyContent: "center",
  },
  message: { flexDirection: "row", alignItems: "flex-start", gap: spacing.sm, borderRadius: radius.md, padding: spacing.sm, marginTop: spacing.md },
  errorMessage: { backgroundColor: colors.dangerSoft },
  warningMessage: { backgroundColor: colors.warningSoft },
  error: { flex: 1, color: colors.dangerText, fontSize: 12, lineHeight: 17, fontWeight: "700" },
  warning: { flex: 1, color: colors.warningText, fontSize: 12, lineHeight: 17, fontWeight: "700" },
  submitButton: { marginTop: spacing.lg },
});
