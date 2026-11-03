import { useRouter } from "expo-router";
import { useCallback, useEffect, useState } from "react";
import { ActivityIndicator, FlatList, RefreshControl, StyleSheet, Text, TextInput, TouchableOpacity, View } from "react-native";
import { getPulloutQueue } from "../api/endpoints";
import { useAuth } from "../auth/AuthContext";
import type { PulloutHistoryDto } from "../types/api";
import { allocationStatusColor, allocationStatusLabel } from "../utils/status";

export default function PulloutQueueScreen() {
  const { token } = useAuth();
  const router = useRouter();
  const [search, setSearch] = useState("");
  const [items, setItems] = useState<PulloutHistoryDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);

  const load = useCallback(
    async (refreshing = false) => {
      if (!token) return;
      refreshing ? setIsRefreshing(true) : setIsLoading(true);
      try {
        const page = await getPulloutQueue(token, { search: search.trim() || undefined, pageSize: 50 });
        setItems(page.items);
      } finally {
        refreshing ? setIsRefreshing(false) : setIsLoading(false);
      }
    },
    [token, search],
  );

  useEffect(() => {
    const timer = setTimeout(() => load(), 300);
    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search, token]);

  return (
    <View style={styles.screen}>
      <TextInput
        style={styles.search}
        placeholder="Search reference, vehicle, destination, material"
        value={search}
        onChangeText={setSearch}
      />

      {isLoading ? (
        <ActivityIndicator style={{ marginTop: 40 }} color="#2563eb" />
      ) : (
        <FlatList
          data={items}
          keyExtractor={(item) => String(item.id)}
          refreshControl={<RefreshControl refreshing={isRefreshing} onRefresh={() => load(true)} />}
          contentContainerStyle={{ paddingBottom: 24 }}
          renderItem={({ item }) => (
            <TouchableOpacity style={styles.card} onPress={() => router.push(`/pullout/${item.id}`)}>
              <View style={styles.cardHeader}>
                <Text style={styles.reference}>{item.referenceNumber}</Text>
                <View style={[styles.statusPill, { backgroundColor: allocationStatusColor(item.status) }]}>
                  <Text style={styles.statusPillText}>{allocationStatusLabel(item.status)}</Text>
                </View>
              </View>
              <Text style={styles.meta}>{new Date(item.createdAt).toLocaleString()}</Text>
              <Text style={styles.meta}>
                {item.lineCount} line(s) · {item.totalQuantityBoxes} boxes
                {item.destination ? ` · ${item.destination}` : ""}
                {item.vehicleNumber ? ` · ${item.vehicleNumber}` : ""}
              </Text>
            </TouchableOpacity>
          )}
          ListEmptyComponent={<Text style={styles.empty}>No pending pullouts.</Text>}
        />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: "#f1f5f9", padding: 16 },
  search: { borderWidth: 1, borderColor: "#cbd5e1", borderRadius: 10, paddingHorizontal: 14, paddingVertical: 10, fontSize: 14, backgroundColor: "#fff", marginBottom: 12 },
  card: { backgroundColor: "#fff", borderRadius: 12, padding: 14, marginBottom: 10, borderWidth: 1, borderColor: "#e2e8f0" },
  cardHeader: { flexDirection: "row", justifyContent: "space-between", alignItems: "center" },
  reference: { fontSize: 15, fontWeight: "700", color: "#0f172a" },
  statusPill: { paddingHorizontal: 10, paddingVertical: 4, borderRadius: 999 },
  statusPillText: { color: "#fff", fontSize: 11, fontWeight: "700" },
  meta: { fontSize: 12, color: "#64748b", marginTop: 4 },
  empty: { textAlign: "center", color: "#94a3b8", marginTop: 40 },
});
