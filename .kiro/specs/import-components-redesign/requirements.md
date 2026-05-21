# Requirements Document

## Introduction

本需求文档描述对 `Quotation/ImportComponents` 页面的全面重新设计。
本次重设计**保留现有全部业务功能**，但不受原有页面布局的限制，在技术栈不变的前提下自由优化页面结构、交互流程与移动端体验。

项目背景：这是一个 PowerBuilder → C# ASP.NET MVC 的迁移项目，数据库与历史系统共用（SQL Server），页面使用 Bootstrap 5 + Handsontable。

---

## Glossary

| 术语 | 定义 |
|------|------|
| **Page（页面）** | 指重新设计后的 `Quotation/ImportComponents` 视图及其前端 JS 逻辑 |
| **Quotation（报价单）** | 以 `fabh`（方案编号）标识的报价方案记录，存于 `BJFAT` 表 |
| **CabinetNode（控制柜节点）** | BJB 表中 `x_bm` 修剪后长度=4 且 ≠ `"0"` 且 ≠ `"9999"` 的记录，表示一个控制柜 |
| **DirectoryTree（目录树）** | 页面左侧显示报价单结构的树形控件，根节点为报价单，子节点为控制柜 |
| **ComponentExcel（Excel元件表）** | 用户上传的含元件清单的 `.xls`/`.xlsx` 文件 |
| **ComponentGrid（元件表格）** | 页面中使用 Handsontable 渲染的元件数据表格 |
| **UnitCode（单元号）** | 元件表格第2列，用于标识控制柜的代号（如 `RH01`），去除首尾空白后长度 > 0 视为有效 |
| **Quantity（数量）** | 元件表格第6列，指控制柜的台数（>1 时触发拆分），有效范围 0.01–999,999,999.99 |
| **Split（拆分）** | 当某控制柜数量 N>1 时，将其展开为 N 个独立节点的操作 |
| **DirectoryPreview（目录预览）** | 将元件表格数据解析为目录树子节点列表，供用户确认后再保存 |
| **SavePlan（方案保存）** | 将目录树节点和元件数据持久化写入 BJB 表的操作 |
| **BJB_Table（BJB 表）** | 数据库中存储报价元件清单的历史表，字段名以历史系统为准 |
| **AntiForgeryToken（防伪令牌）** | ASP.NET MVC 的 CSRF 防护令牌，所有写操作必须携带 |
| **Handsontable** | 前端电子表格组件，licenseKey: `53ea8-c2678-49b80-cb40f-4dad4` |
| **InfoBar（信息栏）** | 页面顶部用于显示操作结果、错误摘要的提示区域 |
| **Backend（后端）** | 指 ASP.NET MVC Controller 及其调用的服务层逻辑 |
| **非空行** | ComponentGrid 中至少有一列内容不为空白字符的行 |

---

## Requirements

### Requirement 1: 页面初始化与目录树加载

**User Story:** 作为报价人，我希望打开页面时能立即看到该报价单现有的控制柜结构，以便了解当前方案状态，决定是否需要重新导入元件。

#### Acceptance Criteria

1. WHEN 用户访问 `GET /Quotation/ImportComponents/{id}`，THE Page SHALL 在首屏渲染时从数据库查询该报价单的控制柜节点，无需用户额外操作。

2. WHEN Page 查询 BJB_Table，THE Backend SHALL 仅加载满足以下条件的记录作为 CabinetNode：`fabh` 与报价单编号精确匹配（使用 LTRIM/RTRIM 后比较）、`x_bm` 修剪后长度等于 4、`x_bm` 修剪后不等于 `"0"` 且不等于 `"9999"`。

3. THE DirectoryTree SHALL 以报价单名称（`QuotationName`）为根节点文本显示。

4. IF 报价单名称为 NULL 或修剪后长度为 0，THEN THE DirectoryTree SHALL 以报价单编号（`fabh`）作为根节点文本代替显示。

5. THE DirectoryTree SHALL 按 `x_bm` 升序排列 CabinetNode 子节点，每个节点显示格式为"名称（编码）"。

6. IF CabinetNode 的 `x_mc` 字段为 NULL 或修剪后长度为 0，THEN THE DirectoryTree SHALL 以该节点的编码（`x_bm`）代替名称显示，格式为"编码（编码）"。

