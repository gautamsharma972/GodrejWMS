import { FlatList, Modal, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import type { WarehouseOption } from "../types/api";

interface Props {
  visible: boolean;
  warehouses: WarehouseOption[];
  onClose: () => void;
  onSelect: (warehouse: WarehouseOption) => void;
}

export function WarehousePickerModal({ visible, warehouses, onClose, onSelect }: Props) {
  return (
    <Modal visible={visible} animationType="slide" transparent onRequestClose={onClose}>
      <TouchableOpacity style={styles.backdrop} activeOpacity={1} onPress={onClose}>
        <View style={styles.sheet}>
          <Text style={styles.title}>Select warehouse</Text>
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
        </View>
      </TouchableOpacity>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: { flex: 1, backgroundColor: "rgba(15,23,42,0.4)", justifyContent: "flex-end" },
  sheet: { backgroundColor: "#fff", borderTopLeftRadius: 16, borderTopRightRadius: 16, padding: 20, maxHeight: "60%" },
  title: { fontSize: 16, fontWeight: "700", color: "#0f172a", marginBottom: 12 },
  row: { paddingVertical: 14, borderBottomWidth: 1, borderBottomColor: "#f1f5f9" },
  rowText: { fontSize: 15, color: "#0f172a" },
  empty: { color: "#94a3b8", paddingVertical: 20, textAlign: "center" },
});
