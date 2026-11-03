import * as DocumentPicker from "expo-document-picker";
import { useRouter } from "expo-router";
import { useState } from "react";
import {
  ActivityIndicator,
  Alert,
  KeyboardAvoidingView,
  Platform,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from "react-native";
import { ApiError } from "../api/client";
import { getSampleTemplate, importInwardWorkbook, submitInwardWithoutPkm } from "../api/endpoints";
import { useAuth } from "../auth/AuthContext";
import { MaterialPickerModal } from "../components/MaterialPickerModal";
import { WarehousePickerModal } from "../components/WarehousePickerModal";
import type { InwardSheetImportOutcome, MaterialDto, WarehouseOption } from "../types/api";
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
      router.push({ pathname: "/inward-result", params: { result: JSON.stringify(result) } });
      setLines([newLine()]);
    } catch (err) {
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
      <ScrollView style={styles.screen} contentContainerStyle={{ paddingBottom: 40 }}>
        <Text style={styles.sectionTitle}>Warehouse</Text>
        <TouchableOpacity style={styles.warehouseButton} onPress={() => setShowWarehousePicker(true)}>
          <Text style={styles.warehouseButtonText}>{warehouse ? `${warehouse.name} (${warehouse.code})` : "Select warehouse"}</Text>
        </TouchableOpacity>

        <Text style={styles.sectionTitle}>Lines</Text>
        {lines.map((line, index) => (
          <View key={line.key} style={styles.lineCard}>
            <View style={styles.lineHeader}>
              <Text style={styles.lineIndex}>Line {index + 1}</Text>
              {lines.length > 1 && (
                <TouchableOpacity onPress={() => removeLine(line.key)}>
                  <Text style={styles.removeLine}>Remove</Text>
                </TouchableOpacity>
              )}
            </View>
            <TouchableOpacity style={styles.materialButton} onPress={() => setMaterialPickerKey(line.key)}>
              <Text style={line.material ? styles.materialButtonText : styles.materialButtonPlaceholder}>
                {line.material ? `${line.material.materialNumber} — ${line.material.description}` : "Select material"}
              </Text>
            </TouchableOpacity>
            <TextInput
              style={styles.qtyInput}
              placeholder="Qty (boxes)"
              keyboardType="number-pad"
              value={line.quantityText}
              onChangeText={(text) => updateLine(line.key, { quantityText: text })}
            />
          </View>
        ))}

        <TouchableOpacity style={styles.addLine} onPress={() => setLines((prev) => [...prev, newLine()])}>
          <Text style={styles.addLineText}>+ Add line</Text>
        </TouchableOpacity>

        {error && <Text style={styles.error}>{error}</Text>}

        <TouchableOpacity style={styles.submitButton} onPress={handleSubmit} disabled={isSubmitting}>
          {isSubmitting ? <ActivityIndicator color="#fff" /> : <Text style={styles.submitButtonText}>Submit Inward</Text>}
        </TouchableOpacity>

        <View style={styles.divider} />

        <Text style={styles.sectionTitle}>Bulk upload</Text>
        <Text style={styles.helperText}>One GRN per sheet. Max 10MB. PKM is assigned later from Inward History.</Text>
        <View style={styles.uploadRow}>
          <TouchableOpacity style={styles.secondaryButton} onPress={handleUpload} disabled={isUploading}>
            {isUploading ? <ActivityIndicator color="#2563eb" /> : <Text style={styles.secondaryButtonText}>Upload Excel</Text>}
          </TouchableOpacity>
          <TouchableOpacity style={styles.secondaryButton} onPress={handleSampleDownload}>
            <Text style={styles.secondaryButtonText}>Sample format</Text>
          </TouchableOpacity>
        </View>

        {importOutcomes && (
          <View style={styles.outcomesBox}>
            {importOutcomes.map((outcome) => (
              <View key={outcome.sheetName} style={styles.outcomeRow}>
                <Text style={styles.outcomeSheet}>{outcome.sheetName}</Text>
                <Text style={styles.outcomeStatus}>{STATUS_LABELS[outcome.status] ?? "Unknown"}</Text>
                <Text style={styles.outcomeMessage}>{outcome.message}</Text>
                {outcome.referenceNumber && <Text style={styles.outcomeRef}>{outcome.referenceNumber}</Text>}
              </View>
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
  screen: { flex: 1, backgroundColor: "#f1f5f9", padding: 16 },
  sectionTitle: { fontSize: 14, fontWeight: "700", color: "#334155", marginTop: 18, marginBottom: 8 },
  helperText: { fontSize: 12, color: "#64748b", marginBottom: 10 },
  warehouseButton: { backgroundColor: "#fff", borderWidth: 1, borderColor: "#cbd5e1", borderRadius: 10, padding: 14 },
  warehouseButtonText: { fontSize: 15, color: "#0f172a", fontWeight: "600" },
  lineCard: { backgroundColor: "#fff", borderRadius: 12, padding: 14, marginBottom: 10, borderWidth: 1, borderColor: "#e2e8f0" },
  lineHeader: { flexDirection: "row", justifyContent: "space-between", marginBottom: 8 },
  lineIndex: { fontSize: 12, fontWeight: "700", color: "#64748b" },
  removeLine: { fontSize: 12, color: "#dc2626", fontWeight: "600" },
  materialButton: { borderWidth: 1, borderColor: "#cbd5e1", borderRadius: 8, padding: 12, marginBottom: 8 },
  materialButtonText: { fontSize: 14, color: "#0f172a" },
  materialButtonPlaceholder: { fontSize: 14, color: "#94a3b8" },
  qtyInput: { borderWidth: 1, borderColor: "#cbd5e1", borderRadius: 8, padding: 12, fontSize: 14 },
  addLine: { alignItems: "center", paddingVertical: 10 },
  addLineText: { color: "#2563eb", fontWeight: "700" },
  error: { color: "#dc2626", marginTop: 8, marginBottom: 4 },
  submitButton: { backgroundColor: "#2563eb", borderRadius: 10, paddingVertical: 14, alignItems: "center", marginTop: 12 },
  submitButtonText: { color: "#fff", fontWeight: "700", fontSize: 15 },
  divider: { height: 1, backgroundColor: "#e2e8f0", marginVertical: 20 },
  uploadRow: { flexDirection: "row", gap: 10 },
  secondaryButton: { flex: 1, borderWidth: 1, borderColor: "#2563eb", borderRadius: 10, paddingVertical: 12, alignItems: "center" },
  secondaryButtonText: { color: "#2563eb", fontWeight: "700" },
  outcomesBox: { marginTop: 16 },
  outcomeRow: { backgroundColor: "#fff", borderRadius: 10, padding: 12, marginBottom: 8, borderWidth: 1, borderColor: "#e2e8f0" },
  outcomeSheet: { fontWeight: "700", color: "#0f172a" },
  outcomeStatus: { fontSize: 12, color: "#2563eb", fontWeight: "600", marginTop: 2 },
  outcomeMessage: { fontSize: 12, color: "#64748b", marginTop: 4 },
  outcomeRef: { fontSize: 12, color: "#0f172a", marginTop: 4, fontWeight: "600" },
});
