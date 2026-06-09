-- ============================================================
-- 历史价格维护 - 剔除来源表 STD_PRICE_EXCLUSION
-- 用途：标记不参与 SP_RefreshPriceHistory 聚合的报价来源
--       x_wzdh 为 NULL 表示整单剔除；非空表示仅剔除该单内某型号来源
-- ============================================================

USE [DKZX_MIS_MSSQL]
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[STD_PRICE_EXCLUSION]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[STD_PRICE_EXCLUSION] (
        [id]         INT IDENTITY(1,1) PRIMARY KEY,
        [fabh]       VARCHAR(50) NOT NULL,
        [x_wzdh]     NVARCHAR(400) NULL,
        [reason]     NVARCHAR(500) NULL,
        [created_by] NVARCHAR(50) NULL,
        [created_at] DATETIME NOT NULL DEFAULT GETDATE()
    );

    -- 按型号来源剔除：同一 fabh + x_wzdh 唯一
    CREATE UNIQUE NONCLUSTERED INDEX [UX_STD_PRICE_EXCLUSION_fabh_wzdh]
    ON [dbo].[STD_PRICE_EXCLUSION]([fabh], [x_wzdh])
    WHERE [x_wzdh] IS NOT NULL;

    -- 整单剔除：同一 fabh 仅允许一条整单记录
    CREATE UNIQUE NONCLUSTERED INDEX [UX_STD_PRICE_EXCLUSION_fabh_whole]
    ON [dbo].[STD_PRICE_EXCLUSION]([fabh])
    WHERE [x_wzdh] IS NULL;

    PRINT N'表 STD_PRICE_EXCLUSION 创建成功';
END
ELSE
BEGIN
    PRINT N'表 STD_PRICE_EXCLUSION 已存在，跳过创建';
END
GO
