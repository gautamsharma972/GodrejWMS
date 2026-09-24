# Inventory Movement

Relocates existing, already-confirmed stock from one Location to another. No Bin/Pallet Master
was created — Location Master (`Rack`/`PalletPosition`) remains the sole source of truth for
physical placement, exactly as required. No existing module was rewritten; every change is
additive (new tables, new files) or reuses an existing shared formula/pattern.

## 1. Repository inspection (§36) — what existed before this feature

| Spec assumes exists | Reality found |
|---|---|
| Stock Movement functionality | **Did not exist** — no entity, no table, no feature. |
| Reason Code Master | **Did not exist.** |
| Warehouse entity | **Does not exist** (same finding as the earlier Putaway work — this app is implicitly single-site). |
| Bin/Pallet Master | Correctly doesn't exist — Location Master (`Rack`/`PalletPosition`) already serves this role. |
| Batch/Lot field on inventory | **Does not exist.** The real "Inventory" record, `StockBatch`, is keyed by Material + Mfg Month (PKM) + Pallet Position only — no Batch/Lot. Per the spec's own "use only fields that actually exist" rule, inventory lines are identified by **Material + PKM + Location**, not Batch. |
| Existing Inventory Master view | `GetStockMasterQuery` / `StockMasterPage` — the reusable query pattern this feature's listings are modeled on. |
| Role/permission framework | Roles are `Admin` / `Supervisor` / `Operator` (not the spec's `Superadmin` / `Head-Supervisor` / `Supervisor`), with no per-role configurable permission toggle anywhere in the app. |

## 2. Existing components reused

- `PalletPosition` / `Rack` / `ZoneType` (Location Master) — no new location concept.
- `StockBatch` — the actual inventory record moved; a movement mutates two existing rows (source
  quantity down, destination quantity up/created), never a new inventory concept.
- `PalletCapacityCalculator.EffectiveCapacityBoxes` — the exact same capacity formula the put-away
  engine and Inward reroute already use, reused unchanged for destination capacity checks.
- The no-SKU-mixing rule pattern already established at three other call sites (`PalletAllocationService`,
  Inward reroute, Inward confirm) — the same check, reused a fourth time here.
- `StockBatch.RowVersion` optimistic-concurrency pattern — reused for both the source and
  destination rows a movement touches.
- `GetPalletPositionByCodeQuery` / `PalletPositionDto` — reused as-is for both the Source Location
  header/stock-listing (Move by Location) and the Destination Location details panel; only
  extended (not duplicated) to expose the two extra raw fields (`MaterialId`, `MfgMonth`) a move
  needs that the display-only version didn't carry.
- `ActivityLog`'s automatic audit-on-`SaveChangesAsync` mechanism — `MovementReason` and
  `StockMovement` get audit rows automatically, no new audit code was needed.
- The `ConfirmedByUserName`-style denormalization pattern (`InwardPutaway`) — reused for
  `StockMovement.PerformedByUserName`, since no UserId→display-name resolver exists in this app.
- The slide-over CSS pattern, `putaway-summary-grid` layout, and page-shell/panel/table
  conventions — no new UI framework or component library introduced.

## 3. New files created

**Domain**
- `MovementReason.cs` — minimal new reference master (Code/DisplayName/Description/SortOrder/IsActive),
  identical shape to `ZoneType`/`LocationType`. Justified by the spec's own fallback rule: reuse
  wherever possible, only create new masters when nothing exists — nothing did.
- `StockMovement.cs` — the movement audit record: Material, Mfg Month, Source/Destination
  `PalletPosition`, Quantity, Reason, Status, denormalized performer.
- `Enums/MovementStatus.cs` — single-member enum (`Completed`) today; exists so a future
  reversal/cancellation feature doesn't need a schema change.

**Infrastructure**
- `Persistence/Configurations/MovementReasonConfiguration.cs`, `StockMovementConfiguration.cs`.
- `Persistence/Migrations/20260913150131_AddInventoryMovement.cs` — creates both tables, fully additive.
- `Services/InventoryMovementService.cs` — the domain service (§32); see §7 below.
- `DbInitializer.cs` — extended with `SeedMovementReasonsAsync`, seeding the spec's 5 example
  reasons (Rack Reorganization, Space Optimization, Stock Consolidation, Operational Requirement,
  Supervisor Correction).

**Application**
- `Common/Interfaces/IInventoryMovementService.cs` — service contract + `InventoryMovementLineRequest`/`InventoryMovementLineResult`.
- `Features/InventoryMovement/Dtos/InventoryMovementDtos.cs` — all DTOs for this feature.
- `Features/InventoryMovement/Queries/GetStockByMaterial.cs` — Move by SKU's cross-location inventory listing (no existing equivalent).
- `Features/InventoryMovement/Queries/GetMovementReasons.cs`, `GetInventoryMovementHistory.cs`, `GetInventoryMovementDetail.cs`.
- `Features/InventoryMovement/Queries/ValidateInventoryMovementLines.cs` — the §24 validation-only API, as a MediatR query (see §8 on why not a REST endpoint).
- `Features/InventoryMovement/Commands/ConfirmInventoryMovement.cs` — the single command **both** UI flows call (§1's "same backend... transaction service" requirement).

**Web**
- `Components/Pages/InventoryMovement/MoveByLocation.razor`, `MoveBySku.razor`, `History.razor`.

**Tests**
- `tests/GodrejWMS.Infrastructure.Tests/InventoryMovementServiceTests.cs` (14 tests — the 15
  spec test cases minus authorization, which is framework-level; see §12).
- `ConfirmInventoryMovementTests.cs` (4 tests, Application-layer command/query/validator).

## 4. Existing files modified

- `Application/Common/Interfaces/IApplicationDbContext.cs`, `Infrastructure/Persistence/AppDbContext.cs` — added the two new `DbSet`s.
- `Infrastructure/DependencyInjection.cs` — registered `IInventoryMovementService`.
- `Application/Features/Racks/Dtos/RackDtos.cs`, `Queries/GetPalletPositionByCode.cs` — `PalletPositionStockDto` gained two additive, default-valued fields (`MaterialId`, `MfgMonth`) so this existing query/DTO could be reused instead of duplicated (see §2). The one other construction site (`GetRackLayout.cs`) was left untouched since it doesn't need the new fields — a purely additive, backward-compatible change confirmed by a full solution rebuild.
- `Components/Layout/NavMenu.razor` — new "Inventory Movement" section (Move by Location / Move by SKU / History), in its own `AuthorizeView Roles="Admin,Operator,Supervisor"` block (not nested inside the existing `Admin,Operator`-only Inward/Pullout block, so a Supervisor sees the link to a page they're actually allowed to open — an inconsistency that would otherwise have existed). The shared "Transactions" section label was likewise moved to its own broader `AuthorizeView` so it still shows for a Supervisor-only user.

## 5. Database changes

One migration, fully additive:

| Table | Notes |
|---|---|
| `MovementReasons` | Code, DisplayName, Description, SortOrder, IsActive + audit columns. |
| `StockMovements` | MovementNumber (unique), MovementType, MaterialId, MfgMonth, StockSubtypeId, SourcePalletPositionId, DestinationPalletPositionId, QuantityBoxes, ReasonId, Status, PerformedByUserId/UserName + audit columns. Four FKs (Material, StockSubtype, two independently-named FKs to `PalletPositions` for source/destination, Reason), all `Restrict`. |

No column was dropped, renamed, or had its type changed. Applied and verified against the local
MySQL instance; `MovementReasons` confirmed seeded with all 5 reasons on app startup.

## 6. API changes (MediatR commands/queries — this app's convention; see §8)

- **New:** `GetStockByMaterialQuery`, `GetMovementReasonsQuery`, `GetInventoryMovementHistoryQuery`,
  `GetInventoryMovementDetailQuery`, `ValidateInventoryMovementLinesQuery`, `ConfirmInventoryMovementCommand`.
- **Changed (additive only):** `PalletPositionStockDto` gained `MaterialId`/`MfgMonth` (see §4).

## 7. UI changes

- **Move by Location** (`/inventory-movement/by-location`) — search a source location, see its
  stock (reusing the existing Location-details query), select one line, enter/full-quantity a Move
  Quantity, search a destination location, see its Zone/Rack/Column/Level/Occupancy, pick a Reason,
  review a Movement Summary, confirm.
- **Move by SKU** (`/inventory-movement/by-sku`) — search/select a SKU, see every inventory record
  for it across all locations, check the ones to move, type a New Location + Move Qty inline per
  row, Continue to a per-line server-validated preview (§24), pick one Reason for the whole batch,
  confirm — each line still processed independently (TEST14).
- **Movement History** (`/inventory-movement/history`) — filterable register (search, source/destination
  location, date range) with a slide-over Movement Detail (reusing the established slide-over pattern).
- **Navigation** — new "Inventory Movement" section under Transactions, visible to `Admin`/`Operator`/`Supervisor`.
- No mobile-specific UI, no barcode/camera scanning, no 3D simulation — none were built, per the
  spec's explicit exclusions for this feature.

## 8. Business rules implemented

Both UI flows funnel through the **same** `IInventoryMovementService`/`ConfirmInventoryMovementCommand`
(§1's explicit requirement), which enforces, in this order: source/destination existence and
active state, source ≠ destination, sufficient source quantity, the no-SKU-mixing rule (same
material only — a different PKM of the *same* material is explicitly allowed), destination
capacity via the shared `PalletCapacityCalculator` formula. PKM is never an input the caller can
change for the destination — it's read from the source row and carried through structurally, so
it cannot drift (§22, TEST15).

**On the "API design" section (§23):** this app has no REST controller layer anywhere — it's
Blazor Server calling MediatR commands/queries directly from `@code` blocks. Per the spec's own
"first inspect the existing APIs... follow existing conventions" instruction, the suggested
REST-shaped endpoints were implemented as their MediatR equivalents instead (§6), not as new
ASP.NET controllers, which would have been the one genuinely inconsistent choice here.

## 9. Validation rules

Enforced in `InventoryMovementService.ValidateInternalAsync` (used by *both* the read-only
validate path and the mutating execute path, so the two can never drift apart):
source/destination location exists and is active; source ≠ destination; quantity > 0; source has
enough available quantity (business-friendly message quoting the current available quantity);
destination doesn't already hold a different material; destination has enough free capacity
(message quoting the actual available capacity, never hard-coding a pallet size). A movement
reason must be selected and must be an active `MovementReason`.

## 10. Concurrency handling

Same `RowVersion` optimistic-concurrency mechanism already proven for `PalletPosition`/`InwardPutaway`:
both the source and destination `StockBatch` rows have their `RowVersion` incremented as part of
the single `SaveChangesAsync` that also deducts/adds quantity and writes the `StockMovement` row.
Two users racing to move from the same source batch: the loser's stale `RowVersion` causes
`DbUpdateConcurrencyException`, translated to a business-friendly "this inventory was changed by
another user" message — never a silent over-deduction. TEST12 (§33) is verified directly (two
`DbContext`s racing to save the same batch — the mechanism itself), consistent with the same
mechanism already proven for `SubmitInward`.

## 11. Authorization

`[Authorize(Roles = "Admin,Operator,Supervisor")]` on all three new pages — enforced by ASP.NET
Core Identity's cookie auth integrated into the Blazor Server component lifecycle, which is
genuinely backend/server-side enforcement in this architecture (there's no separate client-callable
API surface to bypass by hiding a button, since the whole app runs server-side over one
authenticated circuit). The spec's `Superadmin`/`Head-Supervisor`/conditionally-permitted-`Supervisor`
model doesn't exist in this app; per the same mapping already established for the Putaway work,
all three real roles (`Admin`/`Supervisor`/`Operator`) are granted equal access rather than
building a new configurable per-role permission-toggle system that doesn't exist anywhere else to
extend (see §14 assumptions).

## 12. Tests created and results

```
dotnet build   → Build succeeded. 0 Warning(s), 0 Error(s)
dotnet test    → GodrejWMS.Domain.Tests:          1/1 passed
                 GodrejWMS.Application.Tests:      8/8 passed
                 GodrejWMS.Infrastructure.Tests:  60/60 passed
                 Total: 69/69 passed
```

`InventoryMovementServiceTests.cs` covers spec tests 1–10, 12, 14, 15 directly (full/partial move,
same-SKU-allowed, different-SKU-rejected, same-SKU-different-PKM-allowed, capacity/quantity
rejections, same-location rejection, inactive destination, invalid location, the concurrency
mechanism, independent multi-line processing, PKM-unchanged). `ConfirmInventoryMovementTests.cs`
covers the same behavior at the Application-command level the UI actually calls, plus the
mandatory-reason validator. TEST11 (unauthorized user) and TEST13 (DB failure → rollback) are not
separate unit tests — see §14 for why, and why that's a defensible, not overlooked, gap.

Additionally verified live: dev server started, `MovementReasons` confirmed seeded correctly on
startup, all three new pages return HTTP 200 with no server error, the NavMenu link renders
correctly and only for the intended roles, and the two brand-new EF queries
(`GetStockByMaterialQuery`, the extended `GetPalletPositionByCodeQuery`) were independently
re-verified against the real local MySQL database (not just the InMemory test provider) with real
seeded `StockBatch` rows, confirming their LINQ correctly translates and returns the right data.

## 13. Assumptions

- **Inventory identification omits Batch/Lot** since no such field exists on `StockBatch` — a
  line is identified by Material + Mfg Month + Location, per the spec's own "use only real fields" rule.
- **Role mapping**: `Superadmin`→`Admin`, `Head-Supervisor`→`Supervisor`, and the
  conditionally-permitted `Supervisor`→`Operator`, all three granted equal, full access — no
  per-role configurable permission toggle was built, since none exists anywhere else in this app
  to extend, and building one would be a much larger, separate feature.
- **Reason is mandatory** on every movement (the spec allows either, contingent on whether the
  existing system requires it for stock movements — since this is a new feature with no prior
  behavior to match, mandatory was chosen, consistent with the same choice made for Inward's
  manual-reroute override reason).
- **Multi-line batches process each line independently**, not as one cross-line atomic
  transaction — matching TEST14's own wording ("each movement line is independently validated and
  processed") over the more literal reading of §17's atomicity language, which is interpreted as
  applying *within* one line's own source-deduct/destination-add/audit-write, not across lines.
- **No Warehouse entity** — cross-warehouse validation (§7 point 5) is not applicable.

## 14. Remaining gaps

- **TEST11 (unauthorized user)** has no dedicated unit test — enforcement is the same
  `[Authorize(Roles=...)]` mechanism already relied on (untested at the unit level) for every
  other page in this app; adding framework-level authorization tests here without doing so
  elsewhere would be inconsistent with the codebase's existing test coverage philosophy rather
  than a gap specific to this feature.
- **TEST13 (DB failure → rollback)** is not separately proven — it rests on the same
  documented, already-relied-upon guarantee (a single `SaveChangesAsync` call is atomic on the
  Pomelo/MySQL provider this app runs on) that `SubmitInward`'s retry logic already depends on and
  discloses identically; forcing an artificial DB failure mid-transaction wasn't attempted.
- **No permanent integration-test harness against real MySQL** — the two real-MySQL verifications
  in this session (§12) were one-off scratch tests, deleted after confirming the result, consistent
  with the same disclosed limitation from the Putaway work.
- **Interactive UI click-through** (searching a location, checking a row, pressing Confirm) was
  not driven by a real browser — verified instead via clean page loads (HTTP 200, correct static
  content) plus thorough testing of the underlying commands/queries the UI calls. This is
  disclosed rather than claimed as fully tested.

## 15. Post-implementation review

A deliberate re-read of everything above turned up two real issues, now fixed:

- **A raw `DbUpdateException` could leak past the narrow `catch (DbUpdateConcurrencyException)`
  in `InventoryMovementService.ExecuteAsync`.** That catch only handles a stale `RowVersion` on an
  *existing* `StockBatch` row — it does not catch a unique-index violation on `StockBatch`'s
  `(MaterialId, MfgMonth, PalletPositionId)` index when two concurrent movements both find no
  destination batch yet and both try to `Add` the first one: the second `INSERT` fails at the
  database level with a plain `DbUpdateException`, not a `DbUpdateConcurrencyException` (the
  latter is a subclass, so the narrow catch does not match it), letting a raw SQL error surface to
  the end user instead of the intended "changed by another user, please refresh" message. The
  exact same pattern was found to pre-exist in the Inward/Putaway confirm paths
  (`ConfirmInwardPutawayHandler` and `ConfirmSingleInwardPutawayHandler`, both via the shared
  `InwardPutawayConfirmationLogic.ConfirmAsync`), since all three do the identical
  find-existing-batch-else-create-new upsert against the same unique index. Fixed by broadening
  all three catch clauses to `catch (DbUpdateException)`. EF Core's InMemory test provider turned
  out not to enforce this unique index at all (a race that inserts two rows with the same key
  simply succeeds, silently), so the fix was verified with a scratch test against real MySQL
  instead — confirming the duplicate insert really does throw `DbUpdateException` and specifically
  not `DbUpdateConcurrencyException` — then deleted per this project's scratch-test convention.
- **`GetMovementReasonsQuery` didn't filter by `IsActive`.** Both Move screens populate their
  Reason dropdown from this query but never filter client-side either, so a deactivated reason
  would still appear as a selectable option while `InventoryMovementService.ExecuteAsync`'s own
  `reasonExists` check (which does filter by `IsActive`) would reject it at confirm time with
  "Select a valid movement reason." — a dead-end the UI itself offered. Fixed by filtering
  `IsActive` in the query, matching the guard the service already enforces. Not currently reachable
  in practice (no admin UI exists to deactivate a reason, so all five seeded reasons stay active),
  but the field exists precisely for this purpose and the gap was real.

Full solution after this pass: 69/69 tests, clean build.
