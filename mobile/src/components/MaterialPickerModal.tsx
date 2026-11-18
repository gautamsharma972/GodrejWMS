import { useEffect, useState } from "react";
import { FlatList, StyleSheet, Text, TouchableOpacity } from "react-native";
import { searchMaterials } from "../api/endpoints";
import { FullScreenModal } from "./ui/FullScreenModal";
import { LoadingView } from "./ui/LoadingView";
import { SearchField } from "./ui/SearchField";
import { colors, spacing, typography } from "../theme";
import type { MaterialDto, PaginatedList } from "../types/api";

interface Props {
  visible: boolean;
  token: string;
  onClose: () => void;
  onSelect: (material: MaterialDto) => void;
  /** Material search to use; defaults to the Inward.Create search. Supervisors pass their own (Inward.Confirm) search. */
  searchFn?: (token: string, search: string, pageNumber?: number, pageSize?: number) => Promise<PaginatedList<MaterialDto>>;
}

export function MaterialPickerModal({ visible, token, onClose, onSelect, searchFn = searchMaterials }: Props) {
  const [search, setSearch] = useState("");
  const [results, setResults] = useState<MaterialDto[]>([]);
  const [isLoading, setIsLoading] = useState(false);

  useEffect(() => {
    if (!visible) return;
    let cancelled = false;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- intentional reset each time the modal opens/closes
    setIsLoading(true);
    const timer = setTimeout(async () => {
      try {
        const page = await searchFn(token, search, 1, 20);
        if (!cancelled) setResults(page.items);
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    }, 250);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [visible, search, token, searchFn]);

  return (
    <FullScreenModal visible={visible} title="Select material" onClose={onClose}>
      <SearchField placeholder="Search by material code or description" value={search} onChangeText={setSearch} autoFocus />
      {isLoading && <LoadingView label="Searching..." />}
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
    </FullScreenModal>
  );
}

const styles = StyleSheet.create({
  row: { paddingVertical: spacing.md, borderBottomWidth: 1, borderBottomColor: colors.border },
  rowCode: { ...typography.subtitle, color: colors.textPrimary },
  rowDescription: { fontSize: 13, color: colors.textSecondary, marginTop: 2 },
  empty: { textAlign: "center", color: colors.textMuted, marginTop: spacing.xxxl },
});
