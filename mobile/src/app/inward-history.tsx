import { useFocusEffect, useRouter } from "expo-router";
import { useCallback, useEffect, useRef, useState } from "react";
import { ActivityIndicator, FlatList, RefreshControl, ScrollView, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { getInwardHistory } from "../api/endpoints";
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

const PAGE_SIZE = 20;

export default function InwardHistoryScreen() {
  const { token, warehouses } = useAuth();
  const router = useRouter();
  const [search, setSearch] = useState("");
  const [warehouseId, setWarehouseId] = useState<number | undefined>(undefined);
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
        const page = await getInwardHistory(token, { search: search.trim() || undefined, warehouseId, pageNumber: 1, pageSize: PAGE_SIZE });
        setItems(page.items);
        setTotalCount(page.totalCount);
        setHasNextPage(page.hasNextPage);
      } finally {
        if (refreshing) setIsRefreshing(false);
        else setIsLoading(false);
      }
    },
    [token, search, warehouseId],
  );

  const loadMore = useCallback(async () => {
    if (!token || isLoadingMore || !hasNextPage) return;
    setIsLoadingMore(true);
    try {
      const nextPage = pageRef.current + 1;
      const page = await getInwardHistory(token, { search: search.trim() || undefined, warehouseId, pageNumber: nextPage, pageSize: PAGE_SIZE });
      pageRef.current = nextPage;
      setItems((prev) => [...prev, ...page.items]);
      setHasNextPage(page.hasNextPage);
    } finally {
      setIsLoadingMore(false);
    }
  }, [token, search, warehouseId, isLoadingMore, hasNextPage]);

  useEffect(() => {
    const timer = setTimeout(() => load(), 300);
    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search, warehouseId, token]);

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
          <Text style={styles.eyebrow}>GOODS RECEIPT</Text>
          <Text style={styles.introText}>Pending GRNs</Text>
        </View>
        <View style={styles.introActions}>
          <RefreshButton onPress={() => load(true)} refreshing={isRefreshing} />
          <View style={styles.countPill}>
            <Text style={styles.countText}>{totalCount}</Text>
          </View>
        </View>
      </View>
      <SearchField placeholder="Search reference, vehicle, LR, material" value={search} onChangeText={setSearch} />

      {warehouses.length > 1 && (
        <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.warehouseRow} contentContainerStyle={{ gap: spacing.sm }}>
          <TouchableOpacity style={[styles.chip, warehouseId === undefined && styles.chipActive]} onPress={() => setWarehouseId(undefined)}>
            <Text style={[styles.chipText, warehouseId === undefined && styles.chipTextActive]}>All warehouses</Text>
          </TouchableOpacity>
          {warehouses.map((w) => (
            <TouchableOpacity key={w.id} style={[styles.chip, warehouseId === w.id && styles.chipActive]} onPress={() => setWarehouseId(w.id)}>
              <Text style={[styles.chipText, warehouseId === w.id && styles.chipTextActive]}>{w.code}</Text>
            </TouchableOpacity>
          ))}
        </ScrollView>
      )}

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
            <Card onPress={() => router.push(`/inward-detail/${item.id}`)} style={styles.card}>
              <View style={styles.cardHeader}>
                <View style={styles.referenceWrap}>
                  <View style={styles.iconBox}><AppIcon name="document-text-outline" size={17} color={colors.primary} /></View>
                  <Text style={styles.reference} numberOfLines={1}>{item.referenceNumber}</Text>
                </View>
                <StatusBadge label={allocationStatusLabel(item.status)} tone={allocationStatusTone(item.status)} />
              </View>
              <View style={styles.metaRow}>
                <Text style={styles.meta}>{formatDateTime(item.createdAt)}</Text>
                <Text style={styles.meta}>{item.lineCount} lines</Text>
                <Text style={styles.metaStrong}>{item.totalQuantityBoxes} boxes</Text>
              </View>
              {item.vehicleNumber ? <Text style={styles.vehicle}>Vehicle {item.vehicleNumber}</Text> : null}
            </Card>
          )}
          ListEmptyComponent={<EmptyState icon="cube-outline" title="No pending GRNs" subtitle="New inward records will show up here." />}
        />
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  warehouseRow: { marginTop: spacing.sm, marginBottom: spacing.xs, maxHeight: 40 },
  chip: { paddingHorizontal: spacing.md, paddingVertical: 8, borderRadius: 16, backgroundColor: colors.surface, borderWidth: 1, borderColor: colors.border },
  chipActive: { backgroundColor: colors.primarySoft, borderColor: colors.primary },
  chipText: { ...typography.caption, color: colors.textSecondary, fontWeight: "700" },
  chipTextActive: { color: colors.primaryText },
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
  metaRow: { flexDirection: "row", alignItems: "center", flexWrap: "wrap", gap: spacing.md, marginTop: spacing.sm, paddingLeft: 40 },
  meta: { ...typography.caption, color: colors.textSecondary },
  metaStrong: { ...typography.caption, color: colors.textPrimary, fontWeight: "800" },
  vehicle: { ...typography.caption, color: colors.textSecondary, marginTop: spacing.xs, paddingLeft: 40 },
});
