import { useLocalSearchParams, useRouter } from "expo-router";
import { useCallback, useEffect, useRef, useState } from "react";
import { Alert, FlatList, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import {
  addCycleCountFoundStock,
  correctCycleCountCard,
  getCycleCountCards,
  getCycleCountLocations,
  getCycleCountSummary,
  getMyCycleCountBatches,
  searchCycleCountMaterials,
  submitCycleCountRack,
  verifyCycleCountCard,
} from "../../api/endpoints";
import { useAuth } from "../../auth/AuthContext";
import { MaterialPickerModal } from "../../components/MaterialPickerModal";
import { Button } from "../../components/ui/Button";
import { EmptyState } from "../../components/ui/EmptyState";
import { FullScreenModal } from "../../components/ui/FullScreenModal";
import { SearchableOptionSheet } from "../../components/SearchableOptionSheet";
import { OptionListSheet } from "../../components/ui/OptionListSheet";
import { Screen } from "../../components/ui/Screen";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { TextField } from "../../components/ui/TextField";
import { colors, radius, spacing, typography } from "../../theme";
import {
  CycleCountRackStatus,
  type CycleCountBatchDto,
  type CycleCountCardDto,
  type CycleCountCardsDto,
  type CycleCountLocationDto,
  type CycleCountSubmitSummaryDto,
  type MaterialDto,
} from "../../types/api";

const MONTH_LABELS = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];
const CURRENT_YEAR = new Date().getFullYear();

/** "SEP|2026" -> { year: 2026, month: 9 }; {0, 0} when there is no PKM. */
function parsePkm(text: string | null): { year: number; month: number } {
  const [monthText, yearText] = (text ?? "").split("|");
  const month = MONTH_LABELS.indexOf((monthText ?? "").trim().toUpperCase()) + 1;
  const year = Number(yearText);
  return month > 0 && year > 0 ? { year, month } : { year: 0, month: 0 };
}

const pkmText = (e: { pkmYear: number; pkmMonth: number }): string => (e.pkmYear > 0 && e.pkmMonth > 0 ? `${MONTH_LABELS[e.pkmMonth - 1]}|${e.pkmYear}` : "");

interface Edit {
  card: CycleCountCardDto | null; // null = stock found in a location the system lists differently
  locationCode: string;
  palletPositionId: number;
  material: MaterialDto | null;
  materialNumber: number | null;
  pkmYear: number;
  pkmMonth: number;
  quantity: string;
  stockSubtype: number; // 1 = Good, 2 = Damage
}

