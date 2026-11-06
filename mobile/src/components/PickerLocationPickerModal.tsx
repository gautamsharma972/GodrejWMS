import { useMemo, useState } from "react";
import { FlatList, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { AppIcon } from "./ui/AppIcon";
import { FullScreenModal } from "./ui/FullScreenModal";
import { SearchField } from "./ui/SearchField";
import { colors, radius, spacing, typography } from "../theme";
import type { AopLocationOptionDto } from "../types/api";

export const GIT_OPTION_ID = -1;
export const NO_RACK_OPTION_ID = -2;

export interface PickerLocationOption {
  id: number;
  locationCode: string;
}

interface Props {
  visible: boolean;
  options: PickerLocationOption[];
  onClose: () => void;
  onSelect: (option: PickerLocationOption) => void;
  /** Optional - when given, a "Scan" button appears next to the search field (this modal closes
   * itself first since the scanner is its own full-screen takeover). */
  onScanPress?: () => void;
}

/** Searchable location picker for the Picker Put-away flow - mirrors the Razor page's
 * SearchableLocationSelect, with GIT / NO RACK prepended as synthetic options (ids -1 / -2). */
export function PickerLocationPickerModal({ visible, options, onClose, onSelect, onScanPress }: Props) {
  const [search, setSearch] = useState("");

  const filtered = useMemo(() => {
    const term = search.trim().toUpperCase();
    if (!term) return options;
    return options.filter((option) => option.locationCode.toUpperCase().includes(term));
  }, [options, search]);

  return (
    <FullScreenModal visible={visible} title="Select location" onClose={onClose}>
      <View style={styles.searchRow}>
        <View style={{ flex: 1 }}>
          <SearchField placeholder="Search location, GIT or NO RACK" value={search} onChangeText={setSearch} autoFocus />
        </View>
        {onScanPress && (
          <TouchableOpacity
            style={styles.scanButton}
            onPress={() => {
              onClose();
              onScanPress();
            }}
            hitSlop={6}
          >
            <AppIcon name="scan-outline" size={20} color={colors.textOnPrimary} />
          </TouchableOpacity>
        )}
      </View>
      <FlatList
        data={filtered}
        keyExtractor={(item) => String(item.id)}
        renderItem={({ item }) => (
          <TouchableOpacity
            style={styles.row}
            onPress={() => {
              onSelect(item);
              setSearch("");
            }}
          >
            <Text style={[styles.rowText, item.id < 0 && styles.rowTextSpecial]}>{item.locationCode}</Text>
          </TouchableOpacity>
        )}
        ListEmptyComponent={<Text style={styles.empty}>No matching location.</Text>}
      />
    </FullScreenModal>
  );
}

export function buildPickerLocationOptions(locationOptions: AopLocationOptionDto[]): PickerLocationOption[] {
  return [
    { id: GIT_OPTION_ID, locationCode: "GIT" },
    { id: NO_RACK_OPTION_ID, locationCode: "NO RACK" },
    ...locationOptions.map((option) => ({ id: option.id, locationCode: option.locationCode })),
  ];
}

const styles = StyleSheet.create({
  searchRow: { flexDirection: "row", alignItems: "flex-start", gap: spacing.sm },
  scanButton: {
    width: 44,
    height: 44,
    borderRadius: radius.md,
    backgroundColor: colors.primary,
    alignItems: "center",
    justifyContent: "center",
    marginBottom: spacing.md,
  },
  row: { paddingVertical: spacing.md, borderBottomWidth: 1, borderBottomColor: colors.border },
  rowText: { ...typography.subtitle, color: colors.textPrimary },
  rowTextSpecial: { color: colors.primary },
  empty: { textAlign: "center", color: colors.textMuted, marginTop: spacing.xxxl },
});
