import { useEffect, useState } from "react";
import { ActivityIndicator, Modal, ScrollView, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { getPalletPosition } from "../api/endpoints";
import type { PalletPositionDto } from "../types/api";

interface Props {
  locationCode: string | null;
  token: string;
  onClose: () => void;
}

export function PalletPositionModal({ locationCode, token, onClose }: Props) {
  const [data, setData] = useState<PalletPositionDto | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  useEffect(() => {
    if (!locationCode) {
      setData(null);
      return;
    }
    setIsLoading(true);
    getPalletPosition(token, locationCode)
      .then(setData)
      .finally(() => setIsLoading(false));
  }, [locationCode, token]);

  return (
    <Modal visible={locationCode !== null} animationType="slide" transparent onRequestClose={onClose}>
      <TouchableOpacity style={styles.backdrop} activeOpacity={1} onPress={onClose}>
        <View style={styles.sheet}>
          <Text style={styles.title}>{locationCode}</Text>
          {isLoading && <ActivityIndicator color="#2563eb" style={{ marginVertical: 20 }} />}
          {data && (
            <ScrollView>
              <Row label="Zone" value={data.zoneTypeName} />
              <Row label="Location type" value={`${data.locationTypeName} / ${data.locationSubtypeName}`} />
              <Row label="Occupied boxes" value={String(data.occupiedBoxes)} />
              <Row label="Capacity boxes" value={data.capacityBoxes != null ? String(data.capacityBoxes) : "—"} />
              <Row label="Status" value={data.isFull ? "Full" : "Available"} />
              {data.stock.length > 0 && (
                <>
                  <Text style={styles.stockTitle}>Stock</Text>
                  {data.stock.map((stock, idx) => (
                    <View key={idx} style={styles.stockRow}>
                      <Text style={styles.stockMaterial}>{stock.materialNumber} — {stock.materialDescription}</Text>
                      <Text style={styles.stockQty}>{stock.quantityBoxes} boxes ({stock.mfgMonthLabel})</Text>
                    </View>
                  ))}
                </>
              )}
            </ScrollView>
          )}
          <TouchableOpacity style={styles.closeButton} onPress={onClose}>
            <Text style={styles.closeButtonText}>Close</Text>
          </TouchableOpacity>
        </View>
      </TouchableOpacity>
    </Modal>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.row}>
      <Text style={styles.rowLabel}>{label}</Text>
      <Text style={styles.rowValue}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  backdrop: { flex: 1, backgroundColor: "rgba(15,23,42,0.4)", justifyContent: "flex-end" },
  sheet: { backgroundColor: "#fff", borderTopLeftRadius: 16, borderTopRightRadius: 16, padding: 20, maxHeight: "70%" },
  title: { fontSize: 18, fontWeight: "700", color: "#0f172a", marginBottom: 12 },
  row: { flexDirection: "row", justifyContent: "space-between", paddingVertical: 6, borderBottomWidth: 1, borderBottomColor: "#f1f5f9" },
  rowLabel: { color: "#64748b", fontSize: 13 },
  rowValue: { color: "#0f172a", fontSize: 13, fontWeight: "600" },
  stockTitle: { fontSize: 13, fontWeight: "700", color: "#334155", marginTop: 14, marginBottom: 6 },
  stockRow: { paddingVertical: 6, borderBottomWidth: 1, borderBottomColor: "#f1f5f9" },
  stockMaterial: { fontSize: 13, color: "#0f172a" },
  stockQty: { fontSize: 12, color: "#64748b", marginTop: 2 },
  closeButton: { marginTop: 16, alignItems: "center", paddingVertical: 10 },
  closeButtonText: { color: "#2563eb", fontWeight: "700" },
});
