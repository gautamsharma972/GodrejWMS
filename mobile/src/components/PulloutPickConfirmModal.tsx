import { useCallback, useEffect, useState } from "react";
import { ScrollView, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { ApiError } from "../api/client";
import { confirmPulloutPick, getPalletPosition, getPickRemainder, getRelocationSuggestions } from "../api/endpoints";
import { BarcodeScannerModal } from "./BarcodeScannerModal";
import { AppIcon } from "./ui/AppIcon";
import { Button } from "./ui/Button";
import { FullScreenModal } from "./ui/FullScreenModal";
import { TextField } from "./ui/TextField";
import { colors, radius, spacing, typography } from "../theme";
import type { ConfirmPickResult, PickRemainderDto, RelocationSuggestionDto } from "../types/api";
import { errorPulse, lightTap, successPulse } from "../utils/haptics";

export interface ConfirmablePick {
  pickId: number;
  referenceNumber: string;
  materialNumber: number;
  materialDescription: string;
  locationCode: string;
  mfgMonthLabel: string;
  quantityBoxes: number;
  isVirtual?: boolean;
  /** Set when the pick was already confirmed: it can be edited until the pick-list is confirmed. */
  pickedQuantityBoxes?: number | null;
  moveToPalletPositionId?: number | null;
  moveToLocationCode?: string | null;
  moveQuantityBoxes?: number | null;
}

interface Props {
  pick: ConfirmablePick | null;
  token: string;
  onClose: () => void;
  onDone: (result: ConfirmPickResult) => void;
}

interface Destination {
  id: number;
  code: string;
}

/** Big, thumb-friendly +/- control: the quantity is changed with taps, not typed. */
function Stepper({ value, min, max, onChange }: { value: number; min: number; max: number; onChange: (value: number) => void }) {
  const set = (next: number) => {
    lightTap();
    onChange(Math.max(min, Math.min(max, next)));
  };
  return (
    <View style={styles.stepper}>
      <TouchableOpacity style={styles.stepButton} onPress={() => set(value - 1)} disabled={value <= min} accessibilityLabel="Decrease quantity">
        <AppIcon name="remove" size={26} color={value <= min ? colors.textMuted : colors.primary} />
      </TouchableOpacity>
      <Text style={styles.stepValue}>{value}</Text>
      <TouchableOpacity style={styles.stepButton} onPress={() => set(value + 1)} disabled={value >= max} accessibilityLabel="Increase quantity">
        <AppIcon name="add" size={26} color={value >= max ? colors.textMuted : colors.primary} />
      </TouchableOpacity>
    </View>
  );
}

/** Mobile pick confirmation: scan (or type) the location to verify you are at the right bin, set the quantity actually picked and,
 * when boxes are left at that location, optionally move the remainder to a lower / better location - all confirmed in one go. */
export function PulloutPickConfirmModal({ pick, token, onClose, onDone }: Props) {
  const [qty, setQty] = useState(0);
  const [verified, setVerified] = useState(false);
  const [typedCode, setTypedCode] = useState("");
  const [remainder, setRemainder] = useState<PickRemainderDto | null>(null);
  const [suggestions, setSuggestions] = useState<RelocationSuggestionDto[]>([]);
  const [moveEnabled, setMoveEnabled] = useState(false);
  const [destination, setDestination] = useState<Destination | null>(null);
  const [moveQty, setMoveQty] = useState(0);
  const [destCode, setDestCode] = useState("");
  const [scanning, setScanning] = useState<"verify" | "destination" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const planned = pick?.quantityBoxes ?? 0;
  // Boxes left on the shelf after this pick: what is there now plus whatever a short pick hands back.
  const remainingAfter = remainder ? remainder.remainingBoxes + (planned - qty) : 0;

  useEffect(() => {
    if (!pick) return;
    // Resets the sheet for the pick it was opened on (same pattern as the other modals in this app).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setQty(pick.pickedQuantityBoxes ?? pick.quantityBoxes);
    setVerified(pick.pickedQuantityBoxes != null); // an edit of an already confirmed pick: the location was verified before
    setTypedCode("");
    setRemainder(null);
    setSuggestions([]);
    setMoveEnabled(!!pick.moveToPalletPositionId);
    setDestination(pick.moveToPalletPositionId && pick.moveToLocationCode ? { id: pick.moveToPalletPositionId, code: pick.moveToLocationCode } : null);
    setDestCode("");
    setError(null);
    let cancelled = false;
    getPickRemainder(token, pick.pickId)
      .then((value) => !cancelled && setRemainder(value))
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [pick, token]);

  // Keep the quantity to move within what is left, and refresh the suggested destinations for it.
  useEffect(() => {
    if (!pick || remainingAfter <= 0) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setMoveQty(0);
      setSuggestions([]);
      return;
    }
    setMoveQty((current) => (current <= 0 || current > remainingAfter ? (pick.moveQuantityBoxes && pick.moveQuantityBoxes <= remainingAfter ? pick.moveQuantityBoxes : remainingAfter) : current));
    let cancelled = false;
    getRelocationSuggestions(token, pick.pickId, remainingAfter)
      .then((value) => !cancelled && setSuggestions(value))
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [pick, token, remainingAfter]);

  const lookupDestination = useCallback(
    async (code: string) => {
      setError(null);
      try {
        const position = await getPalletPosition(token, code.trim());
        if (position.id === undefined) throw new Error("Location not found.");
        setDestination({ id: position.id, code: position.locationCode });
        setMoveEnabled(true);
        lightTap();
      } catch (err) {
        errorPulse();
        setError(err instanceof ApiError ? err.message : `Location ${code.trim()} was not found.`);
      }
    },
    [token],
  );

  if (!pick) return null;

  const matches = (value: string) => value.trim().toUpperCase() === pick.locationCode.toUpperCase();

  const submit = async () => {
    setSubmitting(true);
    setError(null);
    try {
      const result = await confirmPulloutPick(token, pick.pickId, {
        pickedQuantityBoxes: qty,
        moveRemaining: moveEnabled && destination && moveQty > 0
          ? { destinationPalletPositionId: destination.id, quantityBoxes: moveQty }
          : null,
      });
      successPulse();
      onDone(result);
    } catch (err) {
      errorPulse();
      setError(err instanceof ApiError ? err.message : "The pick could not be confirmed.");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <FullScreenModal visible title="Confirm pick" onClose={onClose}>
      <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
        <View style={styles.hero}>
          <Text style={styles.heroLabel}>PICK FROM</Text>
          <Text style={styles.heroLocation}>{pick.locationCode}</Text>
          {pick.isVirtual && <Text style={styles.virtualBadge}>Virtual overflow location</Text>}
          <Text style={styles.heroMaterial}>{pick.materialNumber} · {pick.materialDescription}</Text>
          <Text style={styles.heroMeta}>PKM {pick.mfgMonthLabel} · {planned} boxes allocated</Text>
        </View>

        {error && <Text style={styles.error}>{error}</Text>}

        <Text style={styles.step}>1 · Verify the location</Text>
        {verified ? (
          <View style={styles.okRow}>
            <AppIcon name="checkmark-circle" size={22} color={colors.success} />
            <Text style={styles.okText}>At {pick.locationCode}</Text>
          </View>
        ) : (
          <>
            <Button title="Scan location" size="lg" onPress={() => setScanning("verify")} icon={<AppIcon name="scan-outline" size={22} color={colors.textOnPrimary} />} />
            <TextField
              label="Or type the location code"
              value={typedCode}
              onChangeText={setTypedCode}
              autoCapitalize="characters"
              autoCorrect={false}
              size="lg"
              placeholder={pick.locationCode}
              onSubmitEditing={() => matches(typedCode) ? setVerified(true) : setError(`That is not ${pick.locationCode}.`)}
            />
            <Button
              title="Verify"
              variant="outline"
              size="lg"
              disabled={typedCode.trim().length === 0}
              onPress={() => (matches(typedCode) ? (setError(null), setVerified(true)) : (errorPulse(), setError(`That is not ${pick.locationCode}.`)))}
            />
            <TouchableOpacity onPress={() => setVerified(true)} style={styles.skip}>
              <Text style={styles.skipText}>Skip scanning</Text>
            </TouchableOpacity>
          </>
        )}

        <Text style={styles.step}>2 · Quantity picked</Text>
        <Stepper value={qty} min={1} max={planned} onChange={setQty} />
        {qty < planned && (
          <Text style={styles.hint}>{planned - qty} box(es) not found go back to stock at {pick.locationCode} when you confirm the pick-list.</Text>
        )}

        {remainingAfter > 0 && (
          <>
            <Text style={styles.step}>3 · Move the remaining {remainingAfter} box(es)? (optional)</Text>
            <Text style={styles.hint}>
              {remainingAfter} box(es) of this material stay at {pick.locationCode} unless you choose a location. Nothing moves until you confirm the pick-list, so you can still change your mind.
            </Text>

            <TouchableOpacity
              style={[styles.choice, !moveEnabled && styles.choiceActive]}
              onPress={() => setMoveEnabled(false)}
            >
              <Text style={styles.choiceTitle}>Leave them at {pick.locationCode}</Text>
            </TouchableOpacity>

            {suggestions.map((suggestion) => (
              <TouchableOpacity
                key={suggestion.palletPositionId}
                style={[styles.choice, moveEnabled && destination?.id === suggestion.palletPositionId && styles.choiceActive]}
                onPress={() => {
                  lightTap();
                  setDestination({ id: suggestion.palletPositionId, code: suggestion.locationCode });
                  setMoveEnabled(true);
                }}
              >
                <Text style={styles.choiceTitle}>
                  {suggestion.locationCode}{suggestion.isVirtual ? "  (virtual)" : ""}
                </Text>
                <Text style={styles.choiceMeta}>{suggestion.reason}</Text>
              </TouchableOpacity>
            ))}

            <Button title="Scan another location" variant="outline" size="lg" onPress={() => setScanning("destination")} icon={<AppIcon name="scan-outline" size={20} color={colors.primary} />} />
            <TextField
              label="Or type a location code"
              value={destCode}
              onChangeText={setDestCode}
              autoCapitalize="characters"
              autoCorrect={false}
              size="lg"
              onSubmitEditing={() => destCode.trim() && lookupDestination(destCode)}
            />
            <Button title="Use this location" variant="ghost" disabled={destCode.trim().length === 0} onPress={() => lookupDestination(destCode)} />

            {moveEnabled && destination && (
              <View style={styles.moveBox}>
                <Text style={styles.moveTitle}>Move to {destination.code}</Text>
                <Stepper value={moveQty} min={1} max={remainingAfter} onChange={setMoveQty} />
              </View>
            )}
          </>
        )}
      </ScrollView>

      <View style={styles.footer}>
        <Button
          title={`Confirm pick of ${qty}`}
          size="lg"
          loading={submitting}
          disabled={!verified}
          onPress={submit}
          icon={<AppIcon name="checkmark-circle-outline" size={22} color={colors.textOnPrimary} />}
        />
        {!verified && <Text style={styles.footerHint}>Verify the location first.</Text>}
        {moveEnabled && destination && moveQty > 0 && remainingAfter > 0 && (
          <Text style={styles.footerHint}>{moveQty} box(es) will move to {destination.code} when you confirm the pick-list.</Text>
        )}
      </View>

      <BarcodeScannerModal
        visible={scanning !== null}
        title={scanning === "verify" ? `Scan ${pick.locationCode}` : "Scan the destination location"}
        onClose={() => setScanning(null)}
        onScan={(value) => {
          if (scanning === "verify") {
            if (!matches(value)) return false;
            setError(null);
            setVerified(true);
            setScanning(null);
            return true;
          }
          setScanning(null);
          void lookupDestination(value);
          return true;
        }}
      />
    </FullScreenModal>
  );
}

const styles = StyleSheet.create({
  content: { padding: spacing.lg, paddingBottom: 140 },
  hero: { backgroundColor: colors.primarySoft, borderRadius: radius.xl, padding: spacing.lg, marginBottom: spacing.lg },
  heroLabel: { ...typography.label, color: colors.primary, letterSpacing: 0.6 },
  heroLocation: { fontSize: 40, fontWeight: "800", color: colors.textPrimary, marginTop: 2 },
  virtualBadge: { ...typography.caption, color: colors.primaryText, fontWeight: "700", marginTop: 2 },
  heroMaterial: { ...typography.bodyStrong, color: colors.textPrimary, marginTop: spacing.sm },
  heroMeta: { ...typography.caption, color: colors.textSecondary, marginTop: 2 },
  error: { backgroundColor: colors.dangerSoft, color: colors.dangerText, borderRadius: radius.md, padding: spacing.md, fontWeight: "700", marginBottom: spacing.md },
  step: { ...typography.title, fontSize: 17, color: colors.textPrimary, marginTop: spacing.lg, marginBottom: spacing.sm },
  hint: { ...typography.caption, color: colors.textSecondary, marginBottom: spacing.sm },
  okRow: { flexDirection: "row", alignItems: "center", backgroundColor: colors.successSoft, borderRadius: radius.md, padding: spacing.md },
  okText: { color: colors.successText, fontWeight: "700", marginLeft: spacing.sm },
  skip: { alignSelf: "center", padding: spacing.md, minHeight: 48, justifyContent: "center" },
  skipText: { ...typography.caption, color: colors.textSecondary, textDecorationLine: "underline" },
  stepper: { flexDirection: "row", alignItems: "center", justifyContent: "space-between", marginBottom: spacing.sm },
  stepButton: { width: 64, height: 64, borderRadius: radius.xl, backgroundColor: colors.primarySoft, alignItems: "center", justifyContent: "center" },
  stepValue: { fontSize: 36, fontWeight: "800", color: colors.textPrimary, minWidth: 90, textAlign: "center" },
  choice: { minHeight: 56, borderWidth: 1.5, borderColor: colors.border, borderRadius: radius.md, padding: spacing.md, marginBottom: spacing.sm, backgroundColor: colors.surface, justifyContent: "center" },
  choiceActive: { borderColor: colors.primary, backgroundColor: colors.primarySoft },
  choiceTitle: { ...typography.bodyStrong, color: colors.textPrimary },
  choiceMeta: { ...typography.caption, color: colors.textSecondary, marginTop: 2 },
  moveBox: { backgroundColor: colors.surfaceAlt, borderRadius: radius.md, padding: spacing.md, marginTop: spacing.sm },
  moveTitle: { ...typography.bodyStrong, color: colors.textPrimary, marginBottom: spacing.sm },
  footer: { paddingTop: spacing.md },
  footerHint: { ...typography.caption, color: colors.textMuted, textAlign: "center", marginTop: spacing.xs },
});
