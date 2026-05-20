# 需求文档：导入元件页面重设计（ImportComponents Redesign）

## 引言

本需求文档描述对 `Quotation/ImportComponents` 页面的全面重新设计。
本次重设计**保留现有全部业务功能**，但不受原有页面布局的限制，在技术栈不变的前提下自由优化页面结构、交互流程与移动端体验。

项目背景：这是一个 PowerBuilder → C# ASP.NET MVC 的迁移项目，数据库与历史系统共用（SQL Server），页面使用 Bootstrap 5 + Handsontable。

---

## 术语表

| 术语 | 定义 |
|------|------|
| **页面（Page）** | 指重新设计后的 `Quotation/ImportComponents` 视图及其前端 JS 逻辑 |
| **报价单（Quotation）** | 以 `fabh`（方案编号）标识的报价方案记录，存于 `BJFAT` 表 |
| **控制柜节点（CabinetNode）** | BJB 表中 `x_bm` 长度=4 且 ≠ "9999" 的记录，表示一个控制柜 |
| **目录树（DirectoryTree）** | 页面左侧显示报价单结构的树形控件，根节点为报价单，子节点为控制柜 |
| **Excel元件表（ComponentExcel）** | 用户上传的含元件清单的 `.xls`/`.xlsx` 文件 |
| **元件表格（ComponentGrid）** | 页面中使用 Handsontable 渲染的元件数据表格 |
| **单元号（UnitCode）** | 元件表格第2列，用于标识控制柜的代号（如 `RH01`） |
| **数量（Quantity）** | 元件表格第6列，指控制柜的台数（>1时触发拆分） |
| **拆分（Split）** | 当某控制柜数量 N>1 时，将其展开为 N 个独立节点的操作 |
| **目录预览（DirectoryPreview）** | 将元件表格数据解析为目录树子节点列表，供用户确认后再保存 |
| **方案保存（SavePlan）** | 将目录树节点和元件数据持久化写入 BJB 表的操作 |
| **BJB 表** | 数据库中存储报价元件清单的历史表，字段名以历史系统为准 |
| **防伪令牌（AntiForgeryToken）** | ASP.NET MVC 的 CSRF 防护令牌，所有写操作必须携带 |
| **Handsontable** | 前端电子表格组件，licenseKey: `53ea8-c2678-49b80-cb40f-4dad4` |
| **信息栏（InfoBar）** | 页面顶部用于显示操作结果、错误摘要的提示区域 |

---

## 需求列表

---

### 需求 1：页面初始化——加载报价单与目录树

**用户故事：** 作为报价人，我希望打开页面时能立即看到该报价单现有的控制柜结构，以便了解当前方案状态，决定是否需要重新导入元件。

#### 验收标准

1. WHEN 用户访问 `GET /Quotation/ImportComponents/{id}`，THE 页面（Page）SHALL 在首屏渲染时从数据库查询该报价单的控制柜节点，无需用户额外操作。

2. WHEN 页面查询 BJB 表，THE 页面（Page）SHALL 仅加载满足以下条件的记录作为控制柜节点：`fabh` 与报价单编号精确匹配、`x_bm` 修剪后长度等于 4、且 `x_bm` 不等于 `"9999"`。

3. THE 目录树（DirectoryTree）SHALL 以报价单名称（`x_mc`）为根节点显示；当报价单名称为空时，SHALL 以报价单编号（`fabh`）代替显示。

4. THE 目录树（DirectoryTree）SHALL 按 `x_bm` 升序排列子节点，每个节点格式为"名称（编码）"；当名称为空时，SHALL 以编码代替名称显示。

5. IF 该报价单在 BJB 表中不存在满足条件的控制柜节点，THEN THE 目录树（DirectoryTree）SHALL 在子节点区域显示"暂无控制柜节点"提示文字。

6. IF 报价单编号不存在或为空，THEN THE 页面（Page）SHALL 重定向到报价单列表页并显示错误提示。

---

### 需求 2：上传 Excel 元件表

**用户故事：** 作为报价人，我希望选择本地 Excel 文件后系统自动解析并展示数据，以便我可以在页面上检查和编辑元件清单。

