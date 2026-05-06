-- Declarar los números de orden
DECLARE @OrderNumbers TABLE (OrderNum INT, FacilityName VARCHAR(10), ActualShipDate DATE);
INSERT INTO @OrderNumbers (OrderNum, FacilityName, ActualShipDate) VALUES
(27346, 'PIN', '2026-04-03'),
(27345, 'PIN', '2026-04-05'),
(27343, 'PIN', '2026-04-10'),
(27340, 'PIN', '2026-04-10');

-- Generar JSON simplificado
SELECT 
    (
        SELECT 
            ONum.FacilityName,
            CAST(O.DocNum AS VARCHAR(20)) AS OrderNumber,
            O.DocDate AS DocumentDate,
            ONum.ActualShipDate,
            (
                SELECT 
                    R1.ItemCode AS WarehouseSku,
                    R1.Quantity AS QtyShipped,
                    R1.LineNum AS SapOrderLineNum
                FROM RDR1 R1
                WHERE R1.DocEntry = O.DocEntry
                ORDER BY R1.LineNum
                FOR JSON PATH
            ) AS Detalles
        FROM ORDR O
        INNER JOIN @OrderNumbers ONum ON O.DocNum = ONum.OrderNum
        ORDER BY O.DocNum
        FOR JSON PATH
    ) AS Ordenes
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;