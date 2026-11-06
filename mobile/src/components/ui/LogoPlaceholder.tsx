import { Image, StyleSheet, View, type ImageSourcePropType } from "react-native";
import { radius } from "../../theme";
import { AppIcon } from "./AppIcon";

interface Props {
  /** Pass the real logo asset here later (e.g. `require("../../assets/logo.png")`) - until then this
   * renders a placeholder frame so it's obvious where the brand mark belongs. */
  source?: ImageSourcePropType;
  size?: number;
}

export function LogoPlaceholder({ source, size = 92 }: Props) {
  const box = { width: size, height: size, borderRadius: size / 2 };
  if (source) {
    return <Image source={source} style={[styles.image, box]} resizeMode="contain" />;
  }
  return (
    <View style={[styles.placeholder, box]}>
      <AppIcon name="image-outline" size={size * 0.4} color="rgba(255,255,255,0.65)" />
    </View>
  );
}

const styles = StyleSheet.create({
  placeholder: {
    borderWidth: 1.5,
    borderStyle: "dashed",
    borderColor: "rgba(255,255,255,0.35)",
    backgroundColor: "rgba(255,255,255,0.08)",
    alignItems: "center",
    justifyContent: "center",
  },
  image: {
    backgroundColor: "rgba(255,255,255,0.08)",
    borderRadius: radius.xl,
  },
});