7. IF 该报价单在 BJB_Table 中不存在满足条件的 CabinetNode，THEN THE DirectoryTree SHALL 在子节点区域显示"暂无控制柜节点"提示文字。

8. IF 路由参数 `id` 缺失或无法解析为有效的非空字符串，THEN THE Page SHALL 重定向到报价单列表页（`/Quotation/Index`）并在 TempData 中写入错误提示"报价单不存在"。

9. IF 路由参数 `id` 非空但在数据库 BJFAT 表中查询不到对应记录，THEN THE Page SHALL 重定向到报价单列表页并在 TempData 中写入错误提示"报价单不存在"。

10. WHEN Backend 执行数据库查询，IF 数据库查询抛出异常，THEN THE Backend SHALL 记录错误日志并返回 HTTP 500，THE Page SHALL 向用户显示通用错误提示。

11. WHEN Page 正在从数据库加载控制柜节点数据，THE Page SHALL 在目录树区域显示加载指示（如 spinner 或"加载中..."文字），直到数据加载完成或失败。

---

### Requirement 2: 上传并解析 Excel 元件表

**User Story:** 作为报价人，我希望选择本地 Excel 文件后系统自动解析并展示数据，以便我可以在页面上检查和编辑元件清单。

#### Acceptance Criteria

1. WHEN 用户点击"打开 Excel 元件表"按钮，THE Page SHALL 打开系统文件选择器，仅接受 `.xls` 和 `.xlsx` 格式的文件（input accept=".xls,.xlsx"）。

2. WHEN 用户选择文件后，THE Page SHALL 通过 `POST /Quotation/UploadImportExcel/{id}` 携带 AntiForgeryToken 将文件上传至 Backend。

3. IF 上传文件的扩展名不是 `.xls` 或 `.xlsx`（大小写不敏感），THEN THE Backend SHALL 返回错误响应，THE InfoBar SHALL 显示"仅支持 .xls / .xlsx 文件"。

4. IF 上传文件大小超过 10 MB，THEN THE Backend SHALL 返回错误响应，THE InfoBar SHALL 显示"文件大小不能超过 10 MB"。

5. IF 上传文件大小为零，或文件中不包含从第 2 行起在 A–H 列存在任意非空单元格的工作表，THEN THE Backend SHALL 返回错误响应，THE InfoBar SHALL 显示"Excel 中没有可读取的工作表"或对应错误提示。

6. WHEN Backend 解析 ComponentExcel，THE Backend SHALL 始终读取文件中的**第一个工作表**，跳过第 1 行（标题行），从第 2 行起读取数据，每行取 A–H 共 8 列（不足 8 列的补充空字符串）。

7. WHEN Backend 在读取过程中遇到连续 5 个所有 A–H 列均为空白的行，THE Backend SHALL 停止读取，忽略其后的所有数据。

8. WHEN 已读取的有效数据行数达到 5000，THE Backend SHALL 停止读取并在响应中标记 `reachedLimit: true`。

9. WHEN Backend 成功解析 ComponentExcel，THE ComponentGrid SHALL 使用 Handsontable 展示解析结果，列头固定为：序号、单元号、名称、规格、单价、数量、生产厂家、总价（共 8 列）；其中"总价"列由前端根据单价×数量计算显示，不从 Excel 文件中读取。

10. WHEN ComponentExcel 加载成功，THE InfoBar SHALL 显示"已加载 N 行数据"；IF `reachedLimit: true`，THE InfoBar SHALL 追加"（已达到 5000 行上限）"提示。

11. WHEN ComponentExcel 数据加载完成，THE Page SHALL 启用"数据检查"和"另存 Excel"按钮，并将"目录预览"和"保存方案"按钮重置为禁用状态。

---

### Requirement 3: 数据检查与错误高亮

**User Story:** 作为报价人，我希望在提交前能对表格数据进行自动验证，以便提前发现并修正错误，避免写入异常数据到数据库。

#### Acceptance Criteria

1. WHEN 用户点击"数据检查"按钮，THE ComponentGrid SHALL 对所有**非空行**执行以下五项验证规则，并收集所有错误。

2. IF 非空行第 6 列（Quantity）修剪后为空字符串，THEN THE ComponentGrid SHALL 标记该单元格（行 N，列 6）为无效，并记录错误消息"第 N 行：第6列数量不能为空"。

