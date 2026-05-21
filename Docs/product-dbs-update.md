## 创建审计表(产品库已创建)

## KHYLB 增加字段
USE [DKZX_MIS_MSSQL]
GO

-- 检查字段是否已存在（避免重复执行报错）
IF NOT EXISTS (
    SELECT 1 FROM sys.columns 
    WHERE object_id = OBJECT_ID('dbo.KHYLB') 
    AND name = 'created_at'
)
BEGIN
    ALTER TABLE [dbo].[KHYLB] ADD [created_at] [datetime] NULL;
    PRINT '已添加 created_at 字段';
END
ELSE
BEGIN
    PRINT 'created_at 字段已存在，跳过';
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns 
    WHERE object_id = OBJECT_ID('dbo.KHYLB') 
    AND name = 'updated_at'
)
BEGIN
    ALTER TABLE [dbo].[KHYLB] ADD [updated_at] [datetime] NULL;
    PRINT '已添加 updated_at 字段';
END
ELSE
BEGIN
    PRINT 'updated_at 字段已存在，跳过';
END
GO

-- 可选：为历史数据设置默认值（如需要）
-- UPDATE [dbo].[KHYLB] SET [created_at] = GETDATE() WHERE [created_at] IS NULL;
-- UPDATE [dbo].[KHYLB] SET [updated_at] = GETDATE() WHERE [updated_at] IS NULL;
-- GO

PRINT 'KHYLB 表结构更新完成';
GO
