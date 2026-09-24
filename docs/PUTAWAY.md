# Inward → Putaway Redesign

This document is the deliverable for the Inward → Putaway redesign work: what changed, why, what
was verified, and what's still open. It follows the same 12-point structure the work was scoped
against (files / DB / API / UI / algorithm / barcode / 3D / auth / tests / results / assumptions /
gaps), so each claim below can be checked against a specific commit's worth of change, plus a §13
covering a later self-review pass over the whole feature.

No existing module was rewritten. Every change is additive (new nullable columns, new optional
command parameters, new files) or a targeted bug fix inside the Inward feature; unrelated modules
(Pullout, Inventory Master, Dashboard, Rack/Zone/Location/Material CRUD outside the fields noted
below) were not touched.

## 1. Files changed

**Domain**
- `Material.cs` — added `PreferredZoneTypeId` (nullable FK to `ZoneType`).
- `InwardTransaction.cs` — `InwardPutaway` gained `OverrideReason` (string?), `RowVersion`
  (uint, optimistic-concurrency token, same pattern as `StockBatch.RowVersion`), and
  `ConfirmedByUserName` (string?, denormalized at confirm time — same pattern as
  `ActivityLog.UserName` — since no UserId→display-name resolver exists elsewhere in the app).
- `PalletPosition.cs` — added `RowVersion` (uint, concurrency token), touched on every reservation
  so two racing inward submissions can't silently overbook the same position (see §5/§12).
- `Material.cs` — added `RequirePreferredZone` (bool, default false). When true, promotes
  `PreferredZoneTypeId` from a soft scoring nudge into a hard eligibility gate (§5).

