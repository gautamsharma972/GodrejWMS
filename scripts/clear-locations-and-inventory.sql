-- Clears production stock-on-hand, inward (GRN), outward (pullout), inventory-adjustment,
-- inventory-movement AND location/rack data from GodrejWMS, for ALL warehouses. This is a
-- superset of clear-inventory-inward-outward.sql - run this one instead (not both) when you also
-- want Racks/PalletPositions wiped, e.g. to re-lay out a warehouse from scratch.
--
-- Deletes, in FK-safe child-to-parent order:
--   PulloutPicks, PulloutTransactionLines, PulloutTransactions
--   InwardPutaways, InwardTransactionLines, InwardTransactions
--   InventoryAdjustmentLines, InventoryAdjustments
--   StockMovements (Inventory Movement log - references PalletPositions, must go before them)
--   StockBatches
--   PalletPositions
--   Racks
--
-- Explicitly NOT touched (left exactly as-is):
--   Materials (SKU master), WarehouseCategories/Regions/States/Cities/Warehouses, ActivityLogs
--   (audit trail - no FK to these tables), AspNetUsers/roles, RolePermissions, and every other
--   master/config table.
--
-- ============================================================================================
-- BEFORE RUNNING THIS AGAINST PRODUCTION:
--   1. Take a full backup. Example:
--        mysqldump -h <host> -u <user> -p --single-transaction --routines --triggers GodrejWMS \
--          > GodrejWMS-before-clear-$(date +%Y%m%d-%H%M%S).sql
--   2. Run during a maintenance window - the app should not be accepting inward/outward/
--      adjustment/movement submissions, or creating racks/locations, while this runs (no
--      app-level lock protects against that).
--   3. Review the "counts before" output below before letting the COMMIT happen.
-- ============================================================================================

-- Point this at the right schema if you're not already connected to it:
-- USE GodrejWMS;

-- Row counts before (sanity check - compare this to what you expect to lose).
SELECT 'PulloutTransactions' AS table_name, COUNT(*) AS row_count FROM PulloutTransactions
UNION ALL SELECT 'InwardTransactions', COUNT(*) FROM InwardTransactions
UNION ALL SELECT 'InventoryAdjustments', COUNT(*) FROM InventoryAdjustments
UNION ALL SELECT 'StockMovements', COUNT(*) FROM StockMovements
UNION ALL SELECT 'StockBatches', COUNT(*) FROM StockBatches
UNION ALL SELECT 'PalletPositions', COUNT(*) FROM PalletPositions
UNION ALL SELECT 'Racks', COUNT(*) FROM Racks;

START TRANSACTION;

DELETE FROM PulloutPicks;
DELETE FROM PulloutTransactionLines;
DELETE FROM PulloutTransactions;

DELETE FROM InwardPutaways;
DELETE FROM InwardTransactionLines;
DELETE FROM InwardTransactions;

DELETE FROM InventoryAdjustmentLines;
DELETE FROM InventoryAdjustments;

DELETE FROM StockMovements;

DELETE FROM StockBatches;

DELETE FROM PalletPositions;

DELETE FROM Racks;

-- Review the row counts below (should all be 0) BEFORE committing. If anything looks wrong,
-- run ROLLBACK; instead of COMMIT; and nothing above will have taken effect.
SELECT 'PulloutTransactions' AS table_name, COUNT(*) AS row_count FROM PulloutTransactions
UNION ALL SELECT 'InwardTransactions', COUNT(*) FROM InwardTransactions
UNION ALL SELECT 'InventoryAdjustments', COUNT(*) FROM InventoryAdjustments
UNION ALL SELECT 'StockMovements', COUNT(*) FROM StockMovements
UNION ALL SELECT 'StockBatches', COUNT(*) FROM StockBatches
UNION ALL SELECT 'PalletPositions', COUNT(*) FROM PalletPositions
UNION ALL SELECT 'Racks', COUNT(*) FROM Racks;

-- Uncomment ONE of the following two lines and run it manually once you've checked the counts
-- above. Left commented on purpose so this file can't accidentally be run start-to-finish and
-- auto-commit a mistake.
-- COMMIT;
-- ROLLBACK;

-- Optional, after COMMIT: reset AUTO_INCREMENT so new rows start from 1 again. Harmless to skip.
-- ALTER TABLE PulloutPicks AUTO_INCREMENT = 1;
-- ALTER TABLE PulloutTransactionLines AUTO_INCREMENT = 1;
-- ALTER TABLE PulloutTransactions AUTO_INCREMENT = 1;
-- ALTER TABLE InwardPutaways AUTO_INCREMENT = 1;
-- ALTER TABLE InwardTransactionLines AUTO_INCREMENT = 1;
-- ALTER TABLE InwardTransactions AUTO_INCREMENT = 1;
-- ALTER TABLE InventoryAdjustmentLines AUTO_INCREMENT = 1;
-- ALTER TABLE InventoryAdjustments AUTO_INCREMENT = 1;
-- ALTER TABLE StockMovements AUTO_INCREMENT = 1;
-- ALTER TABLE StockBatches AUTO_INCREMENT = 1;
-- ALTER TABLE PalletPositions AUTO_INCREMENT = 1;
-- ALTER TABLE Racks AUTO_INCREMENT = 1;
