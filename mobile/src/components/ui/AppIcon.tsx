import { Ionicons, MaterialCommunityIcons } from "@expo/vector-icons";

type Family = "ionicons" | "material";

interface Props {
  name: keyof typeof Ionicons.glyphMap | keyof typeof MaterialCommunityIcons.glyphMap;
  family?: Family;
  size?: number;
  color?: string;
}

/** Thin wrapper so the rest of the app picks an icon by name without importing a specific icon set everywhere. */
export function AppIcon({ name, family = "ionicons", size = 20, color }: Props) {
  if (family === "material") {
    return <MaterialCommunityIcons name={name as keyof typeof MaterialCommunityIcons.glyphMap} size={size} color={color} />;
  }
  return <Ionicons name={name as keyof typeof Ionicons.glyphMap} size={size} color={color} />;
}
