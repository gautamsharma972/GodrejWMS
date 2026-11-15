-- AOP Description Location Preferences: delete and reload.   Run in ONE mysql session.
SET NAMES utf8mb4;

-- ============ 1) BACKUP (before anything is deleted) ============
-- 1a. In SQL, next to the original table (quick rollback):
DROP TABLE IF EXISTS AopDescriptionLocationPreferences_bak_20261116;
CREATE TABLE AopDescriptionLocationPreferences_bak_20261116 AS SELECT * FROM AopDescriptionLocationPreferences;
SELECT (SELECT COUNT(*) FROM AopDescriptionLocationPreferences)            AS rows_in_table,
       (SELECT COUNT(*) FROM AopDescriptionLocationPreferences_bak_20261116) AS rows_in_backup;   -- must be equal

-- ============ 2) DELETE + 3) RELOAD, in one transaction ============
START TRANSACTION;

DELETE FROM AopDescriptionLocationPreferences;

-- Ranges to load: AOP code, rack code, from column, to column (all levels of those columns are covered).
CREATE TEMPORARY TABLE tmp_aop_ranges (
  aop_code  VARCHAR(50) NOT NULL,
  rack_code VARCHAR(10) NOT NULL,
  col_from  INT NOT NULL,
  col_to    INT NOT NULL
) DEFAULT CHARSET=utf8mb4;

