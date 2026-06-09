# Implementation Plan: cabinet-batch-edit

## Overview

本计划将设计文档拆解为四个文件的增量编码任务：

1. 新增 `wwwroot/css/quotation-merge-batch.css`（样式）
2. 修改 `Views/Quotation/MergeExcel.cshtml`（HTML + 引用）
3. 修改 `wwwroot/js/quotation-merge.js`（Bridge + Checkbox + 全选按钮 + 回调）
4. 新增 `wwwroot/js/quotation-merge-batch.js`（批量编辑全部业务逻辑）

每个任务均可独立编写或测试，写入操作全部通过 Handsontable 公开 API（进撤销栈），不引入任何新后端 API。

---

## Tasks

- [x] 1. 新增批量编辑面板 CSS 文件
  - 新建 `wwwroot/css/quotation-merge-batch.css`
  - 按照设计文档 CSS 节实现 `.oa-batch-panel`、`.oa-batch-header`、`.oa-batch-section`、`.oa-batch-section-toggle`、`.oa-unit-checkbox`、`#tree-children-container li`（flex）、`#tree-select-actions` 等规则
  - 末尾写入 `@media (max-width: 768px)` 响应式覆盖
  - 底部 padding 使用 `max(8px, env(safe-area-inset-bottom))` 适配刘海屏
  - 不使用 `!important`，类名统一 `oa-` 前缀
  - _Requirements: 5.1, 5.6_

- [x] 2. 修改 MergeExcel.cshtml — 注入批量编辑面板 HTML 与资源引用
  - [x] 2.1 在 `#tree-children-container` 和清空数据按钮之间插入 `#batch-edit-panel` HTML 块
    - HTML 结构严格按照设计文档《HTML Structure》节，包括：`#batch-selected-count`、壳体类型区域（`#shell-type-select`、`#custom-type-wrapper`、`#custom-type-input`、`#dim-w-input`/`#dim-h-input`/`#dim-d-input`、`#apply-shell-btn`）
    - 初始状态为 `d-none`
    - _Requirements: 5.1, 2.1, 3.1_

  - [x] 2.2 在 `@section Styles` 中追加 `<link>` 引用 `quotation-merge-batch.css`，在 `@section Scripts` 中在 `quotation-merge.js` 之后追加 `<script>` 引用 `quotation-merge-batch.js`
    - 两处引用均使用 `asp-append-version="true"` 缓存破坏
    - _Requirements: 5.1_

- [x] 3. 修改 quotation-merge.js — 暴露 Bridge 对象、注入 Checkbox、添加全选按钮
  - [x] 3.1 在 IIFE 顶部声明 `_clearAllCallbacks` 和 `_treeRebuiltCallbacks` 两个数组；在 `clearAllBtn` 点击事件处理器末尾遍历调用 `_clearAllCallbacks`；在 `buildTreeFromGrid()` 末尾遍历调用 `_treeRebuiltCallbacks`
    - _Requirements: 1.8_

  - [x] 3.2 在 `buildTreeFromGrid()` 生成 `<li>` 的代码段中，于创建 `btn` 之前插入 checkbox（`<input type="checkbox" class="oa-unit-checkbox form-check-input me-2 flex-shrink-0">`），`value` 与 `data-unit-no` 均设为 `unit.unitNo`
    - _Requirements: 1.1, 1.2_

  - [x] 3.3 在 `buildTreeFromGrid()` 渲染节点列表前，检查 `#tree-select-actions` 是否已存在，若不存在则在 `treeChildrenContainer` 上方插入包含"全选"和"取消全选"两个按钮的 `<div id="tree-select-actions">`
    - _Requirements: 1.6, 1.7, 1.9_

  - [x] 3.4 在 IIFE 末尾挂载 `window.__mergeBridge` 对象，暴露 `getHot`、`getUnitRowMap`、`isDataLoaded`、`setMessage`、`onClearAll`、`onTreeRebuilt` 六个方法
    - _Requirements: 1.8_

- [x] 4. 检查点 — 确认 CSS 和静态 HTML 无误
  - 确认 `quotation-merge-batch.css` 文件存在且类名无拼写错误
  - 确认 `MergeExcel.cshtml` 中批量面板 HTML 的所有 `id` 与设计文档一致
  - 确认 `quotation-merge.js` 中 Bridge 对象已挂载，callback 数组已声明
  - 若发现问题，回退至对应任务修改后再继续

