# Implementation Plan: Auto-Fill Price (自动填价)

## Overview

基于历史报价数据为当前报价单元件自动匹配并填充历史价格。实现顺序遵循 Model → Service → Controller → View → JS/CSS 原则，采用"后端更新 + 前端同步"双写策略。

## Tasks

- [x] 1. 数据层：实体与数据库映射
  - [x] 1.1 创建 StdPriceHistory 实体类
    - 在 `PanelFlow.Infrastructure/Entities/` 下新建 `StdPriceHistory.cs`
    - 包含字段：Id, x_wzdh, ggxh, x_mc, x_dw, x_sccj, last_price, last_fabh, last_date, avg_price, avg_count, min_price, max_price, updated_at
    - _Requirements: 1.1_

  - [x] 1.2 扩展 BjbItem 实体并注册 StdPriceHistory DbSet
    - 在 `BjbItem.cs` 中补充缺失字段：x_bjb_dj, x_bjb_bj, x_lx, x_bjb_datetime
    - 在 `ApplicationDbContext.cs` 中添加 `DbSet<StdPriceHistory>` 并配置 OnModelCreating 映射（表名 STD_PRICE_HISTORY，x_wzdh 唯一索引，decimal(18,4) 精度）
    - 在 BjbItem 的 OnModelCreating 中补充 x_bjb_dj、x_bjb_bj、x_lx、x_bjb_datetime 的列映射
    - _Requirements: 1.1, 2.1_

  - [x] 1.3 创建 SP_RefreshPriceHistory 存储过程 SQL 脚本
    - 在 `Docs/` 下创建 `SP_RefreshPriceHistory.sql`
    - 使用 MERGE 语句实现 upsert：从 BJB JOIN BJFAT(dqzt=10) 筛选 x_lx=11、x_bj_dj>0、x_wzdh 非空的记录
    - 按 x_wzdh 分组，取 fabh 降序第一条作为最新报价信息
    - 计算近 5 年均价、最低价、最高价、样本数
    - _Requirements: 1.2, 1.3, 1.4, 1.8_

- [x] 2. 后端核心逻辑：DTO 与 ViewModel
  - [x] 2.1 创建 AutoFillPrice 相关 DTO/ViewModel
    - 在 `PanelFlow.Web/Models/Quotation/` 下新建 `AutoFillPriceRequest.cs`（含 [Required] Fabh 属性）
    - 新建 `AutoFillPriceResult.cs`（含 Success, Matched, Updated, Unmatched, Total, Message, Prices 字典）
    - 新建 `PriceInfo.cs`（含 Price, Unit, Vendor）
    - 新建 `ReferencePriceRow.cs`（含 LastPrice, AvgPrice, MinPrice, MaxPrice, AvgCount）
    - _Requirements: 2.5, 5.4, 6.1_

- [x] 3. 后端核心逻辑：Controller 端点实现
  - [x] 3.1 重构 AutoFillPriceFromHistory 端点（后端更新 + 前端同步）
    - 修改 `QuotationController.cs` 中已有的 AutoFillPriceFromHistory 方法
    - 添加权限校验：验证登录状态、报价人/管理员身份、报价单状态（dqzt≠10）
    - 查询 BJB 中 x_lx=11、x_bm.Length=12 的元件行
    - 对 x_wzdh 为空的行实时调用 NormalizeSpec 计算指纹
    - 批量查询 STD_PRICE_HISTORY 获取匹配价格
    - 在事务中批量更新 x_bjb_dj=0 的行（写入 x_bjb_dj、x_bjb_bj、x_bj_dj）
    - 返回 AutoFillPriceResult（含价格映射和统计信息）
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 5.1, 5.2, 5.3, 5.4, 5.5, 7.1, 7.2, 7.3, 7.5_

  - [x] 3.2 实现 GetReferencePrice 端点
    - 在 `QuotationController.cs` 中新增 `GET /Quotation/GetReferencePrice` 方法
    - 参数：id (fabh), unitCode (控制柜编码)
    - 查询当前控制柜下元件的 x_wzdh，批量匹配 STD_PRICE_HISTORY
    - 返回与 GetCabinetComponents 行序一一对应的 ReferencePriceRow 数组（无匹配返回 null）
    - _Requirements: 6.2, 6.4, 6.5_

  - [x] 3.3 增强 SavePlan 端点：负价格校验与 x_dj 计算
    - 在 SavePlan 方法中添加负价格校验逻辑：遍历所有元件行，若 x_bj_dj < 0 则拒绝保存并返回负价格元件列表
    - 保存时对每行计算 x_dj = x_bj_dj * (1 + x_fdds / 100)，x_fdds 为 NULL 视为 0
    - 保存时对每行使用 NormalizeSpec 处理 x_ggxh 写入 x_wzdh
    - _Requirements: 10.1, 10.2, 10.4, 10.5, 12.6_

