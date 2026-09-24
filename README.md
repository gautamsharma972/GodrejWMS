# Godrej WMS — Rack/Pallet Warehouse Inventory System

A .NET 10 Blazor Web App (Interactive Server) for managing rack/pallet-based warehouse
inventory, built from the business logic captured in `logic_rackproject.xlsx`.

## What this app does

The source workbook describes a warehouse where stock lives in **pallet positions** inside
**racks**, addressed as `A-01-01` (Rack A, Column 01, Level 01). Four workflows came out of it:

1. **Material Master** — SKU catalogue (dimensions, weight, pack size, MRP, season) with Excel import.
2. **Racks & Pallet Positions** — define a rack's grid (columns x levels); the app auto-generates
   every pallet position and its location code. A visual, color-coded layout shows live occupancy.
3. **Inward (Goods Receipt)** — upload/enter Material Code + Qty + Mfg Month. A **put-away engine**
   tops up an existing pallet position holding the same material/mfg-month if it has room, then
   spills into the next free position (Rack → Column → Level order) once that position fills up —
   this is exactly the behaviour visible in the workbook's "inward" sheet example.
4. **Pullout (Picking)** — upload/enter Material Code + Qty. A **FEFO engine**
   (First-Manufactured-First-Out) drains the oldest manufacturing month first, across as many
   pallet positions as needed, matching "Format invent pullout-download".

