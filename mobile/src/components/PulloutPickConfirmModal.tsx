import { useCallback, useEffect, useState } from "react";
import { ScrollView, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { ApiError } from "../api/client";
import { confirmPulloutPick, getPalletPosition, getPickRemainder, getRelocationSuggestions } from "../api/endpoints";
import { BarcodeScannerModal } from "./BarcodeScannerModal";
import { AppIcon } from "./ui/AppIcon";
import { Button } from "./ui/Button";
import { FullScreenModal } from "./ui/FullScreenModal";
import { TextField } from "./ui/TextField";
import { colors, radius, shadow, spacing, typography } from "../theme";
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
      <TouchableOpacity style={[styles.stepButton, value <= min && styles.stepButtonDisabled]} onPress={() => set(value - 1)} disabled={value <= min} accessibilityLabel="Decrease quantity">
        <AppIcon name="remove" size={26} color={value <= min ? colors.textMuted : colors.primary} />
      </TouchableOpacity>
      <View style={styles.stepValueWrap}>
        <Text style={styles.stepValue}>{value}</Text>
        <Text style={styles.stepUnit}>BOXES</Text>
      </View>
      <TouchableOpacity style={[styles.stepButton, value >= max && styles.stepButtonDisabled]} onPress={() => set(value + 1)} disabled={value >= max} accessibilityLabel="Increase quantity">
        <AppIcon name="add" size={26} color={value >= max ? colors.textMuted : colors.primary} />
      </TouchableOpacity>
    </View>
  );
}

