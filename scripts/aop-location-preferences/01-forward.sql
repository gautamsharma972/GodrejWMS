START TRANSACTION;
CREATE TABLE `AopDescriptionLocationPreferences` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `AopDescriptionId` int NOT NULL,
    `BrandId` int NULL,
    `FromPalletPositionId` int NOT NULL,
    `ToPalletPositionId` int NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `CreatedByUserId` varchar(450) CHARACTER SET utf8mb4 NULL,
    `UpdatedAt` datetime(6) NULL,
    `UpdatedByUserId` varchar(450) CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_AopDescriptionLocationPreferences` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_AopDescriptionLocationPreferences_AopDescriptions_AopDescrip~` FOREIGN KEY (`AopDescriptionId`) REFERENCES `AopDescriptions` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_AopDescriptionLocationPreferences_Brands_BrandId` FOREIGN KEY (`BrandId`) REFERENCES `Brands` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_AopDescriptionLocationPreferences_PalletPositions_FromPallet~` FOREIGN KEY (`FromPalletPositionId`) REFERENCES `PalletPositions` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_AopDescriptionLocationPreferences_PalletPositions_ToPalletPo~` FOREIGN KEY (`ToPalletPositionId`) REFERENCES `PalletPositions` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE UNIQUE INDEX `IX_AopDescriptionLocationPreferences_AopDescriptionId_FromPalle~` ON `AopDescriptionLocationPreferences` (`AopDescriptionId`, `FromPalletPositionId`, `ToPalletPositionId`);

CREATE INDEX `IX_AopDescriptionLocationPreferences_BrandId` ON `AopDescriptionLocationPreferences` (`BrandId`);

CREATE INDEX `IX_AopDescriptionLocationPreferences_FromPalletPositionId` ON `AopDescriptionLocationPreferences` (`FromPalletPositionId`);

CREATE INDEX `IX_AopDescriptionLocationPreferences_ToPalletPositionId` ON `AopDescriptionLocationPreferences` (`ToPalletPositionId`);

CREATE TABLE `AopDescriptionLocationPreferenceMigrationExceptions` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Action` varchar(30) CHARACTER SET utf8mb4 NOT NULL,
    `Reason` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
    `SourcePreferenceId` int NULL,
    `TargetPreferenceId` int NULL,
    `BrandId` int NULL,
    `BrandCode` varchar(50) CHARACTER SET utf8mb4 NULL,
    `AopDescriptionId` int NULL,
    `AopCode` varchar(50) CHARACTER SET utf8mb4 NULL,
    `FromPalletPositionId` int NOT NULL,
    `ToPalletPositionId` int NOT NULL,
    `FromLocationCode` varchar(50) CHARACTER SET utf8mb4 NULL,
    `ToLocationCode` varchar(50) CHARACTER SET utf8mb4 NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_AopDescriptionLocationPreferenceMigrationExceptions` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE AopDescriptionLocationPreferenceMigrationStage AS
SELECT p.Id AS PrefId, p.BrandId, a.AopDescriptionId, p.FromPalletPositionId, p.ToPalletPositionId,
       p.IsActive, p.CreatedAt, p.CreatedByUserId, p.UpdatedAt, p.UpdatedByUserId
FROM BrandLocationPreferences p
JOIN (SELECT DISTINCT BrandId, AopDescriptionId FROM Materials
      WHERE BrandId IS NOT NULL AND AopDescriptionId IS NOT NULL) a ON a.BrandId = p.BrandId;


INSERT INTO AopDescriptionLocationPreferences (AopDescriptionId, BrandId, FromPalletPositionId, ToPalletPositionId, IsActive,
                 CreatedAt, CreatedByUserId, UpdatedAt, UpdatedByUserId)
SELECT s.AopDescriptionId, s.BrandId, s.FromPalletPositionId, s.ToPalletPositionId, s.IsActive,
       s.CreatedAt, s.CreatedByUserId, s.UpdatedAt, s.UpdatedByUserId
FROM AopDescriptionLocationPreferenceMigrationStage s
WHERE s.PrefId = (SELECT MIN(x.PrefId) FROM AopDescriptionLocationPreferenceMigrationStage x
                  WHERE x.AopDescriptionId = s.AopDescriptionId
                    AND x.FromPalletPositionId = s.FromPalletPositionId AND x.ToPalletPositionId = s.ToPalletPositionId)
ORDER BY s.PrefId, s.AopDescriptionId;


INSERT INTO AopDescriptionLocationPreferenceMigrationExceptions (`Action`, Reason, SourcePreferenceId, TargetPreferenceId, BrandId, BrandCode, AopDescriptionId, AopCode,
                  FromPalletPositionId, ToPalletPositionId, FromLocationCode, ToLocationCode, IsActive, CreatedAt)
SELECT 'NotMigrated', 'Brand has no Material with an AOP Description, so this mapping cannot be converted. Create an AOP Description mapping manually if it is still needed.',
       p.Id, NULL, p.BrandId, b.Code, NULL, NULL, p.FromPalletPositionId, p.ToPalletPositionId, pf.LocationCode, pt.LocationCode, p.IsActive, UTC_TIMESTAMP(6)
FROM BrandLocationPreferences p
JOIN Brands b ON b.Id = p.BrandId
JOIN PalletPositions pf ON pf.Id = p.FromPalletPositionId
JOIN PalletPositions pt ON pt.Id = p.ToPalletPositionId
WHERE NOT EXISTS (SELECT 1 FROM Materials m WHERE m.BrandId = p.BrandId AND m.AopDescriptionId IS NOT NULL);


INSERT INTO AopDescriptionLocationPreferenceMigrationExceptions (`Action`, Reason, SourcePreferenceId, TargetPreferenceId, BrandId, BrandCode, AopDescriptionId, AopCode,
                  FromPalletPositionId, ToPalletPositionId, FromLocationCode, ToLocationCode, IsActive, CreatedAt)
SELECT 'MergedDuplicate', CONCAT('Same AOP Description and range as the mapping migrated from old mapping #', w.WinnerId, '; merged.'),
       s.PrefId, NULL, s.BrandId, b.Code, s.AopDescriptionId, a.Code, s.FromPalletPositionId, s.ToPalletPositionId, pf.LocationCode, pt.LocationCode, s.IsActive, UTC_TIMESTAMP(6)
FROM AopDescriptionLocationPreferenceMigrationStage s
JOIN (SELECT AopDescriptionId, FromPalletPositionId, ToPalletPositionId, MIN(PrefId) AS WinnerId FROM AopDescriptionLocationPreferenceMigrationStage
      GROUP BY AopDescriptionId, FromPalletPositionId, ToPalletPositionId) w
  ON w.AopDescriptionId = s.AopDescriptionId AND w.FromPalletPositionId = s.FromPalletPositionId AND w.ToPalletPositionId = s.ToPalletPositionId
JOIN Brands b ON b.Id = s.BrandId
JOIN AopDescriptions a ON a.Id = s.AopDescriptionId
JOIN PalletPositions pf ON pf.Id = s.FromPalletPositionId
JOIN PalletPositions pt ON pt.Id = s.ToPalletPositionId
WHERE s.PrefId <> w.WinnerId;


INSERT INTO AopDescriptionLocationPreferenceMigrationExceptions (`Action`, Reason, SourcePreferenceId, TargetPreferenceId, BrandId, BrandCode, AopDescriptionId, AopCode,
                  FromPalletPositionId, ToPalletPositionId, FromLocationCode, ToLocationCode, IsActive, CreatedAt)
SELECT 'Deactivated',
       CONCAT('Overlaps active mapping #', v.RootId, ' for the same AOP Description on the same rack; deactivated. Edit one of the ranges, then re-activate.'),
       NULL, n.Id, n.BrandId, b.Code, n.AopDescriptionId, a.Code,
       n.FromPalletPositionId, n.ToPalletPositionId, pf.LocationCode, pt.LocationCode, 1, UTC_TIMESTAMP(6)
FROM (
SELECT n2.Id AS VictimId, MIN(n1.Id) AS RootId
FROM AopDescriptionLocationPreferences n2
JOIN PalletPositions f2 ON f2.Id = n2.FromPalletPositionId
JOIN PalletPositions t2 ON t2.Id = n2.ToPalletPositionId
JOIN AopDescriptionLocationPreferences n1 ON n1.AopDescriptionId = n2.AopDescriptionId AND n1.IsActive = 1 AND n1.Id < n2.Id
JOIN PalletPositions f1 ON f1.Id = n1.FromPalletPositionId
JOIN PalletPositions t1 ON t1.Id = n1.ToPalletPositionId
WHERE n2.IsActive = 1 AND f1.RackId = f2.RackId AND f1.`Column` <= t2.`Column` AND t1.`Column` >= f2.`Column` AND f1.`Level` <= t2.`Level` AND t1.`Level` >= f2.`Level`
AND NOT EXISTS (
    SELECT 1 FROM AopDescriptionLocationPreferences n0
    JOIN PalletPositions f0 ON f0.Id = n0.FromPalletPositionId
    JOIN PalletPositions t0 ON t0.Id = n0.ToPalletPositionId
    WHERE n0.AopDescriptionId = n1.AopDescriptionId AND n0.IsActive = 1 AND n0.Id < n1.Id AND f0.RackId = f1.RackId AND f0.`Column` <= t1.`Column` AND t0.`Column` >= f1.`Column` AND f0.`Level` <= t1.`Level` AND t0.`Level` >= f1.`Level`)
GROUP BY n2.Id) v
JOIN AopDescriptionLocationPreferences n ON n.Id = v.VictimId
LEFT JOIN Brands b ON b.Id = n.BrandId
JOIN AopDescriptions a ON a.Id = n.AopDescriptionId
JOIN PalletPositions pf ON pf.Id = n.FromPalletPositionId
JOIN PalletPositions pt ON pt.Id = n.ToPalletPositionId;

UPDATE AopDescriptionLocationPreferences SET IsActive = 0 WHERE Id IN (SELECT VictimId FROM (
SELECT n2.Id AS VictimId, MIN(n1.Id) AS RootId
FROM AopDescriptionLocationPreferences n2
JOIN PalletPositions f2 ON f2.Id = n2.FromPalletPositionId
JOIN PalletPositions t2 ON t2.Id = n2.ToPalletPositionId
JOIN AopDescriptionLocationPreferences n1 ON n1.AopDescriptionId = n2.AopDescriptionId AND n1.IsActive = 1 AND n1.Id < n2.Id
JOIN PalletPositions f1 ON f1.Id = n1.FromPalletPositionId
JOIN PalletPositions t1 ON t1.Id = n1.ToPalletPositionId
WHERE n2.IsActive = 1 AND f1.RackId = f2.RackId AND f1.`Column` <= t2.`Column` AND t1.`Column` >= f2.`Column` AND f1.`Level` <= t2.`Level` AND t1.`Level` >= f2.`Level`
AND NOT EXISTS (
    SELECT 1 FROM AopDescriptionLocationPreferences n0
    JOIN PalletPositions f0 ON f0.Id = n0.FromPalletPositionId
    JOIN PalletPositions t0 ON t0.Id = n0.ToPalletPositionId
    WHERE n0.AopDescriptionId = n1.AopDescriptionId AND n0.IsActive = 1 AND n0.Id < n1.Id AND f0.RackId = f1.RackId AND f0.`Column` <= t1.`Column` AND t0.`Column` >= f1.`Column` AND f0.`Level` <= t1.`Level` AND t0.`Level` >= f1.`Level`)
GROUP BY n2.Id) vv);


INSERT INTO AopDescriptionLocationPreferenceMigrationExceptions (`Action`, Reason, SourcePreferenceId, TargetPreferenceId, BrandId, BrandCode, AopDescriptionId, AopCode,
                  FromPalletPositionId, ToPalletPositionId, FromLocationCode, ToLocationCode, IsActive, CreatedAt)
SELECT 'Deactivated',
       CONCAT('Overlaps active mapping #', v.RootId, ' for the same AOP Description on the same rack; deactivated. Edit one of the ranges, then re-activate.'),
       NULL, n.Id, n.BrandId, b.Code, n.AopDescriptionId, a.Code,
       n.FromPalletPositionId, n.ToPalletPositionId, pf.LocationCode, pt.LocationCode, 1, UTC_TIMESTAMP(6)
FROM (
SELECT n2.Id AS VictimId, MIN(n1.Id) AS RootId
FROM AopDescriptionLocationPreferences n2
JOIN PalletPositions f2 ON f2.Id = n2.FromPalletPositionId
JOIN PalletPositions t2 ON t2.Id = n2.ToPalletPositionId
JOIN AopDescriptionLocationPreferences n1 ON n1.AopDescriptionId = n2.AopDescriptionId AND n1.IsActive = 1 AND n1.Id < n2.Id
JOIN PalletPositions f1 ON f1.Id = n1.FromPalletPositionId
JOIN PalletPositions t1 ON t1.Id = n1.ToPalletPositionId
WHERE n2.IsActive = 1 AND f1.RackId = f2.RackId AND f1.`Column` <= t2.`Column` AND t1.`Column` >= f2.`Column` AND f1.`Level` <= t2.`Level` AND t1.`Level` >= f2.`Level`
AND NOT EXISTS (
    SELECT 1 FROM AopDescriptionLocationPreferences n0
    JOIN PalletPositions f0 ON f0.Id = n0.FromPalletPositionId
    JOIN PalletPositions t0 ON t0.Id = n0.ToPalletPositionId
    WHERE n0.AopDescriptionId = n1.AopDescriptionId AND n0.IsActive = 1 AND n0.Id < n1.Id AND f0.RackId = f1.RackId AND f0.`Column` <= t1.`Column` AND t0.`Column` >= f1.`Column` AND f0.`Level` <= t1.`Level` AND t0.`Level` >= f1.`Level`)
GROUP BY n2.Id) v
JOIN AopDescriptionLocationPreferences n ON n.Id = v.VictimId
LEFT JOIN Brands b ON b.Id = n.BrandId
JOIN AopDescriptions a ON a.Id = n.AopDescriptionId
JOIN PalletPositions pf ON pf.Id = n.FromPalletPositionId
JOIN PalletPositions pt ON pt.Id = n.ToPalletPositionId;

UPDATE AopDescriptionLocationPreferences SET IsActive = 0 WHERE Id IN (SELECT VictimId FROM (
SELECT n2.Id AS VictimId, MIN(n1.Id) AS RootId
FROM AopDescriptionLocationPreferences n2
JOIN PalletPositions f2 ON f2.Id = n2.FromPalletPositionId
JOIN PalletPositions t2 ON t2.Id = n2.ToPalletPositionId
JOIN AopDescriptionLocationPreferences n1 ON n1.AopDescriptionId = n2.AopDescriptionId AND n1.IsActive = 1 AND n1.Id < n2.Id
JOIN PalletPositions f1 ON f1.Id = n1.FromPalletPositionId
JOIN PalletPositions t1 ON t1.Id = n1.ToPalletPositionId
WHERE n2.IsActive = 1 AND f1.RackId = f2.RackId AND f1.`Column` <= t2.`Column` AND t1.`Column` >= f2.`Column` AND f1.`Level` <= t2.`Level` AND t1.`Level` >= f2.`Level`
AND NOT EXISTS (
    SELECT 1 FROM AopDescriptionLocationPreferences n0
    JOIN PalletPositions f0 ON f0.Id = n0.FromPalletPositionId
    JOIN PalletPositions t0 ON t0.Id = n0.ToPalletPositionId
    WHERE n0.AopDescriptionId = n1.AopDescriptionId AND n0.IsActive = 1 AND n0.Id < n1.Id AND f0.RackId = f1.RackId AND f0.`Column` <= t1.`Column` AND t0.`Column` >= f1.`Column` AND f0.`Level` <= t1.`Level` AND t0.`Level` >= f1.`Level`)
GROUP BY n2.Id) vv);


INSERT INTO AopDescriptionLocationPreferenceMigrationExceptions (`Action`, Reason, SourcePreferenceId, TargetPreferenceId, BrandId, BrandCode, AopDescriptionId, AopCode,
                  FromPalletPositionId, ToPalletPositionId, FromLocationCode, ToLocationCode, IsActive, CreatedAt)
SELECT 'Deactivated',
       CONCAT('Overlaps active mapping #', v.RootId, ' for the same AOP Description on the same rack; deactivated. Edit one of the ranges, then re-activate.'),
       NULL, n.Id, n.BrandId, b.Code, n.AopDescriptionId, a.Code,
       n.FromPalletPositionId, n.ToPalletPositionId, pf.LocationCode, pt.LocationCode, 1, UTC_TIMESTAMP(6)
FROM (
SELECT n2.Id AS VictimId, MIN(n1.Id) AS RootId
FROM AopDescriptionLocationPreferences n2
JOIN PalletPositions f2 ON f2.Id = n2.FromPalletPositionId
JOIN PalletPositions t2 ON t2.Id = n2.ToPalletPositionId
JOIN AopDescriptionLocationPreferences n1 ON n1.AopDescriptionId = n2.AopDescriptionId AND n1.IsActive = 1 AND n1.Id < n2.Id
JOIN PalletPositions f1 ON f1.Id = n1.FromPalletPositionId
JOIN PalletPositions t1 ON t1.Id = n1.ToPalletPositionId
WHERE n2.IsActive = 1 AND f1.RackId = f2.RackId AND f1.`Column` <= t2.`Column` AND t1.`Column` >= f2.`Column` AND f1.`Level` <= t2.`Level` AND t1.`Level` >= f2.`Level`
AND NOT EXISTS (
    SELECT 1 FROM AopDescriptionLocationPreferences n0
    JOIN PalletPositions f0 ON f0.Id = n0.FromPalletPositionId
    JOIN PalletPositions t0 ON t0.Id = n0.ToPalletPositionId
    WHERE n0.AopDescriptionId = n1.AopDescriptionId AND n0.IsActive = 1 AND n0.Id < n1.Id AND f0.RackId = f1.RackId AND f0.`Column` <= t1.`Column` AND t0.`Column` >= f1.`Column` AND f0.`Level` <= t1.`Level` AND t0.`Level` >= f1.`Level`)
GROUP BY n2.Id) v
JOIN AopDescriptionLocationPreferences n ON n.Id = v.VictimId
LEFT JOIN Brands b ON b.Id = n.BrandId
JOIN AopDescriptions a ON a.Id = n.AopDescriptionId
JOIN PalletPositions pf ON pf.Id = n.FromPalletPositionId
JOIN PalletPositions pt ON pt.Id = n.ToPalletPositionId;

UPDATE AopDescriptionLocationPreferences SET IsActive = 0 WHERE Id IN (SELECT VictimId FROM (
SELECT n2.Id AS VictimId, MIN(n1.Id) AS RootId
FROM AopDescriptionLocationPreferences n2
JOIN PalletPositions f2 ON f2.Id = n2.FromPalletPositionId
JOIN PalletPositions t2 ON t2.Id = n2.ToPalletPositionId
JOIN AopDescriptionLocationPreferences n1 ON n1.AopDescriptionId = n2.AopDescriptionId AND n1.IsActive = 1 AND n1.Id < n2.Id
JOIN PalletPositions f1 ON f1.Id = n1.FromPalletPositionId
JOIN PalletPositions t1 ON t1.Id = n1.ToPalletPositionId
WHERE n2.IsActive = 1 AND f1.RackId = f2.RackId AND f1.`Column` <= t2.`Column` AND t1.`Column` >= f2.`Column` AND f1.`Level` <= t2.`Level` AND t1.`Level` >= f2.`Level`
AND NOT EXISTS (
    SELECT 1 FROM AopDescriptionLocationPreferences n0
    JOIN PalletPositions f0 ON f0.Id = n0.FromPalletPositionId
    JOIN PalletPositions t0 ON t0.Id = n0.ToPalletPositionId
    WHERE n0.AopDescriptionId = n1.AopDescriptionId AND n0.IsActive = 1 AND n0.Id < n1.Id AND f0.RackId = f1.RackId AND f0.`Column` <= t1.`Column` AND t0.`Column` >= f1.`Column` AND f0.`Level` <= t1.`Level` AND t0.`Level` >= f1.`Level`)
GROUP BY n2.Id) vv);


INSERT INTO AopDescriptionLocationPreferenceMigrationExceptions (`Action`, Reason, SourcePreferenceId, TargetPreferenceId, BrandId, BrandCode, AopDescriptionId, AopCode,
                  FromPalletPositionId, ToPalletPositionId, FromLocationCode, ToLocationCode, IsActive, CreatedAt)
SELECT 'Deactivated',
       CONCAT('Overlaps active mapping #', v.RootId, ' for the same AOP Description on the same rack; deactivated. Edit one of the ranges, then re-activate.'),
       NULL, n.Id, n.BrandId, b.Code, n.AopDescriptionId, a.Code,
       n.FromPalletPositionId, n.ToPalletPositionId, pf.LocationCode, pt.LocationCode, 1, UTC_TIMESTAMP(6)
FROM (
SELECT n2.Id AS VictimId, MIN(n1.Id) AS RootId
FROM AopDescriptionLocationPreferences n2
JOIN PalletPositions f2 ON f2.Id = n2.FromPalletPositionId
JOIN PalletPositions t2 ON t2.Id = n2.ToPalletPositionId
JOIN AopDescriptionLocationPreferences n1 ON n1.AopDescriptionId = n2.AopDescriptionId AND n1.IsActive = 1 AND n1.Id < n2.Id
JOIN PalletPositions f1 ON f1.Id = n1.FromPalletPositionId
JOIN PalletPositions t1 ON t1.Id = n1.ToPalletPositionId
WHERE n2.IsActive = 1 AND f1.RackId = f2.RackId AND f1.`Column` <= t2.`Column` AND t1.`Column` >= f2.`Column` AND f1.`Level` <= t2.`Level` AND t1.`Level` >= f2.`Level`
AND NOT EXISTS (
    SELECT 1 FROM AopDescriptionLocationPreferences n0
    JOIN PalletPositions f0 ON f0.Id = n0.FromPalletPositionId
    JOIN PalletPositions t0 ON t0.Id = n0.ToPalletPositionId
    WHERE n0.AopDescriptionId = n1.AopDescriptionId AND n0.IsActive = 1 AND n0.Id < n1.Id AND f0.RackId = f1.RackId AND f0.`Column` <= t1.`Column` AND t0.`Column` >= f1.`Column` AND f0.`Level` <= t1.`Level` AND t0.`Level` >= f1.`Level`)
GROUP BY n2.Id) v
JOIN AopDescriptionLocationPreferences n ON n.Id = v.VictimId
LEFT JOIN Brands b ON b.Id = n.BrandId
JOIN AopDescriptions a ON a.Id = n.AopDescriptionId
JOIN PalletPositions pf ON pf.Id = n.FromPalletPositionId
JOIN PalletPositions pt ON pt.Id = n.ToPalletPositionId;

UPDATE AopDescriptionLocationPreferences SET IsActive = 0 WHERE Id IN (SELECT VictimId FROM (
SELECT n2.Id AS VictimId, MIN(n1.Id) AS RootId
FROM AopDescriptionLocationPreferences n2
JOIN PalletPositions f2 ON f2.Id = n2.FromPalletPositionId
JOIN PalletPositions t2 ON t2.Id = n2.ToPalletPositionId
JOIN AopDescriptionLocationPreferences n1 ON n1.AopDescriptionId = n2.AopDescriptionId AND n1.IsActive = 1 AND n1.Id < n2.Id
JOIN PalletPositions f1 ON f1.Id = n1.FromPalletPositionId
JOIN PalletPositions t1 ON t1.Id = n1.ToPalletPositionId
WHERE n2.IsActive = 1 AND f1.RackId = f2.RackId AND f1.`Column` <= t2.`Column` AND t1.`Column` >= f2.`Column` AND f1.`Level` <= t2.`Level` AND t1.`Level` >= f2.`Level`
AND NOT EXISTS (
    SELECT 1 FROM AopDescriptionLocationPreferences n0
    JOIN PalletPositions f0 ON f0.Id = n0.FromPalletPositionId
    JOIN PalletPositions t0 ON t0.Id = n0.ToPalletPositionId
    WHERE n0.AopDescriptionId = n1.AopDescriptionId AND n0.IsActive = 1 AND n0.Id < n1.Id AND f0.RackId = f1.RackId AND f0.`Column` <= t1.`Column` AND t0.`Column` >= f1.`Column` AND f0.`Level` <= t1.`Level` AND t0.`Level` >= f1.`Level`)
GROUP BY n2.Id) vv);


INSERT INTO AopDescriptionLocationPreferenceMigrationExceptions (`Action`, Reason, SourcePreferenceId, TargetPreferenceId, BrandId, BrandCode, AopDescriptionId, AopCode,
                  FromPalletPositionId, ToPalletPositionId, FromLocationCode, ToLocationCode, IsActive, CreatedAt)
SELECT 'Deactivated',
       CONCAT('Overlaps active mapping #', v.RootId, ' for the same AOP Description on the same rack; deactivated. Edit one of the ranges, then re-activate.'),
       NULL, n.Id, n.BrandId, b.Code, n.AopDescriptionId, a.Code,
       n.FromPalletPositionId, n.ToPalletPositionId, pf.LocationCode, pt.LocationCode, 1, UTC_TIMESTAMP(6)
FROM (
SELECT n2.Id AS VictimId, MIN(n1.Id) AS RootId
FROM AopDescriptionLocationPreferences n2
JOIN PalletPositions f2 ON f2.Id = n2.FromPalletPositionId
JOIN PalletPositions t2 ON t2.Id = n2.ToPalletPositionId
JOIN AopDescriptionLocationPreferences n1 ON n1.AopDescriptionId = n2.AopDescriptionId AND n1.IsActive = 1 AND n1.Id < n2.Id
JOIN PalletPositions f1 ON f1.Id = n1.FromPalletPositionId
JOIN PalletPositions t1 ON t1.Id = n1.ToPalletPositionId
WHERE n2.IsActive = 1 AND f1.RackId = f2.RackId AND f1.`Column` <= t2.`Column` AND t1.`Column` >= f2.`Column` AND f1.`Level` <= t2.`Level` AND t1.`Level` >= f2.`Level`
AND NOT EXISTS (
    SELECT 1 FROM AopDescriptionLocationPreferences n0
    JOIN PalletPositions f0 ON f0.Id = n0.FromPalletPositionId
    JOIN PalletPositions t0 ON t0.Id = n0.ToPalletPositionId
    WHERE n0.AopDescriptionId = n1.AopDescriptionId AND n0.IsActive = 1 AND n0.Id < n1.Id AND f0.RackId = f1.RackId AND f0.`Column` <= t1.`Column` AND t0.`Column` >= f1.`Column` AND f0.`Level` <= t1.`Level` AND t0.`Level` >= f1.`Level`)
GROUP BY n2.Id) v
JOIN AopDescriptionLocationPreferences n ON n.Id = v.VictimId
LEFT JOIN Brands b ON b.Id = n.BrandId
JOIN AopDescriptions a ON a.Id = n.AopDescriptionId
JOIN PalletPositions pf ON pf.Id = n.FromPalletPositionId
JOIN PalletPositions pt ON pt.Id = n.ToPalletPositionId;

UPDATE AopDescriptionLocationPreferences SET IsActive = 0 WHERE Id IN (SELECT VictimId FROM (
SELECT n2.Id AS VictimId, MIN(n1.Id) AS RootId
FROM AopDescriptionLocationPreferences n2
JOIN PalletPositions f2 ON f2.Id = n2.FromPalletPositionId
JOIN PalletPositions t2 ON t2.Id = n2.ToPalletPositionId
JOIN AopDescriptionLocationPreferences n1 ON n1.AopDescriptionId = n2.AopDescriptionId AND n1.IsActive = 1 AND n1.Id < n2.Id
JOIN PalletPositions f1 ON f1.Id = n1.FromPalletPositionId
JOIN PalletPositions t1 ON t1.Id = n1.ToPalletPositionId
WHERE n2.IsActive = 1 AND f1.RackId = f2.RackId AND f1.`Column` <= t2.`Column` AND t1.`Column` >= f2.`Column` AND f1.`Level` <= t2.`Level` AND t1.`Level` >= f2.`Level`
AND NOT EXISTS (
    SELECT 1 FROM AopDescriptionLocationPreferences n0
    JOIN PalletPositions f0 ON f0.Id = n0.FromPalletPositionId
    JOIN PalletPositions t0 ON t0.Id = n0.ToPalletPositionId
    WHERE n0.AopDescriptionId = n1.AopDescriptionId AND n0.IsActive = 1 AND n0.Id < n1.Id AND f0.RackId = f1.RackId AND f0.`Column` <= t1.`Column` AND t0.`Column` >= f1.`Column` AND f0.`Level` <= t1.`Level` AND t0.`Level` >= f1.`Level`)
GROUP BY n2.Id) vv);


INSERT INTO AopDescriptionLocationPreferenceMigrationExceptions (`Action`, Reason, SourcePreferenceId, TargetPreferenceId, BrandId, BrandCode, AopDescriptionId, AopCode,
                  FromPalletPositionId, ToPalletPositionId, FromLocationCode, ToLocationCode, IsActive, CreatedAt)
SELECT 'Deactivated',
       CONCAT('Overlaps active mapping #', v.RootId, ' for the same AOP Description on the same rack; deactivated. Edit one of the ranges, then re-activate.'),
       NULL, n.Id, n.BrandId, b.Code, n.AopDescriptionId, a.Code,
       n.FromPalletPositionId, n.ToPalletPositionId, pf.LocationCode, pt.LocationCode, 1, UTC_TIMESTAMP(6)
FROM (
SELECT n2.Id AS VictimId, MIN(n1.Id) AS RootId
FROM AopDescriptionLocationPreferences n2
JOIN PalletPositions f2 ON f2.Id = n2.FromPalletPositionId
JOIN PalletPositions t2 ON t2.Id = n2.ToPalletPositionId
JOIN AopDescriptionLocationPreferences n1 ON n1.AopDescriptionId = n2.AopDescriptionId AND n1.IsActive = 1 AND n1.Id < n2.Id
JOIN PalletPositions f1 ON f1.Id = n1.FromPalletPositionId
JOIN PalletPositions t1 ON t1.Id = n1.ToPalletPositionId
WHERE n2.IsActive = 1 AND f1.RackId = f2.RackId AND f1.`Column` <= t2.`Column` AND t1.`Column` >= f2.`Column` AND f1.`Level` <= t2.`Level` AND t1.`Level` >= f2.`Level`
AND NOT EXISTS (
    SELECT 1 FROM AopDescriptionLocationPreferences n0
    JOIN PalletPositions f0 ON f0.Id = n0.FromPalletPositionId
    JOIN PalletPositions t0 ON t0.Id = n0.ToPalletPositionId
    WHERE n0.AopDescriptionId = n1.AopDescriptionId AND n0.IsActive = 1 AND n0.Id < n1.Id AND f0.RackId = f1.RackId AND f0.`Column` <= t1.`Column` AND t0.`Column` >= f1.`Column` AND f0.`Level` <= t1.`Level` AND t0.`Level` >= f1.`Level`)
GROUP BY n2.Id) v
JOIN AopDescriptionLocationPreferences n ON n.Id = v.VictimId
LEFT JOIN Brands b ON b.Id = n.BrandId
JOIN AopDescriptions a ON a.Id = n.AopDescriptionId
JOIN PalletPositions pf ON pf.Id = n.FromPalletPositionId
JOIN PalletPositions pt ON pt.Id = n.ToPalletPositionId;

UPDATE AopDescriptionLocationPreferences SET IsActive = 0 WHERE Id IN (SELECT VictimId FROM (
SELECT n2.Id AS VictimId, MIN(n1.Id) AS RootId
FROM AopDescriptionLocationPreferences n2
JOIN PalletPositions f2 ON f2.Id = n2.FromPalletPositionId
JOIN PalletPositions t2 ON t2.Id = n2.ToPalletPositionId
JOIN AopDescriptionLocationPreferences n1 ON n1.AopDescriptionId = n2.AopDescriptionId AND n1.IsActive = 1 AND n1.Id < n2.Id
JOIN PalletPositions f1 ON f1.Id = n1.FromPalletPositionId
JOIN PalletPositions t1 ON t1.Id = n1.ToPalletPositionId
WHERE n2.IsActive = 1 AND f1.RackId = f2.RackId AND f1.`Column` <= t2.`Column` AND t1.`Column` >= f2.`Column` AND f1.`Level` <= t2.`Level` AND t1.`Level` >= f2.`Level`
AND NOT EXISTS (
    SELECT 1 FROM AopDescriptionLocationPreferences n0
    JOIN PalletPositions f0 ON f0.Id = n0.FromPalletPositionId
    JOIN PalletPositions t0 ON t0.Id = n0.ToPalletPositionId
    WHERE n0.AopDescriptionId = n1.AopDescriptionId AND n0.IsActive = 1 AND n0.Id < n1.Id AND f0.RackId = f1.RackId AND f0.`Column` <= t1.`Column` AND t0.`Column` >= f1.`Column` AND f0.`Level` <= t1.`Level` AND t0.`Level` >= f1.`Level`)
GROUP BY n2.Id) vv);


INSERT INTO AopDescriptionLocationPreferenceMigrationExceptions (`Action`, Reason, SourcePreferenceId, TargetPreferenceId, BrandId, BrandCode, AopDescriptionId, AopCode,
                  FromPalletPositionId, ToPalletPositionId, FromLocationCode, ToLocationCode, IsActive, CreatedAt)
SELECT 'Deactivated',
       CONCAT('Overlaps active mapping #', v.RootId, ' for the same AOP Description on the same rack; deactivated. Edit one of the ranges, then re-activate.'),
       NULL, n.Id, n.BrandId, b.Code, n.AopDescriptionId, a.Code,
       n.FromPalletPositionId, n.ToPalletPositionId, pf.LocationCode, pt.LocationCode, 1, UTC_TIMESTAMP(6)
FROM (
SELECT n2.Id AS VictimId, MIN(n1.Id) AS RootId
FROM AopDescriptionLocationPreferences n2
JOIN PalletPositions f2 ON f2.Id = n2.FromPalletPositionId
JOIN PalletPositions t2 ON t2.Id = n2.ToPalletPositionId
JOIN AopDescriptionLocationPreferences n1 ON n1.AopDescriptionId = n2.AopDescriptionId AND n1.IsActive = 1 AND n1.Id < n2.Id
JOIN PalletPositions f1 ON f1.Id = n1.FromPalletPositionId
JOIN PalletPositions t1 ON t1.Id = n1.ToPalletPositionId
WHERE n2.IsActive = 1 AND f1.RackId = f2.RackId AND f1.`Column` <= t2.`Column` AND t1.`Column` >= f2.`Column` AND f1.`Level` <= t2.`Level` AND t1.`Level` >= f2.`Level`
AND NOT EXISTS (
    SELECT 1 FROM AopDescriptionLocationPreferences n0
    JOIN PalletPositions f0 ON f0.Id = n0.FromPalletPositionId
    JOIN PalletPositions t0 ON t0.Id = n0.ToPalletPositionId
    WHERE n0.AopDescriptionId = n1.AopDescriptionId AND n0.IsActive = 1 AND n0.Id < n1.Id AND f0.RackId = f1.RackId AND f0.`Column` <= t1.`Column` AND t0.`Column` >= f1.`Column` AND f0.`Level` <= t1.`Level` AND t0.`Level` >= f1.`Level`)
GROUP BY n2.Id) v
JOIN AopDescriptionLocationPreferences n ON n.Id = v.VictimId
LEFT JOIN Brands b ON b.Id = n.BrandId
JOIN AopDescriptions a ON a.Id = n.AopDescriptionId
JOIN PalletPositions pf ON pf.Id = n.FromPalletPositionId
JOIN PalletPositions pt ON pt.Id = n.ToPalletPositionId;

UPDATE AopDescriptionLocationPreferences SET IsActive = 0 WHERE Id IN (SELECT VictimId FROM (
SELECT n2.Id AS VictimId, MIN(n1.Id) AS RootId
FROM AopDescriptionLocationPreferences n2
JOIN PalletPositions f2 ON f2.Id = n2.FromPalletPositionId
JOIN PalletPositions t2 ON t2.Id = n2.ToPalletPositionId
JOIN AopDescriptionLocationPreferences n1 ON n1.AopDescriptionId = n2.AopDescriptionId AND n1.IsActive = 1 AND n1.Id < n2.Id
JOIN PalletPositions f1 ON f1.Id = n1.FromPalletPositionId
JOIN PalletPositions t1 ON t1.Id = n1.ToPalletPositionId
WHERE n2.IsActive = 1 AND f1.RackId = f2.RackId AND f1.`Column` <= t2.`Column` AND t1.`Column` >= f2.`Column` AND f1.`Level` <= t2.`Level` AND t1.`Level` >= f2.`Level`
AND NOT EXISTS (
    SELECT 1 FROM AopDescriptionLocationPreferences n0
    JOIN PalletPositions f0 ON f0.Id = n0.FromPalletPositionId
    JOIN PalletPositions t0 ON t0.Id = n0.ToPalletPositionId
    WHERE n0.AopDescriptionId = n1.AopDescriptionId AND n0.IsActive = 1 AND n0.Id < n1.Id AND f0.RackId = f1.RackId AND f0.`Column` <= t1.`Column` AND t0.`Column` >= f1.`Column` AND f0.`Level` <= t1.`Level` AND t0.`Level` >= f1.`Level`)
GROUP BY n2.Id) vv);


INSERT INTO AopDescriptionLocationPreferenceMigrationExceptions (`Action`, Reason, SourcePreferenceId, TargetPreferenceId, BrandId, BrandCode, AopDescriptionId, AopCode,
                  FromPalletPositionId, ToPalletPositionId, FromLocationCode, ToLocationCode, IsActive, CreatedAt)
SELECT 'Deactivated',
       CONCAT('Overlaps active mapping #', v.RootId, ' for the same AOP Description on the same rack; deactivated. Edit one of the ranges, then re-activate.'),
       NULL, n.Id, n.BrandId, b.Code, n.AopDescriptionId, a.Code,
       n.FromPalletPositionId, n.ToPalletPositionId, pf.LocationCode, pt.LocationCode, 1, UTC_TIMESTAMP(6)
FROM (
SELECT n2.Id AS VictimId, MIN(n1.Id) AS RootId
FROM AopDescriptionLocationPreferences n2
JOIN PalletPositions f2 ON f2.Id = n2.FromPalletPositionId
JOIN PalletPositions t2 ON t2.Id = n2.ToPalletPositionId
JOIN AopDescriptionLocationPreferences n1 ON n1.AopDescriptionId = n2.AopDescriptionId AND n1.IsActive = 1 AND n1.Id < n2.Id
JOIN PalletPositions f1 ON f1.Id = n1.FromPalletPositionId
JOIN PalletPositions t1 ON t1.Id = n1.ToPalletPositionId
WHERE n2.IsActive = 1 AND f1.RackId = f2.RackId AND f1.`Column` <= t2.`Column` AND t1.`Column` >= f2.`Column` AND f1.`Level` <= t2.`Level` AND t1.`Level` >= f2.`Level`
AND NOT EXISTS (
    SELECT 1 FROM AopDescriptionLocationPreferences n0
    JOIN PalletPositions f0 ON f0.Id = n0.FromPalletPositionId
    JOIN PalletPositions t0 ON t0.Id = n0.ToPalletPositionId
    WHERE n0.AopDescriptionId = n1.AopDescriptionId AND n0.IsActive = 1 AND n0.Id < n1.Id AND f0.RackId = f1.RackId AND f0.`Column` <= t1.`Column` AND t0.`Column` >= f1.`Column` AND f0.`Level` <= t1.`Level` AND t0.`Level` >= f1.`Level`)
GROUP BY n2.Id) v
JOIN AopDescriptionLocationPreferences n ON n.Id = v.VictimId
LEFT JOIN Brands b ON b.Id = n.BrandId
JOIN AopDescriptions a ON a.Id = n.AopDescriptionId
JOIN PalletPositions pf ON pf.Id = n.FromPalletPositionId
JOIN PalletPositions pt ON pt.Id = n.ToPalletPositionId;

UPDATE AopDescriptionLocationPreferences SET IsActive = 0 WHERE Id IN (SELECT VictimId FROM (
SELECT n2.Id AS VictimId, MIN(n1.Id) AS RootId
FROM AopDescriptionLocationPreferences n2
JOIN PalletPositions f2 ON f2.Id = n2.FromPalletPositionId
JOIN PalletPositions t2 ON t2.Id = n2.ToPalletPositionId
JOIN AopDescriptionLocationPreferences n1 ON n1.AopDescriptionId = n2.AopDescriptionId AND n1.IsActive = 1 AND n1.Id < n2.Id
JOIN PalletPositions f1 ON f1.Id = n1.FromPalletPositionId
JOIN PalletPositions t1 ON t1.Id = n1.ToPalletPositionId
WHERE n2.IsActive = 1 AND f1.RackId = f2.RackId AND f1.`Column` <= t2.`Column` AND t1.`Column` >= f2.`Column` AND f1.`Level` <= t2.`Level` AND t1.`Level` >= f2.`Level`
AND NOT EXISTS (
    SELECT 1 FROM AopDescriptionLocationPreferences n0
    JOIN PalletPositions f0 ON f0.Id = n0.FromPalletPositionId
    JOIN PalletPositions t0 ON t0.Id = n0.ToPalletPositionId
    WHERE n0.AopDescriptionId = n1.AopDescriptionId AND n0.IsActive = 1 AND n0.Id < n1.Id AND f0.RackId = f1.RackId AND f0.`Column` <= t1.`Column` AND t0.`Column` >= f1.`Column` AND f0.`Level` <= t1.`Level` AND t0.`Level` >= f1.`Level`)
GROUP BY n2.Id) vv);


INSERT INTO AopDescriptionLocationPreferenceMigrationExceptions (`Action`, Reason, SourcePreferenceId, TargetPreferenceId, BrandId, BrandCode, AopDescriptionId, AopCode,
                  FromPalletPositionId, ToPalletPositionId, FromLocationCode, ToLocationCode, IsActive, CreatedAt)
SELECT 'Deactivated',
       CONCAT('Overlaps active mapping #', v.RootId, ' for the same AOP Description on the same rack; deactivated. Edit one of the ranges, then re-activate.'),
       NULL, n.Id, n.BrandId, b.Code, n.AopDescriptionId, a.Code,
       n.FromPalletPositionId, n.ToPalletPositionId, pf.LocationCode, pt.LocationCode, 1, UTC_TIMESTAMP(6)
FROM (
SELECT n2.Id AS VictimId, MIN(n1.Id) AS RootId
FROM AopDescriptionLocationPreferences n2
JOIN PalletPositions f2 ON f2.Id = n2.FromPalletPositionId
JOIN PalletPositions t2 ON t2.Id = n2.ToPalletPositionId
JOIN AopDescriptionLocationPreferences n1 ON n1.AopDescriptionId = n2.AopDescriptionId AND n1.IsActive = 1 AND n1.Id < n2.Id
JOIN PalletPositions f1 ON f1.Id = n1.FromPalletPositionId
JOIN PalletPositions t1 ON t1.Id = n1.ToPalletPositionId
WHERE n2.IsActive = 1 AND f1.RackId = f2.RackId AND f1.`Column` <= t2.`Column` AND t1.`Column` >= f2.`Column` AND f1.`Level` <= t2.`Level` AND t1.`Level` >= f2.`Level`

GROUP BY n2.Id) v
JOIN AopDescriptionLocationPreferences n ON n.Id = v.VictimId
LEFT JOIN Brands b ON b.Id = n.BrandId
JOIN AopDescriptions a ON a.Id = n.AopDescriptionId
JOIN PalletPositions pf ON pf.Id = n.FromPalletPositionId
JOIN PalletPositions pt ON pt.Id = n.ToPalletPositionId;

UPDATE AopDescriptionLocationPreferences SET IsActive = 0 WHERE Id IN (SELECT VictimId FROM (
SELECT n2.Id AS VictimId, MIN(n1.Id) AS RootId
FROM AopDescriptionLocationPreferences n2
JOIN PalletPositions f2 ON f2.Id = n2.FromPalletPositionId
JOIN PalletPositions t2 ON t2.Id = n2.ToPalletPositionId
JOIN AopDescriptionLocationPreferences n1 ON n1.AopDescriptionId = n2.AopDescriptionId AND n1.IsActive = 1 AND n1.Id < n2.Id
JOIN PalletPositions f1 ON f1.Id = n1.FromPalletPositionId
JOIN PalletPositions t1 ON t1.Id = n1.ToPalletPositionId
WHERE n2.IsActive = 1 AND f1.RackId = f2.RackId AND f1.`Column` <= t2.`Column` AND t1.`Column` >= f2.`Column` AND f1.`Level` <= t2.`Level` AND t1.`Level` >= f2.`Level`

GROUP BY n2.Id) vv);

DROP TABLE AopDescriptionLocationPreferenceMigrationStage;

DROP TABLE `BrandLocationPreferences`;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261031062704_AopDescriptionLocationPreferences', '9.0.11');

COMMIT;

