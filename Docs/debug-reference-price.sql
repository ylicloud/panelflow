-- ============================================================
-- 调试参考价格匹配问题
-- 方案编号: test-2026-01
-- ============================================================

-- 1. 查看该方案下所有元件行的 x_bm 结构和 x_wzdh 情况
SELECT 
    LTRIM(RTRIM(b.x_bm)) AS x_bm,
    LEN(LTRIM(RTRIM(b.x_bm))) AS bm_len,
    SUBSTRING(LTRIM(RTRIM(b.x_bm)), 5, 4) AS bm_4_7,
    LTRIM(RTRIM(b.x_mc)) AS x_mc,
    LTRIM(RTRIM(b.x_ggxh)) AS x_ggxh,
    LTRIM(RTRIM(b.x_wzdh)) AS x_wzdh,
    b.x_lx,
    b.x_bj_dj
FROM BJB b
WHERE LTRIM(RTRIM(b.fabh)) = 'test-2026-01'
  AND LEN(LTRIM(RTRIM(b.x_bm))) = 12
ORDER BY b.x_bm;

-- 2. 查看该方案下元件的 x_wzdh 能匹配到 STD_PRICE_HISTORY 的记录
SELECT 
    LTRIM(RTRIM(b.x_bm)) AS x_bm,
    LTRIM(RTRIM(b.x_mc)) AS x_mc,
    LTRIM(RTRIM(b.x_ggxh)) AS x_ggxh,
    LTRIM(RTRIM(b.x_wzdh)) AS x_wzdh,
    h.last_price,
    h.avg_price,
    h.avg_count
FROM BJB b
LEFT JOIN STD_PRICE_HISTORY h ON LTRIM(RTRIM(b.x_wzdh)) = h.x_wzdh
WHERE LTRIM(RTRIM(b.fabh)) = 'test-2026-01'
  AND LEN(LTRIM(RTRIM(b.x_bm))) = 12
ORDER BY b.x_bm;

-- 3. 仅显示能匹配到历史价格的元件（确认 STD_PRICE_HISTORY 中确实有数据）
SELECT 
    LTRIM(RTRIM(b.x_bm)) AS x_bm,
    LTRIM(RTRIM(b.x_mc)) AS x_mc,
    LTRIM(RTRIM(b.x_wzdh)) AS x_wzdh,
    h.last_price,
    h.avg_price,
    h.min_price,
    h.max_price,
    h.avg_count
FROM BJB b
INNER JOIN STD_PRICE_HISTORY h ON LTRIM(RTRIM(b.x_wzdh)) = h.x_wzdh
WHERE LTRIM(RTRIM(b.fabh)) = 'test-2026-01'
  AND LEN(LTRIM(RTRIM(b.x_bm))) = 12
ORDER BY b.x_bm;

-- 4. 检查 x_bm 的 Substring(4,4) 分布（确认 "0001" 过滤是否正确）
SELECT 
    SUBSTRING(LTRIM(RTRIM(b.x_bm)), 5, 4) AS sub_4_4,
    COUNT(*) AS cnt
FROM BJB b
WHERE LTRIM(RTRIM(b.fabh)) = 'test-2026-01'
  AND LEN(LTRIM(RTRIM(b.x_bm))) = 12
GROUP BY SUBSTRING(LTRIM(RTRIM(b.x_bm)), 5, 4)
ORDER BY sub_4_4;

-- 5. 检查 STD_PRICE_HISTORY 表是否有数据
SELECT TOP 10 * FROM STD_PRICE_HISTORY ORDER BY updated_at DESC;