function StepHeader({ number, title, optional = false }: { number: number; title: string; optional?: boolean }) {
  return (
    <View style={styles.stepHeader}>
      <View style={styles.stepNumber}><Text style={styles.stepNumberText}>{number}</Text></View>
      <Text style={styles.stepTitle}>{title}</Text>
      {optional && <Text style={styles.optionalBadge}>OPTIONAL</Text>}
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
    // Always defaults to the full remaining amount - the picker can still dial it down with the
    // stepper, but re-opening/editing a pick should never leave a stale smaller amount selected.
    setMoveQty((current) => (current <= 0 || current > remainingAfter ? remainingAfter : current));
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
    <FullScreenModal visible title="Submit Pick" onClose={onClose}>
      <ScrollView style={styles.scroll} contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator={false}>
        <View style={styles.hero}>
          <View style={styles.heroTop}>
            <View style={styles.heroIcon}><AppIcon name="cube-outline" size={22} color={colors.primary} /></View>
            <View style={styles.heroLocationWrap}>
              <Text style={styles.heroLabel}>PICK FROM</Text>
              <View style={styles.heroLocationRow}>
                <Text style={styles.heroLocation}>{pick.locationCode}</Text>
                {pick.isVirtual && <Text style={styles.virtualBadge}>VIRTUAL</Text>}
              </View>
            </View>
          </View>
          <View style={styles.heroDivider} />
          <Text style={styles.heroMaterialNumber}>{pick.materialNumber}</Text>
          <Text style={styles.heroMaterial}>{pick.materialDescription}</Text>
          <View style={styles.heroMetaRow}>
            <View style={styles.metaPill}><Text style={styles.metaPillLabel}>PKM</Text><Text style={styles.metaPillValue}>{pick.mfgMonthLabel}</Text></View>
            <View style={styles.metaPill}><Text style={styles.metaPillValue}>{planned} boxes allocated</Text></View>
          </View>
        </View>

        {error && <Text style={styles.error}>{error}</Text>}

        <View style={styles.sectionCard}>
          <StepHeader number={1} title="Verify the location" />
          {verified ? (
            <View style={styles.okRow}>
              <View style={styles.okIcon}><AppIcon name="checkmark" size={18} color={colors.textOnPrimary} /></View>
              <View>
                <Text style={styles.okLabel}>LOCATION VERIFIED</Text>
                <Text style={styles.okText}>You are at {pick.locationCode}</Text>
              </View>
            </View>
          ) : (
            <>
              <Button title="Scan location" size="lg" onPress={() => setScanning("verify")} icon={<AppIcon name="scan-outline" size={22} color={colors.textOnPrimary} />} />
              <View style={styles.orRow}><View style={styles.orLine} /><Text style={styles.orText}>OR ENTER CODE</Text><View style={styles.orLine} /></View>
              <TextField
                label="Location code"
                value={typedCode}
                onChangeText={setTypedCode}
                autoCapitalize="characters"
                autoCorrect={false}
                size="lg"
                placeholder={pick.locationCode}
                onSubmitEditing={() => matches(typedCode) ? setVerified(true) : setError(`That is not ${pick.locationCode}.`)}
              />
              <Button
                title="Verify location"
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
        </View>

        <View style={styles.sectionCard}>
          <StepHeader number={2} title="Quantity picked" />
          <Stepper value={qty} min={1} max={planned + (remainder?.remainingBoxes ?? 0)} onChange={setQty} />
          {qty > planned && (
            <View style={styles.hintBox}><AppIcon name="information-circle-outline" size={18} color={colors.info} /><Text style={styles.hint}>
              {qty - planned} extra box(es) are taken from the stock still at {pick.locationCode} when you confirm the pick-list. Use Change location to take more from another location.
            </Text></View>
          )}
          {qty < planned && (
            <View style={styles.hintBox}><AppIcon name="information-circle-outline" size={18} color={colors.info} /><Text style={styles.hint}>{planned - qty} box(es) not found go back to stock at {pick.locationCode} when you confirm the pick-list.</Text></View>
          )}
        </View>

        {remainingAfter > 0 && (
          <View style={styles.sectionCard}>
            <StepHeader number={3} title={`Move the remaining ${remainingAfter} box(es)?`} optional />
            <Text style={styles.sectionIntro}>
              {remainingAfter} box(es) of this material stay at {pick.locationCode} unless you choose a location. Nothing moves until you confirm the pick-list, so you can still change your mind.
            </Text>

            <TouchableOpacity
              style={[styles.choice, !moveEnabled && styles.choiceActive]}
              onPress={() => setMoveEnabled(false)}
            >
              <View style={[styles.choiceIcon, !moveEnabled && styles.choiceIconActive]}><AppIcon name="location-outline" size={20} color={!moveEnabled ? colors.primary : colors.textSecondary} /></View>
              <View style={styles.choiceCopy}><Text style={styles.choiceTitle}>Leave them at {pick.locationCode}</Text><Text style={styles.choiceMeta}>Keep stock in its current location</Text></View>
              <AppIcon name={!moveEnabled ? "checkmark-circle" : "ellipse-outline"} size={22} color={!moveEnabled ? colors.primary : colors.borderStrong} />
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
                <View style={[styles.choiceIcon, moveEnabled && destination?.id === suggestion.palletPositionId && styles.choiceIconActive]}><AppIcon name="arrow-forward" size={20} color={moveEnabled && destination?.id === suggestion.palletPositionId ? colors.primary : colors.textSecondary} /></View>
                <View style={styles.choiceCopy}><Text style={styles.choiceTitle}>{suggestion.locationCode}{suggestion.isVirtual ? "  (virtual)" : ""}</Text><Text style={styles.choiceMeta}>{suggestion.reason}</Text></View>
                <AppIcon name={moveEnabled && destination?.id === suggestion.palletPositionId ? "checkmark-circle" : "ellipse-outline"} size={22} color={moveEnabled && destination?.id === suggestion.palletPositionId ? colors.primary : colors.borderStrong} />
              </TouchableOpacity>
            ))}

            <View style={styles.otherDestination}>
            <Text style={styles.otherDestinationTitle}>Another destination</Text>
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
            </View>

            {moveEnabled && destination && (
              <View style={styles.moveBox}>
                <Text style={styles.moveEyebrow}>MOVE QUANTITY</Text>
                <Text style={styles.moveTitle}>Moving to {destination.code}</Text>
                <Stepper value={moveQty} min={1} max={remainingAfter} onChange={setMoveQty} />
              </View>
            )}
          </View>
        )}
      </ScrollView>

      <View style={styles.footer}>
        <View style={styles.footerSummary}>
          <Text style={styles.footerSummaryLabel}>{verified ? "Ready to confirm" : "Location verification required"}</Text>
          <Text style={styles.footerSummaryValue}>{qty} box(es)</Text>
        </View>
        <Button
          title={`Submit pick of ${qty}`}
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
  scroll: { flex: 1, marginHorizontal: -spacing.lg },
  content: { paddingHorizontal: spacing.lg, paddingTop: spacing.sm, paddingBottom: spacing.xl },
  hero: { backgroundColor: colors.primarySoft, borderWidth: 1, borderColor: "#dfe4ff", borderRadius: radius.xl, padding: spacing.lg, marginBottom: spacing.md },
  heroTop: { flexDirection: "row", alignItems: "center" },
  heroIcon: { width: 42, height: 42, borderRadius: radius.md, backgroundColor: colors.surface, alignItems: "center", justifyContent: "center", marginRight: spacing.md },
  heroLocationWrap: { flex: 1 },
  heroLabel: { ...typography.label, color: colors.primary, letterSpacing: 0.8 },
  heroLocationRow: { flexDirection: "row", alignItems: "center", marginTop: 1 },
  heroLocation: { fontSize: 32, fontWeight: "800", color: colors.textPrimary, letterSpacing: -0.5 },
  virtualBadge: { ...typography.caption, color: colors.primaryText, fontWeight: "800", backgroundColor: colors.surface, borderRadius: radius.pill, paddingHorizontal: spacing.sm, paddingVertical: 3, marginLeft: spacing.sm },
  heroDivider: { height: 1, backgroundColor: "#dfe4ff", marginVertical: spacing.md },
  heroMaterialNumber: { ...typography.bodyStrong, color: colors.textPrimary },
  heroMaterial: { ...typography.body, color: colors.textSecondary, marginTop: 2, lineHeight: 19 },
  heroMetaRow: { flexDirection: "row", flexWrap: "wrap", marginTop: spacing.md, gap: spacing.sm },
  metaPill: { flexDirection: "row", alignItems: "center", backgroundColor: colors.surface, borderRadius: radius.pill, paddingHorizontal: spacing.md, paddingVertical: 7 },
  metaPillLabel: { ...typography.caption, color: colors.textMuted, fontWeight: "700", marginRight: spacing.xs },
  metaPillValue: { ...typography.caption, color: colors.textPrimary, fontWeight: "700" },
  error: { backgroundColor: colors.dangerSoft, color: colors.dangerText, borderRadius: radius.md, padding: spacing.md, fontWeight: "700", marginBottom: spacing.md },
  sectionCard: { backgroundColor: colors.surface, borderWidth: 1, borderColor: colors.border, borderRadius: radius.lg, padding: spacing.lg, marginBottom: spacing.md, ...shadow.card },
  stepHeader: { flexDirection: "row", alignItems: "center", marginBottom: spacing.lg },
  stepNumber: { width: 28, height: 28, borderRadius: radius.pill, alignItems: "center", justifyContent: "center", backgroundColor: colors.primary },
  stepNumberText: { ...typography.label, color: colors.textOnPrimary, fontSize: 13 },
  stepTitle: { ...typography.subtitle, color: colors.textPrimary, flex: 1, marginLeft: spacing.sm },
  optionalBadge: { ...typography.caption, color: colors.textSecondary, fontWeight: "800", backgroundColor: colors.neutralSoft, borderRadius: radius.pill, paddingHorizontal: spacing.sm, paddingVertical: 5 },
  orRow: { flexDirection: "row", alignItems: "center", marginVertical: spacing.md },
  orLine: { flex: 1, height: 1, backgroundColor: colors.border },
  orText: { ...typography.caption, color: colors.textMuted, fontWeight: "800", marginHorizontal: spacing.sm },
  okRow: { flexDirection: "row", alignItems: "center", backgroundColor: colors.successSoft, borderRadius: radius.md, padding: spacing.md },
  okIcon: { width: 34, height: 34, borderRadius: radius.pill, backgroundColor: colors.success, alignItems: "center", justifyContent: "center", marginRight: spacing.md },
  okLabel: { ...typography.caption, color: colors.successText, fontWeight: "800" },
  okText: { color: colors.successText, fontWeight: "700", marginTop: 1 },
  skip: { alignSelf: "center", padding: spacing.md, minHeight: 48, justifyContent: "center" },
  skipText: { ...typography.caption, color: colors.textSecondary, textDecorationLine: "underline" },
  stepper: { flexDirection: "row", alignItems: "center", justifyContent: "space-between" },
  stepButton: { width: 58, height: 58, borderRadius: radius.lg, backgroundColor: colors.primarySoft, borderWidth: 1, borderColor: "#dfe4ff", alignItems: "center", justifyContent: "center" },
  stepButtonDisabled: { opacity: 0.5 },
  stepValueWrap: { minWidth: 100, alignItems: "center" },
  stepValue: { fontSize: 36, fontWeight: "800", color: colors.textPrimary, textAlign: "center", lineHeight: 40 },
  stepUnit: { ...typography.caption, color: colors.textMuted, fontWeight: "800", letterSpacing: 0.8 },
  hintBox: { flexDirection: "row", alignItems: "flex-start", backgroundColor: colors.infoSoft, borderRadius: radius.md, padding: spacing.md, marginTop: spacing.md },
  hint: { ...typography.caption, color: colors.infoText, flex: 1, marginLeft: spacing.sm, lineHeight: 17 },
  sectionIntro: { ...typography.caption, color: colors.textSecondary, lineHeight: 18, marginTop: -spacing.sm, marginBottom: spacing.md },
  choice: { minHeight: 66, flexDirection: "row", alignItems: "center", borderWidth: 1.5, borderColor: colors.border, borderRadius: radius.md, padding: spacing.md, marginBottom: spacing.sm, backgroundColor: colors.surface },
  choiceActive: { borderColor: colors.primary, backgroundColor: colors.primarySoft },
  choiceIcon: { width: 36, height: 36, borderRadius: radius.sm, backgroundColor: colors.neutralSoft, alignItems: "center", justifyContent: "center", marginRight: spacing.md },
  choiceIconActive: { backgroundColor: colors.surface },
  choiceCopy: { flex: 1, paddingRight: spacing.sm },
  choiceTitle: { ...typography.bodyStrong, color: colors.textPrimary },
  choiceMeta: { ...typography.caption, color: colors.textSecondary, marginTop: 2 },
  otherDestination: { marginTop: spacing.sm, paddingTop: spacing.md, borderTopWidth: 1, borderTopColor: colors.border },
  otherDestinationTitle: { ...typography.label, color: colors.textSecondary, marginBottom: spacing.sm },
  moveBox: { backgroundColor: colors.primarySoft, borderWidth: 1, borderColor: "#dfe4ff", borderRadius: radius.md, padding: spacing.md, marginTop: spacing.md },
  moveEyebrow: { ...typography.caption, color: colors.primary, fontWeight: "800", letterSpacing: 0.6 },
  moveTitle: { ...typography.bodyStrong, color: colors.textPrimary, marginTop: 2, marginBottom: spacing.md },
  footer: { marginHorizontal: -spacing.lg, paddingHorizontal: spacing.lg, paddingTop: spacing.md, paddingBottom: spacing.xs, borderTopWidth: 1, borderTopColor: colors.border, backgroundColor: colors.surface, ...shadow.raised },
  footerSummary: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", marginBottom: spacing.sm },
  footerSummaryLabel: { ...typography.caption, color: colors.textSecondary, fontWeight: "700" },
  footerSummaryValue: { ...typography.bodyStrong, color: colors.textPrimary },
  footerHint: { ...typography.caption, color: colors.textMuted, textAlign: "center", marginTop: spacing.xs },
});