#### 验收标准

1. WHEN 用户点击"打开 Excel 元件表"按钮，THE 页面（Page）SHALL 打开系统文件选择器，仅接受 `.xls` 和 `.xlsx` 格式的文件。

2. WHEN 用户选择文件后，THE 页面（Page）SHALL 通过 `POST /Quotation/UploadImportExcel/{id}` 携带防伪令牌（AntiForgeryToken）将文件上传至后端。

3. IF 上传的文件扩展名不是 `.xls` 或 `.xlsx`，THEN THE 后端（Backend）SHALL 返回错误，THE 信息栏（InfoBar）SHALL 显示"仅支持 .xls / .xlsx 文件"。

4. IF 上传的文件大小为零或未包含可读工作表，THEN THE 后端（Backend）SHALL 返回错误，THE 信息栏（InfoBar）SHALL 显示对应错误提示。

5. WHEN 后端解析 Excel，THE 后端（Backend）SHALL 跳过第 1 行（标题行），从第 2 行开始读取数据，读取每行的前 8 列。

6. WHEN 后端遇到连续 5 个完全为空的行，THE 后端（Backend）SHALL 停止读取，忽略其后的所有数据。

7. WHEN 已读取行数达到 5000，THE 后端（Backend）SHALL 停止读取并在响应中标记 `reachedLimit: true`。

8. WHEN 后端成功解析 Excel，THE 元件表格（ComponentGrid）SHALL 使用 Handsontable 展示解析结果，列头固定为：序号、单元号、名称、规格、单价、数量、生产厂家、总价（共 8 列）。

9. WHEN Excel 加载成功，THE 信息栏（InfoBar）SHALL 显示已加载的行数；IF 达到 5000 行上限，SHALL 追加"已达到 5000 行上限"提示。

10. WHEN Excel 数据加载完成，THE 页面（Page）SHALL 启用"数据检查"和"另存 Excel"按钮，并将"目录预览"和"保存方案"按钮重置为禁用状态。

---

### 需求 3：数据检查

**用户故事：** 作为报价人，我希望在提交前能对表格数据进行自动验证，以便提前发现并修正错误，避免写入异常数据到数据库。

#### 验收标准

1. WHEN 用户点击"数据检查"按钮，THE 元件表格（ComponentGrid）SHALL 对所有非空行执行以下四项验证规则。

2. THE 元件表格（ComponentGrid）SHALL 验证每行第 6 列（数量）不为空；IF 数量为空，THEN SHALL 标记该单元格为无效并记录错误：`第 N 行：数量不能为空`。

3. THE 元件表格（ComponentGrid）SHALL 验证每行第 6 列（数量）为正数；IF 数量不是有限正数，THEN SHALL 标记该单元格为无效并记录错误：`第 N 行：数量必须为正数`。

4. THE 元件表格（ComponentGrid）SHALL 验证：IF 同一行第 2 列（单元号）、第 3 列（名称）、第 4 列（规格）同时为空，THEN SHALL 标记该行第 2、3、4 列为无效并记录错误。

5. THE 元件表格（ComponentGrid）SHALL 验证同一文件中第 2 列（单元号）的值不重复；IF 发现重复，THEN SHALL 标记所有重复行的第 2 列为无效并记录错误，指出冲突行号。

6. THE 元件表格（ComponentGrid）SHALL 验证连续 5 个空行之后不得存在非空数据行；IF 在连续 5 空行后发现数据，THEN SHALL 标记该行为无效并记录错误。

7. WHEN 存在验证错误，THE 元件表格（ComponentGrid）SHALL 对所有无效单元格应用红色背景（`#ffe3e3`）和红色边框（`#dc3545`）。

8. WHEN 存在验证错误，THE 信息栏（InfoBar）SHALL 以错误样式显示错误摘要，最多展示 8 条错误信息；IF 错误超过 8 条，SHALL 追加"另有 N 条"提示。

9. WHEN 所有验证规则通过，THE 元件表格（ComponentGrid）SHALL 清除所有高亮，THE 信息栏（InfoBar）SHALL 显示"数据检查通过"，THE 页面（Page）SHALL 启用"目录预览"按钮。