- [x] 5. 新增 quotation-merge-batch.js — 模块骨架与初始化
  - 新建 `wwwroot/js/quotation-merge-batch.js`，写入 IIFE 结构（`'use strict'`）
  - 声明模块级状态：`selectedUnits`（Set）、`bridge`、全部 DOM 引用变量
  - 实现 `init()` 函数：获取 `window.__mergeBridge`、缓存所有 DOM 引用、绑定静态元素事件（`shellTypeSelect.change`、`applyShellBtn.click`、折叠/展开按钮）、注册 `bridge.onClearAll` 和 `bridge.onTreeRebuilt` 回调
  - 在脚本末尾根据 `document.readyState` 决定直接调用 `init()` 或注册 `DOMContentLoaded`
  - _Requirements: 5.1, 5.2_

- [x] 6. 实现 checkbox 状态管理与面板可见性
  - [x] 6.1 实现 `onCheckboxChange(e)`：根据 `e.target.checked` 向 `selectedUnits` 中 add/delete `unitNo`，然后调用 `updatePanelVisibility()`
    - _Requirements: 1.2, 1.4, 1.5_

  - [ ]* 6.2 为 Property 1 编写属性测试（checkbox toggle 是对合）
    - **Property 1: Checkbox toggle is an involution**
    - **Validates: Requirements 1.2**

  - [x] 6.3 实现 `onSelectAll()`：遍历所有 `.oa-unit-checkbox`，设 `checked = true` 并向 `selectedUnits` add，调用 `updatePanelVisibility()`
    - _Requirements: 1.3, 1.6_

  - [ ]* 6.4 为 Property 2 编写属性测试（全选覆盖率）
    - **Property 2: Select-all achieves complete coverage**
    - **Validates: Requirements 1.3, 1.6**

  - [x] 6.5 实现 `onDeselectAll()`：遍历 checkboxes 设 `checked = false`，`selectedUnits.clear()`，调用 `updatePanelVisibility()`
    - _Requirements: 1.7_

  - [ ]* 6.6 为 Property 3 编写属性测试（取消全选清空集合）
    - **Property 3: Deselect-all empties the selection**
    - **Validates: Requirements 1.7**

  - [x] 6.7 实现 `updatePanelVisibility()`：切换 `panelEl` 的 `d-none` class，更新 `batchCountEl.textContent`
    - _Requirements: 1.4, 1.5_

  - [ ]* 6.8 为 Property 4 编写属性测试（面板可见性与选中数一致）
    - **Property 4: Panel visibility mirrors selection count**
    - **Validates: Requirements 1.4, 1.5**

  - [x] 6.9 实现 `rebindCheckboxEvents()`：重新绑定所有 `.oa-unit-checkbox` 的 change 事件，恢复 `selectedUnits` 中已有单元号的 `checked` 状态；同时重新绑定 `#select-all-units-btn` 和 `#deselect-all-units-btn` 的 `onclick`
    - _Requirements: 1.1, 1.6, 1.7_

  - [x] 6.10 实现 `onClearAllReset()`：`selectedUnits.clear()`，调用 `updatePanelVisibility()`
    - _Requirements: 1.8_

  - [x] 6.11 实现 `onTreeRebuilt()`：调用 `rebindCheckboxEvents()` 然后 `updatePanelVisibility()`
    - _Requirements: 1.1_

- [x] 7. 实现折叠/展开控件
  - 实现 `onSectionToggle(e)`：读取 `btn.dataset.target`，切换 body 的 `d-none`，翻转 `aria-expanded`，切换 `oa-toggle-icon` 在 `bi-chevron-down` 和 `bi-chevron-right` 之间
  - 实现 `expandAllSections()`：移除所有 section body 的 `d-none`，将所有 toggle 按钮 `aria-expanded` 设为 `true`，图标恢复 `bi-chevron-down`
  - _Requirements: 5.3_

- [x] 8. 实现 `buildSpecString` 纯函数与壳体类型下拉联动
  - [x] 8.1 实现 `onShellTypeChange()`：当选项为 `__custom__` 时显示 `customTypeWrapper`，否则隐藏并清空 `customTypeInput`
    - _Requirements: 2.1, 2.2_

  - [x] 8.2 实现 `buildSpecString(type, w, h, d)` 纯函数，严格按设计文档规格表处理 4 种输入组合，均空时返回 `null`
    - _Requirements: 2.6, 3.2, 3.3, 3.4, 3.5, 3.6_

  - [ ]* 8.3 为 Property 5 编写属性测试（buildSpecString 输出格式正确）
    - **Property 5: `buildSpecString` produces correct format for all input combinations**
    - **Validates: Requirements 2.6, 3.2, 3.3, 3.4, 3.5, 3.6**

