-- ============================================================
-- STD_PRICE_HISTORY 回填 x_dw（计量单位）、x_sccj（生产厂商）
-- 策略：库内传播 → BJB_XMYJHZ 汇总表 → 名称嵌入品牌 → 型号规则推断 → 名称类别推断单位
-- 仅更新当前为空的字段，不覆盖已有值
-- ============================================================
USE [DKZX_MIS_MSSQL]
GO

SET NOCOUNT ON;

DECLARE @before_dw INT, @before_sccj INT, @after_dw INT, @after_sccj INT;

SELECT @before_dw = SUM(CASE WHEN NULLIF(LTRIM(RTRIM(x_dw)),'') IS NULL THEN 1 ELSE 0 END),
       @before_sccj = SUM(CASE WHEN NULLIF(LTRIM(RTRIM(x_sccj)),'') IS NULL THEN 1 ELSE 0 END)
FROM STD_PRICE_HISTORY;

PRINT '回填前缺失: x_dw=' + CAST(@before_dw AS VARCHAR) + ', x_sccj=' + CAST(@before_sccj AS VARCHAR);

-- ============================================================
-- 厂商名称规范化（BJB_XMYJHZ 中多种写法统一）
-- ============================================================
IF OBJECT_ID('tempdb.#NormSccj') IS NOT NULL DROP TABLE #NormSccj;
CREATE TABLE #NormSccj (raw_sccj NVARCHAR(100) PRIMARY KEY, norm_sccj NVARCHAR(100) NOT NULL);

INSERT INTO #NormSccj (raw_sccj, norm_sccj) VALUES
(N'西门子', N'西门子'),
(N'北京西门子', N'西门子'),
(N'施耐德', N'施耐德'),
(N'上海施耐德', N'施耐德'),
(N'天津施耐德', N'施耐德'),
(N'上海施耐德工业控制有限公司', N'施耐德'),
(N'万可电子（天津）有限公司', N'WAGO'),
(N'南京菲尼克斯电气有限公司', N'菲尼克斯'),
(N'德力西集团', N'德力西'),
(N'浙江正泰电器股份有限公司', N'正泰'),
(N'ABB（中国）有限公司', N'ABB');

-- ============================================================
-- Step 1: 从同表已填 x_mc 传播厂商（取该名称下出现最多的厂商）
-- ============================================================
;WITH McSccj AS (
    SELECT x_mc, x_sccj, COUNT(*) AS cnt,
           ROW_NUMBER() OVER (PARTITION BY x_mc ORDER BY COUNT(*) DESC, x_sccj) AS rn
    FROM STD_PRICE_HISTORY
    WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NOT NULL
    GROUP BY x_mc, x_sccj
),
BestMcSccj AS (
    SELECT x_mc, x_sccj FROM McSccj WHERE rn = 1
)
UPDATE h SET h.x_sccj = b.x_sccj
FROM STD_PRICE_HISTORY h
INNER JOIN BestMcSccj b ON h.x_mc = b.x_mc
WHERE NULLIF(LTRIM(RTRIM(h.x_sccj)), '') IS NULL;

PRINT 'Step1 同表x_mc传播厂商: ' + CAST(@@ROWCOUNT AS VARCHAR);

-- Step 1b: 从同表已填 x_mc 传播单位
;WITH McDw AS (
    SELECT x_mc, x_dw, COUNT(*) AS cnt,
           ROW_NUMBER() OVER (PARTITION BY x_mc ORDER BY COUNT(*) DESC, x_dw) AS rn
    FROM STD_PRICE_HISTORY
    WHERE NULLIF(LTRIM(RTRIM(x_dw)), '') IS NOT NULL
    GROUP BY x_mc, x_dw
),
BestMcDw AS (
    SELECT x_mc, x_dw FROM McDw WHERE rn = 1
)
UPDATE h SET h.x_dw = b.x_dw
FROM STD_PRICE_HISTORY h
INNER JOIN BestMcDw b ON h.x_mc = b.x_mc
WHERE NULLIF(LTRIM(RTRIM(h.x_dw)), '') IS NULL;

PRINT 'Step1b 同表x_mc传播单位: ' + CAST(@@ROWCOUNT AS VARCHAR);

