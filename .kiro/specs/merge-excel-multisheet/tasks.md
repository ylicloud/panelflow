# Implementation Plan: merge-excel-multisheet

## Overview

在现有 `/Quotation/MergeExcel` 页面上新增"单文件多 Sheet"合并模式。后端新增 `GetSheetCount` 和 `MergeExcelMultiSheet` 两个 Action，前端在合并按钮点击时根据文件数量和 Sheet 数量自动路由到对应接口。实现顺序遵循 Model → Controller → View → JS 的分层原则。

## Tasks

- [x] 1. 后端实现 GetSheetCount 辅助接口
  - [x] 1.1 在 `QuotationController` 中新增 `GetSheetCount` Action
    - 添加 `[HttpPost]`、`[ValidateAntiForgeryToken]` 特性
    - 接收 `IFormFile? file` 参数
    - 验证文件扩展名为 `.xls` 或 `.xlsx`，否则返回 `{ success: false, message: "仅支持 .xls / .xlsx 文件" }`
    - 验证文件非 null 且长度 > 0，否则返回 `{ success: false, message: "文件无法读取" }`
    - 使用 NPOI `WorkbookFactory.Create()` 打开文件，读取 `NumberOfSheets` 和各 Sheet 名称
    - 返回 `{ success: true, sheetCount: N, sheetNames: [...] }`
    - 异常捕获后通过 `_logger.LogError` 记录，返回安全错误信息
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 8.1, 8.2, 8.3, 8.6_

  - [ ]* 1.2 Write property test for GetSheetCount (Property 7: SheetCount API 准确性)
    - **Property 7: SheetCount API 准确性**
    - **Validates: Requirements 3.2**
    - 使用 FsCheck 生成包含 1–10 个 Sheet 的内存工作簿
    - 验证返回的 `sheetCount` 等于实际 Sheet 数，`sheetNames` 顺序与工作簿索引一致
    - 测试文件路径：`PanelFlow.Web.Tests/Properties/MultiSheetMergeProperties.cs`

