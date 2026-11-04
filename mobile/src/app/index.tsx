import { useRouter } from "expo-router";
import { ScrollView, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";
import { useAuth } from "../auth/AuthContext";

export default function HomeScreen() {
  const router = useRouter();
  const { user, permissions, logout } = useAuth();

  const canPick = permissions.includes("Inward.Create");
  const canConfirmInward = permissions.includes("Inward.Confirm");
  const canConfirmPullout = permissions.includes("Pullout.Confirm");

  return (
    <SafeAreaView style={styles.screen} edges={["bottom"]}>
      <View style={styles.header}>
        <Text style={styles.greeting}>Welcome{user?.fullName ? `, ${user.fullName}` : ""}</Text>
        <Text style={styles.subtitle}>{user?.roles?.join(", ") ?? "Workspace"}</Text>
      </View>

      <ScrollView contentContainerStyle={{ flexGrow: 1 }}>
        {canPick && (
          <>
            <TouchableOpacity style={styles.card} onPress={() => router.push("/inward-entry")}>
              <Text style={styles.cardTitle}>Record inward</Text>
              <Text style={styles.cardSubtitle}>Enter lines manually or upload an Excel GRN</Text>
            </TouchableOpacity>

            <TouchableOpacity style={styles.card} onPress={() => router.push("/inward-history")}>
              <Text style={styles.cardTitle}>Inward history</Text>
              <Text style={styles.cardSubtitle}>View pending GRNs, assign PKM, download Excel</Text>
            </TouchableOpacity>
          </>
        )}

        {canConfirmInward && (
          <TouchableOpacity style={styles.card} onPress={() => router.push("/inward-putaway-queue")}>
            <Text style={styles.cardTitle}>Inward putaway queue</Text>
            <Text style={styles.cardSubtitle}>Change location and confirm putaway for pending GRNs</Text>
          </TouchableOpacity>
        )}

        {canConfirmPullout && (
          <TouchableOpacity style={styles.card} onPress={() => router.push("/pullout-queue")}>
            <Text style={styles.cardTitle}>Pullout queue</Text>
            <Text style={styles.cardSubtitle}>Change pick location and confirm outward pullouts</Text>
          </TouchableOpacity>
        )}
      </ScrollView>

      <TouchableOpacity style={styles.logout} onPress={logout}>
        <Text style={styles.logoutText}>Sign out</Text>
      </TouchableOpacity>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: "#f1f5f9", padding: 20 },
  header: { marginBottom: 24 },
  greeting: { fontSize: 20, fontWeight: "700", color: "#0f172a" },
  subtitle: { fontSize: 14, color: "#64748b", marginTop: 2 },
  card: { backgroundColor: "#fff", borderRadius: 14, padding: 20, marginBottom: 16, borderWidth: 1, borderColor: "#e2e8f0" },
  cardTitle: { fontSize: 17, fontWeight: "700", color: "#0f172a" },
  cardSubtitle: { fontSize: 13, color: "#64748b", marginTop: 6 },
  logout: { alignItems: "center", paddingVertical: 14 },
  logoutText: { color: "#dc2626", fontWeight: "600", fontSize: 15 },
});
