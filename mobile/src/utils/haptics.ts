import * as Haptics from "expo-haptics";

// Haptics can silently fail on some devices/emulators (no vibration motor, permissions, etc.) -
// every call here is fire-and-forget and never throws into the UI.

export function lightTap() {
  Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light).catch(() => {});
}

export function successPulse() {
  Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success).catch(() => {});
}

export function errorPulse() {
  Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error).catch(() => {});
}