**Infrastructure**
- `Services/PalletAllocationService.cs` — put-away engine: Design Code proximity, weight-based
  level preference, and the Rack→Column→Level fill sequence added; the old level-major
  `DistancePriority`-driven scoring and velocity-based level heuristic removed from this engine
  (Pullout's own use of `DistancePriority` is untouched — see §5). Candidate positions are now
  also filtered to a material's required zone up front, before any tier runs, when
  `RequirePreferredZone` is set.
- `Persistence/Configurations/MaterialConfiguration.cs`, `InwardTransactionConfiguration.cs`,
  `RackConfiguration.cs` (`PalletPositionConfiguration`) — EF mappings for the new columns.
- `Persistence/Migrations/20260909165809_AddMaterialPreferredZoneAndPutawayOverride.cs`,
  `20260910090038_AddPalletPositionRowVersion.cs`,
  `20260910092435_AddInwardPutawayConfirmedByUserName.cs`,
  `20260910093706_AddMaterialRequirePreferredZone.cs`.

**Application**
- `Features/Inward/Commands/ChangeInwardPutawayLocation.cs` — cross-material-mixing guard on
  manual reroute; `Reason` parameter is now mandatory (new `ChangeInwardPutawayLocationValidator`).
- `Features/Inward/Queries/GetInwardPalletChangeOptions.cs` — same cross-material guard applied
  to the reroute candidate list.
- `Features/Inward/Commands/ConfirmInwardPutaway.cs` — confirm-time revalidation (active/material
  match/capacity) against current DB state; fixed a latent bug where the top-up lookup compared
  an un-`Include`d navigation property (`StockSubtype`) and so always evaluated `null`.
- `Features/Inward/Commands/ConfirmSingleInwardPutaway.cs` — **new**, confirms one location at a
  time for the execution screen.
- `Features/Inward/InwardPutawayConfirmationLogic.cs` — **new**, the revalidation + `StockBatch`
  upsert logic shared by both confirm commands.
- `Features/Inward/Dtos/InwardDtos.cs` — `InwardResultDto.Id`, `InwardDetailLineDto.DesignType`,
  `InwardPutawayDto.OverrideReason`/`ConfirmedAt`/`ConfirmedByUserName` (for the Putaway Summary).
- `Features/Inward/Queries/GetInwardDetail.cs`, `Commands/SubmitInward.cs`,
  `InwardResultMapper.cs` — populate the fields above.
- `Features/Inward/Commands/SubmitInward.cs` — reservation now retries (up to 3 attempts) on
  `DbUpdateConcurrencyException`, re-running allocation against current state each time (§5/§12).
- `Common/Interfaces/IApplicationDbContext.cs` — exposed `ChangeTracker` and `Entry<T>()` (needed
  by the retry loop to detach a failed attempt's tracked entities before retrying).
- `Features/Materials/Commands/CreateMaterial.cs`, `UpdateMaterial.cs`,
  `Queries/GetMaterialById.cs`, `GetMaterials.cs`, `Dtos/MaterialDto.cs` — `PreferredZoneTypeId`
  and `RequirePreferredZone` wired through the full CRUD path (a handler-level auto-correct forces
  `RequirePreferredZone` back to false whenever no zone is selected, rather than a hard validation
  error - more forgiving of the UI's own transient state, see §4).
- `Features/Inward/Commands/ChangeInwardPutawayLocation.cs`, `Queries/GetInwardPalletChangeOptions.cs`
  — the same hard zone gate applied to manual reroute and its candidate list, so a required zone
  can't be worked around by overriding.
- `Features/Racks/Dtos/RackDtos.cs`, `Queries/GetPalletPositionByCode.cs`, `GetRackLayout.cs`,
  `Features/Locations/Queries/GetLocations.cs` — added `PalletPositionDto.RackId` (needed to load
  a position's rack for the 3D view).

**Web**
- `Components/Pages/Inward/Putaway.razor` — **new**, the mobile Putaway Execution screen.
- `Components/Pages/Inward/Index.razor`, `Detail.razor`, `History.razor` — `Supervisor` added to
  `[Authorize(Roles=...)]`; mandatory reason field + disabled-until-filled "Move Here" in the
  reroute slide-over; "Putaway" entry points linking to the new screen.
- `Components/Pages/Materials/Index.razor` — "Preferred Zone" dropdown, plus a "Require this
  zone (hard constraint)" checkbox that only appears once a zone is selected.
- `wwwroot/app.css` — `.putaway-execute-*` (execution screen) and `.recommended` /
  `.rack-cell-pin` (3D highlight) rules, reusing the existing `.rack-viewport`/`.pallet-cell-3d`
  pattern from `Racks/Layout.razor`.

**Tests** — see §9.

## 2. Database changes

Four migrations, all fully additive:

| Table | Column | Type | Notes |
|---|---|---|---|
| `Materials` | `PreferredZoneTypeId` | `int` NULL, FK → `ZoneTypes.Id` | Soft put-away preference; index `IX_Materials_PreferredZoneTypeId`. |
| `InwardPutaways` | `OverrideReason` | `varchar(250)` NULL | Operator's justification when manually rerouting. |
| `InwardPutaways` | `RowVersion` | `int unsigned` NOT NULL DEFAULT 0 | Concurrency token, incremented on reroute and on confirm. |
| `PalletPositions` | `RowVersion` | `int unsigned` NOT NULL DEFAULT 0 | Concurrency token, incremented every time a reservation is made against the position; guards `SubmitInward`'s allocation step. |
| `InwardPutaways` | `ConfirmedByUserName` | `varchar(256)` NULL | Denormalized confirmer name for the Putaway Summary panel. |
| `Materials` | `RequirePreferredZone` | `tinyint(1)` NOT NULL DEFAULT 0 | Promotes `PreferredZoneTypeId` to a hard placement constraint for that material. |

No column was dropped, renamed, or had its type changed. Applied and verified against the local
MySQL instance.

## 3. API changes (MediatR commands/queries — this app's CQRS "API" layer)

- **New:** `ConfirmSingleInwardPutawayCommand(int PutawayId)`.
- **Changed:** `ChangeInwardPutawayLocationCommand` gained `string? Reason` (validated
  non-blank); `ConfirmInwardPutawayCommand` now revalidates before committing (throws
  `InvalidOperationException` with a specific reason, or a concurrency message, instead of always
  succeeding); `GetInwardPalletChangeOptionsQuery` excludes cross-material candidates *and* any
  candidate outside a material's required zone; `SubmitInwardCommand` now retries its own
  reservation up to 3 times if a concurrent submission reserves the same position first, instead
  of silently overbooking it.
- **Changed:** `CreateMaterialCommand` / `UpdateMaterialCommand` gained `PreferredZoneTypeId` and
  `RequirePreferredZone`.
- **DTO shape changes** (additive fields only, no removals): `InwardResultDto.Id`,
  `InwardDetailLineDto.DesignType`, `MaterialDto.PreferredZoneTypeId`/`PreferredZoneTypeName`/
  `RequirePreferredZone`, `PalletPositionDto.RackId`, `InwardPutawayDto.OverrideReason`/
  `ConfirmedAt`/`ConfirmedByUserName`.

## 4. UI changes

- **Putaway Execution** (`/inward/{id}/putaway`) — the primary new screen. Shows one recommended
  location at a time: SKU/Design Code/PKM header, the location code in large type with its
  Rack/Column/Level breakdown, live Recommended/Current/Available quantities, a large
  "Confirm This Location" button, and a live-updating progress bar. Confirming advances
  automatically to the next pending location; an empty state shows once all are confirmed.
  Entry points added from the Inward history list, the transaction detail page, and the
  post-upload result modal.
- **Putaway Summary** (spec §23E) — a new panel on the existing Inward Detail page rather than a
  separate route (the data is inherently transaction-scoped, and Detail is already the page for
  "everything about this GRN"): Total/Putaway/Pending Qty, Locations Used, Overrides count, and a
  completion line showing who confirmed the last location and when (or a pending banner with the
  remaining quantity if not yet fully confirmed).
- **Materials** — "Preferred Zone" dropdown in the create/edit form, with an optional "Require
  this zone" checkbox that turns the soft preference into a hard placement constraint for that SKU.
- **Inward reroute** (`Index.razor`/`Detail.razor` "Change Location" slide-over) — "Reason for
  override" is now a required field; "Move Here" stays disabled until it's filled.
- **Authorization** — `Supervisor` can now reach all three Inward pages (previously
  `Admin,Operator` only).

## 5. Putaway algorithm explanation

Three tiers, in order (unchanged from before this work):

1. **Top-up** — positions already holding the exact same SKU + PKM + subtype with spare capacity.
2. **Same-rack consolidation** — for a SKU with existing stock/reservations elsewhere, prefer its
   own rack(s), ranked by existing concentration, capped by a configurable per-`LocationType`
   level cutoff.
3. **Unrestricted best-fit** — any empty, eligible position; also a brand-new SKU's first
   placement and anything Tier 2 couldn't place.

**Hard zone eligibility.** Before any tier runs, if a material has `RequirePreferredZone` set,
candidate positions are filtered down to its `PreferredZoneTypeId` up front - so it applies
uniformly across all three tiers, including topping up the material's own existing stock, and
regardless of how much better another zone would otherwise score. The same gate is enforced on
manual reroute and its candidate list, so a required zone can't be routed around by overriding.
This defaults to off (`RequirePreferredZone = false`) for every material, so existing soft-only
behavior is unchanged unless explicitly opted into per SKU.

Within Tier 2/3, candidates are then ranked lexicographically (most to least significant):

1. **Zone/velocity/season fit** (`ScoreLocation`) — unchanged business logic: Fast-moving SKUs
   favor Fast/DispatchNear zones in the active season, etc. When a preferred zone isn't required,
   `PreferredZoneTypeId` nudges this score without ever gating placement.
2. **Design Code proximity** *(new)* — prefer a rack that already holds a different SKU sharing
   this material's Design Code (never the same location — that would violate no-mixing, which is
   enforced upstream by the candidate filter itself, not by this ranking).
3. **Weight** *(new)* — `Level × BoxWeightKg`; heavier boxes are steered toward lower levels. Zero
   weight is a true no-op.
4. **Rack → Column → Level** *(new)* — the base "fill this bay top-to-bottom, then the next bay"
   sequence. This replaces `PalletPosition.DistancePriority` for put-away purposes specifically.
   `DistancePriority` is level-major (tuned for Pullout's FIFO pick order — a picker working
   levels across all columns before climbing) and is unrelated to how a rack should be *filled*
   (bay-by-bay). The two are deliberately allowed to differ; `PulloutAllocationService` is
   untouched and still drives picking off `DistancePriority`.
5. Fine tie-breaks (unchanged): a small penalty for choosing an empty spot when this SKU has older
   unallocated stock elsewhere, and a small penalty for non-Rack location types.

**Concurrency at reservation time.** `SubmitInwardHandler` increments `PalletPosition.RowVersion`
for every position it reserves against as part of the same save that creates the
`InwardTransactionLine`/`InwardPutaway` rows. If a concurrent submission reserved against the same
position first, that `RowVersion` has already moved, the save fails with
`DbUpdateConcurrencyException`, and the handler discards the failed attempt's tracked entities and
re-runs allocation from scratch (now seeing the winner's reservation) — up to 3 attempts before
surfacing a clear "please retry" error. This relies on the relational provider's guarantee that a
failed `SaveChangesAsync` rolls back the *entire* batch, including the newly-added rows — true for
the Pomelo/MySQL provider this app runs on, verified directly against the real local MySQL
database (see §10), though not reliably modelable in EF Core's InMemory *test* provider for a
mixed Added+Modified batch (documented in code where it's relevant).

## 6. Barcode workflow

**Not implemented — explicit, recorded product decision**, not an oversight. Earlier in this
engagement the question "does barcode-scan confirmation need to be built now?" was asked directly
and answered "do not require this functionality for now." Location entry/confirmation in the new
Execution screen is a straight tap-to-confirm against the system's own recommendation; there is no
scan-and-validate step, and TEST 8 (wrong barcode scanned → warning, no confirm without override)
is not covered.

## 7. 3D simulation changes

The existing pseudo-3D CSS from `Racks/Layout.razor` (pure CSS `transform: skewY(...)` +
drop-shadow, no canvas/WebGL) is reused as-is for cell shape/coloring. The Putaway Execution
screen renders the current step's full rack via `GetRackLayoutQuery` and adds one new visual
state, `.recommended` — an amber pulsing glow plus a bouncing pin marker — applied to exactly the
position the engine recommended. Verified against real seeded data: of a 50-position rack, exactly
one cell carried the `recommended` class, and it was the correct one.

## 8. Authorization changes

`Supervisor` added to the `[Authorize(Roles=...)]` list on `Inward/Index.razor`, `Detail.razor`,
`History.razor` (previously `Admin,Operator` only; `Putaway.razor` ships with the same three
roles from the start). No new roles were created — the spec's "Head-Supervisor"/"Superadmin"
concepts were mapped onto this app's existing `Admin`/`Supervisor`/`Operator` model rather than
inventing new roles the rest of the app doesn't have.

## 9. Tests created

`tests/GodrejWMS.Infrastructure.Tests/`:
- `PalletAllocationServiceTests.cs` (20 tests total) — 6 added across this work: preferred-zone
  soft tiebreak, column-then-level fill for a weight-neutral material (reproduces the spec's own
  380-box/pallet-size-40 example exactly), weight overriding plain column order for a heavy
  material, Design Code proximity for a brand-new SKU, hard zone eligibility never using any other
  zone even when it would score far better, and hard zone eligibility returning `Failed` (not
  silently falling back) when no capacity exists in the required zone; 1 existing test's
  expectation updated to match the new (still-correct) ordering now that the old velocity-level
  heuristic is gone.
- `InwardPutawayReroutingTests.cs` — **new file**, 11 tests: cross-material rejection on reroute
  (both the command and the candidate-list query), successful reroute records `OverrideReason` and
  increments `RowVersion`, the mandatory-reason validator (rejects null/empty/whitespace, accepts a
  real reason), and hard zone eligibility on both the reroute command and its candidate list
  (rejects/excludes a target outside the required zone, includes one inside it).
- `ConfirmInwardPutawayTests.cs` — **new file**, 6 tests: successful confirm creates/tops up the
  right `StockBatch` (verifying the `StockSubtypeId` bug fix) and denormalizes `ConfirmedByUserName`,
  throws when the location has since been deactivated, throws when it's since been claimed by a
  different material, throws when another confirmation has since consumed its capacity, and
  `ConfirmSingleInwardPutawayCommand` confirms only the targeted putaway (leaving the rest of the
  transaction pending) and rejects a double-confirm.
- `SubmitInwardConcurrencyTests.cs` — **new file**, 1 test: two separate `DbContext` instances
  racing to save the same `PalletPosition` — the first save wins, the second throws
  `DbUpdateConcurrencyException` — direct proof of the mechanism `SubmitInwardHandler`'s retry
  loop depends on. The full "retry recovers with zero leftover rows" path was verified separately,
  once, directly against the real local MySQL database rather than as a permanent InMemory test
  (see §5's concurrency note and §10).

## 10. Test results

```
dotnet build   → Build succeeded. 0 Warning(s), 0 Error(s)
dotnet test    → GodrejWMS.Domain.Tests:          1/1 passed
                 GodrejWMS.Application.Tests:      8/8 passed
                 GodrejWMS.Infrastructure.Tests:  41/41 passed
                 Total: 50/50 passed
```

The Putaway Execution screen and its 3D highlight were also exercised live: dev server started,
logged in via a real Identity form POST, a real inward transaction seeded against real rack/
material data, and the rendered page checked for correct Rack/Column/Level/Design
Code/PKM/Current/Available values and the correct single highlighted cell. Not automated as a
repeatable test — interactive `@onclick` behavior (the actual button press) was not driven by a
browser and so is unverified beyond code review; this is disclosed rather than claimed as tested.

The `SubmitInward` retry-recovers-cleanly path (§5) was verified once, directly, against the real
local MySQL database using a throwaway test forcing a genuine race (a second `DbContext` bumps the
target position's `RowVersion` mid-flight): confirmed 2 allocation attempts occurred, the result
was `Fulfilled`, and exactly one `InwardTransactionLine`/`InwardPutaway` existed afterward — no
orphaned row from the failed first attempt. That scratch test was deleted after confirming the
result; it is not part of the permanent suite (see §12 on why an integration-test harness doesn't
exist yet for this to live in permanently).

The Putaway Summary panel was checked live against real seeded data in both states: a fully
confirmed two-line transaction (Total 40 / Putaway 40 / Pending 0 / Locations Used 2 / Overrides 1,
correctly attributing "Completed by" to whichever of two different confirmers acted *last*, not
just whoever confirmed first) and a partially confirmed one (Putaway 25 / Pending 15, pending
banner shown instead of the completion line).

The Materials list page was checked live post-change (logged in, all 6 seeded materials rendered
correctly) to confirm `RequirePreferredZone` didn't break `GetMaterials`/`GetMaterialById`; the
"Require this zone" checkbox's own show/hide-on-selection behavior is client-side interactive
state and, like the Execution screen's button press, wasn't driven by a real browser - verified by
code review instead.

## 11. Assumptions

- **No `Warehouse` entity exists in this app** (confirmed by a full-repo search) — it's implicitly
  single-site. Every spec requirement that assumes multi-warehouse scoping (location-belongs-to-
  warehouse validation, a Warehouse column in list screens, etc.) is not applicable here.
- **This app's Zone Master models velocity/season categories** (`Fast`/`Reserve`/`Seasonal`/
  `DispatchNear`), not the SKU-category model (`Aerosol`/`Insecticides`/`General`) the spec's own
  examples use. `Material.PreferredZoneTypeId` bridges this against the existing zone concept
  rather than inventing a second, parallel zone system; `RequirePreferredZone` (§1/§5) lets a
  specific material opt into treating it as a hard constraint, but every material is still soft
  (nudge-only) by default.
- Barcode scanning is out of scope by explicit decision (§6).
- The relative priority weighting between Design Code proximity, weight, and the base traversal
  sequence is a reasoned engineering choice (documented inline in `PalletAllocationService`), not
  something validated against real warehouse throughput data — it satisfies the spec's own worked
  examples and priority ordering, but the exact numbers are a judgment call.
- "Reason mandatory" (TEST 9) applies specifically to a *manual* reroute; the engine's own initial
  recommendation never requires or prompts for a reason.

## 12. Remaining gaps

- **Barcode validation** (TEST 8) — deferred by decision, not built.
- **Explicit Putaway status enum** — the existing `AllocationStatus` + `IsConfirmed` model is
  reused rather than introducing `PENDING`/`RECOMMENDED`/`IN_PROGRESS`/`PARTIALLY_PUTAWAY`/
  `COMPLETED`/`CANCELLED`.
- **Integration/E2E test automation** — only unit tests exist; UI correctness was verified
  manually (see §10), not via an automated browser suite.
- **No permanent integration-test harness against real MySQL** — the `SubmitInward` concurrency
  retry's full recovery path is real-database-verified (§10) but only as a one-off, not as a
  repeatable automated test; a proper integration-test project (own connection config, isolation/
  cleanup, opt-in for CI) would be needed to keep re-verifying this, and wasn't built as it's a
  bigger scope than the concurrency fix itself.

## 13. Post-implementation review

A deliberate re-read of everything above turned up three real issues, now fixed:

- **Confirm-time revalidation never checked hard zone eligibility.** §5's hard zone gate was
  enforced at allocation time, on manual reroute, and on the reroute candidate list - but not in
  `InwardPutawayConfirmationLogic.ConfirmAsync`, the code all three confirm paths share. A
  material's required zone (or a location's own zone) can still change between reservation and
  confirmation, exactly the class of problem confirm-time revalidation exists to catch. Fixed with
  the same check pattern as the existing active/material/capacity guards, and covered by a new
  test (`Confirm_Throws_WhenMaterialsRequiredZoneChangedSinceReservation`).
- **A raw `DbUpdateConcurrencyException` could leak past `SubmitInward`'s retry loop.** The catch
  clause's `when (attempt < MaxAllocationAttempts)` guard meant that on the *final* attempt, a
  conflict wasn't caught at all - it propagated as an unfriendly EF exception instead of the
  intended "please retry" message, and the code after the loop that was meant to produce that
  message was unreachable in exactly the case it existed for. Fixed by removing the guard (the
  loop's own exit condition already stops retrying after the last attempt). Verified against real
  MySQL with a fake allocator that conflicts on every attempt, not just the first - confirmed the
  friendly message now surfaces and all 3 attempts are made before giving up.
- **`Detail.razor`'s `CompletionDate` used a convoluted default-value sentinel** to emulate
  "max of a nullable sequence, or null if empty" instead of just doing that directly (`Max()` on
  `IEnumerable<DateTimeOffset?>` already ignores nulls and returns null for an all-null/empty
  sequence). Simplified; re-verified live against the same seeded scenario to confirm identical
  output before and after.

Also updated for clarity, no behavior change: `PalletAllocationService`'s class-level doc comment
now describes the hard zone gate (it was written before that feature existed and was never
updated); the audit-facing `AllocationReason` text now distinguishes a hard-required zone match
("This SKU's required zone") from a soft-preferred one ("Preferred zone for this SKU").

Full solution after this pass: 51/51 tests, clean build.
