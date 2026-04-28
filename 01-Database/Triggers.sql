USE [RAMP_ENTERPRISE]
GO

/****** Object:  Trigger [dbo].[TRG_RAMP_TO_SBO_INTEGRATION]    Script Date: 4/24/2026 4:08:08 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER TRIGGER [dbo].[TRG_RAMP_TO_SBO_INTEGRATION]
ON [dbo].[ShipmentOrder]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) RETURN;

    INSERT INTO [DB_RAMP_BUFF].[dbo].[ZRAMP_QUEUE] 
    (
        [U_Empresa], 
        [U_RampID], 
        [U_OrderDate], 
        [U_ShipDate],
        [U_Action], 
        [U_Status], 
        [CreatedAt]
    )
    SELECT 
        ins.CustomerName, 
        ins.OrderNumber, 
        ins.DocumentDate,
        ins.ActualShipDate,
        CASE 
            WHEN del.Status = 80 AND ins.Status <> 80 THEN 'CANCEL' 
            ELSE 'CREATE' 
        END,
        'P', 
        GETDATE()
    FROM inserted ins
    INNER JOIN deleted del ON ins.OrderNumber = del.OrderNumber
    WHERE 
        ins.CustomerName IN ('GTB', 'PIN', 'TEST') 
        AND (
            -- 1. LÓGICA DE REVERSO: Cambio de Status desde 80 a cualquier otro valor
            (del.Status = 80 AND ins.Status <> 80)
            OR 
            -- 2. LÓGICA DE CREACIÓN: Cumplimiento de los 3 pilares
            (
                ins.Status = 80 
                AND ins.DocumentStatus = 80 
                AND ins.ActualShipDate IS NOT NULL 
                AND (del.Status <> 80 OR del.DocumentStatus <> 80 OR del.ActualShipDate IS NULL)
            )
        );
END

GO

ALTER TABLE [dbo].[ShipmentOrder] ENABLE TRIGGER [TRG_RAMP_TO_SBO_INTEGRATION]
GO


