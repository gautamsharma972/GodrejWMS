# Reports data dictionary

- **Warehouse**: `Warehouse.Code`; existing records are assigned to GCPL.
- **SKU**: `Material.MaterialNumber`; SKU name is `Material.Description`; design is `Material.DesignType`.
- **PKM**: `StockBatch.MfgMonth`, `InwardTransactionLine.MfgMonth`, `PulloutPick.MfgMonth`, or `StockMovement.MfgMonth`, stored as `yyyyMM`.
- **Current quantity**: `StockBatch.QuantityBoxes`.
- **Configured capacity**: `PalletPosition.CapacityBoxes`.
- **Reserved capacity**: pending, non-rejected inward putaway allocations. Unconfirmed pullouts do not reserve stock.
- **Available capacity**: configured capacity minus on-hand stock minus pending inward reservations.
- **Completed outward quantity**: picked quantity only after pullout confirmation.
- **Pending picking**: requested quantity minus confirmed picked quantity.
- **Last updated**: `StockBatch.UpdatedAt`, falling back to `CreatedAt`.

Batch number, PKD, physical quantity, and adjustment quantities are omitted because the domain does not currently persist them.