- [x] 9. 实现 `findCabinetBlock` 与 `upsertShellRow`
  - [x] 9.1 实现 `findCabinetBlock(hot, unitNo)` 纯函数：遍历 `hot.getData()`，定位 `col[1] === unitNo` 的 `headerRow`，找下一个 `col[1]` 非空行确定 `endRow`，若无则取末行
    - _Requirements: 2.3, 2.4_

  - [x] 9.2 实现 `upsertShellRow(hot, headerRow, endRow, specString)`：在块内查找 `col[2]==="壳体"` 的行；若找到则 `setDataAtCell` 更新 `col[3]`；若未找到则 `alter('insert_row_above', headerRow+1, 1)` 再批量 `setDataAtCell` 写入 5 列
    - _Requirements: 2.3, 2.4, 2.5_

  - [ ]* 9.3 为 Property 6 编写属性测试（upsertShellRow 保持"恰好一行壳体"不变式）
    - **Property 6: `upsertShellRow` maintains exactly-one-shell-row invariant**
    - **Validates: Requirements 2.3, 2.4, 2.5**

- [x] 10. 实现 `applyShellType` 业务函数
  - 实现 `applyShellType()`：依次校验 `isDataLoaded`、`selectedUnits.size > 0`、壳体类型不为空（含自定义非空）、尺寸验证（任一有值则全部 1–9999 整数）、`buildSpecString` 不返回 null；按 `headerRow` 降序排列选中节点；循环调用 `upsertShellRow`；成功后调用 `bridge.setMessage` 写入摘要；`try/catch` 包裹写入循环
  - _Requirements: 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 3.2, 3.3, 3.4, 3.5, 3.6, 4.1, 4.2_

- [ ]* 11. 为 Property 7 编写属性测试（批量壳体操作成功后必定发出 setMessage）
  - **Property 7: Batch shell operation always emits a result message**
  - **Validates: Requirements 4.1**

- [x] 12. 最终检查点 — 确保所有测试通过，向用户询问是否有疑问
  - 运行全部单元测试与属性测试（若已配置测试框架）
  - 确认四个目标文件均已正确修改或新建：
    - `wwwroot/css/quotation-merge-batch.css`（新增）
    - `Views/Quotation/MergeExcel.cshtml`（修改）
    - `wwwroot/js/quotation-merge.js`（修改）
    - `wwwroot/js/quotation-merge-batch.js`（新增）
  - 确认 `window.__mergeBridge` 挂载顺序正确（merge.js 先于 batch.js 执行）
  - 确认所有 Handsontable 写操作均使用 `alter` / `setDataAtCell` API（进撤销栈）
  - 向用户报告完成情况，询问是否有疑问

---

## Notes

- 标注 `*` 的子任务为可选属性测试，可跳过以加快 MVP 进度
- 属性测试推荐使用 [fast-check](https://github.com/dubzzz/fast-check)，无需真实 DOM，只需轻量 Handsontable mock（记录 `alter`/`setDataAtCell` 调用即可）
- 批量写入均按行号降序处理，消除行号漂移，详见设计文档《批量操作中的行号漂移处理策略》
- 所有错误通过 `bridge.setMessage(msg, true)` 显示到 `#page-info-bar`，不弹 alert
- CSS 前缀统一为 `oa-`，不使用 `!important`，响应式断点 `≤768px`
- 任务 3 和任务 5–10 存在依赖关系，任务 2 可与任务 1 并行，任务 1 可与任务 3 并行

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1", "2.1", "2.2", "3.1", "3.2", "3.3", "3.4"] },
    { "id": 1, "tasks": ["5"] },
    { "id": 2, "tasks": ["6.1", "6.3", "6.5", "6.7", "6.9", "6.10", "6.11", "7", "8.1", "8.2", "9.1"] },
    { "id": 3, "tasks": ["6.2", "6.4", "6.6", "6.8", "8.3", "9.2"] },
    { "id": 4, "tasks": ["9.3", "10"] },
    { "id": 5, "tasks": ["11"] },
    { "id": 6, "tasks": ["12"] }
  ]
}
```