3. IF 非空行第 6 列（Quantity）不为空但无法解析为有限正数（0.01–999,999,999.99），THEN THE ComponentGrid SHALL 标记该单元格为无效，并记录错误消息"第 N 行：第6列数量必须为正数"。

4. IF 非空行第 2 列（UnitCode）、第 3 列（名称）、第 4 列（规格）修剪后均为空字符串，THEN THE ComponentGrid SHALL 标记该行第 2、3、4 列的单元格为无效，并记录错误消息"第 N 行：单元号、名称、规格不能同时为空"。

5. IF 同一表格中两行或更多行的 UnitCode（修剪后非空）的值相同，THEN THE ComponentGrid SHALL 标记所有重复行的第 2 列单元格为无效，并为每组重复记录一条错误消息，指出首次出现行号与当前行号，UnitCode 为空的行不参与重复检测。

6. IF 表格中连续 5 个空行之后存在非空行（行 N），THEN THE ComponentGrid SHALL 标记行 N 的所有非空列为无效，并记录错误消息"第 N 行：连续空行超过 5 行后不能再有数据"。

7. WHEN 存在验证错误，THE ComponentGrid SHALL 对所有无效单元格应用红色背景（`#ffe3e3`）和红色边框（`#dc3545`），有效单元格清除自定义背景和边框。

8. WHEN 存在验证错误，THE InfoBar SHALL 以错误样式（`alert-danger`）显示错误摘要，最多展示 8 条错误信息，每条以"；"分隔；IF 错误总数超过 8 条，THE InfoBar SHALL 在末尾追加"（另有 N 条）"提示。

9. WHEN 所有验证规则均无错误，THE ComponentGrid SHALL 清除所有单元格高亮，THE InfoBar SHALL 以成功样式（`alert-success`）显示"数据检查通过"，THE Page SHALL 启用"目录预览"按钮，同时禁用"保存方案"按钮。

10. IF 数据检查已通过后，用户在 ComponentGrid 中执行了单元格编辑、行插入或行删除操作，THEN THE Page SHALL 在该次操作完成后立即将"目录预览"和"保存方案"按钮重置为禁用状态。

---

### Requirement 4: 目录预览与控制柜拆分

**User Story:** 作为报价人，我希望在保存前预览目录树结构，以便确认控制柜节点数量和命名是否符合预期，特别是数量>1时的自动拆分结果。

#### Acceptance Criteria

1. IF 数据检查尚未通过（按钮处于禁用状态），THEN THE Page SHALL 禁用"目录预览"按钮，阻止用户点击触发 DirectoryPreview。

2. WHEN 用户点击已启用的"目录预览"按钮，THE Page SHALL 执行 DirectoryPreview。

3. WHEN 执行 DirectoryPreview，THE Page SHALL 遍历 ComponentGrid 所有行，仅处理 UnitCode（第 2 列去除首尾空白后长度 > 0）的行，将每行解析为一个控制柜来源块。

4. THE Page SHALL 读取每个控制柜来源块第 6 列（Quantity）的整数值作为 Split 数量 N；IF Quantity 解析失败、不是正整数或解析结果 ≤ 0，THEN THE Page SHALL 默认 N = 1；IF Quantity 解析结果超过 99，THEN THE Page SHALL 将 N 截断为 99。

5. IF 控制柜来源块的 N = 1，THEN THE DirectoryTree SHALL 生成 1 个子节点，节点名称等于该行 UnitCode 去除首尾空白后的原始值。

6. IF 控制柜来源块的 N > 1，THEN THE DirectoryTree SHALL 生成 N 个子节点；节点命名规则：检测 UnitCode 末尾的连续数字段，从原始值开始逐一递增生成后续名称，数字宽度与原始值对齐（如 `RH01` 且 N=3 → `RH01, RH02, RH03`；`RH99` 且 N=2 → `RH99, RH100`）；IF UnitCode 末尾无数字，THEN THE DirectoryTree SHALL 在 UnitCode 后追加从 1 开始的递增序号（如 `AB` 且 N=2 → `AB1, AB2`）。

7. WHEN DirectoryPreview 完成，THE DirectoryTree SHALL 用预览生成的节点列表**完全替换**原有子节点列表（含数据库加载的节点）。

8. IF DirectoryPreview 成功生成至少 1 个节点，THEN THE InfoBar SHALL 以成功样式（`alert-success`）显示"目录预览完成：已生成 N 个目录子节点"，THE Page SHALL 启用"保存方案"按钮。

