import { useLocalSearchParams } from "expo-router";
import { useCallback, useEffect, useState } from "react";
import { Alert, ScrollView, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { ApiError } from "../../api/client";
import { assignInwardPutawayPkm, downloadInwardDetail, getInwardDetail } from "../../api/endpoints";
import { useAuth } from "../../auth/AuthContext";
import { BottomSheet } from "../../components/ui/BottomSheet";
import { AppIcon } from "../../components/ui/AppIcon";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { LoadingView } from "../../components/ui/LoadingView";
import { Screen } from "../../components/ui/Screen";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { colors, radius, spacing, typography } from "../../theme";
import type { InwardDetailDto, InwardPutawayDto } from "../../types/api";
import { saveAndShareXlsx } from "../../utils/saveAndShare";
import { formatDateTime } from "../../utils/status";

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

  const rows: PutawayRow[] = detail.lines.flatMap((line) =>
    line.putaways.map((putaway) => ({ putaway, materialNumber: line.materialNumber, materialDescription: line.materialDescription })),
  );
  const pendingRows = rows.filter((row) => !row.putaway.isConfirmed).length;
  const totalBoxes = rows.reduce((total, row) => total + row.putaway.quantityBoxes, 0);

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
    <Screen>
      <ScrollView contentContainerStyle={styles.content} showsVerticalScrollIndicator={false}>
        <View style={styles.headerCard}>
          <View style={styles.headerTop}>
            <View style={styles.headerIcon}><AppIcon name="receipt-outline" size={22} color={colors.primary} /></View>
            <View style={styles.headerCopy}>
              <Text style={styles.eyebrow}>{pendingRows > 0 ? "PKM ASSIGNMENT REQUIRED" : "GRN COMPLETE"}</Text>
              <Text style={styles.reference} numberOfLines={1}>{detail.referenceNumber}</Text>
            </View>
          </View>
          <View style={styles.dateRow}>
            <AppIcon name="time-outline" size={14} color={colors.textSecondary} />
            <Text style={styles.subtitle}>{formatDateTime(detail.createdAt)}</Text>
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
              <Text style={[styles.statValue, pendingRows > 0 && styles.pendingValue]}>{pendingRows}</Text>
              <Text style={styles.statLabel}>Need PKM</Text>
            </View>
          </View>
        </View>

        <View style={styles.sectionHeading}>
          <Text style={styles.sectionTitle}>Put-away allocations</Text>
          <Text style={styles.sectionCount}>{rows.length}</Text>
        </View>

        {rows.map((row, index) => (
          <Card key={row.putaway.id} style={styles.row}>
            <View style={styles.rowHeader}>
              <View style={styles.rowNumber}><Text style={styles.rowNumberText}>{index + 1}</Text></View>
              <View style={styles.materialCopy}>
                <Text style={styles.materialCode}>{row.materialNumber}</Text>
                <Text style={styles.rowMaterial} numberOfLines={2}>{row.materialDescription}</Text>
              </View>
              {row.putaway.isConfirmed && <StatusBadge label="PKM set" tone="success" variant="soft" />}
            </View>
            <View style={styles.rowFooter}>
              <View style={styles.locationBadge}>
                <AppIcon name="location-outline" size={15} color={colors.primary} />
                <Text style={styles.rowLocation}>{row.putaway.locationCode}</Text>
              </View>
              <View style={styles.quantityBadge}>
                <AppIcon name="cube-outline" size={14} color={colors.textSecondary} />
                <Text style={styles.qtyText}>{row.putaway.quantityBoxes} boxes</Text>
              </View>
              {!row.putaway.isConfirmed && (
                <Button
                  title="Assign PKM"
                  onPress={() => openAssign(row)}
                  variant="secondary"
                  fullWidth={false}
                  icon={<AppIcon name="calendar-outline" size={16} color={colors.primaryText} />}
                  style={styles.assignButton}
                />
              )}
            </View>
          </Card>
        ))}
      </ScrollView>

      <View style={styles.actionBar}>
        <View style={styles.actionCopy}>
          <Text style={styles.actionTitle}>GRN allocation</Text>
          <Text style={styles.actionHint}>Export all location details</Text>
        </View>
        <Button
          title="Download Excel"
          variant="outline"
          onPress={handleDownload}
          fullWidth={false}
          icon={<AppIcon name="download-outline" size={18} color={colors.primary} />}
          style={styles.downloadButton}
        />
      </View>

      <BottomSheet visible={activePutaway !== null} title={`Assign PKM — ${activePutaway?.putaway.locationCode ?? ""}`} onClose={() => setActivePutaway(null)}>
        {activePutaway && (
          <View style={styles.assignmentSummary}>
            <View style={styles.assignmentIcon}><AppIcon name="cube-outline" size={18} color={colors.primary} /></View>
            <View style={styles.assignmentCopy}>
              <Text style={styles.assignmentCode}>{activePutaway.materialNumber}</Text>
              <Text style={styles.assignmentDescription} numberOfLines={1}>{activePutaway.materialDescription}</Text>
            </View>
            <Text style={styles.assignmentQty}>{activePutaway.putaway.quantityBoxes} boxes</Text>
          </View>
        )}
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

        <Button
          title="Assign PKM"
          onPress={handleAssign}
          loading={isSaving}
          disabled={!selectedMonth || !selectedYear}
          icon={<AppIcon name="checkmark-circle-outline" size={19} color={colors.textOnPrimary} />}
        />
      </BottomSheet>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { padding: spacing.lg, paddingBottom: 104 },
  headerCard: { backgroundColor: colors.primarySoft, borderWidth: 1, borderColor: "#c7d2fe", borderRadius: radius.xl, padding: spacing.lg, marginBottom: spacing.lg },
  headerTop: { flexDirection: "row", alignItems: "center" },
  headerIcon: { width: 48, height: 48, borderRadius: radius.md, backgroundColor: colors.surface, alignItems: "center", justifyContent: "center", marginRight: spacing.md },
  headerCopy: { flex: 1, minWidth: 0 },
  eyebrow: { ...typography.label, fontSize: 10, color: colors.primary, letterSpacing: 0.7 },
  reference: { ...typography.title, fontSize: 18, color: colors.textPrimary, marginTop: 2 },
  dateRow: { flexDirection: "row", alignItems: "center", marginTop: spacing.md, marginLeft: 2 },
  subtitle: { ...typography.caption, color: colors.textSecondary, marginLeft: 5 },
  summaryStats: { flexDirection: "row", alignItems: "center", backgroundColor: "rgba(255,255,255,0.72)", borderRadius: radius.md, marginTop: spacing.md, paddingVertical: spacing.sm },
  statItem: { flex: 1, alignItems: "center" },
  statValue: { fontSize: 18, fontWeight: "800", color: colors.textPrimary },
  pendingValue: { color: colors.warningText },
  statLabel: { ...typography.caption, color: colors.textSecondary, marginTop: 1 },
  statDivider: { width: 1, height: 28, backgroundColor: "#dbe2f3" },
  sectionHeading: { flexDirection: "row", alignItems: "center", marginBottom: spacing.sm, paddingHorizontal: 2 },
  sectionTitle: { ...typography.title, fontSize: 17, color: colors.textPrimary },
  sectionCount: { ...typography.label, color: colors.primaryText, backgroundColor: colors.primarySoft, borderRadius: radius.pill, paddingHorizontal: spacing.sm, paddingVertical: 4, marginLeft: spacing.sm },
  row: { marginBottom: spacing.md, padding: spacing.md, shadowOpacity: 0.035 },
  rowHeader: { flexDirection: "row", alignItems: "flex-start" },
  rowNumber: { width: 30, height: 30, borderRadius: radius.pill, backgroundColor: colors.neutralSoft, alignItems: "center", justifyContent: "center", marginRight: spacing.sm },
  rowNumberText: { ...typography.label, color: colors.textSecondary },
  materialCopy: { flex: 1, minWidth: 0, paddingRight: spacing.sm },
  materialCode: { ...typography.bodyStrong, color: colors.textPrimary },
  rowMaterial: { ...typography.body, color: colors.textSecondary, lineHeight: 19, marginTop: 1 },
  rowFooter: { flexDirection: "row", alignItems: "center", borderTopWidth: 1, borderTopColor: colors.border, marginTop: spacing.md, paddingTop: spacing.md, gap: spacing.sm },
  locationBadge: { flexDirection: "row", alignItems: "center", backgroundColor: colors.primarySoft, borderRadius: radius.pill, paddingHorizontal: spacing.sm, paddingVertical: 7 },
  rowLocation: { ...typography.caption, color: colors.primaryText, fontWeight: "800", marginLeft: spacing.xs },
  quantityBadge: { flexDirection: "row", alignItems: "center", backgroundColor: colors.neutralSoft, borderRadius: radius.pill, paddingHorizontal: spacing.sm, paddingVertical: 7 },
  qtyText: { ...typography.caption, color: colors.textSecondary, fontWeight: "700", marginLeft: spacing.xs },
  assignButton: { marginLeft: "auto" },
  actionBar: { flexDirection: "row", alignItems: "center", backgroundColor: colors.surface, borderTopWidth: 1, borderTopColor: colors.border, paddingHorizontal: spacing.lg, paddingVertical: spacing.md },
  actionCopy: { flex: 1 },
  actionTitle: { ...typography.bodyStrong, color: colors.textPrimary },
  actionHint: { ...typography.caption, color: colors.textSecondary, marginTop: 1 },
  downloadButton: { minWidth: 166 },
  assignmentSummary: { flexDirection: "row", alignItems: "center", backgroundColor: colors.primarySoft, borderRadius: radius.md, padding: spacing.md, marginBottom: spacing.lg },
  assignmentIcon: { width: 36, height: 36, borderRadius: radius.sm, backgroundColor: colors.surface, alignItems: "center", justifyContent: "center", marginRight: spacing.sm },
  assignmentCopy: { flex: 1, minWidth: 0 },
  assignmentCode: { ...typography.bodyStrong, color: colors.textPrimary },
  assignmentDescription: { ...typography.caption, color: colors.textSecondary, marginTop: 1 },
  assignmentQty: { ...typography.caption, color: colors.primaryText, fontWeight: "800", marginLeft: spacing.sm },
  pickerLabel: { ...typography.label, color: colors.textSecondary, marginBottom: spacing.sm },
  chipRow: { flexDirection: "row", flexWrap: "wrap", gap: spacing.sm, marginBottom: spacing.lg },
  chip: { borderWidth: 1.5, borderColor: colors.border, borderRadius: radius.pill, paddingHorizontal: spacing.lg, paddingVertical: spacing.sm },
  chipActive: { backgroundColor: colors.primary, borderColor: colors.primary },
  chipText: { fontSize: 13, color: colors.textSecondary, fontWeight: "600" },
  chipTextActive: { color: colors.textOnPrimary, fontWeight: "700" },
});
