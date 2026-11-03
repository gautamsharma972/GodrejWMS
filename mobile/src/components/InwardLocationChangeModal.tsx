import { useEffect, useState } from "react";
import { ActivityIndicator, FlatList, Modal, StyleSheet, Text, TextInput, TouchableOpacity, View } from "react-native";
import { getInwardPutawayLocationOptions } from "../api/endpoints";
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

  useEffect(() => {
    if (!putawayId) {
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
    <Modal visible={putawayId !== null} animationType="slide" onRequestClose={onClose}>
      <View style={styles.container}>
        <View style={styles.header}>
          <Text style={styles.title}>Change location</Text>
          <TouchableOpacity onPress={onClose}>
            <Text style={styles.close}>Close</Text>
          </TouchableOpacity>
        </View>

        {isLoading && <ActivityIndicator color="#2563eb" style={{ marginTop: 20 }} />}

        {!selected && !isLoading && (
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
        )}

        {selected && (
          <View style={styles.form}>
            <Text style={styles.selectedLabel}>New location</Text>
            <Text style={styles.selectedValue}>{selected.locationCode}</Text>

            <Text style={styles.reasonLabel}>Reason</Text>
            <TextInput
              style={styles.reasonInput}
              value={reason}
              onChangeText={setReason}
              placeholder="Why are you changing this location?"
              multiline
            />

            {error && <Text style={styles.error}>{error}</Text>}

            <TouchableOpacity style={styles.secondaryButton} onPress={() => setSelected(null)}>
              <Text style={styles.secondaryButtonText}>Pick a different location</Text>
            </TouchableOpacity>

            <TouchableOpacity
              style={[styles.submitButton, (!reason.trim() || isSaving) && styles.submitButtonDisabled]}
              disabled={!reason.trim() || isSaving}
              onPress={handleSubmit}
            >
              {isSaving ? <ActivityIndicator color="#fff" /> : <Text style={styles.submitButtonText}>Apply change</Text>}
            </TouchableOpacity>
          </View>
        )}
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: "#fff", paddingTop: 56, paddingHorizontal: 16 },
  header: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginBottom: 12 },
  title: { fontSize: 18, fontWeight: "700", color: "#0f172a" },
  close: { color: "#2563eb", fontWeight: "600" },
  row: { paddingVertical: 12, borderBottomWidth: 1, borderBottomColor: "#f1f5f9" },
  rowHeader: { flexDirection: "row", justifyContent: "space-between", alignItems: "center" },
  rowLocation: { fontSize: 15, fontWeight: "700", color: "#0f172a" },
  currentTag: { fontSize: 11, color: "#2563eb", fontWeight: "700" },
  rowMeta: { fontSize: 12, color: "#64748b", marginTop: 2 },
  empty: { textAlign: "center", color: "#94a3b8", marginTop: 40 },
  form: { paddingTop: 12 },
  selectedLabel: { fontSize: 12, color: "#64748b", fontWeight: "700" },
  selectedValue: { fontSize: 20, fontWeight: "700", color: "#0f172a", marginTop: 4, marginBottom: 16 },
  reasonLabel: { fontSize: 13, fontWeight: "600", color: "#334155", marginBottom: 6 },
  reasonInput: { borderWidth: 1, borderColor: "#cbd5e1", borderRadius: 10, padding: 12, minHeight: 80, textAlignVertical: "top", fontSize: 14 },
  error: { color: "#dc2626", marginTop: 10, fontSize: 13 },
  secondaryButton: { alignItems: "center", paddingVertical: 12, marginTop: 16 },
  secondaryButtonText: { color: "#2563eb", fontWeight: "600" },
  submitButton: { backgroundColor: "#2563eb", borderRadius: 10, paddingVertical: 14, alignItems: "center", marginTop: 4 },
  submitButtonDisabled: { opacity: 0.5 },
  submitButtonText: { color: "#fff", fontWeight: "700" },
});