export default function CycleCountScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const batchId = Number(id);
  const router = useRouter();
  const { token, permissions } = useAuth();
  const canSubmit = permissions.includes("CycleCount.Submit");

  const [batch, setBatch] = useState<CycleCountBatchDto | null>(null);
  const [rack, setRack] = useState<string | null>(null);
  const [locations, setLocations] = useState<CycleCountLocationDto[]>([]);
  const [fromIdx, setFromIdx] = useState<number | null>(null);
  const [toIdx, setToIdx] = useState<number | null>(null);
  const [picking, setPicking] = useState<"from" | "to" | null>(null);
  const [cards, setCards] = useState<CycleCountCardsDto | null>(null);
  const [edit, setEdit] = useState<Edit | null>(null);
  const [materialOpen, setMaterialOpen] = useState(false);
  const [editPicker, setEditPicker] = useState<"year" | "month" | "location" | null>(null);
  const [summary, setSummary] = useState<CycleCountSubmitSummaryDto | null>(null);
  const [stockType, setStockType] = useState<number>(0); // 0 Both (default), 1 Good, 2 Damage
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [editError, setEditError] = useState<string | null>(null);
  const submitting = useRef(false); // blocks a second tap while the first submit is still in flight

  const loadBatch = useCallback(async () => {
    if (!token) return;
    try {
      const all = await getMyCycleCountBatches(token);
      setBatch(all.find((b) => b.id === batchId) ?? null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Unable to load the cycle count.");
    }
  }, [token, batchId]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- initial load of the batch the user opened
    void loadBatch();
  }, [loadBatch]);

  const loadCards = useCallback(
    async (rackCode: string, from: number | null, to: number | null, locs: CycleCountLocationDto[], subtype: number = stockType) => {
      if (!token) return;
      try {
        const result = await getCycleCountCards(token, batchId, rackCode, from === null ? undefined : locs[from]?.locationCode, to === null ? undefined : locs[to]?.locationCode, subtype === 0 ? undefined : subtype);
        setCards(result);

        setError(null);
      } catch (e) {
        setError(e instanceof Error ? e.message : "Unable to load the cards.");
      }
    },
    [token, batchId, stockType],
  );

  const openRack = async (rackCode: string) => {
    if (!token) return;
    setRack(rackCode);
    setFromIdx(null);
    setToIdx(null);
    setStockType(0);
    try {
      const locs = await getCycleCountLocations(token, batchId, rackCode);
      setLocations(locs);
      await loadCards(rackCode, null, null, locs, 0);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Unable to open the rack.");
    }
  };

  const ok = async (card: CycleCountCardDto) => {
    if (!token || !rack || busy) return;
    setBusy(true);
    try {
      await verifyCycleCountCard(token, card.id);
      await loadCards(rack, fromIdx, toIdx, locations);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Unable to save.");
    } finally {
      setBusy(false);
    }
  };

  const openEdit = (card: CycleCountCardDto) => {
    setEditError(null);
    setEdit({
      card,
      locationCode: card.locationCode,
      palletPositionId: card.palletPositionId,
      material: null,
      materialNumber: card.physicalMaterialNumber ?? card.systemMaterialNumber,
      pkmYear: parsePkm(card.physicalPkm ?? card.systemPkm).year,
      pkmMonth: parsePkm(card.physicalPkm ?? card.systemPkm).month,
      quantity: String(card.physicalQuantityBoxes ?? card.systemQuantityBoxes),
      stockSubtype: card.physicalStockSubtypeId ?? card.systemStockSubtypeId,
    });
  };

  const openFound = () => {
    const first = locations[0];
    if (!first) return;
    setEditError(null);
    setEdit({ card: null, locationCode: first.locationCode, palletPositionId: first.palletPositionId, material: null, materialNumber: null, pkmYear: 0, pkmMonth: 0, quantity: "", stockSubtype: 1 });
  };

  const saveEdit = async () => {
    if (!token || !edit || !rack || busy) return;
    const quantity = Number(edit.quantity);
    if (edit.quantity.trim() === "" || !Number.isInteger(quantity) || quantity < 0) {
      setEditError("Enter a whole number of boxes (0 or more).");
      return;
    }
    setBusy(true);
    setEditError(null);
    try {
      if (edit.card) {
        await correctCycleCountCard(token, edit.card.id, { materialNumber: edit.materialNumber, mfgMonthText: pkmText(edit) || null, physicalQuantityBoxes: quantity, stockSubtypeId: edit.stockSubtype });
      } else {
        if (edit.materialNumber === null) {
          setEditError("Select the material found.");
          return;
        }
        await addCycleCountFoundStock(token, batchId, { palletPositionId: edit.palletPositionId, materialNumber: edit.materialNumber, mfgMonthText: pkmText(edit), physicalQuantityBoxes: quantity, stockSubtypeId: edit.stockSubtype });
      }
      setEdit(null);
      await loadCards(rack, fromIdx, toIdx, locations);
    } catch (e) {
      setEditError(e instanceof Error ? e.message : "Unable to save.");
    } finally {
      setBusy(false);
    }
  };

  const changeStockType = (value: number) => {
    setStockType(value);
    if (rack) void loadCards(rack, fromIdx, toIdx, locations, value);
  };

  const openSummary = async () => {
    if (!token || !rack) return;
    try {
      setSummary(await getCycleCountSummary(token, batchId, rack));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Unable to load the summary.");
    }
  };

  const submit = async () => {
    if (!token || !rack || submitting.current) return;
    submitting.current = true;
    setBusy(true);
    try {
      const result = await submitCycleCountRack(token, batchId, rack);
      setSummary(null);
      // Back to the batch list: once every rack is submitted the batch is no longer open for counting, so this screen would have nothing to show.
      Alert.alert(
        result.alreadySubmitted ? "Already submitted" : "Submitted",
        result.alreadySubmitted
          ? `Rack ${result.rackCode} was already submitted.`
          : `Rack ${result.rackCode}: ${result.verifiedCards} OK, ${result.correctedCards} corrected, ${result.stockChanges} stock change(s) applied.`,
        [{ text: "OK", onPress: () => router.back() }],
        { cancelable: false },
      );
      setRack(null);
      setCards(null);
    } catch (e) {
      setSummary(null);
      setError(e instanceof Error ? e.message : "Unable to submit.");
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  };

  const locationOptions = locations.map((l, i) => ({ value: i, label: l.locationCode, hint: l.pendingCards > 0 ? `${l.pendingCards} pending` : undefined }));

  if (!batch) {
    return (
      <Screen padded>
        {error ? <Text style={styles.error}>{error}</Text> : <EmptyState title="Cycle count not available" subtitle="It may have been closed or dropped." />}
      </Screen>
    );
  }

  if (!rack) {
    return (
      <Screen padded>
        <Text style={styles.heading}>{batch.batchNumber}</Text>
        <Text style={styles.meta}>Choose a rack to count</Text>
        {error ? <Text style={styles.error}>{error}</Text> : null}
        <View style={styles.rackGrid}>
          {batch.racks.map((r) => (
            <TouchableOpacity
              key={r.rackCode}
              style={[styles.rackTile, r.status !== CycleCountRackStatus.Open && styles.rackTileOff]}
              disabled={r.status !== CycleCountRackStatus.Open}
              onPress={() => openRack(r.rackCode)}
            >
              <Text style={styles.rackCode}>{r.rackCode}</Text>
              {r.firstLocation ? <Text style={styles.meta}>{r.firstLocation} - {r.lastLocation}</Text> : null}
              <Text style={styles.meta}>{r.completedCards}/{r.totalCards} counted</Text>
              {r.status !== CycleCountRackStatus.Open ? <Text style={styles.meta}>{r.status === CycleCountRackStatus.Submitted ? "Submitted" : "Excluded"}</Text> : null}
            </TouchableOpacity>
          ))}
        </View>
      </Screen>
    );
  }

  return (
    <Screen padded>
      <View style={styles.rowBetween}>
        <Text style={styles.heading}>Rack {rack}</Text>
        <Button title="Racks" variant="outline" fullWidth={false} onPress={() => { setRack(null); setCards(null); void loadBatch(); }} />
      </View>
      <View style={styles.rangeRow}>
        <TouchableOpacity style={styles.rangeBox} onPress={() => setPicking("from")}>
          <Text style={styles.meta}>From location</Text>
          <Text style={styles.rangeValue}>{fromIdx === null ? "First" : locations[fromIdx]?.locationCode}</Text>
        </TouchableOpacity>
        <TouchableOpacity style={styles.rangeBox} onPress={() => setPicking("to")}>
          <Text style={styles.meta}>To location</Text>
          <Text style={styles.rangeValue}>{toIdx === null ? "Last" : locations[toIdx]?.locationCode}</Text>
        </TouchableOpacity>
      </View>
      <View style={styles.chipRow}>
        {[{ v: 1, l: "Good" }, { v: 2, l: "Damage" }, { v: 0, l: "Both" }].map((o) => (
          <TouchableOpacity key={o.v} style={[styles.chip, stockType === o.v && styles.chipOn]} onPress={() => changeStockType(o.v)}>
            <Text style={[styles.chipText, stockType === o.v && styles.chipTextOn]}>{o.l}</Text>
          </TouchableOpacity>
        ))}
      </View>
      {error ? <Text style={styles.error}>{error}</Text> : null}
      {cards ? (
        <>
          <View style={styles.track}><View style={[styles.fill, { width: `${Math.min(100, cards.completionPercent)}%` }]} /></View>
          <Text style={styles.meta}>{cards.completedCards} of {cards.totalCards} counted ({cards.completionPercent.toFixed(1)}%)</Text>
          <FlatList
            data={cards.cards}
            keyExtractor={(c) => String(c.id)}
            ListEmptyComponent={<EmptyState title="No cards in this range" />}
            contentContainerStyle={{ paddingVertical: spacing.md }}
            renderItem={({ item: c }) => (
              <TouchableOpacity style={[styles.card, c.isCompleted && styles.cardDone]} onPress={() => openEdit(c)} activeOpacity={0.8}>
                <View style={styles.rowBetween}>
                  <Text style={styles.cardLocation}>{c.locationCode}</Text>
                  {c.isCompleted ? <StatusBadge label={c.isDiscrepancy ? "Corrected" : "OK"} tone="success" /> : null}
                  {c.isExtra ? <StatusBadge label="Found" tone="warning" /> : null}
                  {c.stockType === "Damage" ? <StatusBadge label="Damage" tone="danger" /> : null}
                </View>
                {c.systemMaterialNumber === null ? (
                  <Text style={styles.cardInfo}>Empty in the system</Text>
                ) : (
                  <>
                    <Text style={styles.material}>{c.systemMaterialNumber}</Text>
                    <Text style={styles.cardDescription} numberOfLines={2}>{c.systemDescription}</Text>
                    <Text style={styles.cardInfo}>PKM {c.systemPkm} · System qty <Text style={styles.cardQty}>{c.systemQuantityBoxes}</Text></Text>
                  </>
                )}
                {c.isDiscrepancy ? (
                  <Text style={styles.cardInfo}>
                    Physical: {c.physicalMaterialNumber ?? "-"} {c.physicalPkm ?? ""} × <Text style={styles.cardQty}>{c.physicalQuantityBoxes}</Text>
                  </Text>
                ) : null}
                <View style={styles.cardActions}>
                  {!c.isExtra ? <Button title="OK" size="md" style={{ flex: 1 }} disabled={busy} onPress={() => ok(c)} /> : null}
                  <Button title={c.isCompleted ? "Edit" : "Correct"} size="md" variant="outline" style={{ flex: 1 }} disabled={busy} onPress={() => openEdit(c)} />
                </View>
              </TouchableOpacity>
            )}
          />
          <Button title="Add stock found in a location" variant="ghost" onPress={openFound} />
          {canSubmit ? <Button title="Submit rack" size="lg" disabled={busy} onPress={openSummary} style={{ marginTop: spacing.sm }} /> : null}
        </>
      ) : null}

      <SearchableOptionSheet
        visible={picking !== null}
        title={picking === "from" ? "From location" : "To location"}
        options={locationOptions}
        placeholder="Search location (e.g. A-12)"
        clearLabel={picking === "from" ? "First location" : "Last location"}
        onClose={() => setPicking(null)}
        onSelect={(i) => {
          const nextFrom = picking === "from" ? (i < 0 ? null : i) : fromIdx;
          const nextTo = picking === "to" ? (i < 0 ? null : i) : toIdx;
          setPicking(null);
          if (nextFrom !== null && nextTo !== null && nextFrom > nextTo) {
            setError("From Location cannot be after To Location.");
            return;
          }
          setFromIdx(nextFrom);
          setToIdx(nextTo);
          if (rack) void loadCards(rack, nextFrom, nextTo, locations);
        }}
      />

      <FullScreenModal visible={edit !== null} title={edit?.card ? "Physical count" : "Stock found"} onClose={() => setEdit(null)}>
        {edit ? (
          <View>
            <Text style={styles.meta}>Location</Text>
            {edit.card ? (
              <Text style={styles.rangeValue}>{edit.locationCode}</Text>
            ) : (
              <TouchableOpacity style={styles.field} onPress={() => setEditPicker("location")}>
                <Text style={styles.rangeValue}>{edit.locationCode || "Tap to select"}</Text>
              </TouchableOpacity>
            )}
            {edit.card?.systemMaterialNumber ? (
              <Text style={styles.meta}>System: {edit.card.systemMaterialNumber} · {edit.card.systemPkm} · {edit.card.systemQuantityBoxes} boxes</Text>
            ) : null}
            <TouchableOpacity style={styles.field} onPress={() => setMaterialOpen(true)}>
              <Text style={styles.meta}>Material code</Text>
              <Text style={styles.rangeValue}>{edit.materialNumber ?? "Tap to select"}</Text>
              {edit.material ? <Text style={styles.meta}>{edit.material.description}</Text> : null}
            </TouchableOpacity>
            <View style={styles.chipRow}>
              {[{ v: 1, l: "Good" }, { v: 2, l: "Damage" }].map((o) => (
                <TouchableOpacity key={o.v} style={[styles.chip, edit.stockSubtype === o.v && styles.chipOn]} onPress={() => setEdit({ ...edit, stockSubtype: o.v })}>
                  <Text style={[styles.chipText, edit.stockSubtype === o.v && styles.chipTextOn]}>{o.l}</Text>
                </TouchableOpacity>
              ))}
            </View>
            <Text style={styles.meta}>PKM</Text>
            <View style={styles.pkmRow}>
              <TouchableOpacity style={styles.pkmButton} onPress={() => setEditPicker("year")} activeOpacity={0.72}>
                <Text style={styles.meta}>YEAR</Text>
                <Text style={styles.rangeValue}>{edit.pkmYear > 0 ? edit.pkmYear : "Select"}</Text>
              </TouchableOpacity>
              <TouchableOpacity style={styles.pkmButton} onPress={() => setEditPicker("month")} activeOpacity={0.72}>
                <Text style={styles.meta}>MONTH</Text>
                <Text style={styles.rangeValue}>{edit.pkmMonth > 0 ? MONTH_LABELS[edit.pkmMonth - 1] : "Select"}</Text>
              </TouchableOpacity>
            </View>
            <TextField label="Physical quantity (boxes)" value={edit.quantity} keyboardType="number-pad" onChangeText={(quantity) => setEdit({ ...edit, quantity })} />
            {editError ? <Text style={styles.error}>{editError}</Text> : null}
            <Button title="OK" size="lg" loading={busy} onPress={saveEdit} />
            <Button title="Cancel" variant="outline" style={{ marginTop: spacing.sm }} onPress={() => setEdit(null)} />
          </View>
        ) : null}
      </FullScreenModal>

      <OptionListSheet
        visible={editPicker === "year"}
        title="PKM year"
        options={Array.from({ length: 5 }, (_, i) => CURRENT_YEAR - 3 + i).map((y) => ({ value: y, label: String(y) }))}
        onClose={() => setEditPicker(null)}
        onSelect={(value) => { setEdit((e) => (e ? { ...e, pkmYear: value } : e)); setEditPicker(null); }}
      />
      <OptionListSheet
        visible={editPicker === "month"}
        title="PKM month"
        options={MONTH_LABELS.map((label, index) => ({ value: index + 1, label }))}
        onClose={() => setEditPicker(null)}
        onSelect={(value) => { setEdit((e) => (e ? { ...e, pkmMonth: value } : e)); setEditPicker(null); }}
      />
      <SearchableOptionSheet
        visible={editPicker === "location"}
        title="Location of the stock found"
        options={locationOptions}
        placeholder="Search location (e.g. A-12)"
        onClose={() => setEditPicker(null)}
        onSelect={(i) => {
          const loc = locations[i];
          if (loc) setEdit((e) => (e ? { ...e, palletPositionId: loc.palletPositionId, locationCode: loc.locationCode } : e));
          setEditPicker(null);
        }}
      />

      {token ? (
        <MaterialPickerModal
          visible={materialOpen}
          token={token}
          searchFn={searchCycleCountMaterials}
          onClose={() => setMaterialOpen(false)}
          onSelect={(m) => {
            setMaterialOpen(false);
            setEdit((e) => (e ? { ...e, material: m, materialNumber: m.materialNumber } : e));
          }}
        />
      ) : null}

      <FullScreenModal visible={summary !== null} title={`Submit rack ${summary?.rackCode ?? ""}`} onClose={() => setSummary(null)}>
        {summary ? (
          <View>
            <Text style={styles.summaryLine}>Batch {summary.batchNumber}</Text>
            <Text style={styles.summaryLine}>{summary.warehouseName}</Text>
            <Text style={styles.summaryLine}>Total count cards: {summary.totalCards}</Text>
            <Text style={styles.summaryLine}>Verified (OK): {summary.verifiedCards}</Text>
            <Text style={styles.summaryLine}>Corrected: {summary.correctedCards}</Text>
            <Text style={styles.summaryLine}>Net quantity difference: {summary.totalDifferenceBoxes > 0 ? "+" : ""}{summary.totalDifferenceBoxes} ({summary.totalAbsoluteDifferenceBoxes} boxes in total)</Text>
            <Text style={styles.summaryLine}>Pending: {summary.pendingCards}</Text>
            {summary.uncountedDamageCards > 0 ? <Text style={styles.meta}>{summary.uncountedDamageCards} damage card(s) not counted: the damaged stock stays unchanged.</Text> : null}
            {summary.blocker ? <Text style={styles.error}>{summary.blocker}</Text> : <Text style={styles.meta}>Submitting updates the system stock to what you counted. It cannot be changed afterwards.</Text>}
            <Button title="Confirm and submit" size="lg" loading={busy} disabled={!summary.canSubmit} style={{ marginTop: spacing.md }} onPress={submit} />
            <Button title="Cancel" variant="outline" style={{ marginTop: spacing.sm }} onPress={() => setSummary(null)} />
          </View>
        ) : null}
      </FullScreenModal>
    </Screen>
  );
}

