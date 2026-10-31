-- Data-migration exceptions for the Brand -> AOP Description location preference migration.
-- Run after 01-forward.sql (or after the EF migration). One row per item that needs a human decision.
--   NotMigrated     the Brand has no Material with an AOP Description; create an AOP mapping manually if still needed
--   MergedDuplicate identical (AOP Description, range) produced by two Brands sharing an AOP; merged into one row
--   Deactivated     active ranges overlapped for the same AOP Description on the same rack; the later row was
--                   deactivated (not deleted) - edit one of the ranges, then re-activate it in the UI
SELECT `Action`, BrandCode, AopCode, FromLocationCode, ToLocationCode, Reason,
       SourcePreferenceId AS OldMappingId, TargetPreferenceId AS NewMappingId
FROM AopDescriptionLocationPreferenceMigrationExceptions
ORDER BY `Action`, BrandCode, AopCode;
