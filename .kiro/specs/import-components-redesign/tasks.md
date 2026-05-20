# Implementation Plan: 报价单导入元件页面重设计（Import Components Redesign）

## Overview

按照 Model → Service → Controller → View → JS/CSS 的实施顺序，对 `Quotation/ImportComponents` 页面进行结构化重构。
本计划严格遵循设计文档，**保留全部现有业务功能**与 `BJB` 表的 4 位 / 8 位 / 12 位编码规则；
不引入新的 Service 抽象，仅在 `QuotationController` 内做必要的微调（10MB 校验、节点数显式校验），
重写 `ImportComponents.cshtml` / `quotation-import.js`，并新增 `quotation-import.css`。

测试策略遵循"测试金字塔"：
- **属性测试（PBT）** 覆盖设计文档"Correctness Properties"的 8 条属性，C# 用 **FsCheck.Xunit**、JS 用 **fast-check**，每条不少于 100 次迭代；
- **单元测试（xUnit / Vitest）** 覆盖状态机、DOM 渲染、错误反馈等示例；
- **集成测试（WebApplicationFactory）** 覆盖 5 个 Controller Action 的关键路径；
- **E2E 测试（可选）** 覆盖布局拖动 / 移动端折叠等纯交互行为。

> 实施约束：所有数据库操作必须 `async/await`；写操作必须带 `AntiForgeryToken`；BJB 写操作必须在事务中执行，禁止删除 `x_bm IN ('0','9999')` 的保留节点。

## Tasks

- [x] 1. 准备测试基础设施与可见性桥接
  - [x] 1.1 新建 `PanelFlow.Web.Tests` xUnit 测试项目并加入 `PanelFlow.sln`
    - 创建 `PanelFlow.Web.Tests/PanelFlow.Web.Tests.csproj`（`net8.0`，引用 `xunit`、`xunit.runner.visualstudio`、`Microsoft.NET.Test.Sdk`、`FsCheck.Xunit`、`Microsoft.AspNetCore.Mvc.Testing`、`Microsoft.EntityFrameworkCore.InMemory`、`Moq`、`NPOI`）
    - 添加项目引用：`PanelFlow.Web`、`PanelFlow.Core`、`PanelFlow.Infrastructure`
    - 在 `dotnet test` 中确认空项目可成功发现 0 测试
    - _Requirements: 全部（测试基础设施支撑）_
  - [x] 1.2 在 `PanelFlow.Web` 暴露内部类型给测试程序集
    - 在 `PanelFlow.Web` 中新增 `Properties/AssemblyInfo.cs`，添加 `[assembly: InternalsVisibleTo("PanelFlow.Web.Tests")]`
    - 不改变现有类型可见性，仅让 `BuildRowsFromTable` / `ParseSourceUnits` / `SourceUnitBlock` / `BjbWriteRow` 可被测试程序集访问
    - _Requirements: 5.7（PBT 6/7 的前置条件）_
  - [x] 1.3 新建前端单元测试与属性测试基础设施
    - 在仓库根目录新建 `PanelFlow.Web/wwwroot/js/__tests__/` 目录
    - 在 `PanelFlow.Web` 下添加 `package.json`（含 `vitest`、`fast-check`、`jsdom`）
    - 配置 `vitest.config.ts`，将 `wwwroot/js/quotation-import.js` 中要被测试的纯函数（`validateRows` / `buildSplitNames` / `buildPreviewNodes`）通过 `module.exports`（在 IIFE 之外的 ESM 兼容入口）暴露给测试，但保持浏览器加载行为不变
    - 提供 `npm test`（`vitest run`）能成功发现 0 测试
    - _Requirements: 3.1–3.6, 4.3–4.6（PBT 1–5 的前置条件）_

- [x] 2. 抽取 ViewModel 到独立文件并复用
  - [x] 2.1 将 `QuotationPriceViewModel` / `QuotationTreeNodeViewModel` 抽取到独立文件
    - 在 `PanelFlow.Web/Models/Quotation/` 下新建 `QuotationPriceViewModel.cs` / `QuotationTreeNodeViewModel.cs`
    - 保留原有公共字段与默认值，命名空间改为 `PanelFlow.Web.Models.Quotation`
    - 同步修改 `QuotationController.cs` 与 `ImportComponents.cshtml` / `FillPrice.cshtml` / `Details.cshtml` 的 `using` 与 `@model` 引用
    - 不改变字段语义，确保现有视图渲染结果一致
    - _Requirements: 1.3, 1.4, 1.5, 1.6（强类型 ViewModel 复用）_

