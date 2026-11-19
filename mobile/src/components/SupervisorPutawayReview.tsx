import { useMemo, useState } from "react";
import { ScrollView, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { recordExcessMaterial, recordInwardVariance, removeInwardVariance, searchSupervisorMaterials } from "../api/endpoints";
import { ApiError } from "../api/client";
import { InwardVarianceType, type AopLocationOptionDto, type InwardDetailDto, type InwardVarianceDto, type InwardVarianceTypeValue, type MaterialDto } from "../types/api";
import { colors, radius, spacing, typography } from "../theme";
import { errorPulse, successPulse } from "../utils/haptics";
import { MaterialPickerModal } from "./MaterialPickerModal";
import { BarcodeScannerModal } from "./BarcodeScannerModal";
import { PickerLocationPickerModal, type PickerLocationOption } from "./PickerLocationPickerModal";
import { AppIcon } from "./ui/AppIcon";
import { Button } from "./ui/Button";
import { FullScreenModal } from "./ui/FullScreenModal";
import { OptionListSheet } from "./ui/OptionListSheet";
import { StatusBadge } from "./ui/StatusBadge";
import { TextField } from "./ui/TextField";

interface Props {
  token: string;
  detail: InwardDetailDto;
  locationOptions: AopLocationOptionDto[];
  /** Called after any change so the GRN is reloaded. */
  onChanged: () => void;
}

const TYPE_LABEL: Record<number, string> = {
  [InwardVarianceType.Damage]: "Damage",
  [InwardVarianceType.Shortage]: "Shortage",
  [InwardVarianceType.Mismatch]: "Mismatch",
  [InwardVarianceType.Excess]: "Excess",
};

const TYPE_TONE: Record<number, "danger" | "warning" | "info"> = {
  [InwardVarianceType.Damage]: "danger",
  [InwardVarianceType.Shortage]: "warning",
  [InwardVarianceType.Mismatch]: "warning",
  [InwardVarianceType.Excess]: "info",
};

const MONTH_LABELS = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];
const CURRENT_YEAR = new Date().getFullYear();
// Same range as the picker's PKM year list.
const YEAR_OPTIONS = Array.from({ length: 5 }, (_, i) => CURRENT_YEAR - 3 + i);

const RECORD_TYPES: InwardVarianceTypeValue[] = [InwardVarianceType.Damage, InwardVarianceType.Shortage, InwardVarianceType.Mismatch];

/** Supervisor-only review panel of a GRN: damage / shortage / mismatch recorded against a put-away (the row shrinks, boxes leave Good stock,
 * damaged boxes stay in the cell as Damage stock) and extra material found while unloading (posted to the chosen location straight away). */
