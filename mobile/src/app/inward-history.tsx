import { useFocusEffect, useRouter } from "expo-router";
import { useCallback, useEffect, useState } from "react";
import { FlatList, RefreshControl, StyleSheet, Text, View } from "react-native";
import { getInwardHistory } from "../api/endpoints";
import { useAuth } from "../auth/AuthContext";
import { Card } from "../components/ui/Card";
import { AppIcon } from "../components/ui/AppIcon";
import { EmptyState } from "../components/ui/EmptyState";
import { SkeletonList } from "../components/ui/Skeleton";
import { Screen } from "../components/ui/Screen";
import { SearchField } from "../components/ui/SearchField";
import { StatusBadge } from "../components/ui/StatusBadge";
import { colors, spacing, typography } from "../theme";
import type { InwardHistoryDto } from "../types/api";
import { allocationStatusLabel, allocationStatusTone, formatDateTime } from "../utils/status";

export default function InwardHistoryScreen() {
  const { token } = useAuth();
  const router = useRouter();
  const [search, setSearch] = useState("");
  const [items, setItems] = useState<InwardHistoryDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);

  const load = useCallback(
    async (refreshing = false) => {
      if (!token) return;
      if (refreshing) setIsRefreshing(true);
      else setIsLoading(true);
      try {
        const page = await getInwardHistory(token, { search: search.trim() || undefined, pageSize: 50 });
        setItems(page.items);
      } finally {
        if (refreshing) setIsRefreshing(false);
        else setIsLoading(false);
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
      <View style={styles.introRow}>
        <View>
          <Text style={styles.eyebrow}>GOODS RECEIPT</Text>
          <Text style={styles.introText}>Pending GRNs</Text>
        </View>
        <View style={styles.countPill}>
          <Text style={styles.countText}>{items.length}</Text>
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
  introRow: { flexDirection: "row", alignItems: "center", justifyContent: "space-between", marginBottom: spacing.md },
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
