# Requirements Document

## Introduction

本需求文档描述对 `/Quotation/MergeExcel` 页面的功能扩展——新增"单文件多 Sheet"合并模式。

现有功能支持"多文件合并"模式：用户选择多个 Excel 文件，每个文件代表一个控制柜/操作箱的元件明细表，合并后在同一张 Handsontable 表格中呈现所有文件内容。

本次新增"单文件多 Sheet"模式：用户只上传 1 个包含多个 Sheet 页的 Excel 文件，每个 Sheet 页对应一个控制柜/操作箱的元件明细表，系统以与多文件模式等价的方式完成解析与合并，最终在同一张 Handsontable 表格中显示所有 Sheet 的合并结果。

两种模式在同一页面共存；前端在用户点击"合并"时根据文件数量与 Sheet 数量自动判断并调用对应接口，用户无需手动切换。

---

## Glossary

| 术语 | 定义 |
|------|------|
| **合并页面（MergePage）** | `/Quotation/MergeExcel` 页面及其前端 JS 逻辑（`quotation-merge.js`） |
| **多文件模式（MultiFileMode）** | 现有逻辑，用户选择 ≥2 个 Excel 文件，每文件只读第一个 Sheet |
| **单文件多 Sheet 模式（MultiSheetMode）** | 新增逻辑，用户选择 1 个含 ≥2 个 Sheet 的 Excel 文件，逐一读取每个 Sheet |
| **单文件单 Sheet 模式（SingleSheetMode）** | 现有逻辑，用户选择 1 个只有 1 个 Sheet 的 Excel 文件，按多文件模式逻辑处理 |
| **单元号（UnitCode）** | 合并表格中标识控制柜/操作箱的字符串；多文件模式下取文件名（去扩展名），多 Sheet 模式下取 Sheet 名称 |
| **单元首行（UnitHeaderRow）** | 合并表格中每个控制柜块的第一行，序号 + 单元号，其余列为空字符串 |
| **元件行（ComponentRow）** | 单元首行之后的各数据行，单元号列为空字符串 |
| **必需列（RequiredColumns）** | Excel 表头中必须同时存在的 5 列：`名称`、`型号规格`(或`规格型号`或`规格`或`型号`)、`数量`、`厂商`（或 `生产厂家`）、`备注` |
| **有效 Sheet（ValidSheet）** | 包含全部必需列、能产生合并行数据的 Sheet |
| **被忽略 Sheet（IgnoredSheet）** | 在解析阶段因缺少任意必需列而被排除、不产生任何合并行数据的 Sheet |
| **信息栏（InfoBar）** | 页面顶部 `#page-info-bar` 元素，用于显示操作状态与统计信息 |
| **目录树（DirectoryTree）** | 页面左侧 `#tree-children-container` 按单元号构建的树形节点列表 |
| **后端解析接口（MultiSheetApi）** | 新增 POST 接口 `/Quotation/MergeExcelMultiSheet`，接收单个多 Sheet 文件并返回合并结果 |
| **辅助接口（SheetCountApi）** | 新增 POST 接口 `/Quotation/GetSheetCount`，轻量读取文件 Sheet 数量和名称列表 |
| **现有单文件接口（SingleFileApi）** | 现有 POST 接口 `/Quotation/MergeExcelFile`，接收单个文件并返回单 Sheet 合并结果 |
| **行上限（RowLimit）** | 单次合并操作中所有 Sheet 合并后总行数的上限，值为 5000 |
| **NPOI** | 项目统一使用的 Excel 解析库 |
| **Handsontable** | 前端电子表格组件，licenseKey: `53ea8-c2678-49b80-cb40f-4dad4` |

---

## Requirements

---

### 需求 1：检测上传文件的 Sheet 数量并选择处理模式

**用户故事：** 作为报价员，我希望系统能自动识别我上传的 Excel 文件是否包含多个 Sheet，以便无需手动切换模式即可完成合并操作。

#### 验收标准

1. WHEN 用户点击"合并"按钮且 `selectedFiles` 数量等于 1，THE 合并页面（MergePage）SHALL 在向后端发起合并请求前，先通过辅助接口（SheetCountApi）`POST /Quotation/GetSheetCount` 获取该文件的 Sheet 数量，请求超时时限为 10 秒。

2. IF 辅助接口（SheetCountApi）返回的 `sheetCount` 等于 1，THEN THE 合并页面（MergePage）SHALL 按单文件单 Sheet 模式（SingleSheetMode）调用现有单文件接口（SingleFileApi）处理该文件，不触发多 Sheet 逻辑。