export function SupervisorPutawayReview({ token, detail, locationOptions, onChanged }: Props) {
  // PICK-RESERVED rows are closed by a pullout, not by a confirmation: only the other rows say whether the GRN is confirmed.
  const locked = detail.lines.some((line) => line.putaways.some((p) => p.isExcludedLocked && p.excludedLocation !== "PICK-RESERVED"));
  const [showRecord, setShowRecord] = useState(false);
  const [showExcess, setShowExcess] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // ---- damage / shortage / mismatch form
  const putawayRows = useMemo(
    () => detail.lines.flatMap((line) =>
      line.putaways
        .filter((p) => !p.excludedLocation && p.quantityBoxes > 0)
        .map((p) => ({ id: p.id, label: `${p.locationCode} · ${line.materialNumber} · ${line.mfgMonthLabel}`, quantityBoxes: p.quantityBoxes }))),
    [detail],
  );
  const [rowId, setRowId] = useState<number | null>(null);
  const [type, setType] = useState<InwardVarianceTypeValue>(InwardVarianceType.Damage);
  const [qtyText, setQtyText] = useState("");
  const [remarks, setRemarks] = useState("");
  const [saving, setSaving] = useState(false);
  // Mismatch: what actually arrived. Damage / mismatch: an optional other location to put the boxes in.
  const [actualMaterial, setActualMaterial] = useState<MaterialDto | null>(null);
  const [actualYear, setActualYear] = useState(0);
  const [actualMonth, setActualMonth] = useState(0);
  const [destination, setDestination] = useState<PickerLocationOption | null>(null);
  const [showActualMaterials, setShowActualMaterials] = useState(false);
  const [showActualYear, setShowActualYear] = useState(false);
  const [showActualMonth, setShowActualMonth] = useState(false);
  const [showDestinations, setShowDestinations] = useState(false);
  const [showDestScanner, setShowDestScanner] = useState(false);

  // ---- excess form
  const [material, setMaterial] = useState<MaterialDto | null>(null);
  // PKM is chosen as a year and a month (like the picker's put-away), never typed.
  const [pkmYear, setPkmYear] = useState(0);
  const [pkmMonth, setPkmMonth] = useState(0);
  const [showYearPicker, setShowYearPicker] = useState(false);
  const [showMonthPicker, setShowMonthPicker] = useState(false);
  const [location, setLocation] = useState<PickerLocationOption | null>(null);
  const [excessQty, setExcessQty] = useState("");
  const [excessRemarks, setExcessRemarks] = useState("");
  const [warning, setWarning] = useState<string | null>(null);
  const [showMaterials, setShowMaterials] = useState(false);
  const [showLocations, setShowLocations] = useState(false);
  const locationChoices: PickerLocationOption[] = useMemo(
    () => locationOptions.map((option) => ({ id: option.id, locationCode: option.locationCode })),
    [locationOptions],
  );

  const reset = () => {
    setRowId(null); setType(InwardVarianceType.Damage); setQtyText(""); setRemarks("");
    setActualMaterial(null); setActualYear(0); setActualMonth(0); setDestination(null);
    setMaterial(null); setPkmYear(0); setPkmMonth(0); setLocation(null); setExcessQty(""); setExcessRemarks(""); setWarning(null); setError(null);
  };

  const submitRecord = async () => {
    if (rowId == null) return;
    setSaving(true);
    setError(null);
    try {
      await recordInwardVariance(token, rowId, {
        type,
        quantityBoxes: Number(qtyText),
        remarks: remarks.trim(),
        ...(type === InwardVarianceType.Mismatch && actualMaterial
          ? { actualMaterialId: actualMaterial.id, actualMfgMonthText: `${MONTH_LABELS[actualMonth - 1]}|${actualYear}` }
          : {}),
        ...(type !== InwardVarianceType.Shortage && destination ? { destinationPalletPositionId: destination.id } : {}),
      });
      successPulse();
      setShowRecord(false);
      reset();
      onChanged();
    } catch (err) {
      errorPulse();
      setError(err instanceof ApiError ? err.message : "The record could not be saved.");
    } finally {
      setSaving(false);
    }
  };

  const submitExcess = async (acknowledgeWarning: boolean) => {
    if (!material || !location) return;
    setSaving(true);
    setError(null);
    try {
      const result = await recordExcessMaterial(token, detail.id, {
        materialId: material.id,
        mfgMonthText: `${MONTH_LABELS[pkmMonth - 1]}|${pkmYear}`,
        palletPositionId: location.id,
        quantityBoxes: Number(excessQty),
        remarks: excessRemarks.trim(),
        acknowledgeWarning,
      });
      if (!result.saved) {
        setWarning(result.warning ?? "Check the location.");
        return;
      }
      successPulse();
      setShowExcess(false);
      reset();
      onChanged();
    } catch (err) {
      errorPulse();
      setError(err instanceof ApiError ? err.message : "The excess material could not be saved.");
    } finally {
      setSaving(false);
    }
  };

  const remove = async (record: InwardVarianceDto) => {
    setError(null);
    try {
      await removeInwardVariance(token, record.id);
      successPulse();
      onChanged();
    } catch (err) {
      errorPulse();
      setError(err instanceof ApiError ? err.message : "The record could not be removed.");
    }
  };

  const isMismatch = type === InwardVarianceType.Mismatch;
  const canSaveRecord = rowId != null && Number(qtyText) > 0 && remarks.trim().length > 0
    && (!isMismatch || (actualMaterial != null && actualYear > 0 && actualMonth > 0));
  const canSaveExcess = material != null && location != null && pkmYear > 0 && pkmMonth > 0 && Number(excessQty) > 0 && excessRemarks.trim().length > 0;
  const records = detail.variances ?? [];

  return (
    <View style={styles.card}>
      <Text style={styles.heading}>Damage, shortage, mismatch and excess</Text>
      <Text style={styles.hint}>
        Recorded against the put-away while you review it. Damaged boxes stay in their location as Damage stock; excess is extra material found while unloading and goes to stock at the location you choose.
      </Text>
      {error && !showRecord && !showExcess && <Text style={styles.error}>{error}</Text>}

      {records.map((record) => (
        <View key={record.id} style={styles.record}>
          <View style={{ flex: 1 }}>
            <View style={styles.recordTop}>
              <StatusBadge label={TYPE_LABEL[record.type] ?? "Record"} tone={TYPE_TONE[record.type] ?? "neutral"} variant="soft" />
              <Text style={styles.recordQty}>{record.quantityBoxes} boxes</Text>
            </View>
            <Text style={styles.recordMeta}>
              {record.materialNumber} · {record.mfgMonthLabel} · {record.fromLocationCode ? `${record.fromLocationCode} → ${record.locationCode}` : record.locationCode}
            </Text>
            {record.actualMaterialNumber ? (
              <Text style={styles.recordMeta}>Arrived instead: {record.actualMaterialNumber} · {record.actualMfgMonthLabel}</Text>
            ) : null}
            <Text style={styles.recordRemark}>{record.remarks}</Text>
          </View>
          {!locked && (
            <TouchableOpacity onPress={() => remove(record)} hitSlop={10} accessibilityLabel="Remove record">
              <AppIcon name="close-circle-outline" size={22} color={colors.danger} />
            </TouchableOpacity>
          )}
        </View>
      ))}

      {locked ? (
        <Text style={styles.hint}>This GRN is confirmed, so records can no longer be changed.</Text>
      ) : (
        <View style={styles.buttons}>
          <Button title="Record damage / shortage / mismatch" variant="outline" disabled={putawayRows.length === 0} onPress={() => { reset(); setShowRecord(true); }} />
          <Button title="Add excess material" variant="outline" onPress={() => { reset(); setShowExcess(true); }} />
        </View>
      )}

      <FullScreenModal visible={showRecord} title="Damage / shortage / mismatch" onClose={() => setShowRecord(false)}>
        <ScrollView keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator={false}>
          <Text style={styles.label}>PUT-AWAY</Text>
          {putawayRows.map((row) => (
            <TouchableOpacity key={row.id} style={[styles.choice, rowId === row.id && styles.choiceActive]} onPress={() => setRowId(row.id)}>
              <Text style={[styles.choiceText, rowId === row.id && styles.choiceTextActive]}>{row.label}</Text>
              <Text style={styles.choiceQty}>{row.quantityBoxes} boxes</Text>
            </TouchableOpacity>
          ))}
          <Text style={styles.label}>TYPE</Text>
          <View style={styles.typeRow}>
            {RECORD_TYPES.map((t) => (
              <TouchableOpacity key={t} style={[styles.typeChip, type === t && styles.choiceActive]} onPress={() => setType(t)}>
                <Text style={[styles.choiceText, type === t && styles.choiceTextActive]}>{TYPE_LABEL[t]}</Text>
              </TouchableOpacity>
            ))}
          </View>
          {isMismatch && (
            <>
              <Text style={styles.label}>MATERIAL THAT ARRIVED</Text>
              <TouchableOpacity style={styles.picker} onPress={() => setShowActualMaterials(true)}>
                <Text style={actualMaterial ? styles.pickerValue : styles.pickerPlaceholder}>
                  {actualMaterial ? `${actualMaterial.materialNumber} · ${actualMaterial.description}` : "Select material code"}
                </Text>
              </TouchableOpacity>
              <Text style={styles.label}>PKM THAT ARRIVED</Text>
              <View style={styles.pkmRow}>
                <TouchableOpacity style={styles.pkmButton} onPress={() => setShowActualYear(true)} activeOpacity={0.72}>
                  <View>
                    <Text style={styles.pkmHint}>YEAR</Text>
                    <Text style={styles.pkmValue}>{actualYear > 0 ? actualYear : "Select"}</Text>
                  </View>
                  <AppIcon name="chevron-down" size={17} color={colors.textSecondary} />
                </TouchableOpacity>
                <TouchableOpacity style={styles.pkmButton} onPress={() => setShowActualMonth(true)} activeOpacity={0.72}>
                  <View>
                    <Text style={styles.pkmHint}>MONTH</Text>
                    <Text style={styles.pkmValue}>{actualMonth > 0 ? MONTH_LABELS[actualMonth - 1] : "Select"}</Text>
                  </View>
                  <AppIcon name="chevron-down" size={17} color={colors.textSecondary} />
                </TouchableOpacity>
              </View>
            </>
          )}
          <TextField label="Quantity (boxes)" value={qtyText} onChangeText={setQtyText} keyboardType="decimal-pad" selectTextOnFocus />
          {type !== InwardVarianceType.Shortage && (
            <>
              <Text style={styles.label}>LOCATION (PUT THESE BOXES IN)</Text>
              <View style={styles.locationRow}>
                <TouchableOpacity style={[styles.picker, styles.locationPicker]} onPress={() => setShowDestinations(true)}>
                  <Text style={destination ? styles.pickerValue : styles.pickerPlaceholder}>
                    {destination ? destination.locationCode : "Same location as the put-away"}
                  </Text>
                </TouchableOpacity>
                <TouchableOpacity style={styles.scanButton} onPress={() => setShowDestScanner(true)} hitSlop={6} activeOpacity={0.78} accessibilityLabel="Scan location QR code">
                  <AppIcon name="scan-outline" size={22} color={colors.primary} />
                </TouchableOpacity>
              </View>
              {destination && (
                <TouchableOpacity onPress={() => setDestination(null)}>
                  <Text style={styles.link}>Use the put-away&apos;s own location</Text>
                </TouchableOpacity>
              )}
            </>
          )}
          <TextField label="Remark" value={remarks} onChangeText={setRemarks} placeholder="What was found" maxLength={500} />
          {error && <Text style={styles.error}>{error}</Text>}
          <Button title="Save record" size="lg" loading={saving} disabled={!canSaveRecord} onPress={submitRecord} />
        </ScrollView>
      </FullScreenModal>

      <FullScreenModal visible={showExcess} title="Add excess material" onClose={() => setShowExcess(false)}>
        <ScrollView keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator={false}>
          <Text style={styles.label}>MATERIAL</Text>
          <TouchableOpacity style={styles.picker} onPress={() => setShowMaterials(true)}>
            <Text style={material ? styles.pickerValue : styles.pickerPlaceholder}>
              {material ? `${material.materialNumber} · ${material.description}` : "Select material code"}
            </Text>
          </TouchableOpacity>
          <Text style={styles.label}>PKM</Text>
          <View style={styles.pkmRow}>
            <TouchableOpacity style={styles.pkmButton} onPress={() => setShowYearPicker(true)} activeOpacity={0.72}>
              <View>
                <Text style={styles.pkmHint}>YEAR</Text>
                <Text style={styles.pkmValue}>{pkmYear > 0 ? pkmYear : "Select"}</Text>
              </View>
              <AppIcon name="chevron-down" size={17} color={colors.textSecondary} />
            </TouchableOpacity>
            <TouchableOpacity style={styles.pkmButton} onPress={() => setShowMonthPicker(true)} activeOpacity={0.72}>
              <View>
                <Text style={styles.pkmHint}>MONTH</Text>
                <Text style={styles.pkmValue}>{pkmMonth > 0 ? MONTH_LABELS[pkmMonth - 1] : "Select"}</Text>
              </View>
              <AppIcon name="chevron-down" size={17} color={colors.textSecondary} />
            </TouchableOpacity>
          </View>
          <Text style={styles.label}>LOCATION</Text>
          <TouchableOpacity style={styles.picker} onPress={() => setShowLocations(true)}>
            <Text style={location ? styles.pickerValue : styles.pickerPlaceholder}>{location ? location.locationCode : "Select location"}</Text>
          </TouchableOpacity>
          <TextField label="Quantity (boxes)" value={excessQty} onChangeText={(text) => { setExcessQty(text); setWarning(null); }} keyboardType="decimal-pad" selectTextOnFocus />
          <TextField label="Remark" value={excessRemarks} onChangeText={setExcessRemarks} placeholder="Where it was found" maxLength={500} />
          {warning && <Text style={styles.warning}>{warning}</Text>}
          {error && <Text style={styles.error}>{error}</Text>}
          {warning ? (
            <Button title="Save anyway" size="lg" loading={saving} onPress={() => submitExcess(true)} />
          ) : (
            <Button title="Save excess material" size="lg" loading={saving} disabled={!canSaveExcess} onPress={() => submitExcess(false)} />
          )}
        </ScrollView>
      </FullScreenModal>

      <OptionListSheet
        visible={showYearPicker}
        title="PKM year"
        options={YEAR_OPTIONS.map((year) => ({ value: year, label: String(year) }))}
        onClose={() => setShowYearPicker(false)}
        onSelect={(value) => { setPkmYear(value); setShowYearPicker(false); setWarning(null); }}
      />
      <OptionListSheet
        visible={showMonthPicker}
        title="PKM month"
        options={MONTH_LABELS.map((label, index) => ({ value: index + 1, label }))}
        onClose={() => setShowMonthPicker(false)}
        onSelect={(value) => { setPkmMonth(value); setShowMonthPicker(false); setWarning(null); }}
      />
      <MaterialPickerModal
        visible={showMaterials}
        token={token}
        searchFn={searchSupervisorMaterials}
        onClose={() => setShowMaterials(false)}
        onSelect={(selected) => { setMaterial(selected); setShowMaterials(false); setWarning(null); }}
      />
      <OptionListSheet
        visible={showActualYear}
        title="PKM year that arrived"
        options={YEAR_OPTIONS.map((year) => ({ value: year, label: String(year) }))}
        onClose={() => setShowActualYear(false)}
        onSelect={(value) => { setActualYear(value); setShowActualYear(false); }}
      />
      <OptionListSheet
        visible={showActualMonth}
        title="PKM month that arrived"
        options={MONTH_LABELS.map((label, index) => ({ value: index + 1, label }))}
        onClose={() => setShowActualMonth(false)}
        onSelect={(value) => { setActualMonth(value); setShowActualMonth(false); }}
      />
      <MaterialPickerModal
        visible={showActualMaterials}
        token={token}
        searchFn={searchSupervisorMaterials}
        onClose={() => setShowActualMaterials(false)}
        onSelect={(selected) => { setActualMaterial(selected); setShowActualMaterials(false); }}
      />
      <BarcodeScannerModal
        visible={showDestScanner}
        onClose={() => setShowDestScanner(false)}
        onScan={(value) => {
          // Matched against the same list the search picker uses: exact location code, case and spaces ignored.
          const normalized = value.trim().toUpperCase();
          const match = locationChoices.find((option) => option.locationCode.toUpperCase() === normalized);
          if (!match) return false;
          setDestination(match);
          setShowDestScanner(false);
          return true;
        }}
      />
      <PickerLocationPickerModal
        visible={showDestinations}
        options={locationChoices}
        onScanPress={() => setShowDestScanner(true)}
        onClose={() => setShowDestinations(false)}
        onSelect={(option) => { setDestination(option); setShowDestinations(false); }}
      />
      <PickerLocationPickerModal
        visible={showLocations}
        options={locationChoices}
        onClose={() => setShowLocations(false)}
        onSelect={(option) => { setLocation(option); setShowLocations(false); setWarning(null); }}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  card: { backgroundColor: colors.surface, borderRadius: radius.lg, borderWidth: 1, borderColor: colors.border, padding: spacing.lg, marginTop: spacing.md },
  heading: { ...typography.subtitle, color: colors.textPrimary },
  hint: { ...typography.caption, color: colors.textSecondary, marginTop: spacing.xs, marginBottom: spacing.sm },
  error: { color: colors.dangerText, marginVertical: spacing.sm },
  warning: { color: colors.warningText, backgroundColor: colors.warningSoft, padding: spacing.md, borderRadius: radius.md, marginBottom: spacing.md },
  record: { flexDirection: "row", alignItems: "center", gap: spacing.md, paddingVertical: spacing.md, borderTopWidth: 1, borderTopColor: colors.border },
  recordTop: { flexDirection: "row", alignItems: "center", justifyContent: "space-between" },
  recordQty: { ...typography.subtitle, color: colors.textPrimary },
  recordMeta: { ...typography.caption, color: colors.textSecondary, marginTop: spacing.xs },
  recordRemark: { color: colors.textPrimary, marginTop: spacing.xs },
  buttons: { gap: spacing.sm, marginTop: spacing.md },
  label: { ...typography.caption, color: colors.textSecondary, marginTop: spacing.md, marginBottom: spacing.xs },
  choice: { flexDirection: "row", justifyContent: "space-between", padding: spacing.md, borderWidth: 1, borderColor: colors.border, borderRadius: radius.md, marginBottom: spacing.sm },
  choiceActive: { backgroundColor: colors.primarySoft, borderColor: colors.primary },
  choiceText: { color: colors.textPrimary },
  choiceTextActive: { color: colors.primaryText, fontWeight: "600" },
  choiceQty: { color: colors.textSecondary },
  typeRow: { flexDirection: "row", gap: spacing.sm, marginBottom: spacing.md },
  typeChip: { flex: 1, alignItems: "center", padding: spacing.md, borderWidth: 1, borderColor: colors.border, borderRadius: radius.md },
  pkmRow: { flexDirection: "row", gap: spacing.sm, marginBottom: spacing.md },
  pkmButton: {
    flex: 1, minHeight: 58, flexDirection: "row", justifyContent: "space-between", alignItems: "center",
    borderWidth: 1, borderColor: colors.border, borderRadius: radius.md, paddingVertical: 10, paddingHorizontal: spacing.md, backgroundColor: colors.surfaceAlt,
  },
  pkmHint: { fontSize: 9, lineHeight: 12, fontWeight: "800", color: colors.textMuted, letterSpacing: 0.7, marginBottom: 2 },
  pkmValue: { ...typography.subtitle, color: colors.textPrimary, fontWeight: "800" },
  picker: { padding: spacing.md, borderWidth: 1, borderColor: colors.border, borderRadius: radius.md, marginBottom: spacing.md },
  pickerValue: { color: colors.textPrimary },
  pickerPlaceholder: { color: colors.textMuted },
  locationRow: { flexDirection: "row", alignItems: "center", gap: spacing.sm },
  locationPicker: { flex: 1 },
  scanButton: {
    width: 52, height: 52, borderRadius: radius.md, borderWidth: 1, borderColor: colors.border, backgroundColor: colors.surfaceAlt,
    alignItems: "center", justifyContent: "center", marginBottom: spacing.md,
  },
  link: { color: colors.primary, marginTop: -spacing.sm, marginBottom: spacing.md, fontWeight: "600" },
});
