# Known limitations

- Physical inventory reconciliation is not implemented because no physical-count workflow or approved discrepancy entity exists.
- Stock adjustment reporting is omitted because there is no authorized stock-adjustment workflow.
- FIFO compliance/override reporting is omitted because the system does not persist an immutable recommendation-versus-actual decision or override metadata. FIFO allocation itself remains oldest-PKM-first.
- The transaction ledger lists persisted stock-changing events. It does not calculate historical running balances because no reliable opening-balance ledger exists.
- PDF generation is not included. The reports provide a print-friendly layout that can be saved as PDF by the browser.
- Batch number and PKD are not modeled and are omitted.