3. IF 辅助接口（SheetCountApi）返回的 `sheetCount` 大于等于 2，THEN THE 合并页面（MergePage）SHALL 进入单文件多 Sheet 模式（MultiSheetMode），调用后端解析接口（MultiSheetApi）处理该文件。

4. IF 用户选择的文件数量大于等于 2，THEN THE 合并页面（MergePage）SHALL 直接使用多文件模式（MultiFileMode），逐一调用现有单文件接口（SingleFileApi），不执行 Sheet 数量检测，每个文件只读取第一个 Sheet。

5. IF 辅助接口（SheetCountApi）的请求超时（超过 10 秒）、网络异常或返回 `success: false`，THEN THE 合并页面（MergePage）SHALL 在信息栏（InfoBar）以错误样式显示"无法读取文件 Sheet 信息，请重试"，终止当次合并操作，且 `selectedFiles` 保持不变。

6. WHEN 多文件模式（MultiFileMode）逐一处理文件时，IF 某个文件调用现有单文件接口（SingleFileApi）返回失败（非网络异常），THEN THE 合并页面（MergePage）SHALL 立即停止继续处理后续文件，在信息栏（InfoBar）显示"文件 {文件名} 读取失败：{错误原因}"，并终止当次合并操作。

---

### 需求 2：后端新增多 Sheet 解析接口

**用户故事：** 作为报价员，我希望系统能将单个多 Sheet Excel 文件中所有有效 Sheet 的元件数据合并到一张表格，以便与多文件模式得到一致的报价汇总结果。

#### 验收标准

1. THE 后端解析接口（MultiSheetApi）SHALL 接受 `POST /Quotation/MergeExcelMultiSheet`，请求体为 `multipart/form-data`，包含：`file`（IFormFile，`.xls` 或 `.xlsx`）和 `startSeqNo`（int，全局起始序号，默认 0）。

2. WHEN 后端解析接口（MultiSheetApi）接收到文件，THE 后端解析接口（MultiSheetApi）SHALL 使用 NPOI 按 Sheet 在工作簿中的索引顺序（从 0 开始升序）依次遍历每一个 Sheet。

3. WHEN 解析某个 Sheet 的表头行（第 1 行），THE 后端解析接口（MultiSheetApi）SHALL 扫描该行所有单元格，建立以下列的列索引映射：`名称`→colName、`型号规格`→colSpec、`数量`→colQty、`厂商` 或 `生产厂家`→colVendor、`备注`→colRemark；IF 上述任意一列未找到，THEN THE 后端解析接口（MultiSheetApi）SHALL 将该 Sheet 标记为被忽略 Sheet（IgnoredSheet），不产生任何合并行，继续处理下一个 Sheet。

4. WHEN 解析某个有效 Sheet（ValidSheet），THE 后端解析接口（MultiSheetApi）SHALL 将当前全局序号加 1，并在合并行列表中插入一行单元首行（UnitHeaderRow）：第 1 列（序号）填入当前全局序号字符串，第 2 列（单元号）填入该 Sheet 的单元号（见需求 4），第 3–8 列均为空字符串。

5. WHEN 解析某个有效 Sheet 的数据行（从第 2 行开始逐行读取），THE 后端解析接口（MultiSheetApi）SHALL 跳过 colName、colSpec、colQty 三列同时为空的行；对每条有效行，将当前全局序号加 1，追加一行元件行（ComponentRow）：第 1 列填入当前全局序号字符串，第 2 列（单元号）置为空字符串，第 3 列填入 colName 值，第 4 列填入 colSpec 值，第 5 列（单价）置为 `"0.0"`，第 6 列填入 colQty 值，第 7 列填入 colVendor 值，第 8 列（总价）置为 `"0.0"`。

6. WHEN 解析某个有效 Sheet 的数据行时，IF 遇到连续 5 行 colName、colSpec、colQty 三列同时为空，THEN THE 后端解析接口（MultiSheetApi）SHALL 停止读取当前 Sheet 的后续行，继续处理下一个 Sheet。

7. WHEN 合并行列表的总行数达到行上限（RowLimit）5000，THE 后端解析接口（MultiSheetApi）SHALL 停止处理所有后续 Sheet，并在响应中将 `reachedLimit` 字段置为 `true`；`importedSheets` 仅统计在截断点之前已完整处理完所有数据行的 Sheet 数。