- [x] 3. 后端：QuotationController 微调与显式校验
  - [x] 3.1 在 `Program.cs` 显式声明 10MB 上传上限
    - 在 `Program.cs` 中添加 `services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 10L * 1024 * 1024)`
    - 不影响其他上传入口（其他入口若无单独限制则共用此值；如有特殊需求后续单独处理）
    - _Requirements: 2.4_
  - [x] 3.2 在 `UploadImportExcel` 增加文件大小预检
    - 在现有 `file == null || file.Length <= 0` 判断之后追加 `if (file.Length > 10L * 1024 * 1024) return Json(new { success = false, message = "文件大小不能超过 10 MB" })`
    - 异常路径以 `Json` 返回，与现有错误响应风格保持一致（HTTP 200 + `success:false`）
    - _Requirements: 2.4_
  - [x] 3.3 在 `SavePlan` 增加节点数显式前置校验
    - 在 `BuildRowsFromTable` 之前提取 `var sourceUnits = ParseSourceUnits(tableRows); var expected = sourceUnits.Sum(u => u.SplitCount);`
    - `if (expected != treeNodeNames.Count) return BadRequest(new { success = false, message = "目录树节点数量与表格单元拆分数量不一致，请重新执行目录预览" });`
    - 保持 `BuildRowsFromTable` 内部 `InvalidOperationException` 兜底逻辑不变
    - _Requirements: 5.6_
  - [x] 3.4 在 `UploadImportExcel` 错误路径补全 `_logger.LogError`
    - 仅在 NPOI 解析异常分支中追加 `_logger.LogError(ex, "Excel 解析失败 fabh={fabh}", id)`，保留现有用户友好错误消息
    - _Requirements: 2.5（异常可观测）, 设计 Error Handling 表_
  - [x] 3.5 编写 `UploadImportExcel` 集成测试
    - 使用 `WebApplicationFactory<Program>` 构建测试客户端，覆盖：合法 xlsx、合法 xls、非法扩展名、>10MB、空文件、连续 5 空行截断、5001 行触发 `reachedLimit`
    - _Requirements: 2.3, 2.4, 2.5, 2.6, 2.7, 2.8_
  - [x] 3.6 编写 `SavePlan` 集成测试
    - 覆盖：本人提交成功、管理员提交成功、非本人 403、节点数不一致 400、空表 400、事务回滚（注入故障 BJB 行触发异常）
    - _Requirements: 5.5, 5.6, 5.7, 5.8, 5.9_
  - [x] 3.7 编写 `SaveImportExcel` 集成测试
    - 覆盖：8 列严格补齐、列头正确、文件名符合 `报价元件表_{fabh}_{yyyyMMddHHmmss}.xlsx`
    - _Requirements: 6.3, 6.4_
  - [x] 3.8 编写 `ImportComponents` GET 集成测试
    - 覆盖：含节点 / 不含节点 / 路由 id 缺失 / 路由 id 不存在
    - _Requirements: 1.1, 1.2, 1.5, 1.7, 1.8, 1.9_

