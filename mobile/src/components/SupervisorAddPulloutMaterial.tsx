import { useState } from "react";
import { ScrollView, StyleSheet, Text, TouchableOpacity } from "react-native";
import { addPulloutPick, getPulloutAddOptions, searchSupervisorMaterials } from "../api/endpoints";
import { ApiError } from "../api/client";
import type { MaterialDto, PulloutAddOptionDto } from "../types/api";
import { colors, radius, spacing, typography } from "../theme";
import { errorPulse, successPulse } from "../utils/haptics";
import { MaterialPickerModal } from "./MaterialPickerModal";
import { Button } from "./ui/Button";
import { FullScreenModal } from "./ui/FullScreenModal";
import { TextField } from "./ui/TextField";

interface Props {
  token: string;
  pulloutId: number;
  /** Called after material was added so the pick-list is reloaded. */
  onAdded: () => void;
}

/** Supervisor: while the vehicle still has room, add material to the open pick-list by choosing the material code and the location to pick
 * it from. The boxes are deducted from that location at once and the new pick still has to be submitted and confirmed. */
export function SupervisorAddPulloutMaterial({ token, pulloutId, onAdded }: Props) {
  const [open, setOpen] = useState(false);
  const [showMaterials, setShowMaterials] = useState(false);
  const [material, setMaterial] = useState<MaterialDto | null>(null);
  const [options, setOptions] = useState<PulloutAddOptionDto[]>([]);
  const [option, setOption] = useState<PulloutAddOptionDto | null>(null);
  const [qtyText, setQtyText] = useState("");
  const [reason, setReason] = useState("");
  const [loadingOptions, setLoadingOptions] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const reset = () => {
    setMaterial(null); setOptions([]); setOption(null); setQtyText(""); setReason(""); setError(null);
  };

  const chooseMaterial = async (selected: MaterialDto) => {
    setShowMaterials(false);
    setMaterial(selected);
    setOption(null);
    setError(null);
    setLoadingOptions(true);
    try {
      setOptions(await getPulloutAddOptions(token, pulloutId, selected.id));
    } catch (err) {
      setOptions([]);
      setError(err instanceof ApiError ? err.message : "The locations could not be loaded.");
    } finally {
      setLoadingOptions(false);
    }
  };

  const quantity = Number(qtyText);
  const needsReason = option?.breaksFifo === true;
  const canSave = material != null && option != null && Number.isInteger(quantity) && quantity > 0 && quantity <= option.quantityBoxes
    && (!needsReason || reason.trim().length > 0);

  const save = async () => {
    if (!material || !option) return;
    setSaving(true);
    setError(null);
    try {
      await addPulloutPick(token, pulloutId, {
        materialId: material.id,
        palletPositionId: option.palletPositionId,
        quantityBoxes: quantity,
        overrideReason: needsReason ? reason.trim() : null,
      });
      successPulse();
      setOpen(false);
      reset();
      onAdded();
    } catch (err) {
      errorPulse();
      setError(err instanceof ApiError ? err.message : "The material could not be added.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <>
      <Button title="Add material to this pick-list" variant="outline" onPress={() => { reset(); setOpen(true); }} />
      <FullScreenModal visible={open} title="Add material" onClose={() => setOpen(false)}>
        <ScrollView keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator={false}>
          <Text style={styles.label}>MATERIAL</Text>
          <TouchableOpacity style={styles.picker} onPress={() => setShowMaterials(true)}>
            <Text style={material ? styles.pickerValue : styles.pickerPlaceholder}>
              {material ? `${material.materialNumber} · ${material.description}` : "Select material code"}
            </Text>
          </TouchableOpacity>

          {material && (
            <>
              <Text style={styles.label}>PICK FROM LOCATION</Text>
              {loadingOptions && <Text style={styles.hint}>Loading locations...</Text>}
              {!loadingOptions && options.length === 0 && <Text style={styles.hint}>No stock of this material in this warehouse.</Text>}
              {options.map((o) => (
                <TouchableOpacity
                  key={`${o.palletPositionId}-${o.mfgMonthLabel}`}
                  style={[styles.choice, option === o && styles.choiceActive]}
                  onPress={() => { setOption(o); setError(null); }}
                >
                  <Text style={[styles.choiceText, option === o && styles.choiceTextActive]}>
                    {o.locationCode} · {o.mfgMonthLabel}{o.isVirtual ? " · virtual" : ""}
                  </Text>
                  <Text style={styles.choiceQty}>{o.quantityBoxes} boxes{o.breaksFifo ? " · skips older stock" : ""}</Text>
                </TouchableOpacity>
              ))}
            </>
          )}

          {option && (
            <>
              <TextField label={`Quantity (boxes, up to ${option.quantityBoxes})`} value={qtyText} onChangeText={setQtyText} keyboardType="number-pad" selectTextOnFocus />
              {needsReason && (
                <TextField label="Reason for skipping older stock (FIFO)" value={reason} onChangeText={setReason} placeholder="Required" maxLength={250} />
              )}
            </>
          )}
          {error && <Text style={styles.error}>{error}</Text>}
          <Button title="Add to pick-list" size="lg" loading={saving} disabled={!canSave} onPress={save} />
        </ScrollView>
      </FullScreenModal>
      <MaterialPickerModal
        visible={showMaterials}
        token={token}
        searchFn={searchSupervisorMaterials}
        onClose={() => setShowMaterials(false)}
        onSelect={chooseMaterial}
      />
    </>
  );
}

const styles = StyleSheet.create({
  label: { ...typography.caption, color: colors.textSecondary, marginTop: spacing.md, marginBottom: spacing.xs },
  hint: { ...typography.caption, color: colors.textSecondary, marginBottom: spacing.sm },
  error: { color: colors.dangerText, marginVertical: spacing.sm },
  picker: { padding: spacing.md, borderWidth: 1, borderColor: colors.border, borderRadius: radius.md, marginBottom: spacing.md },
  pickerValue: { color: colors.textPrimary },
  pickerPlaceholder: { color: colors.textMuted },
  choice: { flexDirection: "row", justifyContent: "space-between", padding: spacing.md, borderWidth: 1, borderColor: colors.border, borderRadius: radius.md, marginBottom: spacing.sm },
  choiceActive: { backgroundColor: colors.primarySoft, borderColor: colors.primary },
  choiceText: { color: colors.textPrimary },
  choiceTextActive: { color: colors.primaryText, fontWeight: "600" },
  choiceQty: { color: colors.textSecondary },
});
