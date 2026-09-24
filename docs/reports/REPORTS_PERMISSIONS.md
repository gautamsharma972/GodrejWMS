# Reports permissions

- Admin: all warehouses and all report pages.
- Supervisor: only assigned warehouses.
- Operator: operational transaction screens; no management report query access.

Warehouse restrictions are enforced in EF query filters and rechecked when a report explicitly requests a warehouse. UI visibility is not the security boundary.

New non-admin users must receive at least one `UserWarehouse` assignment before warehouse data becomes visible.