8. WHEN 后端解析接口（MultiSheetApi）处理完成，THE 后端解析接口（MultiSheetApi）SHALL 在 JSON 响应体中返回以下字段：`success`（bool）、`rows`（二维字符串数组，每行 8 列）、`rowCount`（int，合并行总数）、`lastSeqNo`（int，处理完成时的全局序号值）、`reachedLimit`（bool）、`totalSheets`（int，文件总 Sheet 数）、`importedSheets`（int，成功完整处理的有效 Sheet 数）、`ignoredSheets`（int，被忽略 Sheet 数）、`ignoredSheetNames`（string[]，被忽略 Sheet 的原始名称列表）。

9. IF 上传文件扩展名不是 `.xls` 或 `.xlsx`，THEN THE 后端解析接口（MultiSheetApi）SHALL 返回 `{ "success": false, "message": "仅支持 .xls / .xlsx 文件" }`，不执行任何解析操作。

10. IF 上传文件大小为零或 NPOI 无法读取任何 Sheet，THEN THE 后端解析接口（MultiSheetApi）SHALL 返回 `{ "success": false, "message": "文件无法读取或不包含任何 Sheet" }`。

---

### 需求 3：新增获取 Sheet 数量的辅助接口

**用户故事：** 作为报价员，我希望系统在合并前能轻量级地判断文件的 Sheet 数量，以便在不重复提交完整解析请求的情况下决定走哪条处理路径。

#### 验收标准

1. THE 辅助接口（SheetCountApi）SHALL 接受 `POST /Quotation/GetSheetCount`，请求体为 `multipart/form-data`，包含 `file`（IFormFile）参数。

2. WHEN 辅助接口（SheetCountApi）成功读取文件的工作簿结构，THE 辅助接口（SheetCountApi）SHALL 返回 `{ "success": true, "sheetCount": N, "sheetNames": ["Sheet1", "Sheet2", ...] }`，其中 N 为该文件实际包含的 Sheet 总数，`sheetNames` 按工作簿中 Sheet 索引顺序排列。

3. IF 辅助接口（SheetCountApi）接收到的文件扩展名不是 `.xls` 或 `.xlsx`，THEN THE 辅助接口（SheetCountApi）SHALL 返回 `{ "success": false, "message": "仅支持 .xls / .xlsx 文件" }`。

4. IF 辅助接口（SheetCountApi）接收到的文件大小为零或 NPOI 无法打开该文件，THEN THE 辅助接口（SheetCountApi）SHALL 返回 `{ "success": false, "message": "文件无法读取" }`。

---

### 需求 4：多 Sheet 模式下的单元号唯一性规则

**用户故事：** 作为报价员，我希望多 Sheet 模式下每个 Sheet 以其名称作为单元号，且当存在同名 Sheet 时系统自动处理冲突，以便目录树中每个节点都有唯一可识别的标识。

#### 验收标准

1. WHEN 多 Sheet 模式（MultiSheetMode）解析某个有效 Sheet（ValidSheet），THE 后端解析接口（MultiSheetApi）SHALL 直接使用该 Sheet 的名称作为单元号（UnitCode），不拼接文件名前缀。

2. IF 同一文件中存在两个或多个名称相同（大小写不敏感比较）的 Sheet，THEN THE 后端解析接口（MultiSheetApi）SHALL 按工作簿 Sheet 索引升序处理，第一个出现的 Sheet 使用原始名称作为单元号，后续同名 Sheet 依次追加序号后缀 `_2`、`_3` …（N 从 2 开始递增）；若生成的候选单元号与任意已存在的单元号（大小写敏感）或任意原始 Sheet 名称（大小写不敏感）冲突，则继续递增 N 直到不冲突为止。

3. WHEN 后端解析接口（MultiSheetApi）解析完成，THE 后端解析接口（MultiSheetApi）SHALL 在响应体的 `ignoredSheetNames` 字段中返回所有被忽略 Sheet（IgnoredSheet）的原始名称，不含任何重名后缀。

---

### 需求 5：前端触发多 Sheet 合并并展示结果

**用户故事：** 作为报价员，我希望点击"合并"按钮后，无论是多文件还是单文件多 Sheet，都能看到同样结构的合并结果表格和目录树，以便后续统一编辑和导出。

#### 验收标准

1. WHEN 用户点击"合并"按钮且系统判断需进入单文件多 Sheet 模式（MultiSheetMode），THE 合并页面（MergePage）SHALL 将 `selectedFiles[0]` 以 `multipart/form-data` 格式携带防伪令牌（AntiForgeryToken）和 `startSeqNo=0` 提交到后端解析接口（MultiSheetApi）。

