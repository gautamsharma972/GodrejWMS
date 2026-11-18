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
import { SupervisorAddPulloutMaterial } from "../../components/SupervisorAddPulloutMaterial";
import { PulloutPickConfirmModal, type ConfirmablePick } from "../../components/PulloutPickConfirmModal";
import { PulloutPickerChangeLocationModal, type PickerActivePick } from "../../components/PulloutPickerChangeLocationModal";
import { AppIcon } from "../../components/ui/AppIcon";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { LoadingView } from "../../components/ui/LoadingView";
import { Screen } from "../../components/ui/Screen";
import { colors, radius, spacing, typography } from "../../theme";
import type { ConfirmPickResult, PulloutDetailDto, PulloutPickChangeOptionDto } from "../../types/api";
import { errorPulse, successPulse } from "../../utils/haptics";
import { formatDateTime } from "../../utils/status";

export default function PulloutDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { token, permissions } = useAuth();
  const router = useRouter();

  const [detail, setDetail] = useState<PulloutDetailDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [activePick, setActivePick] = useState<ActivePick | null>(null);
  const [pickMode, setPickMode] = useState<PickChangeMode>("move");
  const [pickerChangePick, setPickerChangePick] = useState<PickerActivePick | null>(null);
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

  // Ditto of the web app's picker Change location: one location, one quantity (min(this pick's
  // quantity, what's available there)) - no splitting, same as ChangeLocationAsync in PulloutPicker/Index.razor.
  const handlePickerLocationChange = async (option: PulloutPickChangeOptionDto, quantityBoxes: number, reason: string) => {
    if (!pickerChangePick) return;
    await changePulloutPickLocation(token, {
      referenceNumber: pickerChangePick.referenceNumber,
      materialNumber: pickerChangePick.materialNumber,
      currentLocationCode: pickerChangePick.currentLocationCode,
      mfgMonthLabel: pickerChangePick.mfgMonthLabel,
      sources: [{ palletPositionId: option.palletPositionId, mfgMonthLabel: option.mfgMonthLabel, quantityBoxes }],
      reason,
    });
    setPickerChangePick(null);
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

  // The picker queue can contain both posted pullouts and legacy drafts that already have
  // allocated pick rows. Treat both as pick-lists; the API posts a legacy draft only when
  // its final pick-list confirmation succeeds.
  const posted = detail.isConfirmed && !detail.isRejected;
  const picklistConfirmed = !!detail.pickingConfirmed;
  const allPicks = detail.lines.flatMap((line) => line.picks);
  const isPicker = permissions.includes("Pullout.Pick");
  const pickerView = isPicker && !detail.isRejected && allPicks.some((pick) => !!pick.pickId);
  const pickerActionable = pickerView && !picklistConfirmed;
  const picksDone = allPicks.filter((pick) => pick.pickedAt).length;
  const totalRequested = detail.lines.reduce((total, line) => total + line.requestedQuantityBoxes, 0);
  const totalPicked = detail.lines.reduce((total, line) => total + line.pickedQuantityBoxes, 0);
  const progress = pickerView
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

  const handlePickConfirmed = async (_result: ConfirmPickResult) => {
    setConfirmPick(null);
    // No confirmation dialog - the pick row itself already shows the outcome
    // (e.g. "Picked 16 - move 1 to A-13-01") once the list reloads below.
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
              <Text style={styles.eyebrow}>{pickerView ? "PULLOUT PICKING" : "PULLOUT CONFIRMATION"}</Text>
              <Text style={styles.reference} numberOfLines={1}>{detail.referenceNumber}</Text>
              <Text style={styles.subtitle}>{formatDateTime(detail.createdAt)}</Text>
            </View>
          </View>
          <View style={styles.progressHeader}>
            <Text style={styles.progressLabel}>Picking progress</Text>
            <Text style={styles.progressValue}>{pickerView ? `${picksDone} / ${allPicks.length} picks` : `${totalPicked} / ${totalRequested} boxes`}</Text>
          </View>
          <View style={styles.progressTrack}><View style={[styles.progressFill, { width: `${progress}%` }]} /></View>
        </View>
        {pickerView && (
          <View style={styles.confirmedBanner}>
            <Text style={styles.confirmedBannerText}>
              {picklistConfirmed
                ? "This pick-list is confirmed."
                : posted
                  ? "Stock for this pullout was deducted when it was uploaded. Confirm each pick as you take it, then confirm the pick-list."
                  : "This legacy pullout has allocated picks. Confirm each pick as you take it; inventory will be posted when you confirm the pick-list."}
            </Text>
          </View>
        )}
        {detail.isRejected && (
          <View style={styles.rejectedBanner}>
            <Text style={styles.rejectedBannerText}>This pullout was rejected: {detail.rejectionReason}</Text>
          </View>
        )}

        {pickerView ? (
          <>
            <View style={styles.sectionHeading}>
              <Text style={styles.sectionTitle}>Picks</Text>
              <Text style={styles.lineCount}>{pickerActionable ? `${picksDone} of ${allPicks.length} confirmed` : `${allPicks.length}`}</Text>
            </View>
            {/* Ditto of web's PulloutPicker Rows(): a flat list of actual picks only - a material
                line with nothing allocated to it (pickId is 0/unset) never shows a row at all,
                it is not a "0 of N boxes" card like the Floor Supervisor's pre-confirm view below. */}
            {detail.lines
              .flatMap((line) => line.picks.filter((pick) => pick.pickId).map((pick) => ({ line, pick })))
              .sort((a, b) => Number(!!a.pick.pickedAt) - Number(!!b.pick.pickedAt) || a.pick.locationCode.localeCompare(b.pick.locationCode))
              .map(({ line, pick }, index) => {
                const openConfirm = () => setConfirmPick({
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
                });
                return (
                  <TouchableOpacity
                    key={index}
                    activeOpacity={pickerActionable ? 0.6 : 1}
                    disabled={!pickerActionable}
                    onPress={openConfirm}
                  >
                    <Card style={{ ...styles.pickCard, ...(pick.pickedAt ? styles.pickCardDone : null) }}>
                      <View style={styles.pickRowTop}>
                        <View style={styles.pickLocationBlock}>
                          <View style={[styles.pickLocationIcon, pick.pickedAt ? styles.pickLocationIconDone : null]}>
                            <AppIcon name={pick.pickedAt ? "checkmark" : "location"} size={18} color={pick.pickedAt ? colors.successText : colors.primary} />
                          </View>
                          <View style={styles.pickLocationCopy}>
                            <Text style={styles.pickLocationLabel}>PICK FROM</Text>
                            <View style={styles.pickLocationRow}>
                              <Text style={[styles.pickLocationLarge, pick.pickedAt ? styles.pickLocationLargeDone : null]}>{pick.locationCode}</Text>
                              {pick.isVirtual && <Text style={styles.virtualTag}>Virtual</Text>}
                            </View>
                          </View>
                        </View>
                        <View style={[styles.statusBadge, pick.pickedAt ? styles.statusBadgeDone : styles.statusBadgeTodo]}>
                          <Text style={[styles.statusBadgeText, pick.pickedAt ? styles.statusBadgeTextDone : styles.statusBadgeTextTodo]} numberOfLines={1}>
                            {pick.pickedAt ? "Picked" : "To pick"}
                          </Text>
                        </View>
                      </View>
                      <Text style={styles.pickMaterialDescription} numberOfLines={2}>{line.materialDescription}</Text>
                      {pick.pickedAt && (
                        <Text style={styles.pickedDetailText}>
                          Picked {pick.pickedQuantityBoxes ?? pick.quantityBoxes}
                          {pick.moveToLocationCode ? ` · move ${pick.moveQuantityBoxes} to ${pick.moveToLocationCode}` : ""}
                        </Text>
                      )}
                      <View style={styles.pickMetaRow}>
                        <View style={styles.pickMetaItem}>
                          <AppIcon name="cube-outline" size={14} color={colors.textSecondary} />
                          <Text style={styles.pickMetaText}>{line.materialNumber}</Text>
                        </View>
                        <View style={styles.pickMetaItem}>
                          <AppIcon name="calendar-outline" size={14} color={colors.textSecondary} />
                          <Text style={styles.pickMetaText}>{pick.mfgMonthLabel}</Text>
                        </View>
                        <View style={styles.pickQuantityChip}>
                          <Text style={styles.pickQuantityText}>{pick.quantityBoxes} {pick.quantityBoxes === 1 ? "box" : "boxes"}</Text>
                        </View>
                      </View>
                      {pickerActionable && (
                        <View style={styles.pickCardFooter}>
                          <Text style={styles.pickCardHint}>{pick.pickedAt ? "Tap to review or edit" : "Tap card to confirm"}</Text>
                          <TouchableOpacity
                            style={styles.changeLocationButton}
                            onPress={() => setPickerChangePick({
                              pickId: pick.pickId!,
                              referenceNumber: detail.referenceNumber,
                              materialNumber: line.materialNumber,
                              currentLocationCode: pick.locationCode,
                              mfgMonthLabel: pick.mfgMonthLabel,
                              quantityBoxes: pick.quantityBoxes,
                            })}
                            accessibilityRole="button"
                            accessibilityLabel={`Change pick location from ${pick.locationCode}`}
                          >
                            <AppIcon name="swap-horizontal" size={16} color={colors.primary} />
                            <Text style={styles.changeLocationText}>Change location</Text>
                          </TouchableOpacity>
                        </View>
                      )}
                    </Card>
                  </TouchableOpacity>
                );
              })}
          </>
        ) : (
          <>
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
                    {posted && picklistConfirmed && pick.pickedAt && (
                      <View style={styles.pickedRow}>
                        <AppIcon name="checkmark-circle" size={18} color={colors.success} />
                        <Text style={styles.pickedText}>Picked {pick.pickedQuantityBoxes ?? pick.quantityBoxes}</Text>
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
          </>
        )}
      </ScrollView>

      {!pickerView && !detail.isConfirmed && !detail.isRejected && (
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

      {pickerActionable && permissions.includes("Pullout.Edit") && (
        <View style={styles.picklistBar}>
          <SupervisorAddPulloutMaterial token={token} pulloutId={detail.id} onAdded={load} />
        </View>
      )}

      {pickerActionable && !permissions.includes("Pullout.Confirm") && (
        <View style={styles.picklistBar}>
          <Text style={styles.picklistHint}>
            {picksDone < allPicks.length
              ? "Submit every pick. A supervisor reviews and confirms the pick-list afterwards."
              : "Every pick is submitted. Waiting for the supervisor to review and confirm."}
          </Text>
        </View>
      )}

      {pickerActionable && permissions.includes("Pullout.Confirm") && (
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

      <PulloutPickerChangeLocationModal
        pick={pickerChangePick}
        token={token}
        onClose={() => setPickerChangePick(null)}
        onSubmit={handlePickerLocationChange}
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
  pickRowTop: { flexDirection: "row", alignItems: "center", justifyContent: "space-between", gap: spacing.sm },
  pickLocationPill: { flexDirection: "row", alignItems: "center", backgroundColor: colors.primarySoft, borderRadius: radius.pill, paddingHorizontal: spacing.sm, paddingVertical: 6 },
  pickLocation: { ...typography.caption, color: colors.primaryText, fontWeight: "700", marginLeft: spacing.xs },
  pickDetail: { ...typography.caption, color: colors.textSecondary, flex: 1 },
  pickCard: { marginBottom: spacing.md, padding: spacing.md, shadowOpacity: 0.035, borderColor: colors.borderStrong },
  pickCardDone: { borderColor: "#bbf7d0", backgroundColor: "#fbfffc" },
  pickLocationBlock: { flexDirection: "row", alignItems: "center", flex: 1, minWidth: 0 },
  pickLocationIcon: { width: 36, height: 36, borderRadius: radius.md, alignItems: "center", justifyContent: "center", backgroundColor: colors.primarySoft, marginRight: spacing.sm },
  pickLocationIconDone: { backgroundColor: colors.successSoft },
  pickLocationCopy: { flex: 1, minWidth: 0 },
  pickLocationLabel: { fontSize: 9.5, lineHeight: 12, fontWeight: "800", letterSpacing: 0.8, color: colors.textMuted },
  pickLocationRow: { flexDirection: "row", alignItems: "center", flexShrink: 1 },
  pickLocationLarge: { fontSize: 22, lineHeight: 26, fontWeight: "800", color: colors.primary, letterSpacing: -0.3 },
  pickLocationLargeDone: { color: colors.successText },
  virtualTag: { fontSize: 10, color: colors.infoText, fontWeight: "800", backgroundColor: colors.infoSoft, borderRadius: radius.pill, paddingHorizontal: 7, paddingVertical: 2, marginLeft: spacing.sm },
  pickMaterialDescription: { ...typography.bodyStrong, color: colors.textPrimary, lineHeight: 19, marginTop: spacing.md },
  pickMetaRow: { flexDirection: "row", alignItems: "center", flexWrap: "wrap", gap: 6, marginTop: spacing.sm },
  pickMetaItem: { flexDirection: "row", alignItems: "center", gap: 5, borderRadius: radius.sm, backgroundColor: colors.neutralSoft, paddingHorizontal: spacing.sm, paddingVertical: 6 },
  pickMetaText: { ...typography.caption, color: colors.textSecondary, fontWeight: "700" },
  pickQuantityChip: { borderRadius: radius.sm, backgroundColor: colors.primarySoft, paddingHorizontal: spacing.sm, paddingVertical: 6 },
  pickQuantityText: { ...typography.caption, color: colors.primaryText, fontWeight: "800" },
  pickActions: { flexDirection: "row", justifyContent: "flex-end", marginTop: spacing.sm },
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
  pickCardFooter: { flexDirection: "row", alignItems: "center", justifyContent: "space-between", borderTopWidth: 1, borderTopColor: colors.border, marginTop: spacing.md, paddingTop: spacing.sm },
  pickCardHint: { ...typography.caption, color: colors.textMuted },
  changeLocationButton: {
    flexShrink: 0,
    flexDirection: "row",
    alignItems: "center",
    paddingHorizontal: spacing.sm,
    paddingVertical: 6,
    minHeight: 32,
    borderRadius: radius.pill,
    backgroundColor: colors.primarySoft,
  },
  changeLocationText: { color: colors.primary, fontSize: 12, fontWeight: "800", marginLeft: 4 },
  pickedRow: { flexDirection: "row", alignItems: "center", marginTop: spacing.sm },
  pickedText: { color: colors.successText, fontWeight: "700", marginLeft: spacing.xs },
  statusBadge: { flexShrink: 0, alignSelf: "flex-start", borderRadius: radius.md, paddingHorizontal: 10, paddingVertical: 6 },
  statusBadgeTodo: { minWidth: 76, backgroundColor: colors.warningSoft },
  statusBadgeDone: { backgroundColor: colors.successSoft },
  statusBadgeText: { fontSize: 12, lineHeight: 16, fontWeight: "800", textAlign: "center" },
  statusBadgeTextTodo: { color: colors.warningText },
  statusBadgeTextDone: { color: colors.successText },
  pickedDetailText: { ...typography.caption, color: colors.successText, fontWeight: "700", marginTop: spacing.sm },
  actionBar: { flexDirection: "row", alignItems: "center", backgroundColor: colors.surface, borderTopWidth: 1, borderTopColor: colors.border, paddingHorizontal: spacing.lg, paddingTop: spacing.md, paddingBottom: spacing.md },
  actionCopy: { flex: 1 },
  actionLabel: { ...typography.bodyStrong, color: colors.textPrimary },
  actionHint: { ...typography.caption, color: colors.textSecondary, marginTop: 1 },
  confirmAction: { minWidth: 190 },
});