- [x] 4. Checkpoint - 后端逻辑验证
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. 前端：参考价格列与自动填充
  - [x] 5.1 增强 quotation-fill-price.js：自动填价核心逻辑
    - 实现 `autoFillPrice()` 函数：调用 POST /Quotation/AutoFillPriceFromHistory
    - 实现 `applyPriceToTable(prices)` 函数：遍历表格行，按 x_wzdh 匹配并填充空价格单元格（使用 setDataAtCell）
    - 同时填充空单位和空厂家列
    - 填充后自动重算金额列
    - 显示匹配统计信息："已匹配 M/N 个元件的历史报价，X 个元件无历史记录"
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9_

  - [x] 5.2 增强 quotation-fill-price.js：参考价格列加载与显示
    - 实现 `loadReferencePrice(unitCode)` 函数：调用 GET /Quotation/GetReferencePrice
    - 在 Handsontable 列配置中添加"参考价格"只读列（浅绿色背景，位于单价列之后）
    - 加载控制柜数据时同时请求参考价格并填充
    - 无匹配记录显示为空（不显示 0）
    - 加载中显示加载状态指示
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.7_

  - [x] 5.3 增强 quotation-fill-price.js：参考价格 tooltip
    - 实现 `showPriceTooltip(row, col)` 函数
    - 鼠标悬停参考价格单元格时显示 tooltip：均价、最低价、最高价、样本数
    - 无匹配记录时不显示 tooltip
    - _Requirements: 6.5, 6.6_

  - [x] 5.4 增强 quotation-fill-price.js：金额列自动计算
    - 在 Handsontable 列配置中添加"金额"只读列（浅蓝色背景，位于数量列之后）
    - 实现 `recalcAmount(row)` 函数：公式 x_bj_dj * (1 + x_fdds/100) * x_sl
    - 单价、浮动点数、数量任一变化时立即重算
    - null/空值视为 0，显示保留 2 位小数
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5_

- [x] 6. 前端：价格异常检测与交互
  - [x] 6.1 实现价格异常检测着色逻辑
    - 实现 `applyPriceAnomalyStyles()` 函数
    - 单价为 0 或空：浅灰色背景
    - 单价为负数：红色背景
    - 单价偏离历史均价超过 ±20%：橙色背景（公式 |x_bj_dj - avg_price| / avg_price > 0.2）
    - avg_price 为 0 或无历史记录时不检测偏离
    - 触发时机：数据加载后、单价变更后、自动填价完成后
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.6_

  - [x] 6.2 实现异常价格 tooltip
    - 鼠标悬停异常标记的单价单元格时显示 tooltip 说明异常原因
    - 负数："价格为负数，请修正"
    - 偏离："偏离历史均价 ¥X.XX 超过 20%"
    - _Requirements: 9.5_

  - [x] 6.3 实现保存前负价格客户端校验
    - 实现 `validateBeforeSave()` 函数
    - 检测到负数价格时阻止提交，信息栏显示错误提示并指明哪些元件价格为负
    - _Requirements: 10.3_

  - [x] 6.4 实现"引用历史报价"按钮交互状态管理
    - 工具栏添加"引用历史报价"按钮
    - 未加载数据时点击：信息栏提示"请先点击左侧控制柜节点加载元件数据"
    - 请求执行中：禁用按钮 + 信息栏显示"正在匹配历史报价..."
    - 请求完成/失败：恢复按钮 + 清除/显示相应提示
    - 30 秒超时处理
    - 支持重复点击（幂等）
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7_

