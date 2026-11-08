START TRANSACTION;

                UPDATE RolePermissions SET IsGranted = 0
                WHERE (RoleName = 'Pickers' AND PermissionKey IN ('Menu.ViewPullout', 'Pullout.View', 'Pullout.Pick'))
                   OR (RoleName = 'Floor Supervisor' AND PermissionKey = 'Pullout.Pick');
                -- The ledger and the virtual-location log go with their tables. Virtual locations that nothing refers to are
                -- removed; any that still hold stock or history stay behind as ordinary locations, so no stock is ever lost.
                DELETE FROM StockLedgerEntries;
                DELETE FROM VirtualLocationLogs;
                DELETE p FROM PalletPositions p
                WHERE p.IsVirtual = 1
                  AND NOT EXISTS (SELECT 1 FROM StockBatches x WHERE x.PalletPositionId = p.Id)
                  AND NOT EXISTS (SELECT 1 FROM InwardPutaways x WHERE x.PalletPositionId = p.Id)
                  AND NOT EXISTS (SELECT 1 FROM PulloutPicks x WHERE x.PalletPositionId = p.Id)
                  AND NOT EXISTS (SELECT 1 FROM StockMovements x WHERE x.SourcePalletPositionId = p.Id OR x.DestinationPalletPositionId = p.Id);
                DELETE FROM Racks WHERE IsSystem = 1 AND NOT EXISTS (SELECT 1 FROM PalletPositions x WHERE x.RackId = Racks.Id);
                DELETE FROM ZoneTypes WHERE Code = 'Overflow' AND NOT EXISTS (SELECT 1 FROM PalletPositions x WHERE x.ZoneType = ZoneTypes.Id);
            

DROP TABLE `StockLedgerEntries`;

DROP TABLE `VirtualLocationLogs`;

DROP TABLE `VirtualLocationSequences`;

ALTER TABLE `PulloutTransactions` DROP INDEX `IX_PulloutTransactions_WarehouseId_UploadHash`;

ALTER TABLE `PalletPositions` DROP INDEX `IX_PalletPositions_IsVirtual_VirtualStatus`;

ALTER TABLE `InwardTransactions` DROP INDEX `IX_InwardTransactions_WarehouseId_UploadHash`;

ALTER TABLE `Racks` DROP COLUMN `IsSystem`;

ALTER TABLE `PulloutTransactions` DROP COLUMN `UploadHash`;

ALTER TABLE `PulloutPicks` DROP COLUMN `PickedAt`;

ALTER TABLE `PulloutPicks` DROP COLUMN `PickedByUserId`;

ALTER TABLE `PulloutPicks` DROP COLUMN `PickedByUserName`;

ALTER TABLE `PulloutPicks` DROP COLUMN `PickedQuantityBoxes`;

ALTER TABLE `PalletPositions` DROP COLUMN `IsVirtual`;

ALTER TABLE `PalletPositions` DROP COLUMN `LastReleasedAt`;

ALTER TABLE `PalletPositions` DROP COLUMN `VirtualStatus`;

ALTER TABLE `InwardTransactions` DROP COLUMN `PostedAt`;

ALTER TABLE `InwardTransactions` DROP COLUMN `UploadHash`;

DELETE FROM `__EFMigrationsHistory`
WHERE `MigrationId` = '20261108101407_AddVirtualLocationsAndPostOnUpload';

COMMIT;

