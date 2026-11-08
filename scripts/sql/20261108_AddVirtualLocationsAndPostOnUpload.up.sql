START TRANSACTION;

                INSERT INTO ZoneTypes (Id, Code, DisplayName, Description, SortOrder, IsActive, CreatedAt)
                SELECT 5, 'Overflow', 'Overflow', 'Auto-generated virtual overflow locations', 90, 1, UTC_TIMESTAMP(6)
                FROM DUAL
                WHERE NOT EXISTS (SELECT 1 FROM ZoneTypes WHERE Code = 'Overflow');

                UPDATE RolePermissions SET IsGranted = 1
                WHERE (RoleName = 'Pickers' AND PermissionKey IN ('Menu.ViewPullout', 'Pullout.View', 'Pullout.Pick'))
                   OR (RoleName = 'Floor Supervisor' AND PermissionKey = 'Pullout.Pick');
            

ALTER TABLE `Racks` ADD `IsSystem` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `PulloutPicks` ADD `PickedAt` datetime(6) NULL;

ALTER TABLE `PulloutPicks` ADD `PickedByUserId` varchar(450) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `PulloutPicks` ADD `PickedByUserName` varchar(256) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `PulloutPicks` ADD `PickedQuantityBoxes` decimal(14,3) NULL;

ALTER TABLE `PalletPositions` ADD `IsVirtual` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `PalletPositions` ADD `LastReleasedAt` datetime(6) NULL;

ALTER TABLE `PalletPositions` ADD `VirtualStatus` varchar(10) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `InwardTransactions` ADD `PostedAt` datetime(6) NULL;

ALTER TABLE `InwardTransactions` ADD `UploadHash` varchar(64) CHARACTER SET utf8mb4 NULL;

CREATE TABLE `StockLedgerEntries` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `PostingNumber` varchar(60) CHARACTER SET utf8mb4 NOT NULL,
    `EntryType` varchar(30) CHARACTER SET utf8mb4 NOT NULL,
    `WarehouseId` int NOT NULL,
    `MaterialId` int NOT NULL,
    `MfgMonth` int NOT NULL,
    `StockSubtypeId` int NOT NULL,
    `PalletPositionId` int NOT NULL,
    `QuantityBoxes` decimal(14,3) NOT NULL,
    `BalanceAfterBoxes` decimal(14,3) NOT NULL,
    `ReferenceType` varchar(30) CHARACTER SET utf8mb4 NULL,
    `ReferenceNumber` varchar(60) CHARACTER SET utf8mb4 NULL,
    `Notes` varchar(300) CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `CreatedByUserId` varchar(450) CHARACTER SET utf8mb4 NULL,
    `CreatedByUserName` varchar(256) CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_StockLedgerEntries` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_StockLedgerEntries_Materials_MaterialId` FOREIGN KEY (`MaterialId`) REFERENCES `Materials` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_StockLedgerEntries_PalletPositions_PalletPositionId` FOREIGN KEY (`PalletPositionId`) REFERENCES `PalletPositions` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE TABLE `VirtualLocationLogs` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `PalletPositionId` int NOT NULL,
    `InwardTransactionId` int NULL,
    `InwardPutawayId` int NULL,
    `AssignedAt` datetime(6) NOT NULL,
    `ReleasedAt` datetime(6) NULL,
    CONSTRAINT `PK_VirtualLocationLogs` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_VirtualLocationLogs_PalletPositions_PalletPositionId` FOREIGN KEY (`PalletPositionId`) REFERENCES `PalletPositions` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE TABLE `VirtualLocationSequences` (
    `WarehouseId` int NOT NULL,
    `LastValue` int NOT NULL,
    CONSTRAINT `PK_VirtualLocationSequences` PRIMARY KEY (`WarehouseId`)
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_PalletPositions_IsVirtual_VirtualStatus` ON `PalletPositions` (`IsVirtual`, `VirtualStatus`);

CREATE UNIQUE INDEX `IX_InwardTransactions_WarehouseId_UploadHash` ON `InwardTransactions` (`WarehouseId`, `UploadHash`);

CREATE INDEX `IX_StockLedgerEntries_MaterialId_CreatedAt` ON `StockLedgerEntries` (`MaterialId`, `CreatedAt`);

CREATE INDEX `IX_StockLedgerEntries_PalletPositionId_CreatedAt` ON `StockLedgerEntries` (`PalletPositionId`, `CreatedAt`);

CREATE INDEX `IX_StockLedgerEntries_PostingNumber` ON `StockLedgerEntries` (`PostingNumber`);

CREATE INDEX `IX_StockLedgerEntries_ReferenceType_ReferenceNumber` ON `StockLedgerEntries` (`ReferenceType`, `ReferenceNumber`);

CREATE INDEX `IX_StockLedgerEntries_WarehouseId_CreatedAt` ON `StockLedgerEntries` (`WarehouseId`, `CreatedAt`);

CREATE INDEX `IX_VirtualLocationLogs_InwardTransactionId` ON `VirtualLocationLogs` (`InwardTransactionId`);

CREATE INDEX `IX_VirtualLocationLogs_PalletPositionId_ReleasedAt` ON `VirtualLocationLogs` (`PalletPositionId`, `ReleasedAt`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261108100756_AddVirtualLocationsAndPostOnUpload', '9.0.11');

COMMIT;