- [x] 7. 前端：控制柜编辑与拖拽排序
  - [x] 7.1 实现控制柜和元件的增删改功能
    - 左侧目录树：新增控制柜（自动分配下一个 4 位编码）、删除控制柜（同时移除下属元件）
    - 表格：新增元件行（末尾追加空行）、删除选中元件行
    - 元件字段编辑：名称、规格型号、单位、数量、单价、浮动点数、厂商
    - 未保存状态标记显示
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5, 13.6_

  - [x] 7.2 实现控制柜拖拽排序
    - 左侧目录树支持鼠标拖拽排序
    - 拖拽完成后更新所有控制柜及下属元件的 x_bm 编号
    - 仅允许同级排序，不允许嵌套
    - 显示拖拽占位符和插入位置指示线
    - 标记"未保存"状态
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5_

  - [x] 7.3 实现根节点元件汇总视图
    - 点击根节点时显示所有元件汇总（只读）
    - 按 x_mc、x_ggxh、x_dj 分组，显示合计数量和金额小计
    - 底部显示总金额合计行
    - 切换到控制柜节点时恢复可编辑视图
    - _Requirements: 15.1, 15.2, 15.3, 15.4, 15.5, 15.6_

- [x] 8. 前端：权限控制 UI
  - [x] 8.1 实现前端权限控制
    - 非报价人且非管理员时隐藏"引用历史报价"按钮和保存按钮
    - 后端在 FillPrice 视图中传递当前用户权限信息（ViewBag 或 ViewModel）
    - _Requirements: 7.6_

