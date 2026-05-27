-- ============================================================
-- 自动填价功能 - 数据库脚本
-- 1. 创建 STD_PRICE_HISTORY 表
-- 2. 创建 SP_RefreshPriceHistory 存储过程
-- 3. 一次性回填历史数据 x_wzdh
-- 4. 创建定时任务（需在 SQL Server Agent 中手动配置）
-- ============================================================

USE [DKZX_MIS_MSSQL]
GO

-- ============================================================
-- 1. 创建表
-- ============================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[STD_PRICE_HISTORY]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[STD_PRICE_HISTORY] (
        [id]          INT IDENTITY(1,1) PRIMARY KEY,
        [x_wzdh]     NVARCHAR(400) NOT NULL,        -- 标准化型号指纹
        [ggxh]       NVARCHAR(400) NULL,            -- 原始规格型号
        [x_mc]       NVARCHAR(100) NULL,            -- 元件名称
        [x_dw]       VARCHAR(10) NULL,              -- 计量单位
        [x_sccj]     NVARCHAR(100) NULL,            -- 厂商
        [last_price] DECIMAL(18,4) NOT NULL,        -- 最新报价
        [last_fabh]  VARCHAR(50) NULL,              -- 最新报价来源方案编号
        [last_date]  DATETIME NULL,                 -- 最新报价时间
        [avg_price]  DECIMAL(18,4) NULL,            -- 近5年均价
        [avg_count]  INT DEFAULT 0,                 -- 均价样本数
        [min_price]  DECIMAL(18,4) NULL,            -- 近5年最低价
        [max_price]  DECIMAL(18,4) NULL,            -- 近5年最高价
        [updated_at] DATETIME DEFAULT GETDATE()     -- 最后刷新时间
    );

    CREATE UNIQUE NONCLUSTERED INDEX [UX_STD_PRICE_HISTORY_wzdh]
    ON [dbo].[STD_PRICE_HISTORY]([x_wzdh]);

    PRINT '表 STD_PRICE_HISTORY 创建成功';
END
ELSE
BEGIN
    PRINT '表 STD_PRICE_HISTORY 已存在，跳过创建';
END
GO

-- ============================================================
-- 2. 一次性回填历史数据 x_wzdh（首次部署时执行，仅回填最近5年）
-- ============================================================
-- 注意：此操作可能耗时较长（取决于数据量），建议在低峰期执行
/*
UPDATE BJB 
SET x_wzdh = dbo.F_CleanString(x_ggxh) 
WHERE (x_wzdh IS NULL OR x_wzdh = '')
  AND x_ggxh IS NOT NULL AND LTRIM(RTRIM(x_ggxh)) != ''
  AND x_lx = 11
  AND (x_bjb_datetime >= DATEADD(YEAR, -10, GETDATE()) OR x_bjb_datetime IS NULL);

PRINT '历史数据 x_wzdh 回填完成（最近5年），影响行数: ' + CAST(@@ROWCOUNT AS VARCHAR(20));
*/
GO

-- ============================================================
-- 3. 创建存储过程
-- ============================================================
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SP_RefreshPriceHistory]') AND type = 'P')
    DROP PROCEDURE [dbo].[SP_RefreshPriceHistory];
GO

