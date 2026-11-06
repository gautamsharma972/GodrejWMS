import { LinearGradient } from "expo-linear-gradient";
import { StatusBar } from "expo-status-bar";
import { useEffect, useRef, useState } from "react";
import {
  Animated,
  Image,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from "react-native";
import { ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { AppIcon } from "../components/ui/AppIcon";
import { Button } from "../components/ui/Button";
import { FadeInUp } from "../components/ui/FadeInUp";
import { Screen } from "../components/ui/Screen";
import { colors, radius, shadow, spacing, typography } from "../theme";

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export default function LoginScreen() {
  const { login } = useAuth();
  const emailRef = useRef<TextInput>(null);
  const passwordRef = useRef<TextInput>(null);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [shake] = useState(() => new Animated.Value(0));

  useEffect(() => {
    if (!error) return;
    shake.setValue(0);
    Animated.sequence([
      Animated.timing(shake, { toValue: 1, duration: 55, useNativeDriver: true }),
      Animated.timing(shake, { toValue: -1, duration: 55, useNativeDriver: true }),
      Animated.timing(shake, { toValue: 1, duration: 55, useNativeDriver: true }),
      Animated.timing(shake, { toValue: -1, duration: 55, useNativeDriver: true }),
      Animated.timing(shake, { toValue: 0, duration: 55, useNativeDriver: true }),
    ]).start();
  }, [error, shake]);

  const handleSubmit = async () => {
    setError(null);
    if (!email.trim() || !EMAIL_PATTERN.test(email.trim())) {
      setError("Enter a valid email address.");
      return;
    }
    if (!password) {
      setError("Password is required.");
      return;
    }

    setIsSubmitting(true);
    try {
      await login(email.trim(), password);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        setError("Invalid login attempt.");
      } else {
        setError("Unable to sign in. Check your connection and try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Screen edges={["top", "bottom"]} style={styles.screen}>
      <StatusBar style="dark" />
      <LinearGradient
        pointerEvents="none"
        colors={["#f8fbff", "#eef4ff", "#f7fbff"]}
        locations={[0, 0.55, 1]}
        start={{ x: 0, y: 0 }}
        end={{ x: 1, y: 1 }}
        style={StyleSheet.absoluteFill}
      />
      <View pointerEvents="none" style={styles.glowTop} />
      <View pointerEvents="none" style={styles.glowBottom} />

      <KeyboardAvoidingView behavior={Platform.OS === "ios" ? "padding" : "height"} style={styles.flex}>
        <ScrollView
          contentContainerStyle={styles.content}
          keyboardShouldPersistTaps="handled"
          showsVerticalScrollIndicator={false}
          bounces={false}
        >
          <FadeInUp style={styles.brandBlock}>
            <Image
              source={require("../../assets/logivue-wms-transparent.png")}
              style={styles.brandLogo}
              resizeMode="contain"
              accessibilityLabel="LogiVue warehouse management system"
            />
          </FadeInUp>

          <FadeInUp delay={80} style={styles.welcomeBlock}>
            <Text style={styles.heroTitle}>Welcome back</Text>
            <Text style={styles.heroSubtitle}>Sign in to continue your assigned warehouse tasks.</Text>
          </FadeInUp>

          <FadeInUp delay={140} style={styles.card}>
            <Text style={styles.cardTitle}>Sign in</Text>
            <Text style={styles.cardSubtitle}>Use your authorized WMS account.</Text>

            <Animated.View
              style={[
                styles.form,
                { transform: [{ translateX: shake.interpolate({ inputRange: [-1, 1], outputRange: [-8, 8] }) }] },
              ]}
            >
              <Text style={styles.label}>Email address</Text>
              <Pressable style={styles.inputShell} onPress={() => emailRef.current?.focus()}>
                <AppIcon name="mail-outline" size={19} color={colors.textSecondary} />
                <TextInput
                  ref={emailRef}
                  style={styles.input}
                  value={email}
                  onChangeText={setEmail}
                  autoCapitalize="none"
                  autoCorrect={false}
                  autoComplete="username"
                  keyboardType="email-address"
                  placeholder="name@company.com"
                  placeholderTextColor={colors.textMuted}
                  showSoftInputOnFocus
                  returnKeyType="next"
                  onSubmitEditing={() => passwordRef.current?.focus()}
                />
              </Pressable>

              <Text style={[styles.label, styles.passwordLabel]}>Password</Text>
              <View style={styles.inputShell}>
                <AppIcon name="lock-closed-outline" size={19} color={colors.textSecondary} />
                <TextInput
                  ref={passwordRef}
                  style={styles.input}
                  value={password}
                  onChangeText={setPassword}
                  secureTextEntry={!showPassword}
                  autoCapitalize="none"
                  autoComplete="password"
                  placeholder="Enter your password"
                  placeholderTextColor={colors.textMuted}
                  showSoftInputOnFocus
                  returnKeyType="go"
                  onSubmitEditing={handleSubmit}
                />
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel={showPassword ? "Hide password" : "Show password"}
                  hitSlop={10}
                  onPress={() => setShowPassword((visible) => !visible)}
                  style={styles.visibilityButton}
                >
                  <AppIcon name={showPassword ? "eye-off-outline" : "eye-outline"} size={20} color={colors.textSecondary} />
                </Pressable>
              </View>

              {error ? (
                <View accessibilityRole="alert" style={styles.errorBanner}>
                  <AppIcon name="alert-circle-outline" size={19} color={colors.dangerText} />
                  <Text style={styles.errorText}>{error}</Text>
                </View>
              ) : null}

              <Button
                title="Sign in"
                onPress={handleSubmit}
                loading={isSubmitting}
                disabled={isSubmitting}
                icon={<AppIcon name="arrow-forward" size={19} color={colors.textOnPrimary} />}
                size="lg"
                style={styles.submitButton}
              />
            </Animated.View>
          </FadeInUp>

          <View style={styles.footerRow}>
            <AppIcon name="shield-checkmark-outline" size={15} color={colors.textSecondary} />
            <Text style={styles.footerText}>Protected role-based access</Text>
          </View>
        </ScrollView>
      </KeyboardAvoidingView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  screen: { backgroundColor: "#f8fbff" },
  flex: { flex: 1 },
  content: {
    flexGrow: 1,
    width: "100%",
    maxWidth: 520,
    alignSelf: "center",
    justifyContent: "center",
    paddingHorizontal: spacing.xl,
    paddingTop: spacing.xl,
    paddingBottom: spacing.lg,
  },
  glowTop: {
    position: "absolute",
    top: -150,
    right: -120,
    width: 340,
    height: 340,
    borderRadius: 170,
    backgroundColor: "rgba(79, 70, 229, 0.08)",
  },
  glowBottom: {
    position: "absolute",
    bottom: -180,
    left: -150,
    width: 360,
    height: 360,
    borderRadius: 180,
    backgroundColor: "rgba(6, 182, 212, 0.06)",
  },
  brandBlock: { alignItems: "center", justifyContent: "center", marginBottom: spacing.lg },
  brandLogo: { width: 180, height: 142 },
  welcomeBlock: { alignItems: "center", marginBottom: spacing.xl },
  heroTitle: {
    color: colors.textPrimary,
    fontSize: 34,
    lineHeight: 39,
    fontWeight: "900",
    letterSpacing: -0.9,
    textAlign: "center",
  },
  heroSubtitle: {
    ...typography.body,
    color: colors.textSecondary,
    lineHeight: 21,
    marginTop: spacing.sm,
    maxWidth: 340,
    textAlign: "center",
  },
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.xl,
    padding: spacing.xl,
    borderWidth: 1,
    borderColor: colors.border,
    ...shadow.card,
  },
  cardTitle: { ...typography.title, color: colors.textPrimary },
  cardSubtitle: { ...typography.caption, color: colors.textSecondary, marginTop: 3, marginBottom: spacing.xl },
  form: { width: "100%" },
  label: { ...typography.caption, color: colors.textSecondary, fontWeight: "700", marginBottom: spacing.xs },
  passwordLabel: { marginTop: spacing.md },
  inputShell: {
    minHeight: 52,
    flexDirection: "row",
    alignItems: "center",
    paddingHorizontal: spacing.md,
    borderWidth: 1.5,
    borderColor: colors.border,
    borderRadius: radius.md,
    backgroundColor: colors.background,
  },
  input: { flex: 1, minWidth: 0, paddingHorizontal: spacing.md, paddingVertical: 13, fontSize: 16, color: colors.textPrimary },
  visibilityButton: { width: 32, height: 32, alignItems: "center", justifyContent: "center" },
  errorBanner: {
    flexDirection: "row",
    alignItems: "flex-start",
    marginTop: spacing.md,
    padding: spacing.md,
    borderRadius: radius.md,
    backgroundColor: colors.dangerSoft,
    borderWidth: 1,
    borderColor: "#fecaca",
  },
  errorText: { flex: 1, color: colors.dangerText, marginLeft: spacing.sm, fontSize: 13, lineHeight: 18, fontWeight: "700" },
  submitButton: { marginTop: spacing.xl },
  footerRow: { flexDirection: "row", alignItems: "center", justifyContent: "center", marginTop: spacing.xl },
  footerText: { color: colors.textSecondary, fontSize: 12, fontWeight: "600", marginLeft: 6 },
});
