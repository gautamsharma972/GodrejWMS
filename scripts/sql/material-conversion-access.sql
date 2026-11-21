-- Material Code Conversion: who may see the page, request a conversion, and approve one. Idempotent; safe to run more than once.
-- Review the role matrix afterwards (Role & Permission Management).
--
--   View + request:   Office Staff, Logistic Manager, Supervisor, Head Supervisor, Superadmin
--   Approve:          Supervisor, Head Supervisor, Superadmin   (a user who can approve and also requests gets the request applied at once)

INSERT INTO RolePermissions (RoleName, PermissionKey, IsGranted)
SELECT r.RoleName, k.PermissionKey, 1
FROM (SELECT 'Office Staff' AS RoleName UNION ALL SELECT 'Logistic Manager' UNION ALL SELECT 'Supervisor'
      UNION ALL SELECT 'Head Supervisor' UNION ALL SELECT 'Superadmin') r
CROSS JOIN (SELECT 'Menu.ViewMaterialConversion' AS PermissionKey UNION ALL SELECT 'Inventory.ConvertMaterial') k
WHERE EXISTS (SELECT 1 FROM AspNetRoles ar WHERE ar.Name = r.RoleName)
ON DUPLICATE KEY UPDATE IsGranted = 1;

INSERT INTO RolePermissions (RoleName, PermissionKey, IsGranted)
SELECT r.RoleName, 'Inventory.ApproveMaterialConversion', 1
FROM (SELECT 'Supervisor' AS RoleName UNION ALL SELECT 'Head Supervisor' UNION ALL SELECT 'Superadmin') r
WHERE EXISTS (SELECT 1 FROM AspNetRoles ar WHERE ar.Name = r.RoleName)
ON DUPLICATE KEY UPDATE IsGranted = 1;

SELECT RoleName, PermissionKey, IsGranted FROM RolePermissions
WHERE PermissionKey IN ('Menu.ViewMaterialConversion', 'Inventory.ConvertMaterial', 'Inventory.ApproveMaterialConversion')
ORDER BY PermissionKey, RoleName;