INSERT INTO tmp_aop_ranges (aop_code, rack_code, col_from, col_to) VALUES
  ('ARSL CLASSIC RANGE','I',28,59),('ARSL CLASSIC RANGE','J',28,59),
  ('ARSL SIGNATURE RANGE','I',28,59),('ARSL SIGNATURE RANGE','J',28,59),
  ('ARSL SPORTZ RANGE','I',28,59),('ARSL SPORTZ RANGE','J',28,59),
  ('DEO LOTION PA','I',28,59),('DEO LOTION PA','J',28,59),
  ('DEO SHOT-PA','I',28,59),('DEO SHOT-PA','J',28,59),
  ('EDP-PA','C',28,52),('EDP-PA','D',20,59),('EDP-PA','J',28,59),
  ('HPC EDC','C',28,56),
  ('HPC POST-SHAVE','I',28,59),('HPC POST-SHAVE','J',28,59),
  ('HPC SHAVING KIT','A',28,51),('HPC SHAVING KIT','I',28,59),('HPC SHAVING KIT','J',28,59),
  ('HPC TALC','I',28,59),('HPC TALC','J',28,59),
  ('LIQUID MIST-PA','I',28,59),('LIQUID MIST-PA','J',28,59),
  ('LIQUIDS 2X-PA','I',28,59),('LIQUIDS 2X-PA','J',28,59),
  ('LIQUIDS 4X-PA','I',28,59),('LIQUIDS 4X-PA','J',28,59),
  ('LIQUIDS 4X-PA ALM CAN','I',28,59),('LIQUIDS 4X-PA ALM CAN','J',28,59),
  ('LIQUIDS POCKET-KS','N',28,59),
  ('LIQUIDS POCKET-PA','I',28,59),('LIQUIDS POCKET-PA','J',28,59),
  ('OTHER DISC.','I',28,59),('OTHER DISC.','J',28,59),
  ('PA-HPC GROOMING KIT','I',28,59),('PA-HPC GROOMING KIT','J',28,59),
  ('AEROSOL KS RANGE','M',28,59),('AEROSOL KS RANGE','N',28,59),
  ('DEO SHOT-KS','M',28,59),('DEO SHOT-KS','N',28,59),
  ('EXCITE','M',28,59),('EXCITE','N',28,59),
  ('EXOTICA','M',28,59),('EXOTICA','N',28,59),
  ('HONEYMOON KIT','M',28,59),('HONEYMOON KIT','N',28,59),
  ('LIQUID MIST-KS','M',28,59),('LIQUID MIST-KS','N',28,59),
  ('LIQUIDS 2X-KS','M',28,59),('LIQUIDS 2X-KS','N',28,59),
  ('LIQUIDS 4X-KS','M',28,59),('LIQUIDS 4X-KS','N',28,59),
  ('LONGLAST','M',28,59),('LONGLAST','N',28,59),
  ('LUBES','M',28,59),('LUBES','N',28,59),
  ('ORGASMAX','M',28,59),('ORGASMAX','N',28,59),
  ('SW-OTHERS','M',28,59),('SW-OTHERS','N',28,59),
  ('TEXTURED','M',28,59),('TEXTURED','N',28,59),
  ('TEXTURED PREMIUM','M',28,59),('TEXTURED PREMIUM','N',28,59),
  ('THIN','M',28,59),('THIN','N',28,59),
  ('FACE WASH','M',28,59),('FACE WASH','N',28,59),
  ('AER CAR CLICK','P',1,27),
  ('AER CAR REFILL','P',1,27),('AER CAR REFILL','P',28,59),('AER CAR REFILL','Q',1,27),('AER CAR REFILL','Q',28,59),
  ('AER CAR TWIST','P',1,27),
  ('AER MEMBRANE','P',1,27),
  ('AER PLUG','P',1,27),
  ('AER POCKET','P',1,27),('AER POCKET','P',28,59),('AER POCKET','Q',1,27),('AER POCKET','Q',28,59),
  ('AER POCKET MINI','P',1,27),
  ('AER SPRAY','P',1,27),('AER SPRAY','P',28,59),('AER SPRAY','Q',1,27),('AER SPRAY','Q',28,59),
  ('CIN TALC','P',1,27),('CIN TALC','P',28,59),('CIN TALC','Q',1,27),('CIN TALC','Q',28,59),
  ('SHAVING CREAM','P',1,27),('SHAVING CREAM','P',28,59),('SHAVING CREAM','Q',1,27),('SHAVING CREAM','Q',28,59),
  ('AER MATIC','E',28,59),('AER MATIC','F',28,59),
  ('EXP CRÈME','E',28,59),('EXP CRÈME','F',28,59),('EXP CRÈME','G',28,59),('EXP CRÈME','H',28,59),
  ('EXP CRÈME SMALL','E',28,59),('EXP CRÈME SMALL','F',28,59),('EXP CRÈME SMALL','G',28,59),('EXP CRÈME SMALL','H',28,59),
  ('EXP ORIG','E',28,59),('EXP ORIG','F',28,59),('EXP ORIG','G',28,59),('EXP ORIG','H',28,59),
  ('EXP SHAMPOO','G',28,59),('EXP SHAMPOO','H',28,59),
  ('EXP SHAMPOO SMALL','G',28,59),('EXP SHAMPOO SMALL','H',28,59),
  ('EXP. CRÈME MAP','E',28,59),('EXP. CRÈME MAP','F',28,59),('EXP. CRÈME MAP','G',28,59),('EXP. CRÈME MAP','H',28,59),
  ('HAIR LAB','E',28,59),('HAIR LAB','F',28,59),('HAIR LAB','G',28,59),('HAIR LAB','H',28,59),
  ('HPC SHAMPOO','D',28,59),
  ('HPC SOAP 125','G',28,59),('HPC SOAP 125','H',9,59),
  ('CIN HAND WASH BOTTLE','O',1,27),
  ('CIN HAND WASH REFILL','O',1,27),
  ('FOAM BODYWASH','O',1,27),('FOAM BODYWASH','O',28,59),
  ('GK ACT+ LMD','G',1,27),('GK ACT+ LMD','H',1,27),('GK ACT+ LMD','I',1,27),('GK ACT+ LMD','J',1,27),('GK ACT+ LMD','K',1,27),
  ('GK ACT+ REFILL','G',1,27),('GK ACT+ REFILL','H',1,27),('GK ACT+ REFILL','I',1,27),('GK ACT+ REFILL','J',1,27),('GK ACT+ REFILL','K',1,27),
  ('GK CARE LMD','G',1,27),('GK CARE LMD','H',1,27),('GK CARE LMD','I',1,27),('GK CARE LMD','J',1,27),('GK CARE LMD','K',1,27),
  ('GK EMD','G',1,27),('GK EMD','H',1,27),('GK EMD','I',1,27),('GK EMD','J',1,27),('GK EMD','K',1,27),
  ('GK FLASH LMD','G',1,27),('GK FLASH LMD','H',1,27),('GK FLASH LMD','I',1,27),('GK FLASH LMD','J',1,27),('GK FLASH LMD','K',1,27),
  ('GK FLASH LMD SMALL','G',1,27),('GK FLASH LMD SMALL','H',1,27),('GK FLASH LMD SMALL','I',1,27),('GK FLASH LMD SMALL','J',1,27),('GK FLASH LMD SMALL','K',1,27),
  ('GK FLASH REFILL','G',1,27),('GK FLASH REFILL','H',1,27),('GK FLASH REFILL','I',1,27),('GK FLASH REFILL','J',1,27),('GK FLASH REFILL','K',1,27),
  ('GK FLASH RFL SMALL','G',1,27),('GK FLASH RFL SMALL','H',1,27),('GK FLASH RFL SMALL','I',1,27),('GK FLASH RFL SMALL','J',1,27),('GK FLASH RFL SMALL','K',1,27),
  ('GK INCENSE STICKS','G',1,27),('GK INCENSE STICKS','H',1,27),('GK INCENSE STICKS','I',1,27),('GK INCENSE STICKS','J',1,27),('GK INCENSE STICKS','K',1,27),
  ('GK MAT','G',1,27),('GK MAT','H',1,27),('GK MAT','I',1,27),('GK MAT','J',1,27),('GK MAT','K',1,27),
  ('GK NATURAL LMD','G',1,27),('GK NATURAL LMD','H',1,27),('GK NATURAL LMD','I',1,27),('GK NATURAL LMD','J',1,27),('GK NATURAL LMD','K',1,27),
  ('GK SKIN SPRAY','G',1,27),('GK SKIN SPRAY','H',1,27),('GK SKIN SPRAY','I',1,27),('GK SKIN SPRAY','J',1,27),('GK SKIN SPRAY','K',1,27),
  ('GK SPRAY','G',1,27),('GK SPRAY','H',1,27),('GK SPRAY','I',1,27),('GK SPRAY','J',1,27),('GK SPRAY','K',1,27),
  ('GK XPRESS LMD','G',1,27),('GK XPRESS LMD','H',1,27),('GK XPRESS LMD','I',1,27),('GK XPRESS LMD','J',1,27),('GK XPRESS LMD','K',1,27),
  ('GK XPRESS REFILL','G',1,27),('GK XPRESS REFILL','H',1,27),('GK XPRESS REFILL','I',1,27),('GK XPRESS REFILL','J',1,27),('GK XPRESS REFILL','K',1,27),
  ('HIT ANTIROACH','A',1,27),('HIT ANTIROACH','B',1,27),('HIT ANTIROACH','C',1,27),('HIT ANTIROACH','D',1,27),('HIT ANTIROACH','E',1,27),('HIT ANTIROACH','F',1,27),
  ('HIT ANTIROACH SMALL','A',1,27),('HIT ANTIROACH SMALL','B',1,27),('HIT ANTIROACH SMALL','C',1,27),('HIT ANTIROACH SMALL','D',1,27),('HIT ANTIROACH SMALL','E',1,27),('HIT ANTIROACH SMALL','F',1,27),
  ('HIT BAIT','A',1,27),('HIT BAIT','B',1,27),('HIT BAIT','C',1,27),('HIT BAIT','D',1,27),('HIT BAIT','E',1,27),('HIT BAIT','F',1,27),
  ('HIT CHALK','A',1,27),('HIT CHALK','B',1,27),('HIT CHALK','C',1,27),('HIT CHALK','D',1,27),('HIT CHALK','E',1,27),('HIT CHALK','F',1,27),
  ('HIT CIK LARGE PACK','A',1,27),('HIT CIK LARGE PACK','B',1,27),('HIT CIK LARGE PACK','C',1,27),('HIT CIK LARGE PACK','D',1,27),('HIT CIK LARGE PACK','E',1,27),('HIT CIK LARGE PACK','F',1,27),
  ('HIT CIK SMALL PACK','A',1,27),('HIT CIK SMALL PACK','B',1,27),('HIT CIK SMALL PACK','C',1,27),('HIT CIK SMALL PACK','D',1,27),('HIT CIK SMALL PACK','E',1,27),('HIT CIK SMALL PACK','F',1,27),
  ('HIT FIK LIME','A',1,27),('HIT FIK LIME','B',1,27),('HIT FIK LIME','C',1,27),('HIT FIK LIME','D',1,27),('HIT FIK LIME','E',1,27),('HIT FIK LIME','F',1,27),
  ('HIT FIK REG LARGE PK','A',1,27),('HIT FIK REG LARGE PK','B',1,27),('HIT FIK REG LARGE PK','C',1,27),('HIT FIK REG LARGE PK','D',1,27),('HIT FIK REG LARGE PK','E',1,27),('HIT FIK REG LARGE PK','F',1,27),
  ('HIT FIK REG MINI','A',1,27),('HIT FIK REG MINI','B',1,27),('HIT FIK REG MINI','C',1,27),('HIT FIK REG MINI','D',1,27),('HIT FIK REG MINI','E',1,27),('HIT FIK REG MINI','F',1,27),
  ('HIT FIK REG SMALL PK','A',1,27),('HIT FIK REG SMALL PK','B',1,27),('HIT FIK REG SMALL PK','C',1,27),('HIT FIK REG SMALL PK','D',1,27),('HIT FIK REG SMALL PK','E',1,27),('HIT FIK REG SMALL PK','F',1,27),
  ('HIT MATIC','A',1,27),('HIT MATIC','B',1,27),('HIT MATIC','C',1,27),('HIT MATIC','D',1,27),('HIT MATIC','E',1,27),('HIT MATIC','F',1,27),
  ('HIT RACQUET','A',1,27),('HIT RACQUET','B',1,27),('HIT RACQUET','C',1,27),('HIT RACQUET','D',1,27),('HIT RACQUET','E',1,27),('HIT RACQUET','F',1,27),
  ('HIT RAT SPRAY','A',1,27),('HIT RAT SPRAY','B',1,27),('HIT RAT SPRAY','C',1,27),('HIT RAT SPRAY','D',1,27),('HIT RAT SPRAY','E',1,27),('HIT RAT SPRAY','F',1,27),
  ('RAT GLUE','A',1,27),('RAT GLUE','B',1,27),('RAT GLUE','C',1,27),('RAT GLUE','D',1,27),('RAT GLUE','E',1,27),('RAT GLUE','F',1,27),
  ('HAND WASH REFILL','M',1,27),('HAND WASH REFILL','N',1,27),
  ('PROTEKT COMBI','M',1,27),('PROTEKT COMBI','N',1,27),
  ('PROTEKT OTHERS','M',1,27),('PROTEKT OTHERS','N',1,27),
  ('PROTEKT REFILL','M',1,27),('PROTEKT REFILL','N',1,27),
  ('RIZZ DISH WASH','L',1,27),
  ('STAIN REMOVER','L',1,27),
  ('FAST CARD','G',1,27),('FAST CARD','H',1,27),('FAST CARD','I',1,27),('FAST CARD','J',1,27),('FAST CARD','K',1,27),
  ('GK CARE REFILL','G',1,27),('GK CARE REFILL','H',1,27),('GK CARE REFILL','I',1,27),('GK CARE REFILL','J',1,27),('GK CARE REFILL','K',1,27),
  ('LSC GREEN SHAKTI','G',1,27),('LSC GREEN SHAKTI','H',1,27),('LSC GREEN SHAKTI','I',1,27),('LSC GREEN SHAKTI','J',1,27),('LSC GREEN SHAKTI','K',1,27),
  ('LSC REG','G',1,27),('LSC REG','H',1,27),('LSC REG','I',1,27),('LSC REG','J',1,27),('LSC REG','K',1,27),
  ('MAHA JUMBO','G',1,27),('MAHA JUMBO','H',1,27),('MAHA JUMBO','I',1,27),('MAHA JUMBO','J',1,27),('MAHA JUMBO','K',1,27),
  ('MINI GREEN SHAKTI','G',1,27),('MINI GREEN SHAKTI','H',1,27),('MINI GREEN SHAKTI','I',1,27),('MINI GREEN SHAKTI','J',1,27),('MINI GREEN SHAKTI','K',1,27),
  ('MINI JUMBO','G',1,27),('MINI JUMBO','H',1,27),('MINI JUMBO','I',1,27),('MINI JUMBO','J',1,27),('MINI JUMBO','K',1,27),
  ('PERSONAL REPELLANT','G',1,27),('PERSONAL REPELLANT','H',1,27),('PERSONAL REPELLANT','I',1,27),('PERSONAL REPELLANT','J',1,27),('PERSONAL REPELLANT','K',1,27);

