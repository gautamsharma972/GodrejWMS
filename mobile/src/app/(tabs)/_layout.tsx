import { Tabs } from "expo-router";
import { AppIcon } from "../../components/ui/AppIcon";
import { colors } from "../../theme";

/** Two top-level destinations (Home dashboard, Account/sign-out) - everything else (queues, detail
 * screens, entry forms) is pushed on top of this as a plain stack screen from the root layout, so
 * the tab bar naturally disappears once the user drills in. */
export default function TabsLayout() {
  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: colors.primary,
        tabBarInactiveTintColor: colors.textMuted,
        tabBarStyle: {
          backgroundColor: colors.surface,
          borderTopColor: colors.border,
          borderTopWidth: 1,
          height: 64,
          paddingBottom: 9,
          paddingTop: 7,
        },
        tabBarLabelStyle: { fontSize: 11, fontWeight: "700" },
      }}
    >
      <Tabs.Screen
        name="index"
        options={{
          title: "Home",
          tabBarIcon: ({ color, focused }) => <AppIcon name={focused ? "grid" : "grid-outline"} color={color as string} size={22} />,
        }}
      />
      <Tabs.Screen
        name="account"
        options={{
          title: "Account",
          tabBarIcon: ({ color, focused }) => (
            <AppIcon name={focused ? "person-circle" : "person-circle-outline"} color={color as string} size={23} />
          ),
        }}
      />
    </Tabs>
  );
}
