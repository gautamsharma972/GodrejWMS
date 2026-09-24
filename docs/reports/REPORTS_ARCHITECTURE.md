# Reports architecture

Reports follow the existing Blazor Server → MediatR query → `IApplicationDbContext` → EF Core pattern. Report queries are read-only, use `AsNoTracking`, project DTOs, and apply filtering, sorting, and pagination before materialization.

`GetOperationalReportQuery` is the shared query surface for inventory, inward, outward, movement, ledger, and capacity registers. The existing specialist analytics remain available on the Inventory, Inward, Pullout, and Movement report pages.

Every operational report is scoped by warehouse. EF global filters protect warehouse-owned racks, locations, stock, inward transactions, pullouts, picks, and movements. Admin users see all warehouses; other report users see only `UserWarehouse` assignments.

The default warehouse is `GCPL` (ID 1). The migration assigns existing warehouse-owned records to GCPL. Startup assigns existing users to GCPL so the schema change does not remove their existing access.

The Operational Reports page refreshes its current filtered page every 30 seconds. Refresh does not mutate inventory.
