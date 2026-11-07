import Constants from "expo-constants";
import { useLocalSearchParams, useRouter } from "expo-router";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { useAuth } from "../../auth/AuthContext";
import { AppIcon } from "../../components/ui/AppIcon";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { Screen } from "../../components/ui/Screen";
import { colors, radius, spacing, typography } from "../../theme";
import { lightTap } from "../../utils/haptics";

export default function AccountScreen() {
  const { user, logout } = useAuth();
  const router = useRouter();
  const { back } = useLocalSearchParams<{ back?: string }>();

  const initials = (user?.fullName ?? "?")
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join("");

  const appVersion = Constants.expoConfig?.version;

  return (
    <Screen edges={["top", "bottom"]} padded>
      <View style={styles.headerRow}>
        {back ? (
          <TouchableOpacity
            style={styles.backButton}
            activeOpacity={0.72}
            accessibilityRole="button"
            accessibilityLabel="Go back"
            hitSlop={8}
            onPress={() => {
              lightTap();
              router.push(back as never);
            }}
          >
            <AppIcon name="arrow-back" size={20} color={colors.textPrimary} />
          </TouchableOpacity>
        ) : null}
        <Text style={styles.heading}>Account</Text>
      </View>

      <Card style={styles.profileCard}>
        <View style={styles.avatar}>
          <Text style={styles.avatarText}>{initials}</Text>
        </View>
        <Text style={styles.name}>{user?.fullName ?? "Unknown"}</Text>
        <View style={styles.roleRow}>
          {(user?.roles ?? []).map((role) => (
            <View key={role} style={styles.rolePill}>
              <Text style={styles.rolePillText}>{role}</Text>
            </View>
          ))}
        </View>
      </Card>

      {appVersion && (
        <Card style={styles.infoCard}>
          <View style={styles.infoRow}>
            <Text style={styles.infoLabel}>App version</Text>
            <Text style={styles.infoValue}>{appVersion}</Text>
          </View>
        </Card>
      )}

      <Button
        title="Sign out"
        variant="danger"
        onPress={() => {
          lightTap();
          logout();
        }}
        style={styles.signOut}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  headerRow: { flexDirection: "row", alignItems: "center", marginBottom: spacing.lg },
  backButton: {
    width: 34,
    height: 34,
    borderRadius: radius.pill,
    backgroundColor: colors.neutralSoft,
    alignItems: "center",
    justifyContent: "center",
    marginRight: spacing.sm,
  },
  heading: { ...typography.display, fontSize: 24, color: colors.textPrimary },
  profileCard: { alignItems: "center", paddingVertical: spacing.xxl },
  avatar: {
    width: 76,
    height: 76,
    borderRadius: radius.pill,
    backgroundColor: colors.primary,
    alignItems: "center",
    justifyContent: "center",
    marginBottom: spacing.md,
  },
  avatarText: { color: colors.textOnPrimary, fontWeight: "800", fontSize: 26 },
  name: { ...typography.title, color: colors.textPrimary },
  roleRow: { flexDirection: "row", flexWrap: "wrap", justifyContent: "center", gap: spacing.sm, marginTop: spacing.md },
  rolePill: { backgroundColor: colors.primarySoft, borderRadius: radius.pill, paddingHorizontal: spacing.md, paddingVertical: 5 },
  rolePillText: { color: colors.primaryText, fontWeight: "700", fontSize: 12 },
  infoCard: { marginTop: spacing.lg },
  infoRow: { flexDirection: "row", justifyContent: "space-between" },
  infoLabel: { ...typography.body, color: colors.textSecondary },
  infoValue: { ...typography.bodyStrong, color: colors.textPrimary },
  signOut: { marginTop: spacing.xxl },
});