9. IF DirectoryPreview 后节点列表为空（无有效 UnitCode 行），THEN THE InfoBar SHALL 以错误样式（`alert-danger`）显示"目录预览完成：未找到可用单元号"，THE Page SHALL 禁用"保存方案"按钮。

---

### Requirement 5: 保存方案到数据库

**User Story:** 作为报价人，我希望将预览通过的目录树和元件数据一键保存到数据库，以便正式建立该报价单的控制柜结构，进入后续报价填写阶段。

#### Acceptance Criteria

1. IF DirectoryPreview 尚未成功执行（按钮处于禁用状态），THEN THE Page SHALL 禁用"保存方案"按钮，阻止用户触发 SavePlan。

2. IF Quotation 的 `currentStatus` 等于 1，THEN THE Page SHALL 在用户点击"保存方案"后直接进入第 4 条，跳过确认对话框。

3. IF Quotation 的 `currentStatus` 不等于 1，THEN THE Page SHALL 先弹出第一次确认对话框"当前报价单有数据，导入时会清空原来数据，是否继续？"；WHEN 用户确认第一次后，THE Page SHALL 弹出第二次确认对话框"确定要覆盖当前报价单吗？"；IF 用户在任意一次对话框中点击"取消"，THEN THE Page SHALL 终止 SavePlan 流程，不向 Backend 发送请求。

4. WHEN 用户确认保存（通过上述第 2 或第 3 条流程），THE Page SHALL 通过 `POST /Quotation/SavePlan` 携带 AntiForgeryToken 以 JSON 格式提交：`fabh`（报价单编号）、`tableJson`（元件表格完整数据，包含每行的 SplitCount 信息）、`treeNodeNames`（目录树当前子节点名称有序列表）。

5. WHEN Backend 收到 SavePlan 请求，THE Backend SHALL 从 Session 中读取当前登录用户，验证其为该 Quotation 的 `bjr` 字段值（大小写不敏感比较）或具有管理员角色；IF 校验失败，THEN THE Backend SHALL 返回 HTTP 403，THE InfoBar SHALL 显示"仅报价人本人或管理员可保存该方案"。

6. THE Backend SHALL 在执行写库前校验 `treeNodeNames` 的数量等于 `tableJson` 中所有控制柜来源块的 `SplitCount` 之和（`Σ SplitCount`）；IF 不一致，THEN THE Backend SHALL 返回 HTTP 400 及错误消息"目录树节点数量与表格单元拆分数量不一致，请重新执行目录预览"，不得写入 BJB_Table。

7. WHEN Backend 执行 SavePlan，THE Backend SHALL 在同一数据库事务中按以下顺序操作：先执行 `DELETE FROM BJB WHERE fabh = {fabh} AND x_bm NOT IN ('0', '9999')`；再按 `treeNodeNames` 的顺序依次为每个控制柜生成编码（第 1 个 → `0001`，第 2 个 → `0002`，依此类推），并批量插入：1 条 4 位主节点（`x_bm = 控制柜编码, x_lx = 1`）、5 条固定子类型节点（`x_bm = 控制柜编码 + "0001"/"0002"/"0003"/"0004"/"0005"`）、以及该控制柜对应的所有元件记录（`x_bm = 控制柜编码 + "0001" + 4 位元件序号`）。

8. IF 事务中任意步骤抛出异常，THEN THE Backend SHALL 回滚整个事务，使用 `_logger.LogError` 记录异常信息，返回 HTTP 500，THE InfoBar SHALL 以错误样式显示"保存方案失败：{errorMessage}"。

9. WHEN Backend 事务提交成功并返回 HTTP 200，THE Backend SHALL 在响应体中包含写入总记录数 N（`{ success: true, message: "保存成功，共写入 N 条记录。" }`），THE InfoBar SHALL 以成功样式显示该消息。

10. IF Backend 返回非 200 状态码，THEN THE InfoBar SHALL 显示响应体中的错误消息，不显示成功消息。

---

### Requirement 6: 另存为 Excel 文件

**User Story:** 作为报价人，我希望将元件表格中当前显示的数据导出为标准格式的 Excel 文件，以便归档或发送给相关人员。

#### Acceptance Criteria

