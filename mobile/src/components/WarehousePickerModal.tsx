import { FlatList, StyleSheet, Text, TouchableOpacity } from "react-native";
import { BottomSheet } from "./ui/BottomSheet";
import { colors, typography } from "../theme";
import type { WarehouseOption } from "../types/api";

interface Props {
  visible: boolean;
  warehouses: WarehouseOption[];
  onClose: () => void;
  onSelect: (warehouse: WarehouseOption) => void;
}

export function WarehousePickerModal({ visible, warehouses, onClose, onSelect }: Props) {
  return (
    <BottomSheet visible={visible} title="Select warehouse" onClose={onClose} maxHeightPercent={60}>
      <FlatList
        data={warehouses}
        keyExtractor={(item) => String(item.id)}
        renderItem={({ item }) => (
          <TouchableOpacity style={styles.row} onPress={() => onSelect(item)}>
            <Text style={styles.rowText}>
              {item.name} ({item.code})
            </Text>
          </TouchableOpacity>
        )}
        ListEmptyComponent={<Text style={styles.empty}>No warehouses assigned to your account.</Text>}
      />
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  row: { paddingVertical: 14, borderBottomWidth: 1, borderBottomColor: colors.border },
  rowText: { ...typography.body, fontSize: 15, color: colors.textPrimary },
  empty: { ...typography.body, color: colors.textMuted, paddingVertical: 20, textAlign: "center" },
});