- [x] 4. 后端属性测试（PBT 6 / 7 / 8）
  - [x] 4.1 PBT 6: SavePlan 节点数一致性守恒（FsCheck.Xunit）
    - 在 `PanelFlow.Web.Tests/Properties/BuildRowsFromTableProperties.cs` 中编写
    - **Property 6: SavePlan 节点数一致性守恒**
    - 生成 `(tableJson, treeNodeNames)` 对，使 `treeNodeNames.Count != Σ SplitCount`，断言 `BuildRowsFromTable` 抛 `InvalidOperationException` 且未返回任何写入行
    - 使用 `[Property(MaxTest = 100)]`（FsCheck.Xunit 默认 100 次，显式标注）
    - **Validates: Requirements 5.6**
  - [x] 4.2 PBT 7: BJB 写入行结构正确性（FsCheck.Xunit）
    - 在同一文件中编写
    - **Property 7: BJB 写入行结构正确性**
    - 自定义 `Arbitrary<Plan>` 生成 `(M cabinets, K_i components per cabinet)` 且满足 `treeNodeNames.Count == M`，断言：
      - 总行数 = `Σ(1 + 5 + K_i)`
      - 4 位主节点（`x_lx == 1`）行数 = `M`
      - 8 位子类型节点（后 4 位 ∈ `{0001..0005}`）行数 = `5 * M`
      - 12 位元件节点（`Xbm.Substring(4,4) == "0001"`）行数 = `Σ K_i`
    - 使用 `[Property(MaxTest = 100)]`
    - **Validates: Requirements 5.7**
  - [x] 4.3 PBT 8: Excel 导出 / 导入往返一致性（FsCheck.Xunit）
    - 在 `PanelFlow.Web.Tests/Properties/ExcelRoundTripProperties.cs` 中编写
    - **Property 8: Excel 导出/导入往返一致性**
    - 生成 `1..50` 行（实际边界 1..5000 由 PBT 收缩控制；为缩短运行时间设上限 50）、每行 8 列任意可表达字符串、不含连续 5 空行的行集
    - 直接复用 `QuotationController.SaveImportExcel` / `UploadImportExcel` 的内部解析逻辑（如必要将 NPOI 写入 / 读取抽为可测试静态方法），断言往返后 `Trim()` 等价
    - 使用 `[Property(MaxTest = 100)]`
    - **Validates: Requirements 6.2, 6.3**

- [x] 5. 前端纯函数模块化抽取
  - [x] 5.1 抽取 `validateRows` / `buildSplitNames` / `buildPreviewNodes` 为可测试纯函数
    - 在 `quotation-import.js` 中将这三个函数移到 IIFE 顶部，保持现有签名（输入 / 输出按设计文档"模块边界与契约"表）
    - 通过 `if (typeof module !== "undefined" && module.exports)` 兼容 Vitest（jsdom 环境）；浏览器加载时该分支不生效
    - 不改变浏览器侧调用入口
    - _Requirements: 3.1–3.6, 4.3–4.6_

- [ ] 6. 前端属性测试（PBT 1 / 2 / 3 / 4 / 5）
  - [ ] 6.1 PBT 1: 数量列必须为有限正数（fast-check）
    - 在 `wwwroot/js/__tests__/validateRows.property.test.js` 中编写
    - **Property 1: 数量列必须为有限正数**
    - 使用 `fc.array(fc.tuple(...))` 生成 8 列字符串行集，特化第 6 列为 `fc.oneof(fc.constant(""), fc.string(), fc.constantFrom("-1","0","abc"))`；断言非空行第 6 列异常时 `invalidCells.has(\`${i}:5\`)`
    - 配置 `numRuns: 100`
    - **Validates: Requirements 3.2, 3.3**
  - [ ] 6.2 PBT 2: 单元号 / 名称 / 规格不得同时为空（fast-check）
    - 在同目录新建 `validateRows.threeColEmpty.property.test.js`
    - **Property 2: 三列同空验证**
    - 生成行集，特化第 2、3、4 列为空白；断言至少其中一列在 `invalidCells` 中
    - `numRuns: 100`
    - **Validates: Requirements 3.4**
  - [ ] 6.3 PBT 3: UnitCode 重复必须被全部标记（fast-check）
    - 在同目录新建 `validateRows.duplicate.property.test.js`
    - **Property 3: UnitCode 重复检测**
    - 生成强制存在重复值的 UnitCode（非空），断言所有重复行的 `${i}:1` 都在 `invalidCells` 中，且 `errors` 含"重复"关键字
    - `numRuns: 100`
    - **Validates: Requirements 3.5**
  - [ ] 6.4 PBT 4: 连续 5 空行后非空行必须被标记（fast-check）
    - 在同目录新建 `validateRows.gapLimit.property.test.js`
    - **Property 4: 连续空行限制**
    - 生成在某索引 `k` 处注入 5 连续空行后跟一行非空的行集，断言 `invalidRows.has(k)`
    - `numRuns: 100`
    - **Validates: Requirements 3.6**
  - [ ] 6.5 PBT 5: 拆分节点数量与命名守恒（fast-check）
    - 在同目录新建 `buildSplitNames.property.test.js`
    - **Property 5: 拆分节点数量与命名**
    - 生成 `baseName: fc.string()`、`N: fc.integer({ min: 1, max: 99 })`，断言：
      - `result.length === N`
      - `new Set(result).size === N`
      - `result[0] === baseName.trim()`
      - 末尾数字段宽度对齐 + 递增
    - `numRuns: 100`
    - **Validates: Requirements 4.3, 4.4, 4.5, 4.6**

