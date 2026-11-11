import { useFocusEffect, useRouter } from "expo-router";
import { useCallback, useEffect, useRef, useState } from "react";
import { ActivityIndicator, FlatList, RefreshControl, StyleSheet, Text, View } from "react-native";
import { getPickerPutawayQueue } from "../api/endpoints";
import { useAuth } from "../auth/AuthContext";
import { Card } from "../components/ui/Card";
import { AppIcon } from "../components/ui/AppIcon";
import { EmptyState } from "../components/ui/EmptyState";
import { RefreshButton } from "../components/ui/RefreshButton";
import { SkeletonList } from "../components/ui/Skeleton";
import { Screen } from "../components/ui/Screen";
import { SearchField } from "../components/ui/SearchField";
import { StatusBadge } from "../components/ui/StatusBadge";
import { colors, spacing, typography } from "../theme";
import type { InwardHistoryDto } from "../types/api";
import { allocationStatusLabel, allocationStatusTone, formatDateTime } from "../utils/status";

function allSubmitted(item: InwardHistoryDto): boolean {
  return item.locationCount > 0 && item.submittedLocationCount >= item.locationCount;
}

function submissionTone(item: InwardHistoryDto): "success" | "warning" | "danger" {
  if (allSubmitted(item)) return "success";
  return item.submittedLocationCount > 0 ? "warning" : "danger";
}

function submissionLabel(item: InwardHistoryDto): string {
  if (item.locationCount === 0) return "No locations";
  return allSubmitted(item) ? "All submitted" : `${item.submittedLocationCount} of ${item.locationCount} submitted`;
}

const PAGE_SIZE = 20;

export default function PickerPutawayQueueScreen() {
  const { token } = useAuth();
  const router = useRouter();
  const [search, setSearch] = useState("");
  const [items, setItems] = useState<InwardHistoryDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [hasNextPage, setHasNextPage] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [isLoadingMore, setIsLoadingMore] = useState(false);
  const pageRef = useRef(1);

  const load = useCallback(
    async (refreshing = false) => {
      if (!token) return;
      pageRef.current = 1;
      if (refreshing) setIsRefreshing(true);
      else setIsLoading(true);
      try {
        const page = await getPickerPutawayQueue(token, { search: search.trim() || undefined, pageNumber: 1, pageSize: PAGE_SIZE });
        setItems(page.items);
        setTotalCount(page.totalCount);
        setHasNextPage(page.hasNextPage);
      } finally {
        if (refreshing) setIsRefreshing(false);
        else setIsLoading(false);
      }
    },
    [token, search],
  );

  const loadMore = useCallback(async () => {
    if (!token || isLoadingMore || !hasNextPage) return;
    setIsLoadingMore(true);
    try {
      const nextPage = pageRef.current + 1;
      const page = await getPickerPutawayQueue(token, { search: search.trim() || undefined, pageNumber: nextPage, pageSize: PAGE_SIZE });
      pageRef.current = nextPage;
      setItems((prev) => [...prev, ...page.items]);
      setHasNextPage(page.hasNextPage);
    } finally {
      setIsLoadingMore(false);
    }
  }, [token, search, isLoadingMore, hasNextPage]);

  useEffect(() => {
    const timer = setTimeout(() => load(), 300);
    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search, token]);

  // Re-pull when returning from a GRN's detail screen, since a put-away placed there changes this list's counts.
  useFocusEffect(
    useCallback(() => {
      load(true);
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [token]),
  );

  return (
    <Screen padded>
      <View style={styles.introRow}>
        <View>
          <Text style={styles.eyebrow}>FLOOR TASKS</Text>
          <Text style={styles.introText}>Put-away queue</Text>
        </View>
        <View style={styles.introActions}>
          <RefreshButton onPress={() => load(true)} refreshing={isRefreshing} />
          <View style={styles.countPill}><Text style={styles.countText}>{totalCount}</Text></View>
        </View>
      </View>
      <SearchField placeholder="Search reference, vehicle, LR, material" value={search} onChangeText={setSearch} />

      {isLoading ? (
        <SkeletonList />
      ) : (
        <FlatList
          data={items}
          keyExtractor={(item) => String(item.id)}
          refreshControl={<RefreshControl refreshing={isRefreshing} onRefresh={() => load(true)} tintColor={colors.primary} />}
          contentContainerStyle={{ paddingBottom: 24 }}
          onEndReachedThreshold={0.4}
          onEndReached={loadMore}
          ListFooterComponent={isLoadingMore ? <ActivityIndicator style={{ marginVertical: spacing.lg }} color={colors.primary} /> : null}
          renderItem={({ item }) => (
            <Card onPress={() => router.push(`/picker-putaway/${item.id}`)} style={styles.card}>
              <View style={styles.cardHeader}>
                <View style={styles.referenceWrap}>
                  <View style={styles.iconBox}><AppIcon name="location-outline" size={17} color={colors.primary} /></View>
                  <Text style={styles.reference} numberOfLines={1}>{item.referenceNumber}</Text>
                </View>
                <StatusBadge label={allocationStatusLabel(item.status)} tone={allocationStatusTone(item.status)} />
              </View>
              <View style={styles.progressRow}>
                <StatusBadge label={submissionLabel(item)} tone={submissionTone(item)} />
                <Text style={styles.boxes}>{item.totalQuantityBoxes} boxes</Text>
              </View>
              <Text style={styles.meta}>{formatDateTime(item.createdAt)} · {item.lineCount} lines{item.vehicleNumber ? ` · ${item.vehicleNumber}` : ""}</Text>
            </Card>
          )}
          ListEmptyComponent={<EmptyState icon="checkmark-circle-outline" title="Nothing waiting" subtitle="No GRN is waiting for put-away." />}
        />
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  introRow: { flexDirection: "row", alignItems: "center", justifyContent: "space-between", marginBottom: spacing.md },
  introActions: { flexDirection: "row", alignItems: "center", gap: spacing.sm },
  eyebrow: { ...typography.label, fontSize: 10, color: colors.primary, letterSpacing: 0.8 },
  introText: { ...typography.title, color: colors.textPrimary, marginTop: 2 },
  countPill: { minWidth: 38, height: 32, borderRadius: 16, backgroundColor: colors.primarySoft, alignItems: "center", justifyContent: "center" },
  countText: { ...typography.bodyStrong, color: colors.primaryText },
  card: { marginBottom: spacing.sm, padding: spacing.md },
  cardHeader: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", gap: spacing.sm },
  referenceWrap: { flex: 1, flexDirection: "row", alignItems: "center" },
  iconBox: { width: 32, height: 32, borderRadius: 10, backgroundColor: colors.primarySoft, alignItems: "center", justifyContent: "center", marginRight: spacing.sm },
  reference: { ...typography.subtitle, color: colors.textPrimary, flex: 1 },
  progressRow: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginTop: spacing.sm, paddingLeft: 40 },
  boxes: { ...typography.caption, color: colors.textPrimary, fontWeight: "800" },
  meta: { ...typography.caption, color: colors.textSecondary, marginTop: spacing.sm, paddingLeft: 40 },
});
