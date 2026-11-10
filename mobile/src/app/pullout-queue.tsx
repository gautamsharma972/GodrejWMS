import { useFocusEffect, useRouter } from "expo-router";
import { useCallback, useEffect, useState } from "react";
import { FlatList, RefreshControl, StyleSheet, Text, View } from "react-native";
import { getPickerPulloutQueue, getPulloutQueue } from "../api/endpoints";
import { useAuth } from "../auth/AuthContext";
import { Card } from "../components/ui/Card";
import { EmptyState } from "../components/ui/EmptyState";
import { RefreshButton } from "../components/ui/RefreshButton";
import { SkeletonList } from "../components/ui/Skeleton";
import { Screen } from "../components/ui/Screen";
import { SearchField } from "../components/ui/SearchField";
import { StatusBadge } from "../components/ui/StatusBadge";
import { colors, spacing, typography } from "../theme";
import type { PulloutHistoryDto } from "../types/api";
import { allocationStatusLabel, allocationStatusTone, formatDateTime } from "../utils/status";

interface PulloutQueueItem {
  id: number;
  referenceNumber: string;
  createdAt: string;
  summary: string;
  badge: { label: string; tone: ReturnType<typeof allocationStatusTone> | "danger" | "warning" };
}

export default function PulloutQueueScreen() {
  const { token, permissions } = useAuth();
  const router = useRouter();
  // A picker works the posted pullouts' picks; a supervisor without the pick permission still sees the old pending ones.
  const isPicker = permissions.includes("Pullout.Pick");
  const [search, setSearch] = useState("");
  const [items, setItems] = useState<PulloutQueueItem[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);

  const load = useCallback(
    async (refreshing = false) => {
      if (!token) return;
      if (refreshing) {
        setIsRefreshing(true);
      } else {
        setIsLoading(true);
      }
      try {
        const query = { search: search.trim() || undefined, pageSize: 50 };
        if (isPicker) {
          const page = await getPickerPulloutQueue(token, query);
          setItems(page.items.map((p) => ({
            id: p.id,
            referenceNumber: p.referenceNumber,
            createdAt: p.createdAt,
            summary: `${p.pickedCount} of ${p.pickCount} picked · ${p.totalBoxes} boxes${p.destination ? ` · ${p.destination}` : ""}${p.virtualPickCount > 0 ? ` · ${p.virtualPickCount} virtual` : ""}`,
            badge: { label: `${p.pickedCount}/${p.pickCount} picked`, tone: p.pickedCount === 0 ? "danger" : "warning" },
          })));
        } else {
          const page = await getPulloutQueue(token, query);
          setItems(page.items.map((p: PulloutHistoryDto) => ({
            id: p.id,
            referenceNumber: p.referenceNumber,
            createdAt: p.createdAt,
            summary: `${p.lineCount} line(s) · ${p.totalQuantityBoxes} boxes${p.destination ? ` · ${p.destination}` : ""}${p.vehicleNumber ? ` · ${p.vehicleNumber}` : ""}`,
            badge: { label: allocationStatusLabel(p.status), tone: allocationStatusTone(p.status) },
          })));
        }
      } finally {
        if (refreshing) {
          setIsRefreshing(false);
        } else {
          setIsLoading(false);
        }
      }
    },
    [token, search, isPicker],
  );

  useEffect(() => {
    const timer = setTimeout(() => load(), 300);
    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search, token]);

  useFocusEffect(
    useCallback(() => {
      load(true);
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [token]),
  );

  return (
    <Screen padded>
      <View style={styles.searchRow}>
        <SearchField
          containerStyle={styles.searchField}
          placeholder="Search reference, vehicle, destination, material"
          value={search}
          onChangeText={setSearch}
        />
        <RefreshButton onPress={() => load(true)} refreshing={isRefreshing} />
      </View>

      {isLoading ? (
        <SkeletonList />
      ) : (
        <FlatList
          data={items}
          keyExtractor={(item) => String(item.id)}
          refreshControl={<RefreshControl refreshing={isRefreshing} onRefresh={() => load(true)} tintColor={colors.primary} />}
          contentContainerStyle={{ paddingBottom: 24 }}
          renderItem={({ item }) => (
            <Card onPress={() => router.push(`/pullout/${item.id}`)} style={styles.card}>
              <View style={styles.cardHeader}>
                <Text style={styles.reference}>{item.referenceNumber}</Text>
                <StatusBadge label={item.badge.label} tone={item.badge.tone} />
              </View>
              <Text style={styles.meta}>{formatDateTime(item.createdAt)}</Text>
              <Text style={styles.meta}>{item.summary}</Text>
            </Card>
          )}
          ListEmptyComponent={<EmptyState icon="car-outline" title={isPicker ? "No pick-lists to confirm" : "No pending pullouts"} subtitle="New outward dispatches will show up here." />}
        />
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  searchRow: { flexDirection: "row", alignItems: "center", gap: spacing.sm, marginBottom: spacing.md },
  searchField: { flex: 1, marginBottom: 0 },
  card: { marginBottom: spacing.md },
  cardHeader: { flexDirection: "row", justifyContent: "space-between", alignItems: "center" },
  reference: { ...typography.subtitle, color: colors.textPrimary },
  meta: { ...typography.caption, color: colors.textSecondary, marginTop: spacing.xs },
});