2. WHEN 后端解析接口（MultiSheetApi）返回 `success: true` 且 `rows` 数组非空，THE 合并页面（MergePage）SHALL 调用 `hot.loadData(rows)` 替换表格中全部已有数据，并调用 `buildTreeFromGrid()` 重建目录树。

3. IF 后端解析接口（MultiSheetApi）返回 `success: true` 且 `rows` 为空数组，THEN THE 合并页面（MergePage）SHALL 在信息栏（InfoBar）以错误样式显示"所有 Sheet 均缺少必需列，无有效数据导入"，不更新 Handsontable 表格数据。

4. WHEN 多 Sheet 模式（MultiSheetMode）合并完成且 `rows` 非空，THE 合并页面（MergePage）SHALL 将 `hasMergedData` 置为 `true`、将 `hasExported` 置为 `false`，并启用"导出 Excel"按钮。

5. IF 后端解析接口（MultiSheetApi）返回 `success: false`，THEN THE 合并页面（MergePage）SHALL 在信息栏（InfoBar）以错误样式显示后端返回的 `message` 字段内容。

6. IF 多 Sheet 模式（MultiSheetMode）下提交到后端解析接口（MultiSheetApi）的请求发生 HTTP 错误（非 2xx 响应）或网络异常，THEN THE 合并页面（MergePage）SHALL 在信息栏（InfoBar）以错误样式显示"合并请求失败，请检查网络或稍后重试"，不更新 Handsontable 表格数据，且 `hasMergedData` 保持原值不变。

---

### 需求 6：多 Sheet 模式下信息栏显示规则

**用户故事：** 作为报价员，我希望合并完成后信息栏能准确告知我读取了多少个 Sheet、成功导入了多少个单元，以及是否有 Sheet 因缺少必需列而被忽略，以便核实导入结果是否符合预期。

#### 验收标准

1. WHEN 单文件多 Sheet 模式（MultiSheetMode）合并完成，THE 信息栏（InfoBar）SHALL 显示标准信息文本：`读取了 1 个文件，{X} 个 sheet 页，共 {Y} 个控制柜/操作箱`，其中 X 等于 `totalSheets`，Y 等于 `importedSheets`。

2. WHEN 单文件多 Sheet 模式（MultiSheetMode）合并完成，IF `ignoredSheets` 大于 0，THEN THE 信息栏（InfoBar）SHALL 在标准信息文本之后追加：`（忽略 {K} 个 sheet：{名称列表}）`，其中 K 等于 `ignoredSheets`，名称列表为 `ignoredSheetNames` 中各名称以顿号（`、`）拼接的字符串。

3. WHEN 单文件多 Sheet 模式（MultiSheetMode）合并完成，IF `reachedLimit` 为 `true`，THEN THE 信息栏（InfoBar）SHALL 在标准信息文本及忽略 sheet 提示（若有）之后追加：`（已达到 5000 行上限，后续 sheet 未完整读取）`。

4. WHEN 多文件模式（MultiFileMode）或单文件单 Sheet 模式（SingleSheetMode）合并完成，THE 信息栏（InfoBar）SHALL 按现有格式显示：`选择 {N} 个文件，导入 {M} 个文件，忽略 {K} 个文件`，本需求不影响该格式。

---

### 需求 7：多 Sheet 模式与现有功能的兼容性

**用户故事：** 作为报价员，我希望多 Sheet 模式的引入不影响现有多文件合并、导出、清空、离开提示等功能，以便日常工作流程不被打断。

#### 验收标准

1. THE 合并页面（MergePage）SHALL 保持"选择 Excel 文件"按钮、文件选择器（`accept=".xls,.xlsx" multiple`）、"合并"按钮、"导出 Excel"按钮、"清空数据"按钮的现有交互逻辑不变。

2. WHEN 多 Sheet 模式（MultiSheetMode）合并完成后，THE 目录树（DirectoryTree）SHALL 按与多文件模式相同的规则构建节点：遍历 Handsontable 表格所有行，将第 2 列（单元号）非空的行作为单元节点；若同一单元号出现多次则标记"重复"徽章；IF 某单元号对应范围内存在第 4 列（规格）为空或第 6 列（数量）非正数的元件行，THEN 该节点标记"错误"徽章。

3. WHEN 多 Sheet 模式（MultiSheetMode）合并完成后用户点击"导出 Excel"，THE 合并页面（MergePage）SHALL 调用现有 `POST /Quotation/ExportMergedExcel` 接口导出合并表格，并在导出成功后将 `hasExported` 置为 `true`，导出逻辑不变。