- [x] 9. Checkpoint - 前端功能验证
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. 测试项目搭建与属性测试
  - [x] 10.1 创建测试项目并配置 FsCheck
    - 新建 `PanelFlow.Web.Tests` xUnit 测试项目
    - 添加 NuGet 依赖：xUnit, FsCheck.Xunit, Moq
    - 配置 InternalsVisibleTo 使测试项目可访问 internal 方法
    - 添加项目引用到 PanelFlow.Web 和 PanelFlow.Infrastructure
    - _Requirements: Testing Strategy_

  - [x] 10.2 Property 1: NormalizeSpec 输出字符集验证
    - **Property 1: NormalizeSpec output contains only allowed characters**
    - 使用 FsCheck 生成包含全角、括号、特殊符号、CJK 的随机字符串
    - 验证输出仅包含 a-z, 0-9, U+4E00-U+9FFF, μωΩ°±℃φ
    - 最少 100 次迭代
    - **Validates: Requirements 8.2, 8.4, 8.5**

  - [x] 10.3 Property 2: NormalizeSpec 括号移除验证
    - **Property 2: NormalizeSpec removes all bracket content**
    - 使用 FsCheck 生成含嵌套/孤立括号的字符串
    - 验证输出不包含括号内的字符
    - 验证孤立左/右括号被丢弃
    - **Validates: Requirements 8.3**

  - [x] 10.4 Property 4: 非零价格保留验证
    - **Property 4: Auto-fill preserves non-zero prices**
    - 生成含不同价格状态的元件集合
    - 验证 x_bj_dj > 0 的行在自动填价后不被修改
    - **Validates: Requirements 3.5, 5.2**

  - [x] 10.5 Property 5: 空价格填充验证
    - **Property 5: Auto-fill fills empty-price elements with matching history**
    - 生成元件集合 + 历史价格映射
    - 验证 x_bj_dj=0 且有匹配历史的行被正确填充 last_price
    - 验证空单位和空厂家也被填充
    - **Validates: Requirements 3.2, 3.3, 3.4, 5.1**

  - [x] 10.6 Property 6: 幂等性验证
    - **Property 6: Auto-fill is idempotent**
    - 生成元件集合，执行两次填充
    - 验证第二次执行后数据库状态与第一次相同
    - 验证第二次报告 0 updated rows
    - **Validates: Requirements 4.7**

  - [x] 10.7 Property 7: 金额公式正确性验证
    - **Property 7: Amount formula correctness**
    - 使用 FsCheck 生成随机 decimal 三元组 (price, fdds, qty)
    - 验证 amount = x_bj_dj * (1 + x_fdds/100) * x_sl
    - 验证 x_dj = x_bj_dj * (1 + x_fdds/100)
    - null 视为 0
    - **Validates: Requirements 3.9, 10.4, 10.5, 11.2, 11.4**

  - [x] 10.8 Property 8: 负价格拒绝验证
    - **Property 8: Negative price validation rejects save**
    - 生成含负数价格的元件集合
    - 验证保存操作被拒绝且响应列出所有负价格元件
    - **Validates: Requirements 10.1, 10.2**

  - [x] 10.9 Property 9: 统计准确性验证
    - **Property 9: Fill statistics accuracy**
    - 生成不同匹配状态的元件集合
    - 验证 M + X = N, U ≤ M, (M - U) = 已有非零价格的匹配元件数
    - **Validates: Requirements 3.6, 5.4**

  - [x] 10.10 Property 10: 价格偏离检测验证
    - **Property 10: Price deviation detection**
    - 生成 (price, avg_price) 对
    - 验证偏离标记当且仅当 |x_bj_dj - avg_price| / avg_price > 0.2
    - avg_price 为 0 或无历史时不检测
    - **Validates: Requirements 9.3, 9.4**

  - [x] 10.11 Property 11: 汇总分组正确性验证
    - **Property 11: Summary grouping correctness**
    - 生成含重复名称/规格的元件集合
    - 验证每组合计数量 = SUM(x_sl)，总金额 = SUM(x_dj * group_qty)
    - **Validates: Requirements 15.2, 15.4**

  - [x] 10.12 Property 12: 权限拒绝验证
    - **Property 12: Authorization enforcement**
    - 生成不同角色/状态组合
    - 验证非报价人且非管理员被拒绝，dqzt=10 被拒绝
    - **Validates: Requirements 7.3, 7.5**

- [x] 11. Final checkpoint - 全功能验证
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties using FsCheck (C#) with minimum 100 iterations
- NormalizeSpec 已存在于 QuotationController 中（internal static），无需重新实现
- AutoFillPriceFromHistory 端点已存在但仅做查询，需重构为"后端更新 + 前端同步"模式
- BjbItem 实体需扩展字段以支持完整的价格更新逻辑
- 前端 quotation-fill-price.js 已存在，需在现有基础上增强
- Property 3 (C#/SQL 等价性) 需要真实 SQL Server 连接，建议作为集成测试单独执行，不纳入 CI 属性测试

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3", "2.1"] },
    { "id": 1, "tasks": ["3.1", "3.2", "3.3"] },
    { "id": 2, "tasks": ["5.1", "5.2", "5.4", "6.4", "8.1"] },
    { "id": 3, "tasks": ["5.3", "6.1", "6.3", "7.1"] },
    { "id": 4, "tasks": ["6.2", "7.2", "7.3"] },
    { "id": 5, "tasks": ["10.1"] },
    { "id": 6, "tasks": ["10.2", "10.3", "10.7", "10.10", "10.11"] },
    { "id": 7, "tasks": ["10.4", "10.5", "10.6", "10.8", "10.9", "10.12"] }
  ]
}
```
