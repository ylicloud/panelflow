# 自动填价功能设计文档

## 概述

基于历史报价数据，为新报价单中的元件自动匹配历史价格，快速生成报价。

## 核心流程

```
报价员导入元件 → 保存方案（写入 BJB，同时写 x_wzdh 标准化指纹）
                         ↓
              数据库定时任务刷新 STD_PRICE_HISTORY
                         ↓
报价员进入填价页面 → 点击"引用历史报价"按钮
                         ↓
              后端用当前元件的 x_wzdh JOIN STD_PRICE_HISTORY
                         ↓
              返回 last_price → 批量更新 BJB 中对应行的单价
                         ↓
              前端刷新表格，信息栏显示匹配统计
```

## 数据库设计

### 新建表：STD_PRICE_HISTORY

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | `INT IDENTITY(1,1)` | 主键 |
| `x_wzdh` | `NVARCHAR(400) NOT NULL` | 标准化型号指纹（F_CleanString 结果），唯一索引 |
| `ggxh` | `NVARCHAR(400)` | 原始规格型号（取最新一条的原始值，方便人工查看） |
| `x_mc` | `NVARCHAR(100)` | 元件名称（取最新一条） |
| `x_sccj` | `NVARCHAR(100)` | 厂商（取最新一条） |
| `last_price` | `DECIMAL(18,4) NOT NULL` | 最新报价（最近一次 x_bjb_dj > 0 的值） |
| `last_fabh` | `VARCHAR(50)` | 最新报价来源方案编号 |
| `last_date` | `DATETIME` | 最新报价时间（x_bjb_datetime） |
| `avg_price` | `DECIMAL(18,4)` | 最近 2 年平均报价 |
| `avg_count` | `INT DEFAULT 0` | 参与平均计算的记录数 |
| `min_price` | `DECIMAL(18,4)` | 最近 2 年最低价 |
| `max_price` | `DECIMAL(18,4)` | 最近 2 年最高价 |
| `updated_at` | `DATETIME DEFAULT GETDATE()` | 本行最后刷新时间 |

**索引：**
- `UX_STD_PRICE_HISTORY_wzdh` — x_wzdh 唯一索引

### 存储过程：SP_RefreshPriceHistory

**刷新逻辑：**
1. 从 BJB 中筛选有效元件行（x_lx = 11, x_bjb_dj > 0, x_wzdh 非空）
2. 按 x_wzdh 分组
3. last_price / last_fabh / last_date / ggxh / x_mc / x_sccj 取最新一条（fabh 降序）
4. avg_price / avg_count / min_price / max_price 取最近 2 年数据聚合
5. 使用 MERGE 语句做 upsert

### 定时任务

- SQL Server Agent Job
- 执行频率：每天凌晨 2:00
- 执行内容：`EXEC SP_RefreshPriceHistory`

## 后端 API 设计

### POST /Quotation/AutoFillPriceFromHistory

**请求：**
```json
{ "fabh": "BJ2026-001" }
```

**处理逻辑：**
1. 查询当前报价单 BJB 中所有 x_lx=11 且 x_wzdh 非空的元件
2. JOIN STD_PRICE_HISTORY 取 last_price
3. 批量 UPDATE BJB 中匹配到的行：x_bjb_dj = last_price, x_bjb_bj = last_price, x_bj_dj = last_price
4. 仅更新当前单价为 0 的行（不覆盖已手动填写的价格）

**响应：**
```json
{
  "success": true,
  "matched": 45,
  "total": 60,
  "message": "已匹配 45/60 个元件的历史报价，15 个元件无历史记录"
}
```

**权限：** 管理员、报价员（与现有 SavePlan 一致）

## 前端设计

### FillPrice 页面

- 工具栏新增"引用历史报价"按钮
- 点击后调用 `POST /Quotation/AutoFillPriceFromHistory`
- 成功后刷新表格数据（重新加载元件列表）
- 信息栏显示匹配结果统计

### 交互规则

- 仅更新单价为 0 的元件（不覆盖已手动填写的价格）
- 按钮可重复点击（幂等操作）
- 未匹配到的元件保持原值不变

## 匹配规则

- 匹配 key：`x_wzdh`（C# NormalizeSpec / SQL F_CleanString 的输出）
- 排除当前报价单自身
- 只取 x_lx = 11 的元件行
- 只取 x_bjb_dj > 0 的记录

## 数据准备

历史数据需要一次性回填 x_wzdh：
```sql
UPDATE BJB 
SET x_wzdh = dbo.F_CleanString(x_ggxh) 
WHERE (x_wzdh IS NULL OR x_wzdh = '')
  AND x_ggxh IS NOT NULL AND x_ggxh != ''
  AND x_lx = 11
```

## 风险与注意事项

1. **价格时效性** — 历史价格可能过时，仅作参考，报价员需确认
2. **同型号不同厂商** — 取最新一条的厂商和价格，可能与当前需求不完全匹配
3. **首次使用** — 需先执行历史数据回填 + 存储过程刷新
4. **不覆盖已填价格** — 只更新单价为 0 的行，避免误覆盖手动调整