4. WHEN 多 Sheet 模式（MultiSheetMode）合并后用户点击"清空数据"，THE 合并页面（MergePage）SHALL 清除表格数据、将 `selectedFiles` 重置为空数组、将 `hasMergedData` 重置为 `false`、重置目录树显示"暂无文件"、禁用"导出 Excel"按钮，行为与多文件模式一致。

5. WHEN 用户在多 Sheet 模式（MultiSheetMode）合并后尝试离开页面，IF `hasMergedData === true` 且 `hasExported === false`，THEN THE 合并页面（MergePage）SHALL 触发现有离开提示逻辑，提示用户"请及时导出保存合并后excel文件。确认离开吗？"。

6. WHEN 多 Sheet 模式（MultiSheetMode）合并完成后用户点击目录树节点，THE 合并页面（MergePage）SHALL 在 Handsontable 表格中搜索第 2 列与该节点单元号精确匹配的第一行，选中该行并将视口滚动至该行，同时在信息栏（InfoBar）显示"已定位到单元号：{单元号}（第 N 行）"。

---

### 需求 8：后端接口权限与安全

**用户故事：** 作为系统管理员，我希望新增的后端接口与现有接口遵循相同的权限和安全规范，以便不引入额外的安全风险。

#### 验收标准

1. WHEN 后端解析接口（MultiSheetApi）或辅助接口（SheetCountApi）收到请求时，THE 后端 SHALL 验证请求携带有效的防伪令牌（AntiForgeryToken）。

2. IF 请求未携带有效防伪令牌，THEN THE 后端 SHALL 拒绝该请求并返回 HTTP 400。

3. WHEN 后端解析接口（MultiSheetApi）或辅助接口（SheetCountApi）收到请求时，THE 后端 SHALL 检查当前会话用户是否具有管理员、报价员或生产管理人员角色之一。

4. IF 当前会话中不存在有效登录用户（未认证），THEN THE 后端 SHALL 返回 HTTP 401。

5. IF 当前登录用户角色不在允许的角色列表（管理员、报价员、生产管理人员）中，THEN THE 后端 SHALL 返回 HTTP 403。

6. IF 上传文件在服务器端解析过程中抛出任意异常，THEN THE 后端 SHALL 捕获该异常，通过 `ILogger` 记录完整的异常信息，并向客户端返回包含 `"success": false` 和人类可读错误摘要的 JSON 响应，不得将原始异常消息或堆栈信息暴露给客户端。

---

## 正确性属性

以下属性用于指导后续测试编写，涵盖核心业务逻辑的可验证性。

### 属性 1：多 Sheet 往返一致性（Round-Trip）

对任意合法多 Sheet Excel 文件（每个 Sheet 均包含必需列，Sheet 数量 ≥2）：
- 调用 `POST /Quotation/MergeExcelMultiSheet` 获得合并行列表 R1
- 将 R1 调用 `POST /Quotation/ExportMergedExcel` 导出为 xlsx 文件
- 将该 xlsx 文件拆分为按单元号分组的多个虚拟文件（每个单元号一个），逐一调用 `POST /Quotation/MergeExcelFile` 重新导入，得到合并行列表 R2

则 R1 与 R2 中每行每列的内容应等价（忽略首尾空格差异），即多 Sheet 导入与等价多文件导入产生相同结果。

### 属性 2：Sheet 计数守恒

对任意多 Sheet 文件，后端解析接口（MultiSheetApi）返回的值必须满足：

`importedSheets + ignoredSheets = totalSheets`

即成功导入的 Sheet 数与被忽略的 Sheet 数之和恒等于文件总 Sheet 数，任何 Sheet 不得既不被导入也不被忽略。

### 属性 3：单元号唯一性

对任意多 Sheet 文件，多 Sheet 模式（MultiSheetMode）解析完成后，合并行列表中所有单元首行（UnitHeaderRow）的单元号（UnitCode）必须互不相同（大小写敏感比较）。

### 属性 4：行数单调递增

对任意多 Sheet 文件，每追加一个有效（非被忽略）Sheet 的解析结果，合并行列表的长度必须大于追加前的长度，至少增加 1（即单元首行本身）。

### 属性 5：行上限截断正确性

对任意多 Sheet 文件，当合并行列表中已有 5000 行时，后端解析接口（MultiSheetApi）必须满足：
- `rows.Count` 不超过 5000
- `reachedLimit` 为 `true`
- `importedSheets` 仅统计在截断前已完整处理所有数据行的 Sheet 数