1. THE Page SHALL 仅在 ComponentGrid 有已加载数据（`hasLoadedExcelData = true`）时启用"另存 Excel"按钮；IF 无数据，THEN THE Page SHALL 禁用该按钮。

2. WHEN 用户点击已启用的"另存 Excel"按钮，THE Page SHALL 通过 `POST /Quotation/SaveImportExcel` 携带 AntiForgeryToken 以 JSON 格式提交：报价单编号（`quotationNo`）和当前 ComponentGrid 所有行的数据（每行 8 列，空白处补充空字符串）。

3. WHEN Backend 生成导出文件，THE Backend SHALL 在 xlsx 文件第 1 行写入固定列头：序号、单元号、名称、规格、单价、数量、生产厂家、总价；从第 2 行开始按顺序写入表格数据，每行严格 8 列，多余列截断，不足列补空字符串。

4. WHEN Backend 生成导出文件，THE Backend SHALL 以 `报价元件表_{fabh}_{yyyyMMddHHmmss}.xlsx` 格式命名文件，并以文件下载形式返回（Content-Type: `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`，Content-Disposition: `attachment; filename*=UTF-8''...`）。

5. WHEN 前端收到成功的文件响应，THE Page SHALL 通过动态创建 `<a>` 元素、设置 `href` 为 Object URL 并触发点击完成浏览器下载，下载触发后立即调用 `URL.revokeObjectURL` 释放资源。

6. IF Backend 返回非 200 状态码或响应体不是有效文件流，THEN THE InfoBar SHALL 以错误样式显示"导出失败，请稍后重试"。

---

### Requirement 7: 目录树节点定位至表格行

**User Story:** 作为报价人，我希望点击目录树中的控制柜节点后，元件表格能自动滚动并定位到对应行，以便在大表格中快速找到该控制柜的元件数据。

#### Acceptance Criteria

1. WHEN 用户点击 DirectoryTree 中的任意子节点，THE ComponentGrid SHALL 在第 2 列（UnitCode）中执行**大小写敏感的精确匹配**，搜索与该节点 `data-unit-name` 属性值（去除首尾空白后）完全一致的行。

2. WHEN 找到一个或多个匹配行，THE ComponentGrid SHALL 选中**第一个**匹配行的第 2 列单元格，调用 `scrollViewportTo` 将视口滚动到该行，THE InfoBar SHALL 以成功样式显示"已定位到控制柜 {名称}（第 N 行）"，其中 N 为该行在表格中从 1 开始计数的行号。

3. IF 在第 2 列中未找到与节点 `data-unit-name`（去除首尾空白后）完全匹配的行，THEN THE InfoBar SHALL 以错误样式显示"未在导入表第2列找到控制柜：{名称}"。

4. IF 节点的 `data-unit-name` 属性不存在、为 NULL 或去除首尾空白后为空字符串，THEN THE InfoBar SHALL 以错误样式显示"目录节点未包含有效控制柜名称"，不执行搜索。

---

### Requirement 8: 页面布局与交互设计

**User Story:** 作为报价人，我希望页面布局清晰合理、操作流程直观，以便高效地完成从导入 Excel 到保存方案的完整工作流。

#### Acceptance Criteria

1. THE Page SHALL 采用双栏布局：左侧为 DirectoryTree 面板（默认宽度 280px）、右侧为 ComponentGrid 面板（占剩余宽度）；两侧之间提供可向左/向右拖动的分隔条，允许用户将左侧宽度调整到最小 220px 至最大为工作区宽度 60% 的范围内。

2. THE Page SHALL 提供"隐藏/显示目录树"切换按钮；WHEN 目录树被隐藏，THE Page SHALL 同时隐藏分隔条，ComponentGrid 面板占满全宽；WHEN 目录树再次显示，THE Page SHALL 还原双栏布局。

3. THE InfoBar SHALL 始终在页面中可见，不自动消失；成功状态应用 `alert-success` 类，错误状态应用 `alert-danger` 类，默认/提示状态应用 `alert-info` 类；WHEN 用户触发新操作时，THE InfoBar SHALL 清除上一次的状态样式，更新为新操作的反馈。

4. WHEN Page 加载完成，THE Page SHALL 在顶部标题区域展示当前报价单编号（`fabh`）和"返回列表"导航链接（`href="/Quotation/Index"`）。