const styles = StyleSheet.create({
  heading: { ...typography.title, color: colors.textPrimary },
  meta: { ...typography.caption, color: colors.textSecondary, marginTop: 2 },
  strong: { fontWeight: "800", color: colors.textPrimary },
  error: { color: colors.dangerText, marginVertical: spacing.sm },
  rowBetween: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", gap: spacing.sm },
  rackGrid: { flexDirection: "row", flexWrap: "wrap", gap: spacing.md, marginTop: spacing.md },
  rackTile: { width: "47%", padding: spacing.lg, borderRadius: radius.md, backgroundColor: colors.surface, borderWidth: 1, borderColor: colors.border },
  rackTileOff: { opacity: 0.5 },
  rackCode: { ...typography.display, color: colors.primary },
  rangeRow: { flexDirection: "row", gap: spacing.md, marginVertical: spacing.sm },
  rangeBox: { flex: 1, padding: spacing.md, borderRadius: radius.md, backgroundColor: colors.surface, borderWidth: 1, borderColor: colors.border },
  rangeValue: { ...typography.subtitle, color: colors.textPrimary },
  track: { height: 8, borderRadius: 4, backgroundColor: colors.surfaceAlt, overflow: "hidden", marginTop: spacing.sm },
  fill: { height: 8, backgroundColor: colors.success },
  card: { padding: spacing.md, borderRadius: radius.md, backgroundColor: colors.surface, borderWidth: 1, borderColor: colors.border, marginBottom: spacing.md },
  cardDone: { backgroundColor: colors.successSoft, borderColor: colors.success },
  cardLocation: { fontSize: 22, fontWeight: "800", color: colors.textPrimary },
  material: { fontSize: 20, fontWeight: "800", color: colors.textPrimary, marginTop: spacing.xs },
  cardDescription: { fontSize: 16, color: colors.textSecondary, marginTop: 2 },
  cardInfo: { fontSize: 17, color: colors.textPrimary, marginTop: 4 },
  cardQty: { fontSize: 20, fontWeight: "800", color: colors.textPrimary },
  cardActions: { flexDirection: "row", gap: spacing.sm, marginTop: spacing.md },
  chipRow: { flexDirection: "row", flexWrap: "wrap", gap: spacing.sm, alignItems: "center", marginVertical: spacing.sm },
  chip: { paddingHorizontal: spacing.md, paddingVertical: spacing.sm, borderRadius: radius.pill, borderWidth: 1, borderColor: colors.border, backgroundColor: colors.surface },
  chipOn: { backgroundColor: colors.primary, borderColor: colors.primary },
  chipText: { ...typography.bodyStrong, color: colors.textPrimary },
  chipTextOn: { color: colors.textOnPrimary },
  pkmRow: { flexDirection: "row", gap: spacing.sm, marginBottom: spacing.md },
  pkmButton: { flex: 1, padding: spacing.md, borderRadius: radius.md, backgroundColor: colors.surface, borderWidth: 1, borderColor: colors.border },
  field: { paddingVertical: spacing.md, borderBottomWidth: 1, borderBottomColor: colors.border, marginBottom: spacing.md },
  summaryLine: { ...typography.body, color: colors.textPrimary, marginBottom: spacing.xs },
});