-- ============================================================
-- Step 2: 从 BJB_XMYJHZ 按 x_mc 匹配厂商（排除不详）
-- ============================================================
IF OBJECT_ID('tempdb.#XmyjhzSccj') IS NOT NULL DROP TABLE #XmyjhzSccj;
;WITH Z AS (
    SELECT LTRIM(RTRIM(z.x_mc)) AS x_mc,
           COALESCE(n.norm_sccj, LTRIM(RTRIM(z.x_sccj))) AS x_sccj,
           COUNT(*) AS cnt
    FROM BJB_XMYJHZ z
    LEFT JOIN #NormSccj n ON LTRIM(RTRIM(z.x_sccj)) = n.raw_sccj
    WHERE NULLIF(LTRIM(RTRIM(z.x_sccj)), '') IS NOT NULL
      AND LTRIM(RTRIM(z.x_sccj)) NOT IN ('厂家不详', '厂家不祥', '0069', '天津市河东区')
    GROUP BY LTRIM(RTRIM(z.x_mc)) COLLATE Chinese_PRC_CI_AS,
             COALESCE(n.norm_sccj, LTRIM(RTRIM(z.x_sccj)) COLLATE Chinese_PRC_CI_AS)
),
Ranked AS (
    SELECT x_mc, x_sccj,
           ROW_NUMBER() OVER (PARTITION BY x_mc ORDER BY cnt DESC, x_sccj) AS rn
    FROM Z
)
SELECT x_mc, x_sccj INTO #XmyjhzSccj FROM Ranked WHERE rn = 1;

UPDATE h SET h.x_sccj = z.x_sccj
FROM STD_PRICE_HISTORY h
INNER JOIN #XmyjhzSccj z ON LTRIM(RTRIM(h.x_mc)) COLLATE Chinese_PRC_CI_AS = z.x_mc
WHERE NULLIF(LTRIM(RTRIM(h.x_sccj)), '') IS NULL;

PRINT 'Step2 BJB_XMYJHZ按x_mc匹配厂商: ' + CAST(@@ROWCOUNT AS VARCHAR);

-- ============================================================
-- Step 3: 从 x_mc 嵌入品牌名提取厂商
-- ============================================================
UPDATE STD_PRICE_HISTORY SET x_sccj = N'施耐德'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (x_mc LIKE '%施耐德%' OR x_mc LIKE '%Schneider%');

UPDATE STD_PRICE_HISTORY SET x_sccj = N'西门子'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (x_mc LIKE '%西门子%' OR x_mc LIKE '%Siemens%');

UPDATE STD_PRICE_HISTORY SET x_sccj = N'ABB'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND x_mc LIKE '%ABB%';

UPDATE STD_PRICE_HISTORY SET x_sccj = N'德力西'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND x_mc LIKE '%德力西%';

UPDATE STD_PRICE_HISTORY SET x_sccj = N'正泰'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND x_mc LIKE '%正泰%';

UPDATE STD_PRICE_HISTORY SET x_sccj = N'研华'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (x_mc LIKE '%研华%' OR x_mc LIKE '%Advantech%');

UPDATE STD_PRICE_HISTORY SET x_sccj = N'WAGO'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (x_mc LIKE '%WAGO%' OR x_mc LIKE '%万可%');

UPDATE STD_PRICE_HISTORY SET x_sccj = N'魏德米勒'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (x_mc LIKE '%魏德%' OR x_mc LIKE '%Weidmuller%');

UPDATE STD_PRICE_HISTORY SET x_sccj = N'惠普'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (x_mc LIKE '%惠普%' OR x_mc LIKE '%HP %' OR x_mc LIKE '%(HP)%');

UPDATE STD_PRICE_HISTORY SET x_sccj = N'山特'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (x_mc LIKE '%SANTAK%' OR x_mc LIKE '%山特%');

UPDATE STD_PRICE_HISTORY SET x_sccj = N'天逸'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (x_mc LIKE '%天逸%' OR x_mc LIKE '%TAYEE%');

UPDATE STD_PRICE_HISTORY SET x_sccj = N'菲尼克斯'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (x_mc LIKE '%菲尼克斯%' OR x_mc LIKE '%Phoenix%');

UPDATE STD_PRICE_HISTORY SET x_sccj = N'世邦'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (x_mc LIKE '%SPON%' OR x_mc LIKE '%世邦%');

UPDATE STD_PRICE_HISTORY SET x_sccj = N'欧姆龙'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (x_mc LIKE '%欧姆龙%' OR x_mc LIKE '%OMRON%');

PRINT 'Step3 名称嵌入品牌提取厂商完成';

-- ============================================================
-- Step 4: 从 ggxh 型号规则推断厂商（施耐德 Acti9 / TeSys 等）
-- ============================================================
UPDATE STD_PRICE_HISTORY SET x_sccj = N'施耐德'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (
    ggxh LIKE '%IC65%' OR ggxh LIKE '%iC65%' OR ggxh LIKE '%C65H%' OR ggxh LIKE '%C65N%'
    OR ggxh LIKE '%NSX%' OR ggxh LIKE '%CVS%' OR ggxh LIKE '%GV2%' OR ggxh LIKE '%LC1%'
    OR ggxh LIKE '%LRD%' OR ggxh LIKE '%LRE%' OR ggxh LIKE '%CW3%' OR ggxh LIKE '%CW1%'
    OR ggxh LIKE '%CW2%' OR ggxh LIKE '%XB2%' OR ggxh LIKE '%XB4%' OR ggxh LIKE '%LA39%'
    OR ggxh LIKE '%NSX100%' OR ggxh LIKE '%NSX160%' OR ggxh LIKE '%NSX250%'
    OR ggxh LIKE '%NSX630%' OR ggxh LIKE '%CM3-%' OR ggxh LIKE '%GSB2%'
    OR ggxh LIKE '%ZB4%' OR ggxh LIKE '%ZB 4%'
  );