- [ ] 7. 检查点 - 全部测试基础就绪
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. 视图重设计：ImportComponents.cshtml + 双重确认 Modal
  - [x] 8.1 重写 `ImportComponents.cshtml` 页面骨架
    - 顶部 header（标题 / `fabh` / `QuotationName` / 返回列表链接）
    - `oa-import-toolbar` 工具栏，集中放置 5 个核心按钮（`open-excel-btn` / `check-data-btn` / `preview-tree-btn` / `save-excel-btn` / `save-plan-btn`）+ `toggle-tree-btn`
    - `#page-info-bar` 全宽 alert（默认 `alert-info`）
    - `.oa-import-workspace` 双栏：`#tree-pane`（含根节点 + `#tree-children-container`） + `#tree-splitter` + `#grid-pane`（含 `#hot-container` 与隐藏 `#excel-upload-form`）
    - `<form id="excel-upload-form">` 中保留 `@Html.AntiForgeryToken()` 与 `<input type="file" accept=".xls,.xlsx">`
    - 引入 `~/css/quotation-import.css` 与 `~/js/quotation-import.js`，使用 `asp-append-version="true"`
    - 移除原 `<style>` 内联块；保留 `data-license-key` 等所有 `data-*` 契约
    - _Requirements: 1.3, 1.4, 1.7, 8.1, 8.2, 8.3, 8.4_
  - [x] 8.2 在视图中加入 Bootstrap Modal 用于双重确认
    - 在视图末尾添加 `#save-plan-confirm-modal` 隐藏 Modal（标题 / body / 取消 / 确认按钮，`role="dialog"`，可键盘 ESC 关闭）
    - Modal 不内联脚本，所有逻辑在 `quotation-import.js` 中通过 `confirmWithModal` 调用
    - _Requirements: 5.3_
  - [x] 8.3 视图首屏：根节点 / 子节点 / 空树渲染
    - 根节点文本：`Model.QuotationName` 为空时回退 `Model.QuotationNo`
    - 子节点列表：按 `Model.TreeNodes` 渲染，名称为空时显示"编码（编码）"
    - 空集合显示"暂无控制柜节点"提示
    - 在 `<head>`（或 _Layout 中确认）含 `<meta name="viewport" content="width=device-width, initial-scale=1.0">`
    - _Requirements: 1.3, 1.4, 1.5, 1.6, 1.7, 9.1_
  - [x] 8.4 视图渲染单元测试
    - 在 `PanelFlow.Web.Tests/Views/ImportComponentsViewTests.cs` 使用 `RazorEngine` 或 Snapshot 测试关键 DOM 节点（`#tree-root-node` 文本、空集合提示、所有按钮 ID 存在）
    - _Requirements: 1.3, 1.4, 1.6, 1.7_

- [x] 9. 新增样式：quotation-import.css
  - [x] 9.1 创建 `wwwroot/css/quotation-import.css`
    - 类名前缀 `oa-`；按设计文档"Frontend：quotation-import.css"段定义页面骨架、双栏布局、目录树、分隔条、移动端断点、`env(safe-area-inset-*)` 适配
    - 仅在响应式覆盖宽度处使用 `!important`
    - _Requirements: 8.1, 8.2, 8.3, 9.2, 9.3, 9.4, 9.5_

