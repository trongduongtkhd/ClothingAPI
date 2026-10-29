USE [PT009_20261022074046_ff8f5b];
GO
SELECT v.VariantId, v.SKU, v.StockQuantity,
       COUNT(DISTINCT i.OrderId) AS OrderCount,
       COALESCE(SUM(i.Quantity), 0) AS SoldQuantity,
       CASE WHEN v.StockQuantity = 0
                  AND COUNT(DISTINCT i.OrderId) = 1
                  AND COALESCE(SUM(i.Quantity), 0) = 1
            THEN 'PASSED' ELSE 'FAILED' END AS TestResult
FROM ProductVariants v
LEFT JOIN OrderItems i ON i.VariantId = v.VariantId
WHERE v.SKU LIKE 'PT009-ROUND-%'
GROUP BY v.VariantId, v.SKU, v.StockQuantity
ORDER BY v.VariantId;

SELECT o.OrderId, o.OrderCode, o.UserId, o.Note, i.VariantId, i.Quantity
FROM Orders o
JOIN OrderItems i ON i.OrderId = o.OrderId
WHERE o.Note LIKE 'PT009 round %'
ORDER BY i.VariantId, o.OrderId;
