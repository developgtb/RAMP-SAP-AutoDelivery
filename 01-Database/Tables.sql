CREATE TABLE [dbo].[ZRAMP_QUEUE](
    -- ID como BIGINT y Autoincremental (empieza en 1, aumenta de 1 en 1)
    [ID] [bigint] IDENTITY(1,1) NOT NULL, 
    
    [U_Empresa] [varchar](20) DEFAULT '' NOT NULL,
    [U_RampID] [varchar](20) DEFAULT '' NOT NULL,
    [U_OrderDate] [datetime] NOT NULL,
    [U_ShipDate] [datetime] NULL,
    [U_Action] [varchar](10) DEFAULT '' NOT NULL, 
    [U_Status] [char](1) CONSTRAINT [DF_ZRAMP_QUEUE_Status] DEFAULT 'P' NOT NULL,
    [U_DocEntry] [int] DEFAULT 0 NOT NULL,
    [U_DocNum] [int] DEFAULT 00 NOT NULL,
    [U_ErrorMsg] [nvarchar](max) DEFAULT '' NOT NULL,
    [U_RetryCount] [int] NOT NULL CONSTRAINT [DF_ZRAMP_QUEUE_Retry] DEFAULT 0,
    [CreatedAt] [datetime] NOT NULL CONSTRAINT [DF_ZRAMP_QUEUE_Date] DEFAULT GETDATE(),
    [ProcessedAt] [datetime] NULL,
    
    CONSTRAINT [PK_ZRAMP_QUEUE] PRIMARY KEY CLUSTERED ([ID] ASC)
);
GO

-- Índice optimizado para el motor de búsqueda del servicio C#
CREATE INDEX IX_ZRAMP_PENDIENTES ON [dbo].[ZRAMP_QUEUE] (U_Status, CreatedAt);
GO