UPDATE STD_PRICE_HISTORY SET x_sccj = N'西门子'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (
    ggxh LIKE '%3RV%' OR ggxh LIKE '%3RT%' OR ggxh LIKE '%3RW%' OR ggxh LIKE '%3RP%'
    OR ggxh LIKE '%3RU%' OR ggxh LIKE '%3VA%' OR ggxh LIKE '%3SU%' OR ggxh LIKE '%3NA%'
    OR ggxh LIKE '%3NE%' OR ggxh LIKE '%3NP%' OR ggxh LIKE '%3KL%' OR ggxh LIKE '%3LD%'
    OR ggxh LIKE '%3KA%' OR ggxh LIKE '%3KC%' OR ggxh LIKE '%3KE%'
    OR ggxh LIKE '%6ES%' OR ggxh LIKE '%6XV%' OR ggxh LIKE '%6GK%' OR ggxh LIKE '%6EP%'
    OR ggxh LIKE '%6FC%' OR ggxh LIKE '%6SL%' OR ggxh LIKE '%6SE%'
    OR ggxh LIKE '%S7-%' OR ggxh LIKE '%ET200%' OR ggxh LIKE '%SM3%' OR ggxh LIKE '%SM1%'
    OR ggxh LIKE '%PM2%' OR ggxh LIKE '%CU3%' OR ggxh LIKE '%CU1%' OR ggxh LIKE '%G120%'
    OR ggxh LIKE '%S120%' OR ggxh LIKE '%5SY%'
  );

UPDATE STD_PRICE_HISTORY SET x_sccj = N'ABB'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (
    ggxh LIKE '%AF%' OR ggxh LIKE '%S200%' OR ggxh LIKE '%S800%'
    OR ggxh LIKE '%T1N%' OR ggxh LIKE '%T2N%' OR ggxh LIKE '%T3N%'
    OR ggxh LIKE '%T4N%' OR ggxh LIKE '%T5N%' OR ggxh LIKE '%T6N%' OR ggxh LIKE '%T7N%'
  );

UPDATE STD_PRICE_HISTORY SET x_sccj = N'德力西'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (ggxh LIKE '%CDM%' OR ggxh LIKE '%DZ47%' OR ggxh LIKE '%DZ15%');

UPDATE STD_PRICE_HISTORY SET x_sccj = N'正泰'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (ggxh LIKE '%NM1%' OR ggxh LIKE '%NXB%');

UPDATE STD_PRICE_HISTORY SET x_sccj = N'魏德米勒'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (ggxh LIKE '%174%' OR ggxh LIKE '%178%' OR ggxh LIKE '%12025%');

UPDATE STD_PRICE_HISTORY SET x_sccj = N'WAGO'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (ggxh LIKE '%2002-%' OR ggxh LIKE '%2004-%' OR ggxh LIKE '%2587-%' OR ggxh LIKE '%734-%');

UPDATE STD_PRICE_HISTORY SET x_sccj = N'天逸'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (ggxh LIKE 'AD17%' OR ggxh LIKE '%AD17-%');

UPDATE STD_PRICE_HISTORY SET x_sccj = N'菲尼克斯'
WHERE NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NULL
  AND (ggxh LIKE 'PTFIX%' OR ggxh LIKE '%PTFIX%');

PRINT 'Step4 型号规则推断厂商完成';

-- ============================================================
-- Step 5: 从 x_mc 产品类别推断计量单位
-- ============================================================
-- 米：线缆类
UPDATE STD_PRICE_HISTORY SET x_dw = N'米'
WHERE NULLIF(LTRIM(RTRIM(x_dw)), '') IS NULL
  AND (
    x_mc LIKE '%电缆%' OR x_mc LIKE '%网线%' OR x_mc LIKE '%线缆%'
    OR x_mc LIKE '%光纤%' OR x_mc LIKE '%母线槽%'
    OR (x_mc LIKE '%铜排%' AND (ggxh LIKE '%米%' OR ggxh LIKE '%m%' OR ggxh LIKE '%M%'))
  );