A **Inventory Master** screen shows the live Material × Mfg-Month × Pallet-Position × Qty grid
(exportable to Excel in the workbook's exact column layout), and a **Dashboard** shows warehouse
capacity utilization and stock-by-season KPIs.

## Architecture

Clean Architecture, four layers, each with a single compile-time direction of dependency:

```
GodrejWMS.Web            → Blazor Web App (Interactive Server), Identity UI, Program.cs wiring
GodrejWMS.Infrastructure → EF Core + MySQL (Pomelo), ASP.NET Core Identity, Excel I/O, allocation engines
GodrejWMS.Application    → CQRS use-cases (MediatR), FluentValidation, DTOs — no EF/ASP.NET dependency
GodrejWMS.Domain         → Entities, enums — no dependencies on anything
```

- **CQRS via MediatR**, vertical-slice style: each use-case file under
  `Application/Features/<Feature>/{Commands,Queries}` bundles its Command/Query, FluentValidation
  validator, and Handler together (the same pattern used by the Jason Taylor Clean Architecture
  template) — one place to read a whole feature instead of hunting across folders.
- **`IApplicationDbContext`** is the only thing Application knows about persistence; the concrete
  EF Core `AppDbContext` lives in Infrastructure.
- **Put-away and FEFO logic** are their own services (`IPalletAllocationService`,
  `IPulloutAllocationService`) so they're independently unit-testable — see
  `tests/GodrejWMS.Infrastructure.Tests`, which asserts the top-up→spillover and FEFO-ordering
  behaviour directly against the rules described above.
- **ASP.NET Core Identity** with three roles: `Admin` (materials, racks, users), `Supervisor`
  (full read + reporting, plus put-away execution), `Operator` (day-to-day inward/pullout). Pages
  are `[Authorize]`-gated; Inward requires `Admin`, `Operator`, or `Supervisor`; Pullout requires
  `Admin` or `Operator`; rack/material edits require `Admin`/`Supervisor`.

## Tech stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10 |
| UI | Blazor Web App, Interactive Server render mode |
| Database | MySQL via Pomelo.EntityFrameworkCore.MySql (EF Core 9.x — see note below) |
| CQRS | MediatR 14 |
| Validation | FluentValidation |
| Auth | ASP.NET Core Identity + role-based authorization |
| Excel I/O | ClosedXML |
| Logging | Serilog (console + rolling file) |
| Tests | xUnit, EF Core InMemory provider |

**Why EF Core 9.x, not 10.x, in Infrastructure/Web's DB packages:** Pomelo's MySQL provider had not
published an EF Core 10-compatible release at the time this was built. The whole solution still
targets `net10.0`; only the EF Core/Identity.EntityFrameworkCore package *versions* are pinned to
9.0.11 to match Pomelo 9.0.0. Bump these once Pomelo ships EF10 support.

## Domain model

| Entity | Purpose |
|---|---|
| `Material` | SKU master (MaterialMaster sheet): pack size, MRP, dimensions, weight, season. `BoxWeightKg` and `VolumeMm3` are computed the same way the workbook's `N = L * D` formula does. |
| `Rack` / `PalletPosition` | A rack is a Columns × Levels grid; each `PalletPosition` has a unique `LocationCode` (`A-01-01`) and a boxes capacity (default 40, per the workbook's "Pallet capacity = 40 boxes" note). |
| `StockBatch` | One live "cell": Material + Mfg Month + PalletPosition + Qty. This is the Inventory Master row shape. |
| `InwardTransaction` / `InwardTransactionLine` | Audit trail of goods-receipt batches and what the put-away engine did with each line. |
| `PulloutTransaction` / `PulloutTransactionLine` / `PulloutPick` | Audit trail of pick requests and the FEFO picks that satisfied them (can span multiple pallet positions/mfg-months). |

### Assumptions made where the workbook was ambiguous

- **Flat physical labels** (`A1`, `B14`, …): the workbook shows Rack A and Rack B using two
  *different* ad-hoc numbering schemes (Rack A serpentines top-to-bottom; Rack B groups by column
  for levels 1–3 then serpentines for 4–5). Rather than guess a "universal" formula, the app
  auto-generates only the systematic `LocationCode`; `FlatLabel` is a free-text field editable per
  position from the rack layout screen.
- **Pallet capacity** is treated as a property of the physical `PalletPosition` (default 40 boxes),
  not the `Material`. `Material.PalletCapacityBoxes` is kept as reference data (mirroring the
  "Pallet Size" column) but doesn't drive the allocation algorithm — a position's real capacity does.
- **"Total Stock in CFB"** is modelled as a plain decimal quantity in boxes (`QuantityBoxes`). The
  workbook's sample rows reuse the same placeholder value (0.008) everywhere, so no unit conversion
  logic could be reverse-engineered from the data; treat the column as whatever box-equivalent unit
  your warehouse team uses.
- **Homogeneous pallets**: the put-away engine happily lets a position hold more than one
  material/mfg-month batch if there's room, but always fills existing same-material/mfg-month
  batches first — matching the "MAR/APR both land in A-01-01" example.

## Running locally

### Prerequisites
- .NET 10 SDK
- A MySQL 8.x server (local or remote)

### 1. Configure the connection string

Edit `src/GodrejWMS.Web/appsettings.json` (or use `dotnet user-secrets` for local dev):

```json
"ConnectionStrings": {
  "Default": "Server=localhost;Port=3306;Database=GodrejWMS;User=root;Password=YOUR_PASSWORD;TreatTinyAsBoolean=true;"
}
```

### 2. Apply migrations

```bash
dotnet ef database update --project src/GodrejWMS.Infrastructure --startup-project src/GodrejWMS.Web
```

(The app also auto-applies pending migrations at startup via `DbInitializer`, so this step is
optional for local dev — useful mainly if you want to inspect the schema first.)

### 3. Run

```bash
dotnet run --project src/GodrejWMS.Web
```

On first run, `DbInitializer` seeds:
- Roles: `Admin`, `Supervisor`, `Operator`
- A default admin account — **change the password after first login**:
  - Email: `admin@godrejwms.local` (configurable under `DefaultAdmin` in appsettings)
  - Password: `Godrej@WMS123!`
- Two demo racks (`A`, `B`, 5×5 grid each) and a handful of demo materials, so the app is
  immediately explorable. Demo data is only seeded when the `Racks` table is empty — it never
  overwrites real data.

### Tests

```bash
dotnet test GodrejWMS.slnx
```

## Documentation

- [`docs/PUTAWAY.md`](docs/PUTAWAY.md) — the Inward → Putaway redesign: algorithm, DB/API/UI
  changes, authorization, tests, assumptions, and known gaps.
- [`docs/INVENTORY_MOVEMENT.md`](docs/INVENTORY_MOVEMENT.md) — Move by Location / Move by SKU:
  business rules, DB/API/UI changes, authorization, tests, assumptions, and known gaps.

## Project structure

```
GodrejWMS.slnx
src/
  GodrejWMS.Domain/            Entities, enums
  GodrejWMS.Application/       CQRS use-cases, DTOs, interfaces, validators
  GodrejWMS.Infrastructure/    EF Core + MySQL, Identity, Excel service, allocation engines
  GodrejWMS.Web/                Blazor Web App, Identity UI, Program.cs
tests/
  GodrejWMS.Domain.Tests/
  GodrejWMS.Application.Tests/
  GodrejWMS.Infrastructure.Tests/   put-away + FEFO behavioural tests
```

## Production hardening TODO

This scaffold is deliberately runnable out of the box; before going live, revisit:
- Disable public self-registration (`Components/Account/Pages/Register.razor`) or gate it behind
  `Admin`-only user management.
- Move the default admin password and DB credentials to a secret store (Key Vault / environment
  variables), not `appsettings.json`.
- Configure a real `IEmailSender<ApplicationUser>` (currently a no-op) if you want email
  confirmation / password-reset emails.
- Review `DbInitializer`'s demo-data seeding — remove it once real Material Master / rack data is imported.