-- Check A: AOP codes that do not exist in the AOP Description master (expect 0 rows)
SELECT DISTINCT t.aop_code AS aop_code_not_found
FROM tmp_aop_ranges t LEFT JOIN AopDescriptions a ON a.Code = t.aop_code
WHERE a.Id IS NULL;

-- Check B: ranges whose rack, first column or last column has no ACTIVE pallet position (expect 0 rows)
SELECT t.aop_code, t.rack_code, t.col_from, t.col_to
FROM tmp_aop_ranges t
LEFT JOIN Racks r ON r.Code = t.rack_code AND r.IsActive = 1 AND r.IsSystem = 0
LEFT JOIN PalletPositions pf ON pf.RackId = r.Id AND pf.`Column` = t.col_from AND pf.IsActive = 1
LEFT JOIN PalletPositions pt ON pt.RackId = r.Id AND pt.`Column` = t.col_to   AND pt.IsActive = 1
WHERE r.Id IS NULL OR pf.Id IS NULL OR pt.Id IS NULL
GROUP BY t.aop_code, t.rack_code, t.col_from, t.col_to;

-- The reload: From = lowest level of the first column, To = highest level of the last column, on the same rack.
INSERT INTO AopDescriptionLocationPreferences
  (AopDescriptionId, FromPalletPositionId, ToPalletPositionId, IsActive, CreatedAt)
