-- Exceptions report for the move to "post on upload": orders whose stock was never posted to inventory.
-- Nothing is posted automatically; each row needs a decision (confirm it, have the picker assign the PKM, or reject it).
-- The same list is available in the app under Reports > Unposted Orders.

-- Inward (GRN): non-rejected GRNs that still have an open (unconfirmed) put-away
SELECT 'GRN' AS Kind, t.ReferenceNumber, t.CreatedAt, w.Code AS Warehouse,
       COUNT(DISTINCT l.Id) AS LineCount,
       SUM(CASE WHEN p.IsConfirmed = 0 THEN p.QuantityBoxes ELSE 0 END) AS BoxesPending,
       CASE WHEN t.IsPkmless = 1 THEN 'Inward without PKM - posts when the picker assigns the PKM'
            ELSE 'Reserved but never confirmed' END AS Reason
FROM InwardTransactions t
JOIN Warehouses w ON w.Id = t.WarehouseId
JOIN InwardTransactionLines l ON l.InwardTransactionId = t.Id
JOIN InwardPutaways p ON p.InwardTransactionLineId = l.Id
WHERE t.IsRejected = 0
GROUP BY t.Id, t.ReferenceNumber, t.CreatedAt, w.Code, t.IsPkmless
HAVING SUM(CASE WHEN p.IsConfirmed = 0 THEN 1 ELSE 0 END) > 0
UNION ALL
-- Pullout: saved picks that were never confirmed (stock not deducted)
SELECT 'Pullout', t.ReferenceNumber, t.CreatedAt, w.Code,
       COUNT(l.Id), COALESCE(SUM(l.PickedQuantityBoxes), 0), 'Saved picks never confirmed'
FROM PulloutTransactions t
JOIN Warehouses w ON w.Id = t.WarehouseId
LEFT JOIN PulloutTransactionLines l ON l.PulloutTransactionId = t.Id
WHERE t.IsRejected = 0 AND t.IsConfirmed = 0
GROUP BY t.Id, t.ReferenceNumber, t.CreatedAt, w.Code
ORDER BY CreatedAt;
