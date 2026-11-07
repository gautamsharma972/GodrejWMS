import { StyleSheet, TouchableOpacity } from "react-native";
import { colors, radius } from "../../theme";
import { lightTap } from "../../utils/haptics";
import { AppIcon } from "./AppIcon";

interface Props {
  onPress: () => void;
  refreshing?: boolean;
}

export function RefreshButton({ onPress, refreshing }: Props) {
  return (
    <TouchableOpacity
      style={styles.button}
      activeOpacity={0.7}
      accessibilityRole="button"
      accessibilityLabel="Refresh list"
      disabled={refreshing}
      onPress={() => {
        lightTap();
        onPress();
      }}
    >
      <AppIcon name="refresh-outline" size={18} color={refreshing ? colors.textMuted : colors.primary} />
    </TouchableOpacity>
  );
}

const styles = StyleSheet.create({
  button: {
    width: 38,
    height: 38,
    borderRadius: radius.pill,
    backgroundColor: colors.neutralSoft,
    alignItems: "center",
    justifyContent: "center",
  },
});
