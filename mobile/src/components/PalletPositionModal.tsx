import { useEffect, useState } from "react";
import { ScrollView, StyleSheet, Text, View } from "react-native";
import { getPalletPosition } from "../api/endpoints";
import { BottomSheet } from "./ui/BottomSheet";
import { LoadingView } from "./ui/LoadingView";
import { StatusBadge } from "./ui/StatusBadge";
import { colors, spacing, typography } from "../theme";
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
      // eslint-disable-next-line react-hooks/set-state-in-effect -- intentional reset each time the modal opens/closes
      setData(null);
      return;
    }
    setIsLoading(true);
    getPalletPosition(token, locationCode)
      .then(setData)
      .finally(() => setIsLoading(false));
  }, [locationCode, token]);

  return (
    <BottomSheet visible={locationCode !== null} title={locationCode ?? ""} onClose={onClose}>
      {isLoading && <LoadingView label="Loading location..." />}
      {data && (
        <ScrollView>
          <Row label="Zone" value={data.zoneTypeName} />
          <Row label="Location type" value={`${data.locationTypeName} / ${data.locationSubtypeName}`} />
          <Row label="Occupied boxes" value={String(data.occupiedBoxes)} />
          <Row label="Capacity boxes" value={data.capacityBoxes != null ? String(data.capacityBoxes) : "—"} />
          <View style={styles.row}>
            <Text style={styles.rowLabel}>Status</Text>
            <StatusBadge label={data.isFull ? "Full" : "Available"} tone={data.isFull ? "danger" : "success"} />
          </View>
          {data.stock.length > 0 && (
            <>
              <Text style={styles.stockTitle}>Stock</Text>
              {data.stock.map((stock, idx) => (
                <View key={idx} style={styles.stockRow}>
                  <Text style={styles.stockMaterial}>
                    {stock.materialNumber} — {stock.materialDescription}
                  </Text>
                  <Text style={styles.stockQty}>
                    {stock.quantityBoxes} boxes ({stock.mfgMonthLabel})
                  </Text>
                </View>
              ))}
            </>
          )}
        </ScrollView>
      )}
    </BottomSheet>
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
  row: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", paddingVertical: spacing.sm, borderBottomWidth: 1, borderBottomColor: colors.border },
  rowLabel: { color: colors.textSecondary, ...typography.caption },
  rowValue: { color: colors.textPrimary, ...typography.bodyStrong, fontSize: 13 },
  stockTitle: { ...typography.subtitle, fontSize: 13, color: colors.textSecondary, marginTop: spacing.lg, marginBottom: spacing.sm },
  stockRow: { paddingVertical: spacing.sm, borderBottomWidth: 1, borderBottomColor: colors.border },
  stockMaterial: { fontSize: 13, color: colors.textPrimary },
  stockQty: { fontSize: 12, color: colors.textSecondary, marginTop: 2 },
});