CREATE PROCEDURE [dbo].[SP_RefreshPriceHistory]
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @tenYearsAgo DATETIME = DATEADD(YEAR, -10, GETDATE());

    -- CTE: 取每个 x_wzdh 的最新一条记录（按 fabh 降序）
    ;WITH LatestRow AS (
        SELECT 
            b.x_wzdh,
            b.x_ggxh,
            b.x_mc,
            b.x_dw,
            b.x_sccj,
            b.x_bjb_dj,
            b.fabh,
            b.x_bjb_datetime,
            ROW_NUMBER() OVER (PARTITION BY b.x_wzdh ORDER BY b.fabh DESC) AS rn
        FROM BJB b
        INNER JOIN BJFAT f ON LTRIM(RTRIM(b.fabh)) = LTRIM(RTRIM(f.fabh))
        WHERE b.x_wzdh IS NOT NULL AND b.x_wzdh != ''
          AND b.x_lx = 11
          AND b.x_bjb_dj > 0
          AND f.dqzt = 10
    ),
    -- CTE: 按 x_wzdh 聚合最近 5 年的统计数据
    RecentStats AS (
        SELECT 
            b.x_wzdh,
            AVG(b.x_bjb_dj) AS avg_price,
            COUNT(*) AS avg_count,
            MIN(b.x_bjb_dj) AS min_price,
            MAX(b.x_bjb_dj) AS max_price
        FROM BJB b
        INNER JOIN BJFAT f ON LTRIM(RTRIM(b.fabh)) = LTRIM(RTRIM(f.fabh))
        WHERE b.x_wzdh IS NOT NULL AND b.x_wzdh != ''
          AND b.x_lx = 11
          AND b.x_bjb_dj > 0
          AND f.dqzt = 10
          AND (b.x_bjb_datetime >= @tenYearsAgo OR b.x_bjb_datetime IS NULL)
        GROUP BY b.x_wzdh
    ),
    -- 合并最新行和统计数据
    SourceData AS (
        SELECT 
            lr.x_wzdh,
            lr.x_ggxh AS ggxh,
            lr.x_mc,
            lr.x_dw,
            lr.x_sccj,
            lr.x_bjb_dj AS last_price,
            lr.fabh AS last_fabh,
            lr.x_bjb_datetime AS last_date,
            rs.avg_price,
            rs.avg_count,
            rs.min_price,
            rs.max_price
        FROM LatestRow lr
        LEFT JOIN RecentStats rs ON lr.x_wzdh = rs.x_wzdh
        WHERE lr.rn = 1
    )
    -- MERGE: 插入或更新
    MERGE INTO STD_PRICE_HISTORY AS target
    USING SourceData AS source
    ON target.x_wzdh = source.x_wzdh
    WHEN MATCHED THEN
        UPDATE SET
            target.ggxh       = source.ggxh,
            target.x_mc       = source.x_mc,
            target.x_dw       = source.x_dw,
            target.x_sccj     = source.x_sccj,
            target.last_price = source.last_price,
            target.last_fabh  = source.last_fabh,
            target.last_date  = source.last_date,
            target.avg_price  = source.avg_price,
            target.avg_count  = source.avg_count,
            target.min_price  = source.min_price,
            target.max_price  = source.max_price,
            target.updated_at = GETDATE()
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (x_wzdh, ggxh, x_mc, x_dw, x_sccj, last_price, last_fabh, last_date, 
                avg_price, avg_count, min_price, max_price, updated_at)
        VALUES (source.x_wzdh, source.ggxh, source.x_mc, source.x_dw, source.x_sccj, source.last_price, 
                source.last_fabh, source.last_date, source.avg_price, source.avg_count, 
                source.min_price, source.max_price, GETDATE());

    PRINT '刷新完成，影响行数: ' + CAST(@@ROWCOUNT AS VARCHAR(20));
END
GO

PRINT '存储过程 SP_RefreshPriceHistory 创建成功';
GO

-- ============================================================
-- 4. 定时任务配置说明
-- ============================================================
-- 在 SQL Server Agent 中创建 Job：
-- Job 名称：RefreshPriceHistory_Daily
-- 执行频率：每天凌晨 2:00
-- 步骤：EXEC [dbo].[SP_RefreshPriceHistory]
-- 
-- 或使用以下脚本自动创建（需要 sysadmin 权限）：
/*
USE msdb;
GO

EXEC sp_add_job 
    @job_name = N'RefreshPriceHistory_Daily',
    @description = N'每天刷新 STD_PRICE_HISTORY 表（历史报价聚合）';

EXEC sp_add_jobstep 
    @job_name = N'RefreshPriceHistory_Daily',
    @step_name = N'执行刷新存储过程',
    @subsystem = N'TSQL',
    @command = N'EXEC [dbo].[SP_RefreshPriceHistory]',
    @database_name = N'DKZX_MIS_MSSQL';

EXEC sp_add_schedule 
    @schedule_name = N'Daily_0200',
    @freq_type = 4,          -- 每天
    @freq_interval = 1,
    @active_start_time = 020000;  -- 02:00:00

EXEC sp_attach_schedule 
    @job_name = N'RefreshPriceHistory_Daily',
    @schedule_name = N'Daily_0200';

EXEC sp_add_jobserver 
    @job_name = N'RefreshPriceHistory_Daily';

PRINT '定时任务 RefreshPriceHistory_Daily 创建成功';
*/
GO
