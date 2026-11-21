import { useMemo, useState } from "react";
import { FlatList, StyleSheet, Text, TouchableOpacity } from "react-native";
import { BottomSheet } from "./ui/BottomSheet";
import { SearchField } from "./ui/SearchField";
import { colors, spacing, typography } from "../theme";

export interface SearchableOption {
  value: number;
  label: string;
  hint?: string;
}

interface Props {
  visible: boolean;
  title: string;
  options: SearchableOption[];
  onClose: () => void;
  onSelect: (value: number) => void;
  placeholder?: string;
  /** Extra first row (for example "First location") that clears the choice; selecting it calls onSelect(-1). */
  clearLabel?: string;
}

/** "Pick one from a long list" bottom sheet with a search box (locations of a rack can run into hundreds). */
export function SearchableOptionSheet({ visible, title, options, onClose, onSelect, placeholder = "Search", clearLabel }: Props) {
  const [search, setSearch] = useState("");
  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    const list = term ? options.filter((o) => o.label.toLowerCase().includes(term) || (o.hint ?? "").toLowerCase().includes(term)) : options;
    return list.slice(0, 200);
  }, [options, search]);

  const close = () => {
    setSearch("");
    onClose();
  };
  const pick = (value: number) => {
    setSearch("");
    onSelect(value);
  };

  return (
    <BottomSheet visible={visible} title={title} onClose={close} maxHeightPercent={80}>
      <SearchField placeholder={placeholder} value={search} onChangeText={setSearch} autoCapitalize="characters" autoCorrect={false} />
      {clearLabel && !search ? (
        <TouchableOpacity style={styles.row} onPress={() => pick(-1)}>
          <Text style={styles.clear}>{clearLabel}</Text>
        </TouchableOpacity>
      ) : null}
      <FlatList
        data={filtered}
        keyExtractor={(item) => String(item.value)}
        keyboardShouldPersistTaps="handled"
        ListEmptyComponent={<Text style={styles.empty}>No match.</Text>}
        renderItem={({ item }) => (
          <TouchableOpacity style={styles.row} onPress={() => pick(item.value)}>
            <Text style={styles.label}>{item.label}</Text>
            {item.hint ? <Text style={styles.hint}>{item.hint}</Text> : null}
          </TouchableOpacity>
        )}
      />
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  row: { paddingVertical: spacing.md, borderBottomWidth: 1, borderBottomColor: colors.border },
  label: { ...typography.subtitle, color: colors.textPrimary },
  hint: { ...typography.caption, color: colors.textSecondary, marginTop: 2 },
  clear: { ...typography.subtitle, color: colors.primary },
  empty: { ...typography.body, color: colors.textSecondary, paddingVertical: spacing.lg, textAlign: "center" },
});
