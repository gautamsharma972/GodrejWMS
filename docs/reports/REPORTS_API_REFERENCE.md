# Reports query reference

`GetOperationalReportQuery` accepts a `ReportKind`, `ReportFilter`, page number, page size, sort column, and direction. Page size is limited to 10,000. Invalid PKM, date ranges, sort columns, warehouse access, and unauthorized roles are rejected.

Supported report kinds include current/SKU/location/PKM stock, inward and putaway registers, discrepancies, outward and picking registers, pending picking, shortages, movements, location/SKU histories, transaction ledger, rack/zone stock, capacity, available locations, and rack/level occupancy.
