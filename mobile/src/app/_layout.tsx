import { Stack, useRouter, useSegments } from "expo-router";
import { useEffect } from "react";
import { ActivityIndicator, View } from "react-native";
import { AuthProvider, useAuth } from "../auth/AuthContext";

function RootNavigator() {
  const { isLoading, token } = useAuth();
  const segments = useSegments();
  const router = useRouter();

  useEffect(() => {
    if (isLoading) return;
    const onLoginScreen = segments[0] === "login";
    if (!token && !onLoginScreen) {
      router.replace("/login");
    } else if (token && onLoginScreen) {
      router.replace("/");
    }
  }, [isLoading, token, segments, router]);

  if (isLoading) {
    return (
      <View style={{ flex: 1, alignItems: "center", justifyContent: "center", backgroundColor: "#f1f5f9" }}>
        <ActivityIndicator size="large" color="#2563eb" />
      </View>
    );
  }

  return (
    <Stack screenOptions={{ headerStyle: { backgroundColor: "#2563eb" }, headerTintColor: "#fff", headerTitleStyle: { fontWeight: "600" } }}>
      <Stack.Screen name="login" options={{ headerShown: false }} />
      <Stack.Screen name="index" options={{ title: "WMS Pickers" }} />
      <Stack.Screen name="inward-entry" options={{ title: "Record Inward" }} />
      <Stack.Screen name="inward-result" options={{ title: "Allocation Result" }} />
      <Stack.Screen name="inward-history" options={{ title: "Inward History" }} />
      <Stack.Screen name="inward-detail/[id]" options={{ title: "GRN Detail" }} />
      <Stack.Screen name="inward-putaway-queue" options={{ title: "Inward Putaway Queue" }} />
      <Stack.Screen name="inward-putaway/[id]" options={{ title: "Confirm Putaway" }} />
      <Stack.Screen name="pullout-queue" options={{ title: "Pullout Queue" }} />
      <Stack.Screen name="pullout/[id]" options={{ title: "Confirm Pullout" }} />
    </Stack>
  );
}

export default function RootLayout() {
  return (
    <AuthProvider>
      <RootNavigator />
    </AuthProvider>
  );
}
