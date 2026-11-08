import { useLocalSearchParams, useRouter } from "expo-router";
import { useCallback, useEffect, useState } from "react";
import { Alert, ScrollView, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { ApiError } from "../../api/client";
import { changePulloutPickLocation, confirmPullout, confirmPulloutPicklist, getPulloutDetail } from "../../api/endpoints";
import { useAuth } from "../../auth/AuthContext";
import {
  PulloutPickLocationChangeModal,
  type ActivePick,
  type PickChangeMode,
  type PickSplit,
} from "../../components/PulloutPickLocationChangeModal";
import { PulloutPickConfirmModal, type ConfirmablePick } from "../../components/PulloutPickConfirmModal";
import { AppIcon } from "../../components/ui/AppIcon";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { LoadingView } from "../../components/ui/LoadingView";
import { Screen } from "../../components/ui/Screen";
import { colors, radius, spacing, typography } from "../../theme";
import type { ConfirmPickResult, PulloutDetailDto } from "../../types/api";
import { errorPulse, successPulse } from "../../utils/haptics";
import { formatDateTime } from "../../utils/status";

export default function PulloutDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { token } = useAuth();
  const router = useRouter();

  const [detail, setDetail] = useState<PulloutDetailDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [activePick, setActivePick] = useState<ActivePick | null>(null);
  const [pickMode, setPickMode] = useState<PickChangeMode>("move");
  const [isConfirming, setIsConfirming] = useState(false);
  const [confirmPick, setConfirmPick] = useState<ConfirmablePick | null>(null);
  const [isConfirmingPicklist, setIsConfirmingPicklist] = useState(false);
  const [picklistError, setPicklistError] = useState<string | null>(null);

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
    // Initial route hydration is intentionally driven by the selected pullout id.
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

  const handleLocationChange = async (splits: PickSplit[], reason: string) => {
    if (!activePick) return;
    await changePulloutPickLocation(token, {
      referenceNumber: activePick.referenceNumber,
      materialNumber: activePick.materialNumber,
      currentLocationCode: activePick.currentLocationCode,
      mfgMonthLabel: activePick.mfgMonthLabel,
      sources: splits.map((split) => ({
        palletPositionId: split.option.palletPositionId,
        mfgMonthLabel: split.option.mfgMonthLabel,
        quantityBoxes: split.quantityBoxes,
      })),
      reason,
    });
    setActivePick(null);
    await load();
  };

  const handleConfirm = async () => {
    setIsConfirming(true);
    try {
      await confirmPullout(token, detail.referenceNumber);
      successPulse();
      await load();
    } catch (err) {
      errorPulse();
      Alert.alert("Could not confirm pullout", err instanceof ApiError ? err.message : "Unexpected error.");
    } finally {
      setIsConfirming(false);
    }
  };

  // A posted pullout already took its stock out at upload: the picker's job is confirming each pick on the floor.
  const posted = detail.isConfirmed && !detail.isRejected;
  const picklistConfirmed = !!detail.pickingConfirmed;
  const allPicks = detail.lines.flatMap((line) => line.picks);
  const picksDone = allPicks.filter((pick) => pick.pickedAt).length;
  const totalRequested = detail.lines.reduce((total, line) => total + line.requestedQuantityBoxes, 0);
  const totalPicked = detail.lines.reduce((total, line) => total + line.pickedQuantityBoxes, 0);
  const progress = posted
    ? (allPicks.length > 0 ? (picksDone / allPicks.length) * 100 : 0)
    : (totalRequested > 0 ? Math.min(100, (totalPicked / totalRequested) * 100) : 0);

  const handleConfirmPicklist = async () => {
    setIsConfirmingPicklist(true);
    setPicklistError(null);
    try {
      await confirmPulloutPicklist(token, detail.id);
      successPulse();
      router.back();
    } catch (err) {
      errorPulse();
      setPicklistError(err instanceof ApiError ? err.message : "Could not confirm this pick-list.");
    } finally {
      setIsConfirmingPicklist(false);
    }
  };

  const handlePickConfirmed = async (result: ConfirmPickResult) => {
    setConfirmPick(null);
    const parts = [`Picked ${result.pickedBoxes} box(es).`];
    if (result.returnedToStockBoxes > 0) parts.push(`${result.returnedToStockBoxes} not found - they go back to stock when you confirm the pick-list.`);
    if (result.movedBoxes > 0) parts.push(`${result.movedBoxes} remaining box(es) will move to ${result.destinationLocationCode} when you confirm the pick-list.`);
    else if (result.remainingAtSourceBoxes > 0) parts.push(`${result.remainingAtSourceBoxes} box(es) stay at the pick location.`);
    Alert.alert("Pick confirmed", parts.join("\n"));
    await load();
  };

  return (
    <Screen>
      <ScrollView contentContainerStyle={styles.content} showsVerticalScrollIndicator={false}>
        <View style={styles.summaryCard}>
          <View style={styles.summaryTopRow}>
            <View style={styles.summaryIcon}>
              <AppIcon name="arrow-up-circle-outline" size={23} color={colors.primary} />
            </View>
            <View style={styles.summaryCopy}>
              <Text style={styles.eyebrow}>{posted ? "PULLOUT PICKING" : "PULLOUT CONFIRMATION"}</Text>
              <Text style={styles.reference} numberOfLines={1}>{detail.referenceNumber}</Text>
              <Text style={styles.subtitle}>{formatDateTime(detail.createdAt)}</Text>
            </View>
          </View>
          <View style={styles.progressHeader}>
            <Text style={styles.progressLabel}>Picking progress</Text>
            <Text style={styles.progressValue}>{posted ? `${picksDone} / ${allPicks.length} picks` : `${totalPicked} / ${totalRequested} boxes`}</Text>
          </View>
          <View style={styles.progressTrack}><View style={[styles.progressFill, { width: `${progress}%` }]} /></View>
        </View>
        {posted && (
          <View style={styles.confirmedBanner}>
            <Text style={styles.confirmedBannerText}>
              {picklistConfirmed
                ? "This pick-list is confirmed."
                : "Stock for this pullout was deducted when it was uploaded. Confirm each pick as you take it, then confirm the pick-list."}
            </Text>
          </View>
        )}
        {detail.isRejected && (
          <View style={styles.rejectedBanner}>
            <Text style={styles.rejectedBannerText}>This pullout was rejected: {detail.rejectionReason}</Text>
          </View>
        )}

        <View style={styles.sectionHeading}>
          <Text style={styles.sectionTitle}>Material lines</Text>
          <Text style={styles.lineCount}>{detail.lines.length}</Text>
        </View>

        {detail.lines.map((line, lineIndex) => {
          const lineProgress = line.requestedQuantityBoxes > 0 ? Math.min(100, (line.pickedQuantityBoxes / line.requestedQuantityBoxes) * 100) : 0;
          return (
          <Card key={lineIndex} style={styles.lineCard}>
            <View style={styles.materialHeader}>
              <View style={styles.materialIcon}><AppIcon name="cube-outline" size={18} color={colors.primary} /></View>
              <View style={styles.materialCopy}>
                <Text style={styles.materialCode}>{line.materialNumber}</Text>
                <Text style={styles.lineMaterial} numberOfLines={2}>{line.materialDescription}</Text>
              </View>
            </View>
            <View style={styles.lineProgressHeader}>
              <Text style={styles.lineMeta}>{line.pickedQuantityBoxes} of {line.requestedQuantityBoxes} boxes picked</Text>
              <Text style={styles.linePercent}>{Math.round(lineProgress)}%</Text>
            </View>
            <View style={styles.lineProgressTrack}><View style={[styles.lineProgressFill, { width: `${lineProgress}%` }]} /></View>
            {line.picks.map((pick, pickIndex) => (
              <View key={pickIndex} style={styles.pickRow}>
                <View style={styles.pickRowTop}>
                  <View style={styles.pickLocationPill}>
                    <AppIcon name="location-outline" size={15} color={colors.primary} />
                    <Text style={styles.pickLocation}>{pick.locationCode}</Text>
                  </View>
                  <Text style={styles.pickDetail}>{pick.mfgMonthLabel} · {pick.quantityBoxes} boxes{pick.isVirtual ? " · virtual" : ""}</Text>
                </View>
                {posted && !picklistConfirmed && pick.pickId && pick.pickedAt && (
                  <View style={styles.pickedRow}>
                    <AppIcon name="checkmark-circle" size={18} color={colors.success} />
                    <Text style={styles.pickedText}>
                      Picked {pick.pickedQuantityBoxes ?? pick.quantityBoxes}
                      {pick.moveToLocationCode ? ` · move ${pick.moveQuantityBoxes} to ${pick.moveToLocationCode}` : ""}
                    </Text>
                  </View>
                )}
                {posted && picklistConfirmed && pick.pickedAt && (
                  <View style={styles.pickedRow}>
                    <AppIcon name="checkmark-circle" size={18} color={colors.success} />
                    <Text style={styles.pickedText}>Picked {pick.pickedQuantityBoxes ?? pick.quantityBoxes}</Text>
                  </View>
                )}
                {posted && !picklistConfirmed && pick.pickId && (
                  <View style={styles.pickActions}>
                    <TouchableOpacity
                      style={[styles.pickActionButton, styles.pickConfirmButton]}
                      onPress={() => setConfirmPick({
                        pickId: pick.pickId!,
                        referenceNumber: detail.referenceNumber,
                        materialNumber: line.materialNumber,
                        materialDescription: line.materialDescription,
                        locationCode: pick.locationCode,
                        mfgMonthLabel: pick.mfgMonthLabel,
                        quantityBoxes: pick.quantityBoxes,
                        isVirtual: pick.isVirtual,
                        pickedQuantityBoxes: pick.pickedQuantityBoxes,
                        moveToPalletPositionId: pick.moveToPalletPositionId,
                        moveToLocationCode: pick.moveToLocationCode,
                        moveQuantityBoxes: pick.moveQuantityBoxes,
                      })}
                    >
                      <AppIcon name="checkmark-circle-outline" size={20} color={colors.textOnPrimary} />
                      <Text style={styles.pickConfirmText}>{pick.pickedAt ? "Edit pick" : "Confirm pick"}</Text>
                    </TouchableOpacity>
                    <TouchableOpacity
                      style={styles.pickActionButton}
                      onPress={() => {
                        setPickMode("change");
                        setActivePick({
                          referenceNumber: detail.referenceNumber,
                          materialNumber: line.materialNumber,
                          currentLocationCode: pick.locationCode,
                          mfgMonthLabel: pick.mfgMonthLabel,
                          quantityBoxes: pick.quantityBoxes,
                        });
                      }}
                    >
                      <AppIcon name="swap-horizontal" size={18} color={colors.primary} />
                      <Text style={styles.pickActionText}>Change location</Text>
                    </TouchableOpacity>
                  </View>
                )}
                {!detail.isConfirmed && !detail.isRejected && (
                  <View style={styles.pickActions}>
                    <TouchableOpacity
                      style={styles.pickActionButton}
                      onPress={() => {
                        setPickMode("move");
                        setActivePick({
                          referenceNumber: detail.referenceNumber,
                          materialNumber: line.materialNumber,
                          currentLocationCode: pick.locationCode,
                          mfgMonthLabel: pick.mfgMonthLabel,
                          quantityBoxes: pick.quantityBoxes,
                        });
                      }}
                    >
                      <AppIcon name="arrow-redo-outline" size={16} color={colors.primary} />
                      <Text style={styles.pickActionText}>Move</Text>
                    </TouchableOpacity>
                    <TouchableOpacity
                      style={styles.pickActionButton}
                      onPress={() => {
                        setPickMode("change");
                        setActivePick({
                          referenceNumber: detail.referenceNumber,
                          materialNumber: line.materialNumber,
                          currentLocationCode: pick.locationCode,
                          mfgMonthLabel: pick.mfgMonthLabel,
                          quantityBoxes: pick.quantityBoxes,
                        });
                      }}
                    >
                      <AppIcon name="swap-horizontal" size={16} color={colors.primary} />
                      <Text style={styles.pickActionText}>Change</Text>
                    </TouchableOpacity>
                  </View>
                )}
              </View>
            ))}
          </Card>
          );
        })}
      </ScrollView>

      {!detail.isConfirmed && !detail.isRejected && (
        <View style={styles.actionBar}>
          <View style={styles.actionCopy}>
            <Text style={styles.actionLabel}>{totalPicked} / {totalRequested} boxes</Text>
            <Text style={styles.actionHint}>Picked for this pullout</Text>
          </View>
          <Button
            title="Confirm Pullout"
            onPress={handleConfirm}
            loading={isConfirming}
            icon={<AppIcon name="checkmark-circle-outline" size={20} color={colors.textOnPrimary} />}
            fullWidth={false}
            style={styles.confirmAction}
          />
        </View>
      )}

      {posted && !picklistConfirmed && (
        <View style={styles.picklistBar}>
          {picklistError && <Text style={styles.picklistError}>{picklistError}</Text>}
          <Button
            title="Confirm pick-list"
            size="lg"
            loading={isConfirmingPicklist}
            disabled={picksDone < allPicks.length}
            onPress={handleConfirmPicklist}
            icon={<AppIcon name="checkmark-done-outline" size={22} color={colors.textOnPrimary} />}
          />
          <Text style={styles.picklistHint}>
            {picksDone < allPicks.length ? "Confirm every pick first, then confirm the pick-list." : "Every pick is confirmed."}
          </Text>
        </View>
      )}

      <PulloutPickConfirmModal pick={confirmPick} token={token} onClose={() => setConfirmPick(null)} onDone={handlePickConfirmed} />

      <PulloutPickLocationChangeModal
        pick={activePick}
        mode={pickMode}
        token={token}
        onClose={() => setActivePick(null)}
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
  progressHeader: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginTop: spacing.lg, marginBottom: spacing.sm },
  progressLabel: { ...typography.caption, color: colors.textSecondary },
  progressValue: { ...typography.caption, color: colors.primaryText, fontWeight: "800" },
  progressTrack: { height: 7, backgroundColor: "#dbe2f3", borderRadius: radius.pill, overflow: "hidden" },
  progressFill: { height: "100%", backgroundColor: colors.primary, borderRadius: radius.pill },
  picklistBar: { backgroundColor: colors.surface, borderTopWidth: 1, borderTopColor: colors.border, padding: spacing.lg },
  picklistError: { backgroundColor: colors.dangerSoft, color: colors.dangerText, borderRadius: radius.md, padding: spacing.md, fontWeight: "700", marginBottom: spacing.md },
  picklistHint: { ...typography.caption, color: colors.textSecondary, textAlign: "center", marginTop: spacing.sm },
  confirmedBanner: { backgroundColor: colors.successSoft, borderRadius: radius.md, padding: spacing.md, marginBottom: spacing.lg },
  confirmedBannerText: { color: colors.successText, fontWeight: "700", fontSize: 13 },
  rejectedBanner: { backgroundColor: colors.dangerSoft, borderRadius: radius.md, padding: spacing.md, marginBottom: spacing.lg },
  rejectedBannerText: { color: colors.dangerText, fontWeight: "700", fontSize: 13 },
  sectionHeading: { flexDirection: "row", alignItems: "center", marginBottom: spacing.sm, paddingHorizontal: 2 },
  sectionTitle: { ...typography.title, fontSize: 17, color: colors.textPrimary },
  lineCount: { ...typography.label, color: colors.primaryText, backgroundColor: colors.primarySoft, borderRadius: radius.pill, paddingHorizontal: spacing.sm, paddingVertical: 4, marginLeft: spacing.sm },
  lineCard: { marginBottom: spacing.md, padding: spacing.md, shadowOpacity: 0.035 },
  materialHeader: { flexDirection: "row", alignItems: "flex-start" },
  materialIcon: { width: 34, height: 34, borderRadius: radius.sm, backgroundColor: colors.primarySoft, alignItems: "center", justifyContent: "center", marginRight: spacing.sm },
  materialCopy: { flex: 1, minWidth: 0 },
  materialCode: { ...typography.bodyStrong, color: colors.textPrimary },
  lineMaterial: { ...typography.body, color: colors.textSecondary, lineHeight: 19, marginTop: 1 },
  lineProgressHeader: { flexDirection: "row", justifyContent: "space-between", marginTop: spacing.md, marginBottom: 6 },
  lineMeta: { ...typography.caption, color: colors.textSecondary },
  linePercent: { ...typography.caption, color: colors.primaryText, fontWeight: "800" },
  lineProgressTrack: { height: 5, backgroundColor: colors.surfaceAlt, borderRadius: radius.pill, overflow: "hidden", marginBottom: spacing.sm },
  lineProgressFill: { height: "100%", backgroundColor: colors.primary, borderRadius: radius.pill },
  pickRow: { paddingTop: spacing.sm, marginTop: spacing.xs, borderTopWidth: 1, borderTopColor: colors.border },
  pickRowTop: { flexDirection: "row", alignItems: "center", gap: spacing.sm },
  pickLocationPill: { flexDirection: "row", alignItems: "center", backgroundColor: colors.primarySoft, borderRadius: radius.pill, paddingHorizontal: spacing.sm, paddingVertical: 6 },
  pickLocation: { ...typography.caption, color: colors.primaryText, fontWeight: "700", marginLeft: spacing.xs },
  pickDetail: { ...typography.caption, color: colors.textSecondary, flex: 1 },
  pickActions: { flexDirection: "row", gap: spacing.sm, marginTop: spacing.sm },
  pickActionButton: {
    flex: 1,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing.sm,
    paddingVertical: 8,
    minHeight: 48,
    borderRadius: radius.pill,
    backgroundColor: colors.primarySoft,
  },
  pickActionText: { color: colors.primary, fontSize: 12.5, fontWeight: "700", marginLeft: spacing.xs },
  pickConfirmButton: { backgroundColor: colors.primary, minHeight: 48 },
  pickConfirmText: { color: colors.textOnPrimary, fontSize: 14, fontWeight: "800", marginLeft: spacing.xs },
  pickedRow: { flexDirection: "row", alignItems: "center", marginTop: spacing.sm },
  pickedText: { color: colors.successText, fontWeight: "700", marginLeft: spacing.xs },
  actionBar: { flexDirection: "row", alignItems: "center", backgroundColor: colors.surface, borderTopWidth: 1, borderTopColor: colors.border, paddingHorizontal: spacing.lg, paddingTop: spacing.md, paddingBottom: spacing.md },
  actionCopy: { flex: 1 },
  actionLabel: { ...typography.bodyStrong, color: colors.textPrimary },
  actionHint: { ...typography.caption, color: colors.textSecondary, marginTop: 1 },
  confirmAction: { minWidth: 190 },
});
