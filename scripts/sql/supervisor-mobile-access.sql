-- Supervisors on the mobile app: they review, correct and confirm the put-away / pick records the pickers submit, and can submit in a
-- picker's absence. Pickers only submit. Idempotent; safe to run more than once. Review the role matrix afterwards (Role & Permission Management).
--
--   Pickers:                       submit only  (Inward.AssignPkm, Pullout.Pick)           - never the final confirm
--   Floor Supervisor / Supervisor / Head Supervisor:
--                                  submit (Inward.AssignPkm, Pullout.Pick) AND confirm (Inward.Confirm, Pullout.Confirm) AND edit (Pullout.Edit)

INSERT INTO RolePermissions (RoleName, PermissionKey, IsGranted)
SELECT r.RoleName, k.PermissionKey, 1
FROM (SELECT 'Floor Supervisor' AS RoleName UNION ALL SELECT 'Supervisor' UNION ALL SELECT 'Head Supervisor') r
CROSS JOIN (SELECT 'Inward.AssignPkm' AS PermissionKey UNION ALL SELECT 'Inward.Confirm' UNION ALL SELECT 'Inward.View'
            UNION ALL SELECT 'Pullout.Pick' UNION ALL SELECT 'Pullout.Confirm' UNION ALL SELECT 'Pullout.Edit' UNION ALL SELECT 'Pullout.View') k
WHERE EXISTS (SELECT 1 FROM AspNetRoles ar WHERE ar.Name = r.RoleName)
ON DUPLICATE KEY UPDATE IsGranted = 1;

-- Pickers must not hold the final confirm.
UPDATE RolePermissions SET IsGranted = 0
WHERE RoleName = 'Pickers' AND PermissionKey IN ('Inward.Confirm', 'Pullout.Confirm');

SELECT RoleName, PermissionKey, IsGranted FROM RolePermissions
WHERE RoleName IN ('Pickers', 'Floor Supervisor', 'Supervisor', 'Head Supervisor')
  AND PermissionKey IN ('Inward.AssignPkm', 'Inward.Confirm', 'Pullout.Pick', 'Pullout.Confirm', 'Pullout.Edit')
ORDER BY RoleName, PermissionKey;
