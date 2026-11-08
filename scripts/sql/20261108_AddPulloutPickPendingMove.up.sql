START TRANSACTION;
ALTER TABLE `PulloutPicks` ADD `MoveQuantityBoxes` decimal(14,3) NULL;

ALTER TABLE `PulloutPicks` ADD `MoveToPalletPositionId` int NULL;

CREATE INDEX `IX_PulloutPicks_MoveToPalletPositionId` ON `PulloutPicks` (`MoveToPalletPositionId`);

ALTER TABLE `PulloutPicks` ADD CONSTRAINT `FK_PulloutPicks_PalletPositions_MoveToPalletPositionId` FOREIGN KEY (`MoveToPalletPositionId`) REFERENCES `PalletPositions` (`Id`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261108140331_AddPulloutPickPendingMove', '9.0.11');

COMMIT;