- [ ] 10. 前端 JS 重写：quotation-import.js
  - [x] 10.1 重写状态管理与按钮闭环（applyButtonStates）
    - 重写 `state` 对象与 `applyButtonStates()` 单一入口；按设计"Mermaid 状态机"图同步 5 个按钮 `disabled`
    - 任意 `afterChange` / `afterCreateRow` / `afterRemoveRow` 触发后立即重置 `hasCheckPassed` / `hasPreviewSucceeded` 为 false 并调用 `applyButtonStates()`
    - _Requirements: 2.11, 3.10, 4.1, 4.7, 4.8, 4.9, 5.1_
  - [x] 10.2 重写 InfoBar：`setInfo(message, level)`
    - `level ∈ {"info","success","error"}`，分别切换 `alert-info` / `alert-success` / `alert-danger`
    - 保证 InfoBar 始终可见，不自动消失
    - _Requirements: 8.3_
  - [x] 10.3 实现纯函数：`validateRows(rows)`
    - 按设计契约返回 `{ errors, invalidCells, invalidRows }`
    - 覆盖：数量列必须为有限正数（0.01–999,999,999.99）、单元号/名称/规格不得同时为空、UnitCode 重复检测、连续 5 空行后非空行检测
    - 错误消息格式严格匹配需求（含"第 N 行"等中文文案）
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6_
  - [x] 10.4 实现纯函数：`buildSplitNames(baseName, N)`
    - 末尾数字段递增 + 字符宽度对齐；无数字段时追加从 1 开始的序号
    - `N` 范围 `1..99`；`baseName` 先 `trim()`
    - _Requirements: 4.3, 4.4, 4.5, 4.6_
  - [ ] 10.5 实现纯函数：`buildPreviewNodes(rows)`
    - 仅处理 UnitCode 非空的行；按 Quantity 解析 N（默认 1，>99 截断为 99）；调用 `buildSplitNames` 展开
    - 返回 `{ unitNo, displayName }[]`
    - _Requirements: 4.2, 4.3, 4.4, 4.5, 4.6_
  - [x] 10.6 错误高亮渲染：`errorCellRenderer`
    - 红色背景 `#ffe3e3` + 红色边框 `#dc3545`，在所有 `invalidCells` / `invalidRows` 单元格渲染
    - _Requirements: 3.7_
  - [ ] 10.7 实现 `runDataCheck` 与错误摘要
    - 调用 `validateRows`，在 InfoBar 显示最多 8 条错误，超出追加"（另有 N 条）"
    - 通过时清除高亮、设置 `hasCheckPassed = true`、`hasPreviewSucceeded = false`、调用 `applyButtonStates`
    - _Requirements: 3.7, 3.8, 3.9, 3.10_
  - [ ] 10.8 实现 `runDirectoryPreview` 与目录树渲染
    - 调用 `buildPreviewNodes(rows)`；空结果时按错误样式提示并禁用"保存方案"
    - 成功时**完全替换**子节点列表、按 `data-unit-name` 渲染按钮、`hasPreviewSucceeded = true`
    - _Requirements: 4.7, 4.8, 4.9_
  - [ ] 10.9 实现 `focusUnitInGrid(unitName)`：节点定位至表格行
    - 大小写敏感精确匹配；命中第一行执行 `selectCell` + `scrollViewportTo`，InfoBar 提示"已定位到控制柜 {名称}（第 N 行）"
    - 未命中显示"未在导入表第2列找到控制柜：{名称}"；`data-unit-name` 缺失显示"目录节点未包含有效控制柜名称"
    - _Requirements: 7.1, 7.2, 7.3, 7.4_
  - [ ] 10.10 实现 `uploadExcel(file)`：调用 `UploadImportExcel`
    - 使用 `safeFetch` 携带 AntiForgeryToken（从 `#excel-upload-form` 读取）
    - 成功：填充 ComponentGrid、设置 `hasLoadedExcelData = true`、重置后续状态、InfoBar 显示行数与 `reachedLimit` 标志
    - 失败：InfoBar 显示后端 message，按钮状态保持
    - 上传过程中禁用按钮，`finally` 调用 `applyButtonStates()`
    - _Requirements: 2.1, 2.2, 2.9, 2.10, 2.11_
  - [ ] 10.11 实现 `exportExcel`：调用 `SaveImportExcel`
    - JSON 提交 8 列、空白补齐；接收 blob 流，动态创建 `<a>` 触发下载，立即 `URL.revokeObjectURL`
    - 失败时 InfoBar 显示"导出失败，请稍后重试"
    - _Requirements: 6.1, 6.2, 6.5, 6.6_
  - [ ] 10.12 实现 `savePlan` + `confirmWithModal` 双重确认
    - `currentStatus === 1` 跳过确认；否则连续两个 Modal，任意取消即终止
    - 提交 `{ fabh, tableJson, treeNodeNames }`，成功后 InfoBar 显示后端返回 message，重置至 Idle 状态以便再次操作
    - 异步过程中按钮禁用，`finally` 恢复
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.9, 5.10_
  - [ ] 10.13 布局交互：分隔条拖动 + 隐藏/显示 + 移动端默认折叠
    - `setupSplitter`：在 220px–60% 范围内拖动；折叠时分隔条 `is-hidden`
    - `setupTreeToggle`：切换 `#tree-pane.is-collapsed` 与按钮文本"隐藏目录树/显示目录树"，并调用 `hot.render()`
    - `setupMobileDefaults`：屏宽 < 768px 时默认折叠，显示控件保持可见
    - _Requirements: 8.1, 8.2, 8.6, 9.4, 9.5_
  - [ ] 10.14 上下文菜单：仅在 ComponentGrid 内显示
    - Handsontable `contextMenu` 项保持现有：行上方/下方插入、删除、撤销、重做、复制、剪切；按需禁用粘贴
    - 显式禁止 ComponentGrid 范围外的右键菜单（默认行为已满足，仅做回归确认）
    - _Requirements: 8.5, 8.7, 8.8, 8.9_
  - [ ] 10.15 前端单元测试：状态机 / DOM 渲染 / 错误展示截断
    - Vitest + jsdom，覆盖：8 个状态转移点、错误超 8 条截断、节点定位命中/未命中/空 `data-unit-name`、文件名格式、双重确认 Modal 流程
    - _Requirements: 2.11, 3.8, 3.9, 3.10, 4.1, 4.7, 4.8, 4.9, 5.1, 5.2, 5.3, 6.4, 7.1, 7.2, 7.3, 7.4_

