import { useEffect, useState } from "react";
import { ActivityIndicator, FlatList, Modal, StyleSheet, Text, TextInput, TouchableOpacity, View } from "react-native";
import { searchMaterials } from "../api/endpoints";
import type { MaterialDto } from "../types/api";

interface Props {
  visible: boolean;
  token: string;
  onClose: () => void;
  onSelect: (material: MaterialDto) => void;
}

export function MaterialPickerModal({ visible, token, onClose, onSelect }: Props) {
  const [search, setSearch] = useState("");
  const [results, setResults] = useState<MaterialDto[]>([]);
  const [isLoading, setIsLoading] = useState(false);

  useEffect(() => {
    if (!visible) return;
    let cancelled = false;
    setIsLoading(true);
    const timer = setTimeout(async () => {
      try {
        const page = await searchMaterials(token, search, 1, 20);
        if (!cancelled) setResults(page.items);
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    }, 250);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [visible, search, token]);

  return (
    <Modal visible={visible} animationType="slide" onRequestClose={onClose}>
      <View style={styles.container}>
        <View style={styles.header}>
          <Text style={styles.title}>Select material</Text>
          <TouchableOpacity onPress={onClose}>
            <Text style={styles.close}>Close</Text>
          </TouchableOpacity>
        </View>
        <TextInput
          style={styles.search}
          placeholder="Search by material code or description"
          value={search}
          onChangeText={setSearch}
          autoFocus
        />
        {isLoading && <ActivityIndicator style={styles.loading} color="#2563eb" />}
        <FlatList
          data={results}
          keyExtractor={(item) => String(item.id)}
          renderItem={({ item }) => (
            <TouchableOpacity
              style={styles.row}
              onPress={() => {
                onSelect(item);
                setSearch("");
              }}
            >
              <Text style={styles.rowCode}>{item.materialNumber}</Text>
              <Text style={styles.rowDescription}>{item.description}</Text>
            </TouchableOpacity>
          )}
          ListEmptyComponent={!isLoading ? <Text style={styles.empty}>No materials found.</Text> : undefined}
        />
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: "#fff", paddingTop: 56, paddingHorizontal: 16 },
  header: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginBottom: 12 },
  title: { fontSize: 18, fontWeight: "700", color: "#0f172a" },
  close: { color: "#2563eb", fontWeight: "600" },
  search: { borderWidth: 1, borderColor: "#cbd5e1", borderRadius: 10, paddingHorizontal: 14, paddingVertical: 10, fontSize: 15, marginBottom: 8 },
  loading: { marginVertical: 8 },
  row: { paddingVertical: 12, borderBottomWidth: 1, borderBottomColor: "#f1f5f9" },
  rowCode: { fontSize: 15, fontWeight: "700", color: "#0f172a" },
  rowDescription: { fontSize: 13, color: "#64748b", marginTop: 2 },
  empty: { textAlign: "center", color: "#94a3b8", marginTop: 40 },
});
