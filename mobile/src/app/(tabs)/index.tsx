import type { Ionicons } from "@expo/vector-icons";
import { StatusBar } from "expo-status-bar";
import { useFocusEffect, useRouter } from "expo-router";
import { useCallback, useState } from "react";
import { Image, ScrollView, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { getInwardHistory, getPickerPulloutQueue, getPickerPutawayQueue, getPulloutQueue } from "../../api/endpoints";
import { useAuth } from "../../auth/AuthContext";
import { AppIcon } from "../../components/ui/AppIcon";
import { FadeInUp } from "../../components/ui/FadeInUp";
import { Pulse } from "../../components/ui/Skeleton";
import { Screen } from "../../components/ui/Screen";
import { colors, radius, spacing, typography } from "../../theme";
import { lightTap } from "../../utils/haptics";

interface StatTile {
  key: string;
  icon: keyof typeof Ionicons.glyphMap;
  label: string;
  route: string;
  fetchCount: (token: string) => Promise<number>;
}

interface ActionTile {
  key: string;
  icon: keyof typeof Ionicons.glyphMap;
  color: string;
  softColor: string;
  title: string;
  subtitle: string;
  route: string;
}

/** Data-driven tone for a stat tile: 0 reads as "all clear" (success), a failed fetch (-1 sentinel)
 * reads as an error state (danger), a small pending count is informational (info), and a double-digit
 * count escalates to a warning - this is the literal "info/warning/error in summary cards" ask, not
 * just decoration. */
type StatTone = "info" | "success" | "warning" | "danger";

function toneForCount(value: number): StatTone {
  if (value < 0) return "danger";
  if (value === 0) return "success";
  if (value >= 10) return "warning";
  return "info";
}

const statTones: Record<StatTone, { background: string; border: string; accent: string }> = {
  info: { background: "#eff6ff", border: "#bfdbfe", accent: "#2563eb" },
  success: { background: "#ecfdf5", border: "#bbf7d0", accent: "#16a34a" },
  warning: { background: "#fffbeb", border: "#fde68a", accent: "#d97706" },
  danger: { background: "#fff1f2", border: "#fecdd3", accent: "#dc2626" },
};

export default function HomeScreen() {
  const router = useRouter();
  const { user, permissions } = useAuth();

  const canPick = permissions.includes("Inward.Create");
  const canAssignPkm = permissions.includes("Inward.AssignPkm");
  const canConfirmInward = permissions.includes("Inward.Confirm");
  // Supervisors confirm old pending pullouts; pickers work the picks of posted ones - both reach the Pullout queue.
  const canConfirmPullout = permissions.includes("Pullout.Confirm") || permissions.includes("Pullout.Pick");
  // Mirrors the web app's naming: the picker-facing queue is "Picker Pick-list" (/pullout-picker),
  // the supervisor's is "Pullout Queue" (/pullout/history) - same route on mobile, different label.
  const isPickerPulloutRole = permissions.includes("Pullout.Pick");

  const [counts, setCounts] = useState<Record<string, number | null>>({});

  const statTiles: StatTile[] = [
    canPick && {
      key: "inward",
      icon: "document-text-outline",
      label: "Pending GRNs",
      route: "/inward-history",
      fetchCount: async (token: string) => (await getInwardHistory(token, { pageSize: 1 })).totalCount,
    },
    canAssignPkm && {
      key: "putaway",
      icon: "file-tray-stacked-outline",
      label: "To put away",
      route: "/picker-putaway-queue",
      fetchCount: async (token: string) => (await getPickerPutawayQueue(token, { pageSize: 1 })).totalCount,
    },
    canConfirmInward && {
      key: "inward-confirm",
      icon: "cube-outline",
      label: "Putaway queue",
      route: "/inward-putaway-queue",
      fetchCount: async (token: string) => (await getInwardHistory(token, { pageSize: 1 })).totalCount,
    },
    canConfirmPullout && {
      key: "pullout",
      icon: "car-outline",
      label: isPickerPulloutRole ? "Picker Pick-list" : "Pullout queue",
      route: "/pullout-queue",
      fetchCount: async (token: string) => (permissions.includes("Pullout.Pick")
        ? (await getPickerPulloutQueue(token, { pageSize: 1 })).totalCount
        : (await getPulloutQueue(token, { pageSize: 1 })).totalCount),
    },
  ].filter(Boolean) as StatTile[];

  const actionTiles: ActionTile[] = [
    canPick && {
      key: "record-inward",
      icon: "download-outline",
      color: colors.primary,
      softColor: colors.primarySoft,
      title: "Record inward",
      subtitle: "Manual entry or Excel upload",
      route: "/inward-entry",
    },
    canPick && {
      key: "inward-history",
      icon: "list-outline",
      color: colors.info,
      softColor: colors.infoSoft,
      title: "Inward history",
      subtitle: "Pending GRNs, assign PKM",
      route: "/inward-history",
    },
    canAssignPkm && {
      key: "picker-putaway",
      icon: "pricetag-outline",
      color: "#7c3aed",
      softColor: "#f3e8ff",
      title: "Picker put-away",
      subtitle: "Location, qty and PKM",
      route: "/picker-putaway-queue",
    },
    canConfirmInward && {
      key: "inward-putaway-queue",
      icon: "cube-outline",
      color: colors.warningSolid,
      softColor: colors.warningSoft,
      title: "Inward putaway queue",
      subtitle: "Edit location, confirm GRNs",
      route: "/inward-putaway-queue",
    },
    canConfirmPullout && {
      key: "pullout-queue",
      icon: "car-outline",
      color: "#0d9488",
      softColor: "#ccfbf1",
      title: isPickerPulloutRole ? "Picker Pick-list" : "Pullout queue",
      subtitle: isPickerPulloutRole ? "Confirm each pick as you take it" : "Confirm picks, change locations",
      route: "/pullout-queue",
    },
  ].filter(Boolean) as ActionTile[];

  const initials = (user?.fullName ?? "?")
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join("");

  return (
    <Screen edges={["top"]} style={styles.screen}>
      <StatusBar style="dark" />
      <ScrollView contentContainerStyle={styles.page} showsVerticalScrollIndicator={false} bounces={false}>
        <FadeInUp delay={0} style={styles.header}>
          <View style={styles.headerTop}>
            <Image
              source={require("../../../assets/logivue-warehouse-mark.png")}
              style={styles.brandLogo}
              resizeMode="contain"
              accessibilityLabel="LogiVue warehouse mark"
            />
            <View style={styles.greetingCopy}>
              <Text style={styles.eyebrow}>WELCOME BACK</Text>
              <Text style={styles.greeting}>Hi{user?.fullName ? `, ${user.fullName.split(" ")[0]}` : ""}</Text>
            </View>
            <TouchableOpacity
              style={styles.avatar}
              activeOpacity={0.72}
              accessibilityRole="button"
              accessibilityLabel="Open account"
              hitSlop={8}
              onPress={() => {
                lightTap();
                router.push("/account" as never);
              }}
            >
              <Text style={styles.avatarText}>{initials}</Text>
            </TouchableOpacity>
          </View>
        </FadeInUp>

        <HomeBody
          statTiles={statTiles}
          actionTiles={actionTiles}
          counts={counts}
          setCounts={setCounts}
          router={router}
        />
      </ScrollView>
    </Screen>
  );
}

// Split out so the stat-count fetch (which needs the token) can live in one focused effect without
// cluttering the header/layout code above.
function HomeBody({
  statTiles,
  actionTiles,
  counts,
  setCounts,
  router,
}: {
  statTiles: StatTile[];
  actionTiles: ActionTile[];
  counts: Record<string, number | null>;
  setCounts: (updater: (prev: Record<string, number | null>) => Record<string, number | null>) => void;
  router: ReturnType<typeof useRouter>;
}) {
  const { token } = useAuth();

  useFocusEffect(
    useCallback(() => {
      if (!token) return;
      setCounts((prev) => {
        const next = { ...prev };
        for (const tile of statTiles) next[tile.key] = null;
        return next;
      });
      statTiles.forEach((tile) => {
        tile
          .fetchCount(token)
          .then((value) => setCounts((prev) => ({ ...prev, [tile.key]: value })))
          .catch(() => setCounts((prev) => ({ ...prev, [tile.key]: -1 })));
      });
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [token]),
  );

  return (
    <>
      {statTiles.length > 0 && (
        <FadeInUp delay={80} style={styles.statRow}>
          {statTiles.map((tile) => {
            const value = counts[tile.key];
            const tone = value == null ? null : toneForCount(value);
            const toneStyle = tone == null ? null : statTones[tone];
            return (
              <TouchableOpacity
                key={tile.key}
                style={styles.statTileWrap}
                activeOpacity={0.85}
                onPress={() => {
                  lightTap();
                  router.push(tile.route as never);
                }}
              >
                {tone == null ? (
                  <View style={[styles.statTile, styles.statTileLoading]}>
                    <Pulse style={styles.statPulse} />
                    <Text style={styles.statLabelLoading}>{tile.label}</Text>
                  </View>
                ) : (
                  <View
                    style={[
                      styles.statTile,
                      { backgroundColor: toneStyle?.background, borderColor: toneStyle?.border },
                    ]}
                  >
                    <View style={styles.statTopRow}>
                      <Text style={[styles.statNumber, { color: toneStyle?.accent }]}>{value != null && value < 0 ? "—" : value}</Text>
                      <View style={[styles.statIcon, { backgroundColor: toneStyle?.border }]}>
                        <AppIcon name={tile.icon} size={18} color={toneStyle?.accent} />
                      </View>
                    </View>
                    <Text style={styles.statLabel}>{tile.label}</Text>
                  </View>
                )}
              </TouchableOpacity>
            );
          })}
        </FadeInUp>
      )}

      <FadeInUp delay={160} style={styles.scroll}>
        <View style={styles.sectionHeading}>
          <View>
            <Text style={styles.sectionLabel}>QUICK ACTIONS</Text>
            <Text style={styles.sectionTitle}>Start a task</Text>
          </View>
          <Text style={styles.actionCount}>{actionTiles.length} available</Text>
        </View>
        <View style={styles.actionGrid}>
          {actionTiles.map((tile) => (
            <TouchableOpacity
              key={tile.key}
              style={styles.actionTile}
              activeOpacity={0.76}
              onPress={() => {
                lightTap();
                router.push(tile.route as never);
              }}
            >
              <View style={[styles.actionIcon, { backgroundColor: tile.softColor }]}>
                <AppIcon name={tile.icon} size={22} color={tile.color} />
              </View>
              <View style={styles.actionCopy}>
                <Text style={styles.actionTitle}>{tile.title}</Text>
                <Text style={styles.actionSubtitle}>{tile.subtitle}</Text>
              </View>
              <View style={styles.actionArrow}>
                <AppIcon name="chevron-forward" size={19} color={colors.textSecondary} />
              </View>
            </TouchableOpacity>
          ))}
        </View>
      </FadeInUp>
    </>
  );
}

const styles = StyleSheet.create({
  screen: { backgroundColor: colors.background },
  page: { paddingBottom: spacing.xl },
  header: {
    paddingHorizontal: spacing.xl,
    paddingTop: spacing.sm,
    paddingBottom: spacing.lg,
    backgroundColor: colors.surface,
    borderBottomWidth: 1,
    borderBottomColor: colors.border,
  },
  headerTop: { flexDirection: "row", alignItems: "center" },
  brandLogo: { width: 58, height: 54 },
  avatar: {
    width: 42,
    height: 42,
    borderRadius: radius.pill,
    backgroundColor: colors.primarySoft,
    borderWidth: 1,
    borderColor: "#c7d2fe",
    alignItems: "center",
    justifyContent: "center",
  },
  avatarText: { color: colors.primaryText, fontWeight: "800", fontSize: 14 },
  greetingCopy: { flex: 1, minWidth: 0, marginLeft: spacing.md },
  eyebrow: { ...typography.label, color: colors.textMuted, letterSpacing: 0.7, marginBottom: 2 },
  greeting: { ...typography.display, fontSize: 22, lineHeight: 27, color: colors.textPrimary },
  statRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing.md,
    paddingHorizontal: spacing.xl,
    paddingTop: spacing.lg,
  },
  statTileWrap: { flexBasis: "47%", flexGrow: 1 },
  statTile: {
    minHeight: 88,
    justifyContent: "space-between",
    borderRadius: radius.lg,
    borderWidth: 1,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.md,
  },
  statTileLoading: { backgroundColor: colors.surface, justifyContent: "center" },
  statTopRow: { flexDirection: "row", alignItems: "center", justifyContent: "space-between" },
  statIcon: { width: 32, height: 32, borderRadius: radius.md, alignItems: "center", justifyContent: "center" },
  statPulse: { width: 44, height: 25, borderRadius: radius.sm, marginBottom: 6 },
  statNumber: { ...typography.statNumber, fontSize: 30 },
  statLabel: { ...typography.caption, color: colors.textSecondary, marginTop: 2, fontWeight: "700" },
  statLabelLoading: { ...typography.caption, color: colors.textSecondary, marginTop: 2 },
  scroll: { paddingHorizontal: spacing.xl, paddingTop: spacing.xl },
  sectionHeading: { flexDirection: "row", alignItems: "flex-end", justifyContent: "space-between", marginBottom: spacing.md },
  sectionLabel: {
    ...typography.label,
    color: colors.textMuted,
    letterSpacing: 0.7,
  },
  sectionTitle: { ...typography.title, color: colors.textPrimary, marginTop: 2 },
  actionCount: { ...typography.caption, color: colors.textSecondary, marginBottom: 2 },
  actionGrid: { gap: spacing.sm },
  actionTile: {
    minHeight: 72,
    flexDirection: "row",
    alignItems: "center",
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.border,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
  },
  actionIcon: {
    width: 44,
    height: 44,
    borderRadius: radius.md,
    alignItems: "center",
    justifyContent: "center",
  },
  actionCopy: { flex: 1, minWidth: 0, marginHorizontal: spacing.md },
  actionTitle: { ...typography.subtitle, fontSize: 15, color: colors.textPrimary },
  actionSubtitle: { ...typography.caption, color: colors.textSecondary, marginTop: 2 },
  actionArrow: {
    width: 32,
    height: 32,
    borderRadius: radius.pill,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: colors.neutralSoft,
  },
});