10. WHEN 用户在数据检查通过后修改了元件表格（ComponentGrid）中任意数据（包括编辑、插入行、删除行），THE 页面（Page）SHALL 立即将"目录预览"和"保存方案"按钮重置为禁用状态。

---

### 需求 4：目录预览

**用户故事：** 作为报价人，我希望在保存前预览目录树结构，以便确认控制柜节点数量和命名是否符合预期，特别是数量>1时的自动拆分结果。

#### 验收标准

1. WHEN 用户点击"目录预览"按钮，THE 页面（Page）SHALL 仅在数据检查已通过的状态下执行目录预览；IF 数据检查未通过，SHALL 忽略点击。

2. WHEN 执行目录预览，THE 页面（Page）SHALL 遍历元件表格（ComponentGrid）中所有第 2 列（单元号）非空的行，将每行解析为一个控制柜来源块。

3. THE 页面（Page）SHALL 读取每个控制柜来源块第 6 列（数量）的整数值作为拆分数量 N；IF 数量解析失败或不是正整数，SHALL 默认 N=1。

4. WHEN 控制柜来源块的拆分数量 N=1，THE 目录树（DirectoryTree）SHALL 生成 1 个子节点，节点名称等于该行单元号的原始值。

5. WHEN 控制柜来源块的拆分数量 N>1，THE 目录树（DirectoryTree）SHALL 生成 N 个子节点；节点命名规则：检测单元号末尾的连续数字段，将其从原始值开始逐一递增，数字宽度与原始值保持一致（如 `RH01` → `RH01, RH02, RH03`）；IF 末尾无数字，SHALL 在单元号后追加序号（如 `AB` → `AB1, AB2`）。

6. WHEN 目录预览完成，THE 目录树（DirectoryTree）SHALL 用预览生成的节点列表**完全替换**原有子节点列表（含数据库加载的节点）。

7. WHEN 目录预览成功生成至少 1 个节点，THE 信息栏（InfoBar）SHALL 显示"目录预览完成：已生成 N 个目录子节点"，THE 页面（Page）SHALL 启用"保存方案"按钮。

8. IF 目录预览后节点列表为空（无有效单元号行），THEN THE 信息栏（InfoBar）SHALL 显示"目录预览完成：未找到可用单元号"，THE 页面（Page）SHALL 禁用"保存方案"按钮。

---

### 需求 5：保存方案

**用户故事：** 作为报价人，我希望将预览通过的目录树和元件数据一键保存到数据库，以便正式建立该报价单的控制柜结构，进入后续报价填写阶段。

#### 验收标准

1. WHEN 用户点击"保存方案"按钮，THE 页面（Page）SHALL 仅在目录预览已成功执行的状态下执行保存；IF 目录预览未执行，SHALL 忽略点击。

2. WHEN 报价单 `currentStatus` 不等于 1（即已有数据），THE 页面（Page）SHALL 先弹出第一次确认对话框"当前报价单有数据，导入时会清空原来数据，是否继续？"；IF 用户确认，SHALL 弹出第二次确认对话框"确定要覆盖当前报价单吗？"；IF 任意一次用户取消，SHALL 终止保存操作。

3. WHEN 用户确认保存，THE 页面（Page）SHALL 通过 `POST /Quotation/SavePlan` 携带防伪令牌（AntiForgeryToken）提交：报价单编号（`fabh`）、元件表格数据（`tableJson`）、目录树节点名称列表（`treeNodeNames`）。

4. WHEN 后端收到保存请求，THE 后端（Backend）SHALL 验证当前登录用户为该报价单的报价人本人或管理员角色；IF 校验失败，SHALL 返回 403 错误，THE 信息栏（InfoBar）SHALL 显示权限不足提示。