5. WHEN 用户在 ComponentGrid 内右键点击，THE ComponentGrid SHALL 显示上下文菜单，菜单包含：在上方插入行、在下方插入行、删除行、撤销、重做、复制、剪切。

6. WHEN DirectoryTree 被隐藏或分隔条被拖动调整宽度，THE ComponentGrid SHALL 调用 `hot.render()` 重新渲染以适应新的容器宽度。

7. IF 撤销栈为空，THEN"撤销"菜单项 SHALL 显示为禁用状态；IF 重做栈为空，THEN"重做"菜单项 SHALL 显示为禁用状态。

8. IF 剪贴板为空或浏览器不支持粘贴，THEN"粘贴"菜单项 SHALL 显示为禁用状态。

9. THE 上下文菜单 SHALL 仅在用户右键点击位置位于 ComponentGrid 单元格区域内时显示，不得在 ComponentGrid 范围外响应右键点击而显示菜单。

---

### Requirement 9: 移动端适配

**User Story:** 作为报价人，我希望能在手机浏览器上正常使用本页面，以便在没有电脑的情况下也能查看和操作报价数据。

#### Acceptance Criteria

1. THE Page SHALL 在 `<head>` 中包含 `<meta name="viewport" content="width=device-width, initial-scale=1.0">`，确保移动端正常缩放。

2. THE Page SHALL 在宽度不低于 320px 的任意屏幕上不出现横向滚动条，所有内容区域不超出视口宽度。

3. THE Page 中所有可交互元素（按钮、输入框、表格滚动区域）和文本内容 SHALL 不被设备硬件（刘海屏、圆角屏）遮挡，确保用户可正常点击和阅读。

4. IF 页面加载时检测到屏幕宽度小于 768px，THEN THE Page SHALL 默认将 DirectoryTree 面板设置为折叠状态，ComponentGrid 面板占 100% 视口宽度显示。

5. WHEN DirectoryTree 面板处于折叠状态（无论屏幕尺寸），THE Page SHALL 提供可见且可点击的"显示目录树"控件，允许用户展开 DirectoryTree 面板。

---

## 正确性属性（Correctness Properties）

以下属性用于指导后续属性测试（Property-Based Test）的编写：

### 属性 1：Excel 往返一致性（Round-Trip）

对任意合法的 ComponentGrid 数据（1–5000 行，8 列任意文本内容）：
- 调用 `SaveImportExcel` 导出为 xlsx 文件
- 再调用 `UploadImportExcel` 将该 xlsx 文件重新导入

导入结果中每行每列的内容应与导出前原始内容等价（忽略首尾空格差异）。

> 这是序列化/反序列化的往返属性，能捕获特殊字符、数字精度、编码等潜在 bug。

### 属性 2：数量验证普遍性

对任意一行数据，若第 6 列（Quantity）的值为空字符串、非数字字符串或 ≤ 0 的数字，则 `validateRows` 函数对该行第 6 列返回的无效标记数量必须 ≥ 1。

### 属性 3：单元号重复检测完备性

对任意包含至少 2 行且存在重复 UnitCode（非空）的数据集，`validateRows` 函数必须对所有重复行的第 2 列均标记为无效，且错误列表包含至少 1 条与"重复"相关的错误。

### 属性 4：拆分节点数量守恒

对任意控制柜来源块列表（每行 Split 数量 N ∈ [1, 99]），执行 DirectoryPreview 后生成的节点总数必须等于所有 N 值之和（`Σ N_i`）。

### 属性 5：拆分节点名称唯一性

对任意单个控制柜来源块，当 Split 数量 N > 1 时，`buildSplitNames(baseName, N)` 生成的 N 个名称必须互不相同。

### 属性 6：SavePlan 节点数一致性守恒

对任意合法的 `tableJson` 和 `treeNodeNames` 组合，若 `treeNodeNames.Count` 不等于 `tableJson` 解析出的 CabinetNode 拆分总数（`Σ SplitCount`），则 `BuildRowsFromTable` 必须抛出 `InvalidOperationException`，不得插入任何 BJB_Table 记录。

### 属性 7：生成记录结构正确性

对任意包含 M 个 CabinetNode、每个 CabinetNode 有 K_i 条元件的方案，`BuildRowsFromTable` 生成的记录总数必须等于 `Σ(1 + 5 + K_i)`（每柜 1 主节点 + 5 子类型节点 + 实际元件数）。
