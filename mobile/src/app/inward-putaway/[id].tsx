import { useLocalSearchParams } from "expo-router";
import { useCallback, useEffect, useState } from "react";
import { Alert, ScrollView, StyleSheet, Text, View } from "react-native";
import { ApiError } from "../../api/client";
import { changeInwardPutawayLocation, confirmInwardPutaway, getInwardDetail } from "../../api/endpoints";
import { useAuth } from "../../auth/AuthContext";
import { InwardLocationChangeModal } from "../../components/InwardLocationChangeModal";
import { AppIcon } from "../../components/ui/AppIcon";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { LoadingView } from "../../components/ui/LoadingView";
import { Screen } from "../../components/ui/Screen";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { colors, radius, spacing, typography } from "../../theme";
import type { InwardDetailDto } from "../../types/api";
import { errorPulse, successPulse } from "../../utils/haptics";
import { formatDateTime } from "../../utils/status";

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
    // Initial route hydration is intentionally driven by the selected GRN id.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    load();
  }, [load]);

  if (!token) return null;

  if (isLoading || !detail) {
    return (
      <Screen>
        <LoadingView />
      </Screen>
    );
  }

  const rows = detail.lines.flatMap((line) =>
    line.putaways.map((putaway) => ({ putaway, materialNumber: line.materialNumber, materialDescription: line.materialDescription })),
  );
  const confirmedRows = rows.filter((row) => row.putaway.isConfirmed).length;
  const totalBoxes = rows.reduce((total, row) => total + row.putaway.quantityBoxes, 0);

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
      successPulse();
      await load();
    } catch (err) {
      errorPulse();
      Alert.alert("Could not confirm putaway", err instanceof ApiError ? err.message : "Unexpected error.");
    } finally {
      setIsConfirming(false);
    }
  };

  return (
    <Screen>
      <ScrollView contentContainerStyle={styles.content} showsVerticalScrollIndicator={false}>
        <View style={styles.summaryCard}>
          <View style={styles.summaryTopRow}>
            <View style={styles.summaryIcon}>
              <AppIcon name="cube-outline" size={22} color={colors.primary} />
            </View>
            <View style={styles.summaryCopy}>
              <Text style={styles.eyebrow}>PUT-AWAY CONFIRMATION</Text>
              <Text style={styles.reference} numberOfLines={1}>{detail.referenceNumber}</Text>
              <Text style={styles.subtitle}>{formatDateTime(detail.createdAt)}</Text>
            </View>
          </View>
          <View style={styles.summaryStats}>
            <View style={styles.statItem}>
              <Text style={styles.statValue}>{rows.length}</Text>
              <Text style={styles.statLabel}>Locations</Text>
            </View>
            <View style={styles.statDivider} />
            <View style={styles.statItem}>
              <Text style={styles.statValue}>{totalBoxes}</Text>
              <Text style={styles.statLabel}>Boxes</Text>
            </View>
            <View style={styles.statDivider} />
            <View style={styles.statItem}>
              <Text style={styles.statValue}>{confirmedRows}/{rows.length}</Text>
              <Text style={styles.statLabel}>Confirmed</Text>
            </View>
          </View>
        </View>
        {detail.isConfirmed && (
          <View style={styles.confirmedBanner}>
            <Text style={styles.confirmedBannerText}>This GRN is fully confirmed.</Text>
          </View>
        )}

        <View style={styles.sectionHeading}>
          <Text style={styles.sectionTitle}>Put-away locations</Text>
          <Text style={styles.sectionHint}>Review before confirming</Text>
        </View>

        {rows.map((row, index) => (
          <Card key={row.putaway.id} style={styles.row}>
            <View style={styles.rowTop}>
              <View style={styles.rowNumber}><Text style={styles.rowNumberText}>{index + 1}</Text></View>
              <View style={styles.rowCopy}>
                <Text style={styles.rowMaterial} numberOfLines={2}>{row.materialDescription}</Text>
                <Text style={styles.materialCode}>{row.materialNumber}</Text>
              </View>
              {row.putaway.isConfirmed && <StatusBadge label="Confirmed" tone="success" variant="soft" />}
            </View>
            <View style={styles.allocationRow}>
              <View style={styles.locationPill}>
                <AppIcon name="location-outline" size={16} color={colors.primary} />
                <Text style={styles.locationText}>{row.putaway.locationCode}</Text>
              </View>
              <View style={styles.quantityPill}>
                <AppIcon name="cube-outline" size={15} color={colors.textSecondary} />
                <Text style={styles.quantityText}>{row.putaway.quantityBoxes} boxes</Text>
              </View>
              {!row.putaway.isConfirmed && (
                <Button
                  title="Change"
                  variant="ghost"
                  fullWidth={false}
                  icon={<AppIcon name="swap-horizontal" size={17} color={colors.primary} />}
                  onPress={() => setActivePutawayId(row.putaway.id)}
                  style={styles.changeAction}
                />
              )}
            </View>
          </Card>
        ))}
      </ScrollView>

      {!detail.isConfirmed && !detail.isRejected && (
        <View style={styles.actionBar}>
          <View style={styles.actionCopy}>
            <Text style={styles.actionLabel}>{totalBoxes} boxes</Text>
            <Text style={styles.actionHint}>Ready to put away</Text>
          </View>
          <Button
            title="Confirm Putaway"
            onPress={handleConfirm}
            loading={isConfirming}
            icon={<AppIcon name="checkmark-circle-outline" size={20} color={colors.textOnPrimary} />}
            fullWidth={false}
            style={styles.confirmAction}
          />
        </View>
      )}

      <InwardLocationChangeModal
        putawayId={activePutawayId}
        token={token}
        onClose={() => setActivePutawayId(null)}
        onSubmit={handleLocationChange}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { padding: spacing.lg, paddingBottom: 112 },
  summaryCard: { backgroundColor: colors.primarySoft, borderColor: "#c7d2fe", borderWidth: 1, borderRadius: radius.xl, padding: spacing.lg, marginBottom: spacing.lg },
  summaryTopRow: { flexDirection: "row", alignItems: "center" },
  summaryIcon: { width: 48, height: 48, borderRadius: radius.md, backgroundColor: colors.surface, alignItems: "center", justifyContent: "center", marginRight: spacing.md },
  summaryCopy: { flex: 1, minWidth: 0 },
  eyebrow: { ...typography.label, color: colors.primary, letterSpacing: 0.6, marginBottom: 2 },
  reference: { ...typography.title, color: colors.textPrimary },
  subtitle: { ...typography.caption, color: colors.textSecondary, marginTop: 3 },
  summaryStats: { flexDirection: "row", alignItems: "center", backgroundColor: "rgba(255,255,255,0.72)", borderRadius: radius.md, marginTop: spacing.lg, paddingVertical: spacing.md },
  statItem: { flex: 1, alignItems: "center" },
  statValue: { fontSize: 18, lineHeight: 22, fontWeight: "800", color: colors.textPrimary },
  statLabel: { ...typography.caption, color: colors.textSecondary, marginTop: 2 },
  statDivider: { width: 1, height: 30, backgroundColor: "#dbe2f3" },
  confirmedBanner: { backgroundColor: colors.successSoft, borderRadius: radius.md, padding: spacing.md, marginBottom: spacing.lg },
  confirmedBannerText: { color: colors.successText, fontWeight: "700", fontSize: 13 },
  sectionHeading: { flexDirection: "row", alignItems: "baseline", justifyContent: "space-between", marginBottom: spacing.sm, paddingHorizontal: 2 },
  sectionTitle: { ...typography.title, fontSize: 17, color: colors.textPrimary },
  sectionHint: { ...typography.caption, color: colors.textMuted },
  row: { marginBottom: spacing.md, padding: spacing.md, shadowOpacity: 0.035 },
  rowTop: { flexDirection: "row", alignItems: "flex-start" },
  rowNumber: { width: 30, height: 30, borderRadius: 15, backgroundColor: colors.neutralSoft, alignItems: "center", justifyContent: "center", marginRight: spacing.sm },
  rowNumberText: { ...typography.label, color: colors.textSecondary },
  rowCopy: { flex: 1, minWidth: 0, paddingRight: spacing.sm },
  rowMaterial: { ...typography.subtitle, color: colors.textPrimary, lineHeight: 20 },
  materialCode: { ...typography.caption, color: colors.textSecondary, marginTop: 2 },
  allocationRow: { flexDirection: "row", alignItems: "center", marginTop: spacing.md, paddingTop: spacing.md, borderTopWidth: 1, borderTopColor: colors.border, gap: spacing.sm },
  locationPill: { flexDirection: "row", alignItems: "center", backgroundColor: colors.primarySoft, borderRadius: radius.pill, paddingHorizontal: spacing.sm, paddingVertical: 7 },
  locationText: { ...typography.caption, color: colors.primaryText, fontWeight: "700", marginLeft: spacing.xs },
  quantityPill: { flexDirection: "row", alignItems: "center", backgroundColor: colors.neutralSoft, borderRadius: radius.pill, paddingHorizontal: spacing.sm, paddingVertical: 7 },
  quantityText: { ...typography.caption, color: colors.textSecondary, fontWeight: "700", marginLeft: spacing.xs },
  changeAction: { marginLeft: "auto" },
  actionBar: { flexDirection: "row", alignItems: "center", backgroundColor: colors.surface, borderTopWidth: 1, borderTopColor: colors.border, paddingHorizontal: spacing.lg, paddingTop: spacing.md, paddingBottom: spacing.md },
  actionCopy: { flex: 1 },
  actionLabel: { ...typography.bodyStrong, color: colors.textPrimary },
  actionHint: { ...typography.caption, color: colors.textSecondary, marginTop: 1 },
  confirmAction: { minWidth: 190 },
});
