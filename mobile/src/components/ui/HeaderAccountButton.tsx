import { usePathname, useRouter } from "expo-router";
import { StyleSheet, Text, TouchableOpacity } from "react-native";
import { useAuth } from "../../auth/AuthContext";
import { colors, radius } from "../../theme";
import { lightTap } from "../../utils/haptics";

export function HeaderAccountButton() {
  const router = useRouter();
  const pathname = usePathname();
  const { user } = useAuth();

  const initials = (user?.fullName ?? "?")
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join("");

  return (
    <TouchableOpacity
      style={styles.avatar}
      activeOpacity={0.72}
      accessibilityRole="button"
      accessibilityLabel="Open account"
      hitSlop={8}
      onPress={() => {
        lightTap();
        router.push({ pathname: "/account", params: { back: pathname } } as never);
      }}
    >
      <Text style={styles.avatarText}>{initials}</Text>
    </TouchableOpacity>
  );
}

const styles = StyleSheet.create({
  avatar: {
    width: 34,
    height: 34,
    borderRadius: radius.pill,
    backgroundColor: colors.primarySoft,
    borderWidth: 1,
    borderColor: "#c7d2fe",
    alignItems: "center",
    justifyContent: "center",
    marginRight: 4,
  },
  avatarText: { color: colors.primaryText, fontWeight: "800", fontSize: 12 },
});
