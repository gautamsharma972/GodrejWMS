import { useLocalSearchParams } from "expo-router";
import { useCallback, useEffect, useState } from "react";
import { ActivityIndicator, Alert, Modal, ScrollView, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { ApiError } from "../../api/client";
import { assignInwardPutawayPkm, downloadInwardDetail, getInwardDetail } from "../../api/endpoints";
import { useAuth } from "../../auth/AuthContext";
import type { InwardDetailDto, InwardPutawayDto } from "../../types/api";
import { saveAndShareXlsx } from "../../utils/saveAndShare";

const MONTHS = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];
const CURRENT_YEAR = new Date().getFullYear();
const YEARS = [CURRENT_YEAR - 1, CURRENT_YEAR, CURRENT_YEAR + 1];

interface PutawayRow {
  putaway: InwardPutawayDto;
  materialNumber: number;
  materialDescription: string;
}

export default function InwardDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { token } = useAuth();

  const [detail, setDetail] = useState<InwardDetailDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [activePutaway, setActivePutaway] = useState<PutawayRow | null>(null);
  const [selectedMonth, setSelectedMonth] = useState<string | null>(null);
  const [selectedYear, setSelectedYear] = useState<number | null>(null);
  const [isSaving, setIsSaving] = useState(false);

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
    load();
  }, [load]);

  if (!token) return null;

  if (isLoading || !detail) {
    return (
      <View style={styles.center}>
        <ActivityIndicator color="#2563eb" />
      </View>
    );
  }

  const rows: PutawayRow[] = detail.lines.flatMap((line) =>
    line.putaways.map((putaway) => ({ putaway, materialNumber: line.materialNumber, materialDescription: line.materialDescription })),
  );

  const handleDownload = async () => {
    try {
      const { bytes, fileName } = await downloadInwardDetail(token, detail.id);
      await saveAndShareXlsx(bytes, fileName);
    } catch {
      Alert.alert("Download failed", "Could not download the Excel file.");
    }
  };

  const openAssign = (row: PutawayRow) => {
    setActivePutaway(row);
    setSelectedMonth(null);
    setSelectedYear(null);
  };

  const handleAssign = async () => {
    if (!activePutaway || !selectedMonth || !selectedYear) return;
    setIsSaving(true);
    try {
      await assignInwardPutawayPkm(token, {
        referenceNumber: detail.referenceNumber,
        assignments: [{ putawayId: activePutaway.putaway.id, mfgMonthText: `${selectedMonth}|${selectedYear}` }],
      });
      setActivePutaway(null);
      await load();
    } catch (err) {
      Alert.alert("Could not assign PKM", err instanceof ApiError ? err.message : "Unexpected error.");
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <View style={styles.screen}>
      <ScrollView contentContainerStyle={{ padding: 16, paddingBottom: 40 }}>
        <Text style={styles.reference}>{detail.referenceNumber}</Text>
        <Text style={styles.subtitle}>{new Date(detail.createdAt).toLocaleString()}</Text>

        {rows.map((row) => {
          return (
            <View key={row.putaway.id} style={styles.row}>
              <Text style={styles.rowMaterial}>
                {row.materialNumber} — {row.materialDescription}
              </Text>
              <Text style={styles.rowLocation}>
                {row.putaway.locationCode} · {row.putaway.quantityBoxes} boxes
              </Text>
              {row.putaway.isConfirmed ? (
                <View style={styles.confirmedPill}>
                  <Text style={styles.confirmedPillText}>PKM confirmed</Text>
                </View>
              ) : (
                <TouchableOpacity style={styles.assignButton} onPress={() => openAssign(row)}>
                  <Text style={styles.assignButtonText}>Assign PKM</Text>
                </TouchableOpacity>
              )}
            </View>
          );
        })}

        <TouchableOpacity style={styles.downloadButton} onPress={handleDownload}>
          <Text style={styles.downloadButtonText}>Download Excel</Text>
        </TouchableOpacity>
      </ScrollView>

      <Modal visible={activePutaway !== null} animationType="slide" transparent onRequestClose={() => setActivePutaway(null)}>
        <TouchableOpacity style={styles.backdrop} activeOpacity={1} onPress={() => setActivePutaway(null)}>
          <View style={styles.sheet}>
            <Text style={styles.sheetTitle}>Assign PKM — {activePutaway?.putaway.locationCode}</Text>

            <Text style={styles.pickerLabel}>Month</Text>
            <View style={styles.chipRow}>
              {MONTHS.map((month) => (
                <TouchableOpacity
                  key={month}
                  style={[styles.chip, selectedMonth === month && styles.chipActive]}
                  onPress={() => setSelectedMonth(month)}
                >
                  <Text style={[styles.chipText, selectedMonth === month && styles.chipTextActive]}>{month}</Text>
                </TouchableOpacity>
              ))}
            </View>

            <Text style={styles.pickerLabel}>Year</Text>
            <View style={styles.chipRow}>
              {YEARS.map((year) => (
                <TouchableOpacity
                  key={year}
                  style={[styles.chip, selectedYear === year && styles.chipActive]}
                  onPress={() => setSelectedYear(year)}
                >
                  <Text style={[styles.chipText, selectedYear === year && styles.chipTextActive]}>{year}</Text>
                </TouchableOpacity>
              ))}
            </View>

            <TouchableOpacity
              style={[styles.submitButton, (!selectedMonth || !selectedYear) && styles.submitButtonDisabled]}
              disabled={!selectedMonth || !selectedYear || isSaving}
              onPress={handleAssign}
            >
              {isSaving ? <ActivityIndicator color="#fff" /> : <Text style={styles.submitButtonText}>Submit</Text>}
            </TouchableOpacity>
          </View>
        </TouchableOpacity>
      </Modal>
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: "#f1f5f9" },
  center: { flex: 1, alignItems: "center", justifyContent: "center" },
  reference: { fontSize: 20, fontWeight: "700", color: "#0f172a" },
  subtitle: { fontSize: 13, color: "#64748b", marginTop: 2, marginBottom: 16 },
  row: { backgroundColor: "#fff", borderRadius: 12, padding: 14, marginBottom: 10, borderWidth: 1, borderColor: "#e2e8f0" },
  rowMaterial: { fontSize: 14, fontWeight: "700", color: "#0f172a" },
  rowLocation: { fontSize: 13, color: "#64748b", marginTop: 4, marginBottom: 8 },
  confirmedPill: { alignSelf: "flex-start", backgroundColor: "#dcfce7", paddingHorizontal: 10, paddingVertical: 4, borderRadius: 999 },
  confirmedPillText: { color: "#16a34a", fontSize: 11, fontWeight: "700" },
  assignButton: { alignSelf: "flex-start", backgroundColor: "#2563eb", paddingHorizontal: 14, paddingVertical: 8, borderRadius: 8 },
  assignButtonText: { color: "#fff", fontSize: 12, fontWeight: "700" },
  downloadButton: { borderWidth: 1, borderColor: "#2563eb", borderRadius: 10, paddingVertical: 14, alignItems: "center", marginTop: 8 },
  downloadButtonText: { color: "#2563eb", fontWeight: "700" },
  backdrop: { flex: 1, backgroundColor: "rgba(15,23,42,0.4)", justifyContent: "flex-end" },
  sheet: { backgroundColor: "#fff", borderTopLeftRadius: 16, borderTopRightRadius: 16, padding: 20 },
  sheetTitle: { fontSize: 16, fontWeight: "700", color: "#0f172a", marginBottom: 16 },
  pickerLabel: { fontSize: 12, fontWeight: "700", color: "#64748b", marginBottom: 8 },
  chipRow: { flexDirection: "row", flexWrap: "wrap", gap: 8, marginBottom: 16 },
  chip: { borderWidth: 1, borderColor: "#cbd5e1", borderRadius: 999, paddingHorizontal: 14, paddingVertical: 8 },
  chipActive: { backgroundColor: "#2563eb", borderColor: "#2563eb" },
  chipText: { fontSize: 13, color: "#334155" },
  chipTextActive: { color: "#fff", fontWeight: "700" },
  submitButton: { backgroundColor: "#2563eb", borderRadius: 10, paddingVertical: 14, alignItems: "center" },
  submitButtonDisabled: { opacity: 0.5 },
  submitButtonText: { color: "#fff", fontWeight: "700" },
});
