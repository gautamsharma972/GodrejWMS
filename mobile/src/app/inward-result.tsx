import { useLocalSearchParams, useRouter } from "expo-router";
import { useMemo, useState } from "react";
import { Alert, ScrollView, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { downloadInwardDetail } from "../api/endpoints";
import { useAuth } from "../auth/AuthContext";
import { PalletPositionModal } from "../components/PalletPositionModal";
import type { InwardResultDto } from "../types/api";
import { saveAndShareXlsx } from "../utils/saveAndShare";
import { allocationStatusColor, allocationStatusLabel } from "../utils/status";

export default function InwardResultScreen() {
  const { result: resultParam } = useLocalSearchParams<{ result: string }>();
  const { token } = useAuth();
  const router = useRouter();
  const [activeLocation, setActiveLocation] = useState<string | null>(null);

  const result = useMemo<InwardResultDto | null>(() => {
    try {
      return resultParam ? (JSON.parse(resultParam) as InwardResultDto) : null;
    } catch {
      return null;
    }
  }, [resultParam]);

  if (!result || !token) {
    return (
      <View style={styles.center}>
        <Text>No result to show.</Text>
      </View>
    );
  }

  const handleDownload = async () => {
    try {
      const { bytes, fileName } = await downloadInwardDetail(token, result.id);
      await saveAndShareXlsx(bytes, fileName);
    } catch {
      Alert.alert("Download failed", "Could not download the Excel file.");
    }
  };

  return (
    <ScrollView style={styles.screen} contentContainerStyle={{ paddingBottom: 40 }}>
      <View style={styles.header}>
        <Text style={styles.reference}>{result.referenceNumber}</Text>
        <Text style={styles.subtitle}>{new Date(result.createdAt).toLocaleString()}</Text>
      </View>

      {result.lines.map((line, index) => (
        <View key={index} style={styles.lineCard}>
          <View style={styles.lineHeader}>
            <Text style={styles.materialName}>
              {line.materialNumber} — {line.materialDescription}
            </Text>
            <View style={[styles.statusPill, { backgroundColor: allocationStatusColor(line.status) }]}>
              <Text style={styles.statusPillText}>{allocationStatusLabel(line.status)}</Text>
            </View>
          </View>
          <Text style={styles.qtyLine}>
            {line.allocatedQuantityBoxes} / {line.requestedQuantityBoxes} boxes allocated
          </Text>
          {line.remarks && <Text style={styles.remarks}>{line.remarks}</Text>}
          {line.allocatedTo.map((allocation, i) => (
            <TouchableOpacity key={i} style={styles.allocationRow} onPress={() => setActiveLocation(allocation.locationCode)}>
              <Text style={styles.allocationLocation}>{allocation.locationCode}</Text>
              <Text style={styles.allocationQty}>{allocation.quantityBoxes} boxes</Text>
            </TouchableOpacity>
          ))}
        </View>
      ))}

      {result.unsavedLines && result.unsavedLines.length > 0 && (
        <View style={styles.unsavedBox}>
          <Text style={styles.unsavedTitle}>Lines not saved</Text>
          {result.unsavedLines.map((line, i) => (
            <Text key={i} style={styles.unsavedRow}>
              {line.codeText}: {line.reason}
            </Text>
          ))}
        </View>
      )}

      <TouchableOpacity style={styles.downloadButton} onPress={handleDownload}>
        <Text style={styles.downloadButtonText}>Download Excel</Text>
      </TouchableOpacity>

      <TouchableOpacity style={styles.doneButton} onPress={() => router.replace("/")}>
        <Text style={styles.doneButtonText}>Done</Text>
      </TouchableOpacity>

      <PalletPositionModal locationCode={activeLocation} token={token} onClose={() => setActiveLocation(null)} />
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: "#f1f5f9", padding: 16 },
  center: { flex: 1, alignItems: "center", justifyContent: "center" },
  header: { marginBottom: 16 },
  reference: { fontSize: 20, fontWeight: "700", color: "#0f172a" },
  subtitle: { fontSize: 13, color: "#64748b", marginTop: 2 },
  lineCard: { backgroundColor: "#fff", borderRadius: 12, padding: 14, marginBottom: 12, borderWidth: 1, borderColor: "#e2e8f0" },
  lineHeader: { flexDirection: "row", justifyContent: "space-between", alignItems: "flex-start", marginBottom: 6 },
  materialName: { fontSize: 14, fontWeight: "700", color: "#0f172a", flex: 1, marginRight: 8 },
  statusPill: { paddingHorizontal: 10, paddingVertical: 4, borderRadius: 999 },
  statusPillText: { color: "#fff", fontSize: 11, fontWeight: "700" },
  qtyLine: { fontSize: 13, color: "#334155", marginBottom: 4 },
  remarks: { fontSize: 12, color: "#dc2626", marginBottom: 6 },
  allocationRow: { flexDirection: "row", justifyContent: "space-between", paddingVertical: 8, borderTopWidth: 1, borderTopColor: "#f1f5f9" },
  allocationLocation: { color: "#2563eb", fontWeight: "700", fontSize: 13 },
  allocationQty: { color: "#64748b", fontSize: 13 },
  unsavedBox: { backgroundColor: "#fef2f2", borderRadius: 10, padding: 12, marginBottom: 12 },
  unsavedTitle: { fontWeight: "700", color: "#991b1b", marginBottom: 6 },
  unsavedRow: { fontSize: 12, color: "#991b1b" },
  downloadButton: { borderWidth: 1, borderColor: "#2563eb", borderRadius: 10, paddingVertical: 14, alignItems: "center", marginTop: 8 },
  downloadButtonText: { color: "#2563eb", fontWeight: "700" },
  doneButton: { backgroundColor: "#2563eb", borderRadius: 10, paddingVertical: 14, alignItems: "center", marginTop: 12 },
  doneButtonText: { color: "#fff", fontWeight: "700" },
});
