import { FlatList, StyleSheet, Text, TouchableOpacity } from "react-native";
import { BottomSheet } from "./BottomSheet";
import { colors, spacing, typography } from "../../theme";

export interface SimpleOption {
  value: number;
  label: string;
}

interface Props {
  visible: boolean;
  title: string;
  options: SimpleOption[];
  onClose: () => void;
  onSelect: (value: number) => void;
}

/** Generic "pick one value from a short list" bottom sheet - used for PKM year/month pickers. */
export function OptionListSheet({ visible, title, options, onClose, onSelect }: Props) {
  return (
    <BottomSheet visible={visible} title={title} onClose={onClose} maxHeightPercent={55}>
      <FlatList
        data={options}
        keyExtractor={(item) => String(item.value)}
        numColumns={3}
        renderItem={({ item }) => (
          <TouchableOpacity style={styles.chip} onPress={() => onSelect(item.value)}>
            <Text style={styles.chipText}>{item.label}</Text>
          </TouchableOpacity>
        )}
      />
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  chip: {
    flex: 1,
    margin: spacing.xs,
    paddingVertical: spacing.md,
    borderRadius: 10,
    backgroundColor: colors.surfaceAlt,
    borderWidth: 1,
    borderColor: colors.border,
    alignItems: "center",
  },
  chipText: { ...typography.bodyStrong, color: colors.textPrimary },
});
