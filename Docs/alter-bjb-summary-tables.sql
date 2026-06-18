-- 为 PB 汇总表增加「单位」字段（军标采购计划表要求）
-- PB 存储过程改造后将自动写入 x_dw；并行期间默认空字符串。
-- 执行前请确认相关存储过程未使用按位置的 INSERT VALUES(...)。

USE [DKZX_MIS_MSSQL]
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.BJB_XMYJHZ') AND name = N'x_dw')
BEGIN
    ALTER TABLE dbo.BJB_XMYJHZ ADD x_dw char(10) NULL
    CONSTRAINT DF_BJB_XMYJHZ_x_dw DEFAULT ('') WITH VALUES
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.BJB_XMYJB') AND name = N'x_dw')
BEGIN
    ALTER TABLE dbo.BJB_XMYJB ADD x_dw char(10) NULL
    CONSTRAINT DF_BJB_XMYJB_x_dw DEFAULT ('') WITH VALUES
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_BJB_XMYJHZ_fabh' AND object_id = OBJECT_ID(N'dbo.BJB_XMYJHZ'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_BJB_XMYJHZ_fabh
    ON dbo.BJB_XMYJHZ (fabh)
    INCLUDE (x_mc, x_ggxh, x_dw, x_sccj, x_sl, x_bcg_sl, x_flbh, x_hzjb)
END
GO
