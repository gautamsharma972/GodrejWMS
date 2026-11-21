-- Cycle Count: creates the two logical roles (only if missing) and grants who may schedule, count, submit, review and report.
-- Idempotent; safe to run more than once. Nothing is granted to Pickers / Floor Supervisors: assign "Cycle Count Performer" (or tick
-- CycleCount.Perform / CycleCount.Submit for the role you want) in Role & Permission Management.
--
--   Cycle Count Scheduler : menu, Schedule, ManageSchedule, Reports
--   Cycle Count Performer : menu, Perform, Submit
--   Head Supervisor       : menu, Review, Reports
--   Superadmin            : everything

INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
SELECT UUID(), 'Cycle Count Scheduler', 'CYCLE COUNT SCHEDULER', UUID()
WHERE NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = 'CYCLE COUNT SCHEDULER');

INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
SELECT UUID(), 'Cycle Count Performer', 'CYCLE COUNT PERFORMER', UUID()
WHERE NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = 'CYCLE COUNT PERFORMER');

INSERT INTO RolePermissions (RoleName, PermissionKey, IsGranted)
SELECT g.RoleName, g.PermissionKey, 1
FROM (
    SELECT 'Cycle Count Scheduler' AS RoleName, 'Menu.ViewCycleCount' AS PermissionKey
    UNION ALL SELECT 'Cycle Count Scheduler', 'CycleCount.Schedule'
    UNION ALL SELECT 'Cycle Count Scheduler', 'CycleCount.ManageSchedule'
    UNION ALL SELECT 'Cycle Count Scheduler', 'CycleCount.Reports'
    UNION ALL SELECT 'Cycle Count Performer', 'Menu.ViewCycleCount'
    UNION ALL SELECT 'Cycle Count Performer', 'CycleCount.Perform'
    UNION ALL SELECT 'Cycle Count Performer', 'CycleCount.Submit'
    UNION ALL SELECT 'Head Supervisor', 'Menu.ViewCycleCount'
    UNION ALL SELECT 'Head Supervisor', 'CycleCount.Review'
    UNION ALL SELECT 'Head Supervisor', 'CycleCount.Reports'
    UNION ALL SELECT 'Superadmin', 'Menu.ViewCycleCount'
    UNION ALL SELECT 'Superadmin', 'CycleCount.Schedule'
    UNION ALL SELECT 'Superadmin', 'CycleCount.ManageSchedule'
    UNION ALL SELECT 'Superadmin', 'CycleCount.Perform'
    UNION ALL SELECT 'Superadmin', 'CycleCount.Submit'
    UNION ALL SELECT 'Superadmin', 'CycleCount.Review'
    UNION ALL SELECT 'Superadmin', 'CycleCount.Reports'
) g
WHERE EXISTS (SELECT 1 FROM AspNetRoles ar WHERE ar.Name = g.RoleName)
ON DUPLICATE KEY UPDATE IsGranted = 1;

SELECT RoleName, PermissionKey, IsGranted FROM RolePermissions
WHERE PermissionKey LIKE 'CycleCount.%' OR PermissionKey = 'Menu.ViewCycleCount'
ORDER BY RoleName, PermissionKey;
