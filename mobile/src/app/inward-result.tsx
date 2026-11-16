import { useLocalSearchParams, useRouter } from "expo-router";
import { useMemo, useState } from "react";
import { Alert, ScrollView, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { downloadInwardDetail } from "../api/endpoints";
import { useAuth } from "../auth/AuthContext";
import { PalletPositionModal } from "../components/PalletPositionModal";
import { Button } from "../components/ui/Button";
import { AppIcon } from "../components/ui/AppIcon";
import { Card } from "../components/ui/Card";
import { CenteredView, Screen } from "../components/ui/Screen";
import { StatusBadge } from "../components/ui/StatusBadge";
import { colors, radius, spacing, typography } from "../theme";
import type { InwardResultDto } from "../types/api";
import { saveAndShareXlsx } from "../utils/saveAndShare";
import { allocationStatusLabel, allocationStatusTone, formatDateTime } from "../utils/status";

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
      <Screen>
        <CenteredView>
          <Text style={{ color: colors.textMuted }}>No result to show.</Text>
        </CenteredView>
      </Screen>
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
    <Screen>
      <ScrollView contentContainerStyle={{ padding: spacing.lg, paddingBottom: 40 }}>
        <View style={styles.headerCard}>
          <View style={styles.headerIcon}><AppIcon name="checkmark-circle-outline" size={22} color={colors.success} /></View>
          <View style={{ flex: 1 }}>
            <Text style={styles.eyebrow}>ALLOCATION GENERATED</Text>
            <Text style={styles.reference}>{result.referenceNumber}</Text>
            <Text style={styles.subtitle}>{formatDateTime(result.createdAt)}</Text>
          </View>
        </View>

        {result.lines.map((line, index) => (
          <Card key={index} style={styles.lineCard}>
            <View style={styles.lineHeader}>
              <Text style={styles.materialName}>
                {line.materialNumber} — {line.materialDescription}
              </Text>
              <StatusBadge label={allocationStatusLabel(line.status)} tone={allocationStatusTone(line.status)} />
            </View>
            <View style={styles.qtyPill}><Text style={styles.qtyLine}>{line.allocatedQuantityBoxes} / {line.requestedQuantityBoxes} boxes allocated</Text></View>
            {line.remarks && <Text style={styles.remarks}>{line.remarks}</Text>}
            {line.allocatedTo.map((allocation, i) => (
              <TouchableOpacity key={i} style={styles.allocationRow} onPress={() => setActiveLocation(allocation.locationCode)}>
                <View style={{ flex: 1 }}>
                  <Text style={styles.allocationLocation}>{allocation.locationCode}</Text>
                  {allocation.reason && <Text style={styles.allocationReason}>{allocation.reason}</Text>}
                </View>
                <Text style={styles.allocationQty}>{allocation.quantityBoxes} boxes</Text>
              </TouchableOpacity>
            ))}
          </Card>
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

        <View style={styles.actionRow}>
          <Button title="Excel" variant="outline" onPress={handleDownload} fullWidth={false} style={{ flex: 1 }} />
          <Button title="Done" onPress={() => router.replace("/")} fullWidth={false} style={{ flex: 1 }} />
        </View>
      </ScrollView>

      <PalletPositionModal locationCode={activeLocation} token={token} onClose={() => setActiveLocation(null)} />
    </Screen>
  );
}

const styles = StyleSheet.create({
  headerCard: { flexDirection: "row", alignItems: "center", backgroundColor: colors.successSoft, borderWidth: 1, borderColor: "#bbf7d0", borderRadius: radius.lg, padding: spacing.md, marginBottom: spacing.lg },
  headerIcon: { width: 42, height: 42, borderRadius: 14, backgroundColor: colors.surface, alignItems: "center", justifyContent: "center", marginRight: spacing.md },
  eyebrow: { ...typography.label, fontSize: 9, letterSpacing: 0.7, color: colors.successText },
  reference: { ...typography.title, color: colors.textPrimary, marginTop: 2 },
  subtitle: { ...typography.body, color: colors.textSecondary, marginTop: 2 },
  lineCard: { marginBottom: spacing.sm, padding: spacing.md },
  lineHeader: { flexDirection: "row", justifyContent: "space-between", alignItems: "flex-start", marginBottom: spacing.xs },
  materialName: { ...typography.subtitle, fontSize: 14, color: colors.textPrimary, flex: 1, marginRight: spacing.sm },
  qtyPill: { alignSelf: "flex-start", backgroundColor: colors.neutralSoft, borderRadius: radius.pill, paddingHorizontal: spacing.sm, paddingVertical: 5, marginBottom: spacing.xs },
  qtyLine: { fontSize: 12, color: colors.textSecondary, fontWeight: "700" },
  remarks: { fontSize: 12, color: colors.danger, marginBottom: spacing.sm, fontWeight: "600" },
  allocationRow: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", paddingVertical: spacing.sm, borderTopWidth: 1, borderTopColor: colors.border },
  allocationLocation: { color: colors.primary, fontWeight: "700", fontSize: 13 },
  allocationReason: { color: colors.textMuted, fontSize: 11, marginTop: 1 },
  allocationQty: { color: colors.textSecondary, fontSize: 13 },
  unsavedBox: { backgroundColor: colors.dangerSoft, borderRadius: radius.md, padding: spacing.md, marginBottom: spacing.md },
  unsavedTitle: { fontWeight: "700", color: colors.dangerText, marginBottom: spacing.sm },
  unsavedRow: { fontSize: 12, color: colors.dangerText },
  actionRow: { flexDirection: "row", gap: spacing.sm, marginTop: spacing.sm },
});