- [ ] 11. 检查点 - 视图与 JS 集成验证
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 12. 端到端验证（自动化，可选）
  - [ ] 12.1 Playwright E2E：完整工作流 happy path
    - 登录 → 进入 `/Quotation/ImportComponents/{id}` → 上传样例 xlsx → 数据检查 → 目录预览 → 保存方案 → 校验 InfoBar 文案与 BJB 写入条数
    - _Requirements: 1.1, 2.1, 2.2, 3.9, 4.7, 4.8, 5.4, 5.9_
  - [ ] 12.2 Playwright E2E：布局与移动端
    - 桌面：拖动分隔条至 200 / 300 / 超大宽度；切换隐藏/显示
    - 移动端（视口 320px / 360px / 414px）：默认折叠、无横向滚动条、安全区适配
    - _Requirements: 8.1, 8.2, 8.6, 9.2, 9.3, 9.4, 9.5_

- [ ] 13. 最终检查点 - 全量回归
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- 标记 `*` 的任务为可选，可跳过以加快 MVP 节奏；核心实现任务（如 1.1, 1.2, 1.3, 2.1, 3.1–3.4, 5.1, 8.x, 9.1, 10.1–10.14）禁止跳过。
- 每条任务都引用了具体需求条款（如 `5.6` 指 Requirement 5 的第 6 条），便于追溯。
- 8 条 Correctness Properties 各有独立的 PBT 子任务（4.1, 4.2, 4.3, 6.1, 6.2, 6.3, 6.4, 6.5），任务体内显式标注 `Property N` 与 `Validates: Requirements ...`。
- C# PBT 统一使用 `FsCheck.Xunit` 的 `[Property(MaxTest = 100)]`；JS PBT 统一使用 `fast-check` 的 `numRuns: 100`，符合设计"Testing Strategy"的统一配置。
- 实施风险（按 design 文档"风险与未验证项"）：`BJFAT.dqzt` 是否需要写 `proj_status_audit`、Bootstrap Modal 与 Handsontable 焦点冲突、`InternalsVisibleTo` 必须由 1.2 提前完成、移动端 320px 列头折叠 — 在对应任务实施时需逐项确认或设置兜底方案。
- 业务流程合规：本页面属于"第 1 阶段 报价"，本计划不变更 `cur_status`，不涉及跨阶段操作，符合 `business-workflow.md` 约束。

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3", "2.1", "9.1"] },
    { "id": 1, "tasks": ["3.1", "3.2", "3.3", "3.4", "5.1", "8.1"] },
    { "id": 2, "tasks": ["3.5", "3.6", "3.7", "3.8", "4.1", "4.2", "4.3", "8.2", "8.3", "10.1", "10.2", "10.6"] },
    { "id": 3, "tasks": ["6.1", "6.2", "6.3", "6.4", "6.5", "8.4", "10.3", "10.4", "10.5"] },
    { "id": 4, "tasks": ["10.7", "10.8", "10.9", "10.10", "10.11", "10.12", "10.13", "10.14"] },
    { "id": 5, "tasks": ["10.15", "12.1", "12.2"] }
  ]
}
```
