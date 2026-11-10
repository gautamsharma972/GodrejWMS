import { useFocusEffect, useRouter } from "expo-router";
import { useCallback, useEffect, useState } from "react";
import { FlatList, RefreshControl, StyleSheet, Text, View } from "react-native";
import { getInwardHistory } from "../api/endpoints";
import { useAuth } from "../auth/AuthContext";
import { Card } from "../components/ui/Card";
import { EmptyState } from "../components/ui/EmptyState";
import { RefreshButton } from "../components/ui/RefreshButton";
import { Screen } from "../components/ui/Screen";
import { SkeletonList } from "../components/ui/Skeleton";
import { SearchField } from "../components/ui/SearchField";
import { StatusBadge } from "../components/ui/StatusBadge";
import { colors, spacing, typography } from "../theme";
import type { InwardHistoryDto } from "../types/api";
import { allocationStatusLabel, allocationStatusTone, formatDateTime } from "../utils/status";

export default function InwardPutawayQueueScreen() {
  const { token } = useAuth();
  const router = useRouter();
  const [search, setSearch] = useState("");
  const [items, setItems] = useState<InwardHistoryDto[]>([]);
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
        const page = await getInwardHistory(token, { search: search.trim() || undefined, pageSize: 50 });
        setItems(page.items);
      } finally {
        if (refreshing) {
          setIsRefreshing(false);
        } else {
          setIsLoading(false);
        }
      }
    },
    [token, search],
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
          placeholder="Search reference, vehicle, LR, material"
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
            <Card onPress={() => router.push(`/inward-putaway/${item.id}`)} style={styles.card}>
              <View style={styles.cardHeader}>
                <Text style={styles.reference}>{item.referenceNumber}</Text>
                <StatusBadge label={allocationStatusLabel(item.status)} tone={allocationStatusTone(item.status)} />
              </View>
              <Text style={styles.meta}>{formatDateTime(item.createdAt)}</Text>
              <Text style={styles.meta}>
                {item.lineCount} line(s) · {item.totalQuantityBoxes} boxes
                {item.vehicleNumber ? ` · ${item.vehicleNumber}` : ""}
              </Text>
            </Card>
          )}
          ListEmptyComponent={<EmptyState icon="checkmark-circle-outline" title="No pending GRNs" subtitle="Every GRN's put-away is confirmed." />}
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