- [x] 2. 后端实现 MergeExcelMultiSheet 核心解析接口
  - [x] 2.1 在 `QuotationController` 中新增 `MergeExcelMultiSheet` Action 框架
    - 添加 `[HttpPost]`、`[ValidateAntiForgeryToken]` 特性
    - 接收 `IFormFile? file` 和 `[FromForm] int startSeqNo = 0` 参数
    - 实现文件扩展名验证和空文件验证（提前返回错误）
    - 使用 NPOI 打开工作簿，按 Sheet 索引升序遍历
    - 异常捕获后通过 `_logger.LogError` 记录，返回安全错误信息
    - _Requirements: 2.1, 2.9, 2.10, 8.1, 8.2, 8.3, 8.6_

  - [x] 2.2 实现表头列映射与 Sheet 有效性检测逻辑
    - 扫描每个 Sheet 第 1 行，建立列索引映射：`名称`→colName、`型号规格`/`规格型号`/`规格`/`型号`→colSpec、`数量`→colQty、`厂商`/`生产厂家`→colVendor、`备注`→colRemark
    - 若任意必需列未找到，标记为 IgnoredSheet，记录原始名称到 `ignoredSheetNames`
    - _Requirements: 2.3_

  - [x] 2.3 实现单元号唯一性生成算法
    - 第一个出现的 Sheet 使用原始名称作为单元号
    - 同名 Sheet（大小写不敏感比较）追加 `_2`、`_3` … 后缀
    - 候选单元号不得与已使用单元号（大小写敏感）或任意原始 Sheet 名称（大小写不敏感）冲突
    - 维护 `usedUnitCodes` (HashSet, Ordinal) 和 `allSheetNames` 列表
    - _Requirements: 4.1, 4.2, 4.3_

  - [x] 2.4 实现数据行解析与合并行生成逻辑
    - 对每个有效 Sheet：插入 UnitHeaderRow（序号 + 单元号 + 6 个空字符串）
    - 从第 2 行开始逐行读取，跳过 colName/colSpec/colQty 三列同时为空的行
    - 有效行追加 ComponentRow：序号、空单元号、colName、colSpec、"0.0"、colQty、colVendor、"0.0"
    - 连续 5 行三列同时为空时停止当前 Sheet
    - 总行数达到 5000 时设 `reachedLimit=true` 并停止所有后续处理
    - _Requirements: 2.4, 2.5, 2.6, 2.7_

  - [x] 2.5 实现响应体构建与统计字段
    - 构建 JSON 响应：`success`、`rows`、`rowCount`、`lastSeqNo`、`reachedLimit`、`totalSheets`、`importedSheets`、`ignoredSheets`、`ignoredSheetNames`
    - `importedSheets` 仅统计截断前已完整处理的 Sheet 数
    - _Requirements: 2.8, 2.7_

  - [ ]* 2.6 Write property test for Sheet 计数守恒 (Property 1)
    - **Property 1: Sheet 计数守恒**
    - **Validates: Requirements 2.8**
    - 生成随机多 Sheet 工作簿 → 调用解析逻辑 → 验证 `importedSheets + ignoredSheets == totalSheets`
    - 测试文件路径：`PanelFlow.Web.Tests/Properties/MultiSheetMergeProperties.cs`

  - [ ]* 2.7 Write property test for 单元号唯一性 (Property 2)
    - **Property 2: 单元号唯一性**
    - **Validates: Requirements 4.1, 4.2**
    - 生成含重复名称的工作簿 → 验证所有 UnitHeaderRow 的单元号互不相同（大小写敏感）
    - 测试文件路径：`PanelFlow.Web.Tests/Properties/MultiSheetMergeProperties.cs`

  - [ ]* 2.8 Write property test for 行过滤正确性 (Property 4)
    - **Property 4: 行过滤正确性**
    - **Validates: Requirements 2.5, 2.6**
    - 生成含空行模式的 Sheet → 验证三列同时为空的行被排除，连续 5 空行后截断
    - 测试文件路径：`PanelFlow.Web.Tests/Properties/MultiSheetMergeProperties.cs`

  - [ ]* 2.9 Write property test for 行上限截断 (Property 5)
    - **Property 5: 行上限截断**
    - **Validates: Requirements 2.7**
    - 生成大量数据行的工作簿 → 验证 `rows.Count <= 5000`、`reachedLimit == true`
    - 测试文件路径：`PanelFlow.Web.Tests/Properties/MultiSheetMergeProperties.cs`

  - [ ]* 2.10 Write property test for 列检测决定 Sheet 有效性 (Property 6)
    - **Property 6: 列检测决定 Sheet 有效性**
    - **Validates: Requirements 2.3**
    - 生成随机表头（随机包含/缺少必需列）→ 验证 Valid/Ignored 分类正确
    - 测试文件路径：`PanelFlow.Web.Tests/Properties/MultiSheetMergeProperties.cs`

  - [ ]* 2.11 Write property test for 行数单调递增 (Property 8)
    - **Property 8: 行数单调递增**
    - **Validates: Requirements 2.4, 2.5**
    - 生成含有效 Sheet 的工作簿 → 验证每个有效 Sheet 至少贡献 1 行（UnitHeaderRow）
    - 测试文件路径：`PanelFlow.Web.Tests/Properties/MultiSheetMergeProperties.cs`

