import * as DocumentPicker from "expo-document-picker";
import { useRouter } from "expo-router";
import { useState } from "react";
import { Alert, KeyboardAvoidingView, Platform, ScrollView, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { ApiError } from "../api/client";
import { getSampleTemplate, importInwardWorkbook, submitInwardWithoutPkm } from "../api/endpoints";
import { useAuth } from "../auth/AuthContext";
import { MaterialPickerModal } from "../components/MaterialPickerModal";
import { Button } from "../components/ui/Button";
import { Card } from "../components/ui/Card";
import { AppIcon } from "../components/ui/AppIcon";
import { TextField } from "../components/ui/TextField";
import { WarehousePickerModal } from "../components/WarehousePickerModal";
import { colors, radius, spacing, typography } from "../theme";
import type { InwardSheetImportOutcome, MaterialDto, WarehouseOption } from "../types/api";
import { errorPulse, successPulse } from "../utils/haptics";
import { saveAndShareXlsx } from "../utils/saveAndShare";

interface LineRow {
  key: string;
  material: MaterialDto | null;
  quantityText: string;
}

function newLine(): LineRow {
  return { key: Math.random().toString(36).slice(2), material: null, quantityText: "" };
}

const STATUS_LABELS: Record<number, string> = {
  0: "Created",
  1: "Not an order sheet",
  2: "No data",
  3: "Missing PKM",
  4: "Duplicate GRN reference",
  5: "Duplicate LR/vehicle",
  6: "Error",
};

export default function InwardEntryScreen() {
  const { token, warehouses } = useAuth();
  const router = useRouter();

  const [warehouse, setWarehouse] = useState<WarehouseOption | null>(warehouses[0] ?? null);
  const [showWarehousePicker, setShowWarehousePicker] = useState(false);
  const [lines, setLines] = useState<LineRow[]>([newLine()]);
  const [materialPickerKey, setMaterialPickerKey] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isUploading, setIsUploading] = useState(false);
  const [importOutcomes, setImportOutcomes] = useState<InwardSheetImportOutcome[] | null>(null);

  if (!token) return null;

  const updateLine = (key: string, patch: Partial<LineRow>) =>
    setLines((prev) => prev.map((line) => (line.key === key ? { ...line, ...patch } : line)));

  const removeLine = (key: string) => setLines((prev) => (prev.length > 1 ? prev.filter((line) => line.key !== key) : prev));

  const validate = (): string | null => {
    if (!warehouse) return "Select a warehouse before submitting.";
    const populated = lines.filter((line) => line.material !== null || line.quantityText.trim() !== "");
    if (populated.length === 0) return "Select a material and enter a quantity.";
    for (const line of populated) {
      if (!line.material) return "Select a valid material for every line.";
      const qty = Number(line.quantityText);
      if (!line.quantityText.trim() || !(qty > 0)) return "Quantity must be greater than zero.";
      if (!Number.isInteger(qty)) return "Quantity must be a whole number (no decimals).";
    }
    return null;
  };

  const handleSubmit = async () => {
    setError(null);
    const validationError = validate();
    if (validationError) {
      setError(validationError);
      return;
    }

    const payload = {
      warehouseId: warehouse!.id,
      lines: lines
        .filter((line) => line.material !== null)
        .map((line) => ({ materialCode: line.material!.materialNumber, quantityBoxes: Number(line.quantityText) })),
    };

    setIsSubmitting(true);
    try {
      const result = await submitInwardWithoutPkm(token, payload);
      successPulse();
      router.push({ pathname: "/inward-result", params: { result: JSON.stringify(result) } });
      setLines([newLine()]);
    } catch (err) {
      errorPulse();
      if (err instanceof ApiError) {
        const firstFieldError = err.fieldErrors ? Object.values(err.fieldErrors)[0]?.[0] : undefined;
        setError(firstFieldError ?? err.message);
      } else {
        setError("Unable to submit. Check your connection and try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleUpload = async () => {
    if (!warehouse) {
      setError("Select a warehouse before uploading.");
      return;
    }
    const result = await DocumentPicker.getDocumentAsync({
      type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
      copyToCacheDirectory: true,
    });
    if (result.canceled || !result.assets?.[0]) return;

    const asset = result.assets[0];
    setIsUploading(true);
    setError(null);
    setImportOutcomes(null);
    try {
      const outcomes = await importInwardWorkbook(token, asset.uri, asset.name, warehouse.id);
      setImportOutcomes(outcomes);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Unable to upload the workbook.");
    } finally {
      setIsUploading(false);
    }
  };

  const handleSampleDownload = async () => {
    try {
      const { bytes, fileName } = await getSampleTemplate(token);
      await saveAndShareXlsx(bytes, fileName);
    } catch {
      Alert.alert("Download failed", "Could not download the sample format.");
    }
  };

  return (
    <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === "ios" ? "padding" : undefined}>
      <ScrollView style={styles.screen} contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
        <View style={styles.introRow}>
          <View>
            <Text style={styles.eyebrow}>GOODS RECEIPT · NO PKM</Text>
            <Text style={styles.introTitle}>Record stock quickly</Text>
          </View>
          <View style={styles.readyPill}><View style={styles.readyDot} /><Text style={styles.readyText}>Ready</Text></View>
        </View>

        <TouchableOpacity style={styles.warehouseButton} onPress={() => setShowWarehousePicker(true)} activeOpacity={0.75}>
          <View style={styles.warehouseIcon}><AppIcon name="business-outline" size={18} color={colors.primary} /></View>
          <View style={styles.warehouseCopy}>
            <Text style={styles.fieldEyebrow}>WAREHOUSE</Text>
            <Text style={styles.warehouseButtonText}>{warehouse ? `${warehouse.name} · ${warehouse.code}` : "Select warehouse"}</Text>
          </View>
          <AppIcon name="chevron-down" size={18} color={colors.textMuted} />
        </TouchableOpacity>

        <View style={styles.sectionHeader}>
          <View>
            <Text style={styles.sectionHeading}>Manual lines</Text>
            <Text style={styles.sectionHint}>Material and box quantity</Text>
          </View>
          <TouchableOpacity style={styles.addLineButton} onPress={() => setLines((prev) => [...prev, newLine()])}>
            <AppIcon name="add" size={17} color={colors.primary} />
            <Text style={styles.addLineText}>Add line</Text>
          </TouchableOpacity>
        </View>
        {lines.map((line, index) => (
          <Card key={line.key} style={styles.lineCard}>
            <View style={styles.lineHeader}>
              <Text style={styles.lineIndex}>LINE {index + 1}</Text>
              {lines.length > 1 && (
                <TouchableOpacity style={styles.removeButton} onPress={() => removeLine(line.key)}>
                  <AppIcon name="trash-outline" size={15} color={colors.danger} />
                </TouchableOpacity>
              )}
            </View>
            <TouchableOpacity style={styles.materialButton} onPress={() => setMaterialPickerKey(line.key)}>
              <Text style={line.material ? styles.materialButtonText : styles.materialButtonPlaceholder}>
                {line.material ? `${line.material.materialNumber} — ${line.material.description}` : "Select material"}
              </Text>
            </TouchableOpacity>
            <TextField
              placeholder="Qty (boxes)"
              keyboardType="number-pad"
              value={line.quantityText}
              onChangeText={(text) => updateLine(line.key, { quantityText: text })}
              style={{ marginBottom: 0 }}
            />
          </Card>
        ))}

        {error && <View style={styles.errorBox}><AppIcon name="alert-circle-outline" size={17} color={colors.danger} /><Text style={styles.error}>{error}</Text></View>}

        <Button title="Submit Inward" onPress={handleSubmit} loading={isSubmitting} size="lg" style={{ marginTop: spacing.sm }} />

        <View style={styles.uploadCard}>
          <View style={styles.uploadTitleRow}>
            <View style={styles.uploadIcon}><AppIcon name="cloud-upload-outline" size={19} color={colors.primary} /></View>
            <View style={{ flex: 1 }}>
              <Text style={styles.sectionHeading}>Bulk upload</Text>
              <Text style={styles.helperText}>One GRN per sheet · XLSX up to 10 MB</Text>
            </View>
          </View>
          <View style={styles.uploadRow}>
            <Button title="Upload Excel" variant="outline" onPress={handleUpload} loading={isUploading} fullWidth={false} style={{ flex: 1 }} />
            <Button title="Template" variant="ghost" onPress={handleSampleDownload} fullWidth={false} style={{ flex: 1 }} />
          </View>
        </View>

        {importOutcomes && (
          <View style={styles.outcomesBox}>
            {importOutcomes.map((outcome) => (
              <Card key={outcome.sheetName} style={styles.outcomeRow}>
                <Text style={styles.outcomeSheet}>{outcome.sheetName}</Text>
                <Text style={styles.outcomeStatus}>{STATUS_LABELS[outcome.status] ?? "Unknown"}</Text>
                <Text style={styles.outcomeMessage}>{outcome.message}</Text>
                {outcome.referenceNumber && <Text style={styles.outcomeRef}>{outcome.referenceNumber}</Text>}
              </Card>
            ))}
          </View>
        )}
      </ScrollView>

      <WarehousePickerModal
        visible={showWarehousePicker}
        warehouses={warehouses}
        onClose={() => setShowWarehousePicker(false)}
        onSelect={(w) => {
          setWarehouse(w);
          setShowWarehousePicker(false);
        }}
      />

      <MaterialPickerModal
        visible={materialPickerKey !== null}
        token={token}
        onClose={() => setMaterialPickerKey(null)}
        onSelect={(material) => {
          if (materialPickerKey) updateLine(materialPickerKey, { material });
          setMaterialPickerKey(null);
        }}
      />
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  screen: { flex: 1, backgroundColor: colors.background },
  content: { padding: spacing.lg, paddingBottom: 40 },
  introRow: { flexDirection: "row", alignItems: "center", justifyContent: "space-between", marginBottom: spacing.md },
  eyebrow: { ...typography.label, fontSize: 10, color: colors.primary, letterSpacing: 0.8 },
  introTitle: { ...typography.title, color: colors.textPrimary, marginTop: 2 },
  readyPill: { flexDirection: "row", alignItems: "center", gap: 5, borderRadius: radius.pill, backgroundColor: colors.successSoft, paddingHorizontal: spacing.sm, paddingVertical: 6 },
  readyDot: { width: 7, height: 7, borderRadius: 4, backgroundColor: colors.success },
  readyText: { ...typography.caption, color: colors.successText, fontWeight: "800" },
  warehouseButton: { flexDirection: "row", alignItems: "center", backgroundColor: colors.surface, borderWidth: 1, borderColor: colors.border, borderRadius: radius.lg, padding: spacing.md, marginBottom: spacing.lg },
  warehouseIcon: { width: 38, height: 38, borderRadius: 12, backgroundColor: colors.primarySoft, alignItems: "center", justifyContent: "center", marginRight: spacing.md },
  warehouseCopy: { flex: 1 },
  fieldEyebrow: { ...typography.label, fontSize: 9, color: colors.textMuted, letterSpacing: 0.7 },
  warehouseButtonText: { ...typography.subtitle, fontSize: 14, color: colors.textPrimary, marginTop: 2 },
  sectionHeader: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginBottom: spacing.sm },
  sectionHeading: { ...typography.subtitle, color: colors.textPrimary },
  sectionHint: { ...typography.caption, color: colors.textSecondary, marginTop: 1 },
  addLineButton: { flexDirection: "row", alignItems: "center", gap: 4, backgroundColor: colors.primarySoft, borderRadius: radius.pill, paddingHorizontal: spacing.md, paddingVertical: spacing.sm },
  addLineText: { ...typography.caption, color: colors.primaryText, fontWeight: "800" },
  helperText: { fontSize: 11, color: colors.textMuted, marginTop: 2 },
  lineCard: { marginBottom: spacing.sm, padding: spacing.md, shadowOpacity: 0 },
  lineHeader: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginBottom: spacing.sm },
  lineIndex: { ...typography.label, color: colors.textMuted },
  removeButton: { width: 30, height: 30, borderRadius: 10, backgroundColor: colors.dangerSoft, alignItems: "center", justifyContent: "center" },
  materialButton: { borderWidth: 1.5, borderColor: colors.border, borderRadius: radius.sm, padding: spacing.md, marginBottom: spacing.sm, backgroundColor: colors.surfaceAlt },
  materialButtonText: { fontSize: 14, color: colors.textPrimary },
  materialButtonPlaceholder: { fontSize: 14, color: colors.textMuted },
  errorBox: { flexDirection: "row", alignItems: "center", gap: spacing.sm, backgroundColor: colors.dangerSoft, borderRadius: radius.md, padding: spacing.md, marginTop: spacing.sm },
  error: { flex: 1, color: colors.dangerText, fontSize: 12, fontWeight: "700" },
  uploadCard: { backgroundColor: colors.surface, borderRadius: radius.lg, borderWidth: 1, borderColor: colors.border, padding: spacing.md, marginTop: spacing.lg },
  uploadTitleRow: { flexDirection: "row", alignItems: "center", marginBottom: spacing.md },
  uploadIcon: { width: 38, height: 38, borderRadius: 12, backgroundColor: colors.primarySoft, alignItems: "center", justifyContent: "center", marginRight: spacing.md },
  uploadRow: { flexDirection: "row", gap: spacing.sm },
  outcomesBox: { marginTop: spacing.lg },
  outcomeRow: { marginBottom: spacing.sm },
  outcomeSheet: { ...typography.subtitle, color: colors.textPrimary },
  outcomeStatus: { fontSize: 12, color: colors.primary, fontWeight: "700", marginTop: 2 },
  outcomeMessage: { fontSize: 12, color: colors.textSecondary, marginTop: 4 },
  outcomeRef: { fontSize: 12, color: colors.textPrimary, marginTop: 4, fontWeight: "700" },
});
