SELECT
    pp.LocationCode                                   AS PalletPosition,
    m.MaterialNumber                                  AS MaterialCode,
    m.Description                                     AS MaterialDescription,
    aop.Code                                          AS AopDescription,
    m.DesignType                                      AS DesignType,
    sb.MfgMonth                                       AS MfgMonth,
    sb.QuantityBoxes                                  AS StockQty,
    pp.MaxPallets * m.PalletCapacityBoxes             AS Capacity,
    CASE WHEN sb.Id IS NULL THEN 'Empty' ELSE 'Occupied' END AS Status
FROM PalletPositions pp
LEFT JOIN StockBatches sb
       ON sb.PalletPositionId = pp.Id
      AND sb.QuantityBoxes > 0
      AND EXISTS (SELECT 1 FROM Materials mm
                  WHERE mm.Id = sb.MaterialId AND mm.IsActive = 1)
LEFT JOIN Materials m         ON m.Id = sb.MaterialId
LEFT JOIN AopDescriptions aop ON aop.Id = m.AopDescriptionId
WHERE pp.IsActive = 1          -- active locations only
ORDER BY pp.LocationCode, sb.MfgMonth
INTO OUTFILE '/tmp/location_stock.csv'
FIELDS TERMINATED BY ',' ENCLOSED BY '"'
LINES TERMINATED BY '\n';