- [x] 3. Checkpoint - 后端接口验证
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. 前端实现多 Sheet 合并路由与请求逻辑
  - [x] 4.1 在 `MergeExcel.cshtml` 中添加新接口 URL 的 data 属性
    - 在 `#hot-container` 上添加 `data-sheet-count-url="@sheetCountUrl"` 和 `data-multi-sheet-merge-url="@multiSheetMergeUrl"`
    - 在 Razor 顶部声明对应的 `Url.Action` 变量
    - _Requirements: 5.1_

  - [x] 4.2 在 `quotation-merge.js` 中实现 Sheet 数量检测与路由分支
    - 在 `mergeExcelBtn` click handler 中，当 `selectedFiles.length === 1` 时：
      - 调用 `POST sheetCountUrl`（携带 AntiForgeryToken，超时 10 秒）
      - 若 `sheetCount >= 2` → 调用 `handleMultiSheetMerge(file)`
      - 若 `sheetCount === 1` → 走现有 SingleFileApi 逻辑
      - 若请求超时/网络异常/`success: false` → 信息栏显示"无法读取文件 Sheet 信息，请重试"，终止合并
    - 当 `selectedFiles.length >= 2` 时保持现有多文件模式不变
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_

  - [x] 4.3 在 `quotation-merge.js` 中实现 `handleMultiSheetMerge(file)` 函数
    - 以 `multipart/form-data` 格式携带 AntiForgeryToken 和 `startSeqNo=0` 提交到 MultiSheetApi
    - 成功且 `rows` 非空：调用 `hot.loadData(rows)` + `buildTreeFromGrid()`，设 `hasMergedData=true`、`hasExported=false`，启用导出按钮
    - 成功但 `rows` 为空：信息栏显示"所有 Sheet 均缺少必需列，无有效数据导入"
    - `success: false`：信息栏显示后端 `message`
    - HTTP 错误/网络异常：信息栏显示"合并请求失败，请检查网络或稍后重试"
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6_

  - [x] 4.4 在 `quotation-merge.js` 中实现 `formatMultiSheetMessage(response)` 信息栏格式化
    - 标准文本：`读取了 1 个文件，{totalSheets} 个 sheet 页，共 {importedSheets} 个控制柜/操作箱`
    - 若 `ignoredSheets > 0`：追加 `（忽略 {K} 个 sheet：{名称列表以顿号拼接}）`
    - 若 `reachedLimit === true`：追加 `（已达到 5000 行上限，后续 sheet 未完整读取）`
    - _Requirements: 6.1, 6.2, 6.3_

  - [ ]* 4.5 Write property test for 信息栏消息格式正确性 (Property 9)
    - **Property 9: 信息栏消息格式正确性**
    - **Validates: Requirements 6.1, 6.2, 6.3**
    - 使用 FsCheck 生成随机响应数据（totalSheets, importedSheets, ignoredSheets, ignoredSheetNames, reachedLimit）
    - 验证格式化后的消息字符串包含正确的模式
    - 注意：此属性测试在后端 C# 中实现（验证格式化逻辑的纯函数），测试文件路径：`PanelFlow.Web.Tests/Properties/MultiSheetMergeProperties.cs`

- [x] 5. Checkpoint - 前端集成验证
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. 兼容性与多文件模式保护
  - [x] 6.1 验证多文件模式下逐文件处理的错误中断逻辑
    - 确认当某文件调用 SingleFileApi 返回失败时，立即停止后续文件处理
    - 信息栏显示"文件 {文件名} 读取失败：{错误原因}"
    - 确认 `selectedFiles` 保持不变
    - _Requirements: 1.6_

  - [x] 6.2 确认目录树、导出、清空、离开提示逻辑在多 Sheet 模式下正常工作
    - 验证 `buildTreeFromGrid()` 复用现有逻辑正确构建节点
    - 验证导出按钮调用现有 `ExportMergedExcel` 接口
    - 验证清空按钮重置所有状态
    - 验证离开提示在 `hasMergedData && !hasExported` 时触发
    - 验证目录树节点点击定位到对应行
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6_

  - [ ]* 6.3 Write unit tests for 后端接口权限验证
    - 测试未登录访问 GetSheetCount → 返回 401
    - 测试无权限角色访问 MergeExcelMultiSheet → 返回 403
    - 测试缺少 AntiForgeryToken → 返回 400
    - 测试文件路径：`PanelFlow.Web.Tests/Integration/MergeExcelMultiSheetTests.cs`
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5_

- [x] 7. Final checkpoint - 全部测试通过
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- 本功能不涉及数据库操作，所有数据在内存中处理
- 后端测试使用 FsCheck.Xunit 3.3.3（已在项目中配置）
- 前端无自动化测试框架，信息栏格式化的属性测试在后端 C# 中以纯函数形式验证

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "4.1"] },
    { "id": 1, "tasks": ["1.2", "2.1"] },
    { "id": 2, "tasks": ["2.2", "2.3"] },
    { "id": 3, "tasks": ["2.4"] },
    { "id": 4, "tasks": ["2.5"] },
    { "id": 5, "tasks": ["2.6", "2.7", "2.8", "2.9", "2.10", "2.11"] },
    { "id": 6, "tasks": ["4.2"] },
    { "id": 7, "tasks": ["4.3"] },
    { "id": 8, "tasks": ["4.4", "4.5"] },
    { "id": 9, "tasks": ["6.1", "6.2", "6.3"] }
  ]
}
```
