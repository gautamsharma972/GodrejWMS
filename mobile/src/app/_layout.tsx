import { Stack, useRouter, useSegments } from "expo-router";
import { useEffect } from "react";
import { StatusBar } from "expo-status-bar";
import { View } from "react-native";
import { AuthProvider, useAuth } from "../auth/AuthContext";
import { HeaderAccountButton } from "../components/ui/HeaderAccountButton";
import { LoadingView } from "../components/ui/LoadingView";
import { colors } from "../theme";

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
      <View style={{ flex: 1, backgroundColor: colors.background }}>
        <LoadingView />
      </View>
    );
  }

  return (
    <Stack
      screenOptions={{
        headerStyle: { backgroundColor: colors.surface },
        headerTintColor: colors.textPrimary,
        headerTitleStyle: { fontWeight: "800", fontSize: 17 },
        headerShadowVisible: false,
        contentStyle: { backgroundColor: colors.background },
        headerRight: () => <HeaderAccountButton />,
      }}
    >
      <Stack.Screen name="login" options={{ headerShown: false }} />
      <Stack.Screen name="(tabs)" options={{ headerShown: false }} />
      <Stack.Screen name="inward-entry" options={{ title: "Record Inward" }} />
      <Stack.Screen name="inward-result" options={{ title: "Allocation Result" }} />
      <Stack.Screen name="inward-history" options={{ title: "Inward History" }} />
      <Stack.Screen name="inward-detail/[id]" options={{ title: "GRN Detail" }} />
      <Stack.Screen name="picker-putaway-queue" options={{ title: "Picker Put-away" }} />
      <Stack.Screen name="picker-putaway/[id]" options={{ title: "Picker Put-away" }} />
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
      <StatusBar style="dark" />
      <RootNavigator />
    </AuthProvider>
  );
}