SELECT a.Id, pf.Id, pt.Id, 1, UTC_TIMESTAMP(6)
FROM tmp_aop_ranges t
JOIN AopDescriptions a ON a.Code = t.aop_code
JOIN Racks r ON r.Code = t.rack_code AND r.IsActive = 1 AND r.IsSystem = 0
JOIN PalletPositions pf ON pf.RackId = r.Id AND pf.`Column` = t.col_from AND pf.IsActive = 1
     AND pf.`Level` = (SELECT MIN(x.`Level`) FROM PalletPositions x WHERE x.RackId = r.Id AND x.`Column` = t.col_from AND x.IsActive = 1)
JOIN PalletPositions pt ON pt.RackId = r.Id AND pt.`Column` = t.col_to AND pt.IsActive = 1
     AND pt.`Level` = (SELECT MAX(x.`Level`) FROM PalletPositions x WHERE x.RackId = r.Id AND x.`Column` = t.col_to AND x.IsActive = 1);

SELECT ROW_COUNT() AS rows_inserted,
       (SELECT COUNT(*) FROM tmp_aop_ranges) AS rows_expected;   -- both must be 335

DROP TEMPORARY TABLE tmp_aop_ranges;

-- Review the checks above. If rows_inserted = rows_expected and Checks A and B returned nothing:
--   COMMIT;
-- otherwise:
--   ROLLBACK;     -- the delete is undone and nothing is lost
