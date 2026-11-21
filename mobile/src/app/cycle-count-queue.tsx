import { useFocusEffect, useRouter } from "expo-router";
import { useCallback, useState } from "react";
import { FlatList, RefreshControl, StyleSheet, Text, View } from "react-native";
import { ApiError } from "../api/client";
import { getMyCycleCountBatches, startCycleCount } from "../api/endpoints";
import { useAuth } from "../auth/AuthContext";
import { Card } from "../components/ui/Card";
import { EmptyState } from "../components/ui/EmptyState";
import { SkeletonList } from "../components/ui/Skeleton";
import { Screen } from "../components/ui/Screen";
import { StatusBadge } from "../components/ui/StatusBadge";
import { colors, spacing, typography } from "../theme";
import { CycleCountBatchStatus, CycleCountRackStatus, type CycleCountBatchDto } from "../types/api";
import { formatDateTime } from "../utils/status";

function statusLabel(status: number): string {
  switch (status) {
    case CycleCountBatchStatus.InProgress: return "In progress";
    case CycleCountBatchStatus.Extended: return "Extended";
    case CycleCountBatchStatus.PartiallyClosed: return "Partially closed";
    default: return "Scheduled";
  }
}

/** A 403 means the signed-in role does not hold the Cycle Count permission (any change made after sign-in only applies after signing in again). */
function friendly(e: unknown, fallback: string): string {
  if (e instanceof ApiError && e.status === 403) {
    return "You do not have permission to count stock. Ask your administrator to give your role the Cycle Count permission, then sign out and sign in again.";
  }
  return e instanceof Error ? e.message : fallback;
}

export default function CycleCountQueueScreen() {
  const { token } = useAuth();
  const router = useRouter();
  const [items, setItems] = useState<CycleCountBatchDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [opening, setOpening] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(
    async (refreshing = false) => {
      if (!token) return;
      if (refreshing) setIsRefreshing(true);
      else setIsLoading(true);
      try {
        setItems(await getMyCycleCountBatches(token));
        setError(null);
      } catch (e) {
        setError(friendly(e, "Unable to load cycle counts."));
      } finally {
        setIsRefreshing(false);
        setIsLoading(false);
      }
    },
    [token],
  );

  useFocusEffect(
    useCallback(() => {
      void load();
    }, [load]),
  );

  const open = async (batch: CycleCountBatchDto) => {
    if (!token || opening !== null) return;
    setOpening(batch.id);
    try {
      // The first counter to open a batch takes the system-stock snapshot (the count cards); later ones just continue.
      await startCycleCount(token, batch.id);
      router.push({ pathname: "/cycle-count/[id]", params: { id: String(batch.id) } });
    } catch (e) {
      setError(friendly(e, "Unable to open the cycle count."));
    } finally {
      setOpening(null);
    }
  };

  return (
    <Screen padded>
      {error ? <Text style={styles.error}>{error}</Text> : null}
      {isLoading ? (
        <SkeletonList />
      ) : (
        <FlatList
          data={items}
          keyExtractor={(item) => String(item.id)}
          refreshControl={<RefreshControl refreshing={isRefreshing} onRefresh={() => load(true)} />}
          ListEmptyComponent={<EmptyState title="No active cycle count" subtitle="Nothing is scheduled for your warehouses right now." />}
          contentContainerStyle={{ paddingBottom: spacing.xl }}
          renderItem={({ item }) => (
            <Card onPress={() => open(item)} disabled={opening !== null} style={styles.card}>
              <View style={styles.row}>
                <Text style={styles.title}>{item.batchNumber}</Text>
                <StatusBadge label={statusLabel(item.status)} tone="info" />
              </View>
              <Text style={styles.meta}>{item.warehouseName}</Text>
              <Text style={styles.meta}>
                {formatDateTime(item.scheduleFrom)} - {formatDateTime(item.scheduleTo)}
              </Text>
              <View style={styles.track}>
                <View style={[styles.fill, { width: `${Math.min(100, item.completionPercent)}%` }]} />
              </View>
              <Text style={styles.meta}>
                {item.completionPercent.toFixed(1)}% done · {item.pendingCards} pending card(s)
              </Text>
              <Text style={styles.meta}>
                Racks: {item.racks.filter((r) => r.status === CycleCountRackStatus.Open).map((r) => r.rackCode).join(", ") || "-"}
              </Text>
              {opening === item.id ? <Text style={styles.meta}>Opening...</Text> : null}
            </Card>
          )}
        />
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  card: { marginBottom: spacing.md },
  row: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginBottom: spacing.xs },
  title: { ...typography.subtitle, color: colors.textPrimary },
  meta: { ...typography.caption, color: colors.textSecondary, marginTop: 2 },
  track: { height: 8, borderRadius: 4, backgroundColor: colors.surfaceAlt, overflow: "hidden", marginTop: spacing.sm },
  fill: { height: 8, backgroundColor: colors.success },
  error: { color: colors.dangerText, marginBottom: spacing.sm },
});
