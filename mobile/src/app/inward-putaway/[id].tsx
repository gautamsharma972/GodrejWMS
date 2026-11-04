import { useLocalSearchParams } from "expo-router";
import { useCallback, useEffect, useState } from "react";
import { ActivityIndicator, Alert, ScrollView, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { ApiError } from "../../api/client";
import { changeInwardPutawayLocation, confirmInwardPutaway, getInwardDetail } from "../../api/endpoints";
import { useAuth } from "../../auth/AuthContext";
import { InwardLocationChangeModal } from "../../components/InwardLocationChangeModal";
import type { InwardDetailDto } from "../../types/api";

export default function InwardPutawayScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { token } = useAuth();

  const [detail, setDetail] = useState<InwardDetailDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [activePutawayId, setActivePutawayId] = useState<number | null>(null);
  const [isConfirming, setIsConfirming] = useState(false);

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

  const rows = detail.lines.flatMap((line) =>
    line.putaways.map((putaway) => ({ putaway, materialNumber: line.materialNumber, materialDescription: line.materialDescription })),
  );

  const handleLocationChange = async (newPalletPositionId: number, reason: string) => {
    if (!activePutawayId) return;
    await changeInwardPutawayLocation(token, activePutawayId, { newPalletPositionId, reason });
    setActivePutawayId(null);
    await load();
  };

  const handleConfirm = async () => {
    setIsConfirming(true);
    try {
      await confirmInwardPutaway(token, detail.referenceNumber);
      await load();
    } catch (err) {
      Alert.alert("Could not confirm putaway", err instanceof ApiError ? err.message : "Unexpected error.");
    } finally {
      setIsConfirming(false);
    }
  };

  return (
    <View style={styles.screen}>
      <ScrollView contentContainerStyle={{ padding: 16, paddingBottom: 40 }}>
        <Text style={styles.reference}>{detail.referenceNumber}</Text>
        <Text style={styles.subtitle}>{new Date(detail.createdAt).toLocaleString()}</Text>
        {detail.isConfirmed && (
          <View style={styles.confirmedBanner}>
            <Text style={styles.confirmedBannerText}>This GRN is fully confirmed.</Text>
          </View>
        )}

        {rows.map((row) => (
          <View key={row.putaway.id} style={styles.row}>
            <Text style={styles.rowMaterial}>
              {row.materialNumber} — {row.materialDescription}
            </Text>
            <Text style={styles.rowLocation}>
              {row.putaway.locationCode} · {row.putaway.quantityBoxes} boxes
            </Text>
            {row.putaway.isConfirmed ? (
              <View style={styles.confirmedPill}>
                <Text style={styles.confirmedPillText}>Confirmed</Text>
              </View>
            ) : (
              <TouchableOpacity style={styles.changeButton} onPress={() => setActivePutawayId(row.putaway.id)}>
                <Text style={styles.changeButtonText}>Change location</Text>
              </TouchableOpacity>
            )}
          </View>
        ))}

        {!detail.isConfirmed && !detail.isRejected && (
          <TouchableOpacity style={styles.confirmButton} onPress={handleConfirm} disabled={isConfirming}>
            {isConfirming ? <ActivityIndicator color="#fff" /> : <Text style={styles.confirmButtonText}>Confirm Putaway</Text>}
          </TouchableOpacity>
        )}
      </ScrollView>

      <InwardLocationChangeModal
        putawayId={activePutawayId}
        token={token}
        onClose={() => setActivePutawayId(null)}
        onSubmit={handleLocationChange}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: "#f1f5f9" },
  center: { flex: 1, alignItems: "center", justifyContent: "center" },
  reference: { fontSize: 20, fontWeight: "700", color: "#0f172a" },
  subtitle: { fontSize: 13, color: "#64748b", marginTop: 2, marginBottom: 12 },
  confirmedBanner: { backgroundColor: "#dcfce7", borderRadius: 10, padding: 12, marginBottom: 16 },
  confirmedBannerText: { color: "#166534", fontWeight: "600", fontSize: 13 },
  row: { backgroundColor: "#fff", borderRadius: 12, padding: 14, marginBottom: 10, borderWidth: 1, borderColor: "#e2e8f0" },
  rowMaterial: { fontSize: 14, fontWeight: "700", color: "#0f172a" },
  rowLocation: { fontSize: 13, color: "#64748b", marginTop: 4, marginBottom: 8 },
  confirmedPill: { alignSelf: "flex-start", backgroundColor: "#dcfce7", paddingHorizontal: 10, paddingVertical: 4, borderRadius: 999 },
  confirmedPillText: { color: "#16a34a", fontSize: 11, fontWeight: "700" },
  changeButton: { alignSelf: "flex-start", borderWidth: 1, borderColor: "#2563eb", paddingHorizontal: 14, paddingVertical: 8, borderRadius: 8 },
  changeButtonText: { color: "#2563eb", fontSize: 12, fontWeight: "700" },
  confirmButton: { backgroundColor: "#2563eb", borderRadius: 10, paddingVertical: 14, alignItems: "center", marginTop: 8 },
  confirmButtonText: { color: "#fff", fontWeight: "700" },
});
