-- ============================================================
-- SP_RefreshPriceHistory 存储过程
-- 功能：每日刷新 STD_PRICE_HISTORY 聚合表
-- 执行频率：SQL Server Agent 每天凌晨 2:00
-- 数据库：SQL Server 2022
-- 依赖：STD_PRICE_EXCLUSION 剔除来源表（见 create-std-price-exclusion.sql）
--
-- 筛选口径：
--   - 仅纳入 x_bjb_datetime 非空且在近 5 年内的元件行（排除早期无报价日期的陈旧数据）
--   - 排除 STD_PRICE_EXCLUSION 中标记的整单或按型号来源
-- ============================================================

USE [DKZX_MIS_MSSQL]
GO

CREATE OR ALTER PROCEDURE [dbo].[SP_RefreshPriceHistory]
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @fiveYearsAgo DATETIME = DATEADD(YEAR, -5, GETDATE());

    -- CTE 1: 取每个 x_wzdh 在近 5 年内的最新一条记录（按 fabh 降序）
    ;WITH LatestRow AS (
        SELECT
            LTRIM(RTRIM(b.x_wzdh))          AS x_wzdh,
            LTRIM(RTRIM(b.x_ggxh))          AS x_ggxh,
            LTRIM(RTRIM(b.x_mc))            AS x_mc,
            LTRIM(RTRIM(b.x_dw))            AS x_dw,
            LTRIM(RTRIM(b.x_sccj))          AS x_sccj,
            b.x_bj_dj,
            LTRIM(RTRIM(b.fabh))            AS fabh,
            b.x_bjb_datetime,
            ROW_NUMBER() OVER (
                PARTITION BY LTRIM(RTRIM(b.x_wzdh))
                ORDER BY b.fabh DESC
            ) AS rn
        FROM BJB b
        INNER JOIN BJFAT f ON LTRIM(RTRIM(b.fabh)) = LTRIM(RTRIM(f.fabh))
        WHERE b.x_wzdh IS NOT NULL
          AND LTRIM(RTRIM(b.x_wzdh)) != ''
          AND b.x_bjb_datetime IS NOT NULL
          AND b.x_bjb_datetime >= @fiveYearsAgo
          AND b.x_lx = 11
          AND b.x_bj_dj > 0
          AND f.dqzt = 10
          AND NOT EXISTS (
              SELECT 1 FROM [dbo].[STD_PRICE_EXCLUSION] e
              WHERE LTRIM(RTRIM(e.fabh)) = LTRIM(RTRIM(b.fabh))
                AND (e.x_wzdh IS NULL OR LTRIM(RTRIM(e.x_wzdh)) = LTRIM(RTRIM(b.x_wzdh)))
          )
    ),

    -- CTE 2: 按 x_wzdh 聚合近 5 年统计数据（x_bjb_datetime 非空）
    RecentStats AS (
        SELECT
            LTRIM(RTRIM(b.x_wzdh))          AS x_wzdh,
            AVG(b.x_bj_dj)                  AS avg_price,
            COUNT(*)                         AS avg_count,
            MIN(b.x_bj_dj)                  AS min_price,
            MAX(b.x_bj_dj)                  AS max_price
        FROM BJB b
        INNER JOIN BJFAT f ON LTRIM(RTRIM(b.fabh)) = LTRIM(RTRIM(f.fabh))
        WHERE b.x_wzdh IS NOT NULL
          AND LTRIM(RTRIM(b.x_wzdh)) != ''
          AND b.x_bjb_datetime IS NOT NULL
          AND b.x_bjb_datetime >= @fiveYearsAgo
          AND b.x_lx = 11
          AND b.x_bj_dj > 0
          AND f.dqzt = 10
          AND NOT EXISTS (
              SELECT 1 FROM [dbo].[STD_PRICE_EXCLUSION] e
              WHERE LTRIM(RTRIM(e.fabh)) = LTRIM(RTRIM(b.fabh))
                AND (e.x_wzdh IS NULL OR LTRIM(RTRIM(e.x_wzdh)) = LTRIM(RTRIM(b.x_wzdh)))
          )
        GROUP BY LTRIM(RTRIM(b.x_wzdh))
    ),

    -- CTE 3: 合并最新行与统计数据
    SourceData AS (
        SELECT
            lr.x_wzdh,
            lr.x_ggxh       AS ggxh,
            lr.x_mc,
            lr.x_dw,
            lr.x_sccj,
            lr.x_bj_dj      AS last_price,
            lr.fabh          AS last_fabh,
            lr.x_bjb_datetime AS last_date,
            rs.avg_price,
            rs.avg_count,
            rs.min_price,
            rs.max_price
        FROM LatestRow lr
        LEFT JOIN RecentStats rs ON lr.x_wzdh = rs.x_wzdh
        WHERE lr.rn = 1
    )

    -- MERGE: upsert 到 STD_PRICE_HISTORY，并删除已无有效来源的型号
    MERGE INTO [dbo].[STD_PRICE_HISTORY] AS target
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
        INSERT (x_wzdh, ggxh, x_mc, x_dw, x_sccj,
                last_price, last_fabh, last_date,
                avg_price, avg_count, min_price, max_price,
                updated_at)
        VALUES (source.x_wzdh, source.ggxh, source.x_mc, source.x_dw, source.x_sccj,
                source.last_price, source.last_fabh, source.last_date,
                source.avg_price, source.avg_count, source.min_price, source.max_price,
                GETDATE())

    WHEN NOT MATCHED BY SOURCE THEN
        DELETE;

    PRINT N'SP_RefreshPriceHistory 刷新完成，影响行数: ' + CAST(@@ROWCOUNT AS VARCHAR(20));
END
GO