5. WHEN 后端执行保存，THE 后端（Backend）SHALL 在同一数据库事务中：先删除 BJB 表中该 `fabh` 下 `x_bm` 不等于 `'0'` 和 `'9999'` 的所有记录，再按以下规则批量插入新记录：
   - 每个控制柜：1 条 4 位主节点记录（`x_lx=1`）
   - 固定插入 5 条子类型节点：`0001`（器件）、`0002`（辅料）、`0003`（壳体）、`0004`（安装）、`0005`（包装）
   - 该控制柜下的每条元件记录：`x_bm = 控制柜编码 + "0001" + 元件序号（4位）`

6. THE 后端（Backend）SHALL 在保存前校验目录树节点总数（`treeNodeNames.Count`）与表格解析出的控制柜拆分总数一致；IF 不一致，SHALL 返回错误"目录树节点数量与表格单元拆分数量不一致，请重新执行目录预览"，不得写入数据库。

7. IF 事务中任意步骤抛出异常，THEN THE 后端（Backend）SHALL 回滚整个事务，记录错误日志，返回 500 错误，THE 信息栏（InfoBar）SHALL 显示"保存方案失败"及错误摘要。

8. WHEN 保存成功，THE 信息栏（InfoBar）SHALL 显示"保存成功，共写入 N 条记录"。

---

### 需求 6：另存 Excel

**用户故事：** 作为报价人，我希望将元件表格中当前显示的数据导出为标准格式的 Excel 文件，以便归档或发送给相关人员。

#### 验收标准

1. WHEN 用户点击"另存 Excel"按钮，THE 页面（Page）SHALL 仅在 Excel 数据已加载的状态下执行导出；IF 无数据，SHALL 禁用该按钮。

2. WHEN 用户点击"另存 Excel"，THE 页面（Page）SHALL 通过 `POST /Quotation/SaveImportExcel` 携带防伪令牌（AntiForgeryToken）提交：报价单编号和当前元件表格数据（共 8 列）。

3. WHEN 后端生成 Excel，THE 后端（Backend）SHALL 在第 1 行写入固定列头：序号、单元号、名称、规格、单价、数量、生产厂家、总价；从第 2 行开始写入表格数据，每行 8 列。

4. WHEN 后端生成 Excel，THE 后端（Backend）SHALL 以 `报价元件表_{报价单编号}_{时间戳}.xlsx` 格式命名文件，并以文件下载形式返回（Content-Type: `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`）。

5. WHEN 文件下载成功，THE 页面（Page）SHALL 通过动态创建 `<a>` 标签触发浏览器下载，下载完成后释放 Object URL。

6. IF 导出过程发生错误，THEN THE 信息栏（InfoBar）SHALL 显示"导出失败，请稍后重试"。

---

### 需求 7：目录树节点定位

**用户故事：** 作为报价人，我希望点击目录树中的控制柜节点后，元件表格能自动滚动并定位到对应行，以便在大表格中快速找到该控制柜的元件数据。

#### 验收标准

1. WHEN 用户点击目录树（DirectoryTree）中的任意子节点，THE 元件表格（ComponentGrid）SHALL 在第 2 列（单元号）中搜索与该节点 `data-unit-name` 属性完全匹配的行。

2. WHEN 找到匹配行，THE 元件表格（ComponentGrid）SHALL 选中该行第 2 列的单元格并将视口滚动到该行，THE 信息栏（InfoBar）SHALL 显示"已定位到控制柜 {名称}（第 N 行）"。

3. IF 在第 2 列中未找到与节点名称匹配的行，THEN THE 信息栏（InfoBar）SHALL 显示"未在导入表第2列找到控制柜：{名称}"的错误提示。

4. IF 节点的 `data-unit-name` 属性为空，THEN THE 信息栏（InfoBar）SHALL 显示"目录节点未包含有效控制柜名称"的错误提示。

---

### 需求 8：页面布局与交互设计

**用户故事：** 作为报价人，我希望页面布局清晰合理、操作流程直观，以便高效地完成从导入 Excel 到保存方案的完整工作流。

#### 验收标准

1. THE 页面（Page）SHALL 采用双栏布局：左侧为目录树面板，右侧为元件表格面板；两侧之间提供可拖动的分隔条，允许用户调整左侧宽度（最小 220px，最大为工作区宽度的 60%）。

