START TRANSACTION;
CREATE TABLE `BrandLocationPreferences` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `BrandId` int NOT NULL,
    `FromPalletPositionId` int NOT NULL,
    `ToPalletPositionId` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `CreatedByUserId` varchar(450) CHARACTER SET utf8mb4 NULL,
    `IsActive` tinyint(1) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `UpdatedByUserId` varchar(450) CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_BrandLocationPreferences` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_BrandLocationPreferences_Brands_BrandId` FOREIGN KEY (`BrandId`) REFERENCES `Brands` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_BrandLocationPreferences_PalletPositions_FromPalletPositionId` FOREIGN KEY (`FromPalletPositionId`) REFERENCES `PalletPositions` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_BrandLocationPreferences_PalletPositions_ToPalletPositionId` FOREIGN KEY (`ToPalletPositionId`) REFERENCES `PalletPositions` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE UNIQUE INDEX `IX_BrandLocationPreferences_BrandId_FromPalletPositionId_ToPall~` ON `BrandLocationPreferences` (`BrandId`, `FromPalletPositionId`, `ToPalletPositionId`);

CREATE INDEX `IX_BrandLocationPreferences_FromPalletPositionId` ON `BrandLocationPreferences` (`FromPalletPositionId`);

CREATE INDEX `IX_BrandLocationPreferences_ToPalletPositionId` ON `BrandLocationPreferences` (`ToPalletPositionId`);


INSERT INTO BrandLocationPreferences (BrandId, FromPalletPositionId, ToPalletPositionId, IsActive,
                                      CreatedAt, CreatedByUserId, UpdatedAt, UpdatedByUserId)
SELECT t.BrandId, t.FromPalletPositionId, t.ToPalletPositionId, g.Active,
       t.CreatedAt, t.CreatedByUserId, t.UpdatedAt, t.UpdatedByUserId
FROM AopDescriptionLocationPreferences t
JOIN (SELECT MIN(r.Id) AS Id,
             MAX(CASE WHEN r.IsActive = 1 OR r.Id IN (SELECT e.TargetPreferenceId FROM AopDescriptionLocationPreferenceMigrationExceptions e WHERE e.`Action` = 'Deactivated')
                      THEN 1 ELSE 0 END) AS Active
      FROM AopDescriptionLocationPreferences r WHERE r.BrandId IS NOT NULL
      GROUP BY r.BrandId, r.FromPalletPositionId, r.ToPalletPositionId) g ON g.Id = t.Id;


INSERT INTO BrandLocationPreferences (BrandId, FromPalletPositionId, ToPalletPositionId, IsActive, CreatedAt)
SELECT e.BrandId, e.FromPalletPositionId, e.ToPalletPositionId, e.IsActive, UTC_TIMESTAMP(6)
FROM AopDescriptionLocationPreferenceMigrationExceptions e
WHERE e.`Action` IN ('NotMigrated', 'MergedDuplicate') AND e.BrandId IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM BrandLocationPreferences p
                  WHERE p.BrandId = e.BrandId AND p.FromPalletPositionId = e.FromPalletPositionId AND p.ToPalletPositionId = e.ToPalletPositionId);

DROP TABLE `AopDescriptionLocationPreferenceMigrationExceptions`;

DROP TABLE `AopDescriptionLocationPreferences`;

DELETE FROM `__EFMigrationsHistory`
WHERE `MigrationId` = '20261031062704_AopDescriptionLocationPreferences';

COMMIT;

