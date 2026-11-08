START TRANSACTION;
ALTER TABLE `PulloutPicks` DROP FOREIGN KEY `FK_PulloutPicks_PalletPositions_MoveToPalletPositionId`;

ALTER TABLE `PulloutPicks` DROP INDEX `IX_PulloutPicks_MoveToPalletPositionId`;

ALTER TABLE `PulloutPicks` DROP COLUMN `MoveQuantityBoxes`;

ALTER TABLE `PulloutPicks` DROP COLUMN `MoveToPalletPositionId`;

DELETE FROM `__EFMigrationsHistory`
WHERE `MigrationId` = '20261108140331_AddPulloutPickPendingMove';

COMMIT;