2. THE 页面（Page）SHALL 提供"隐藏/显示目录树"切换按钮；WHEN 目录树被隐藏，SHALL 同时隐藏分隔条，元件表格面板占满全宽；WHEN 目录树显示时，SHALL 还原双栏布局。

3. THE 信息栏（InfoBar）SHALL 始终显示在页面顶部操作区域下方，用于展示系统提示、操作结果和错误摘要；成功状态使用绿色样式，错误状态使用红色样式，默认状态使用蓝色样式。

4. THE 页面（Page）SHALL 在页面顶部区域展示报价单编号和"返回列表"导航按钮。

5. THE 元件表格（ComponentGrid）SHALL 支持以下右键菜单操作：在上方插入行、在下方插入行、删除行、撤销、重做、复制、剪切。

6. WHEN 目录树被隐藏或宽度被拖动调整，THE 元件表格（ComponentGrid）SHALL 重新渲染以适应新的容器宽度。

---

### 需求 9：移动端适配

**用户故事：** 作为报价人，我希望能在手机浏览器上正常使用本页面，以便在没有电脑的情况下也能查看和操作报价数据。

#### 验收标准

1. THE 页面（Page）SHALL 在 viewport meta 中包含 `width=device-width, initial-scale=1.0`，确保移动端正常缩放。

2. THE 页面（Page）SHALL 在任意屏幕宽度下不出现横向滚动条，所有内容区域不超出视口宽度。

3. THE 页面（Page）SHALL 使用 `env(safe-area-inset-*)` CSS 变量适配带刘海或圆角屏幕的手机设备。

4. WHERE 屏幕宽度小于 768px，THE 页面（Page）SHALL 默认折叠目录树面板，将元件表格面板置于全宽显示，以确保主要功能区域在小屏幕上可用。

---

## 正确性属性

以下属性用于指导后续测试（属性测试 / 单元测试）的编写方向，涵盖核心业务逻辑：

### 属性 1：Excel 往返一致性（Round-Trip）

对任意合法的元件表格数据（任意行数 1–5000，任意 8 列文本内容）：
- 调用 `SaveImportExcel` 导出为 xlsx 文件
- 再调用 `UploadImportExcel` 将该 xlsx 文件重新导入

则导入结果中每行每列的内容应与导出前的原始内容等价（忽略首尾空格差异）。

> 这是解析器/序列化器的往返属性，能捕获特殊字符、数字精度、编码等潜在问题。

### 属性 2：数量验证的普遍性

对任意一行数据，若第 6 列（数量）的值为空字符串、非数字字符串或小于等于 0 的数字，则 `validateRows` 函数必须对该行的第 6 列返回至少 1 条错误记录，且该单元格被标记为无效。

### 属性 3：单元号重复检测的完备性

对任意包含至少 2 行的数据集，若其中第 2 列存在值相同的行，则 `validateRows` 函数必须对所有重复行的第 2 列均标记为无效，且错误列表中至少包含 1 条与"重复"相关的错误。

### 属性 4：拆分节点数量守恒

对任意控制柜来源块列表（任意行数，每行拆分数量 N≥1），执行目录预览后生成的节点总数必须恰好等于所有 N 值之和（`Σ N_i`）。

### 属性 5：拆分节点名称唯一性

对任意单个控制柜来源块，当拆分数量 N>1 时，`buildSplitNames(baseName, N)` 生成的 N 个名称必须互不相同。

### 属性 6：保存前节点数一致性守恒

对任意合法的 `tableJson` 和 `treeNodeNames` 组合，后端 `BuildRowsFromTable` 方法中计算出的 `expectedNodeCount`（`Σ SplitCount`）必须等于 `treeNodeNames.Count`；否则必须抛出 `InvalidOperationException`，不得插入任何数据库记录。

### 属性 7：生成记录结构正确性

对任意包含 M 个控制柜、每个控制柜有 K_i 条元件的方案，`BuildRowsFromTable` 方法生成的记录总数必须恰好等于 `Σ(1 + 5 + K_i)`（即每柜 1 主节点 + 5 子类型节点 + 实际元件数）。