-- 台：设备类
UPDATE STD_PRICE_HISTORY SET x_dw = N'台'
WHERE NULLIF(LTRIM(RTRIM(x_dw)), '') IS NULL
  AND (
    x_mc LIKE '%显示器%' OR x_mc LIKE '%工控机%' OR x_mc LIKE '%UPS%'
    OR x_mc LIKE '%交换机%' OR x_mc LIKE '%风机%' OR x_mc LIKE '%空调%'
    OR x_mc LIKE '%对讲%' OR x_mc LIKE '%服务器%' OR x_mc LIKE '%电脑%'
    OR x_mc LIKE '%打印机%' OR x_mc LIKE '%扫描仪%'
  );

-- 只：开关电器类
UPDATE STD_PRICE_HISTORY SET x_dw = N'只'
WHERE NULLIF(LTRIM(RTRIM(x_dw)), '') IS NULL
  AND (
    x_mc LIKE '%断路器%' OR x_mc LIKE '%接触器%' OR x_mc LIKE '%继电器%'
    OR x_mc LIKE '%空气开关%' OR x_mc LIKE '%空开%' OR x_mc LIKE '%按钮%'
    OR x_mc LIKE '%指示灯%' OR x_mc LIKE '%组合指示灯%'
    OR x_mc LIKE '%熔断器%' OR x_mc LIKE '%隔离开关%'
    OR x_mc LIKE '%热过载%' OR x_mc LIKE '%浪涌%' OR x_mc LIKE '%MCB%'
    OR x_mc LIKE '%塑壳%' OR x_mc LIKE '%互感器%'
    OR x_mc LIKE '%电流互感器%' OR x_mc LIKE '%电压互感器%'
    OR x_mc LIKE '%选择开关%' OR x_mc LIKE '%转换开关%'
    OR x_mc LIKE '%凸轮开关%' OR x_mc LIKE '%限位开关%'
    OR x_mc LIKE '%辅助触点%' OR x_mc LIKE '%灭弧罩%'
  );

-- 件：零部件/模块类
UPDATE STD_PRICE_HISTORY SET x_dw = N'件'
WHERE NULLIF(LTRIM(RTRIM(x_dw)), '') IS NULL
  AND (
    x_mc LIKE '%端子%' OR x_mc LIKE '%标记%' OR x_mc LIKE '%熔芯%'
    OR x_mc LIKE '%导轨%' OR x_mc LIKE '%模块%' OR x_mc LIKE '%插头%'
    OR x_mc LIKE '%存储卡%' OR x_mc LIKE '%CPU%' OR x_mc LIKE '%电抗器%'
    OR x_mc LIKE '%电阻%' OR x_mc LIKE '%制动电阻%' OR x_mc LIKE '%铜排%'
    OR x_mc LIKE '%接线箱%' OR x_mc LIKE '%跨接%' OR x_mc LIKE '%梳状%'
    OR x_mc LIKE '%垫块%' OR x_mc LIKE '%挡板%' OR x_mc LIKE '%盖板%'
    OR x_mc LIKE '%底座%' OR x_mc LIKE '%外壳%' OR x_mc LIKE '%支架%'
    OR x_mc LIKE '%变频器%' OR x_mc LIKE '%电源模块%' OR x_mc LIKE '%开关电源%'
    OR x_mc LIKE '%分接器%' OR x_mc LIKE '%适配器%'
  );

-- 套：成套类
UPDATE STD_PRICE_HISTORY SET x_dw = N'套'
WHERE NULLIF(LTRIM(RTRIM(x_dw)), '') IS NULL
  AND (x_mc LIKE '%工具%' OR x_mc LIKE '%键鼠%' OR x_mc LIKE '%套件%');

PRINT 'Step5 名称类别推断单位完成';

-- ============================================================
-- 汇总
-- ============================================================
SELECT @after_dw = SUM(CASE WHEN NULLIF(LTRIM(RTRIM(x_dw)),'') IS NULL THEN 1 ELSE 0 END),
       @after_sccj = SUM(CASE WHEN NULLIF(LTRIM(RTRIM(x_sccj)),'') IS NULL THEN 1 ELSE 0 END)
FROM STD_PRICE_HISTORY;

PRINT '回填后缺失: x_dw=' + CAST(@after_dw AS VARCHAR) + ', x_sccj=' + CAST(@after_sccj AS VARCHAR);
PRINT '已填充 x_dw: ' + CAST(@before_dw - @after_dw AS VARCHAR);
PRINT '已填充 x_sccj: ' + CAST(@before_sccj - @after_sccj AS VARCHAR);

-- 更新 refreshed_at
UPDATE STD_PRICE_HISTORY SET updated_at = GETDATE()
WHERE NULLIF(LTRIM(RTRIM(x_dw)), '') IS NOT NULL OR NULLIF(LTRIM(RTRIM(x_sccj)), '') IS NOT NULL;

GO
