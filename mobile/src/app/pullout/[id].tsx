import { useLocalSearchParams } from "expo-router";
import { useCallback, useEffect, useState } from "react";
import { ActivityIndicator, Alert, ScrollView, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { ApiError } from "../../api/client";
import { changePulloutPickLocation, confirmPullout, getPulloutDetail } from "../../api/endpoints";
import { useAuth } from "../../auth/AuthContext";
import { PulloutPickLocationChangeModal, type ActivePick } from "../../components/PulloutPickLocationChangeModal";
import type { PulloutDetailDto, PulloutPickChangeOptionDto } from "../../types/api";

export default function PulloutDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { token } = useAuth();

  const [detail, setDetail] = useState<PulloutDetailDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [activePick, setActivePick] = useState<ActivePick | null>(null);
  const [isConfirming, setIsConfirming] = useState(false);

  const load = useCallback(async () => {
    if (!token || !id) return;
    setIsLoading(true);
    try {
      setDetail(await getPulloutDetail(token, Number(id)));
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

  const handleLocationChange = async (option: PulloutPickChangeOptionDto, reason: string) => {
    if (!activePick) return;
    await changePulloutPickLocation(token, {
      referenceNumber: activePick.referenceNumber,
      materialNumber: activePick.materialNumber,
      currentLocationCode: activePick.currentLocationCode,
      mfgMonthLabel: activePick.mfgMonthLabel,
      sources: [{ palletPositionId: option.palletPositionId, mfgMonthLabel: option.mfgMonthLabel, quantityBoxes: activePick.quantityBoxes }],
      reason,
    });
    setActivePick(null);
    await load();
  };

  const handleConfirm = async () => {
    setIsConfirming(true);
    try {
      await confirmPullout(token, detail.referenceNumber);
      await load();
    } catch (err) {
      Alert.alert("Could not confirm pullout", err instanceof ApiError ? err.message : "Unexpected error.");
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
            <Text style={styles.confirmedBannerText}>This pullout is confirmed.</Text>
          </View>
        )}
        {detail.isRejected && (
          <View style={styles.rejectedBanner}>
            <Text style={styles.rejectedBannerText}>This pullout was rejected: {detail.rejectionReason}</Text>
          </View>
        )}

        {detail.lines.map((line, lineIndex) => (
          <View key={lineIndex} style={styles.lineCard}>
            <Text style={styles.lineMaterial}>
              {line.materialNumber} — {line.materialDescription}
            </Text>
            <Text style={styles.lineMeta}>
              {line.pickedQuantityBoxes} / {line.requestedQuantityBoxes} boxes picked
            </Text>
            {line.picks.map((pick, pickIndex) => (
              <View key={pickIndex} style={styles.pickRow}>
                <Text style={styles.pickLocation}>
                  {pick.locationCode} · {pick.mfgMonthLabel} · {pick.quantityBoxes} boxes
                </Text>
                {!detail.isConfirmed && !detail.isRejected && (
                  <TouchableOpacity
                    style={styles.changeButton}
                    onPress={() =>
                      setActivePick({
                        referenceNumber: detail.referenceNumber,
                        materialNumber: line.materialNumber,
                        currentLocationCode: pick.locationCode,
                        mfgMonthLabel: pick.mfgMonthLabel,
                        quantityBoxes: pick.quantityBoxes,
                      })
                    }
                  >
                    <Text style={styles.changeButtonText}>Change</Text>
                  </TouchableOpacity>
                )}
              </View>
            ))}
          </View>
        ))}

        {!detail.isConfirmed && !detail.isRejected && (
          <TouchableOpacity style={styles.confirmButton} onPress={handleConfirm} disabled={isConfirming}>
            {isConfirming ? <ActivityIndicator color="#fff" /> : <Text style={styles.confirmButtonText}>Confirm Pullout</Text>}
          </TouchableOpacity>
        )}
      </ScrollView>

      <PulloutPickLocationChangeModal pick={activePick} token={token} onClose={() => setActivePick(null)} onSubmit={handleLocationChange} />
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
  rejectedBanner: { backgroundColor: "#fef2f2", borderRadius: 10, padding: 12, marginBottom: 16 },
  rejectedBannerText: { color: "#991b1b", fontWeight: "600", fontSize: 13 },
  lineCard: { backgroundColor: "#fff", borderRadius: 12, padding: 14, marginBottom: 10, borderWidth: 1, borderColor: "#e2e8f0" },
  lineMaterial: { fontSize: 14, fontWeight: "700", color: "#0f172a" },
  lineMeta: { fontSize: 13, color: "#334155", marginTop: 4, marginBottom: 8 },
  pickRow: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", paddingVertical: 6, borderTopWidth: 1, borderTopColor: "#f1f5f9" },
  pickLocation: { fontSize: 13, color: "#0f172a", flex: 1 },
  changeButton: { borderWidth: 1, borderColor: "#2563eb", paddingHorizontal: 12, paddingVertical: 6, borderRadius: 8 },
  changeButtonText: { color: "#2563eb", fontSize: 12, fontWeight: "700" },
  confirmButton: { backgroundColor: "#2563eb", borderRadius: 10, paddingVertical: 14, alignItems: "center", marginTop: 8 },
  confirmButtonText: { color: "#fff", fontWeight: "700" },
});
