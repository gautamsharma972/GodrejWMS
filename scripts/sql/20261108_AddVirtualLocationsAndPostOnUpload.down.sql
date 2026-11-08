START TRANSACTION;

                UPDATE RolePermissions SET IsGranted = 0
                WHERE (RoleName = 'Pickers' AND PermissionKey IN ('Menu.ViewPullout', 'Pullout.View', 'Pullout.Pick'))
                   OR (RoleName = 'Floor Supervisor' AND PermissionKey = 'Pullout.Pick');
                DELETE FROM PalletPositions WHERE IsVirtual = 1;
                DELETE FROM Racks WHERE IsSystem = 1;
                DELETE FROM ZoneTypes WHERE Code = 'Overflow';
            

DROP TABLE `StockLedgerEntries`;

DROP TABLE `VirtualLocationLogs`;

DROP TABLE `VirtualLocationSequences`;

ALTER TABLE `PalletPositions` DROP INDEX `IX_PalletPositions_IsVirtual_VirtualStatus`;

ALTER TABLE `InwardTransactions` DROP INDEX `IX_InwardTransactions_WarehouseId_UploadHash`;

ALTER TABLE `Racks` DROP COLUMN `IsSystem`;

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
WHERE `MigrationId` = '20261108100756_AddVirtualLocationsAndPostOnUpload';

COMMIT;

