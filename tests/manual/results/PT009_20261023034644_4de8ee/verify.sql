USE [PT009_20261023034644_4de8ee];
GO

SELECT v.VariantId, v.SKU, v.StockQuantity,
       COUNT(DISTINCT i.OrderId) AS OrderCount,
       COALESCE(SUM(i.Quantity), 0) AS SoldQuantity
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
