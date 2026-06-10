# Design Document: cabinet-batch-edit

## Overview

本功能是对 `/Quotation/MergeExcel` 页面的纯前端增强，为报价员提供批量编辑多个控制柜属性的能力。在现有目录树节点上增加勾选框，勾选后在目录树下方展示批量编辑面板，支持壳体类型/尺寸批量写入。所有写操作通过 Handsontable 的 `alter` / `setDataAtCell` API 完成，自动进入撤销栈，不引入任何新后端 API。

### 核心流程

```
用户合并 Excel → 目录树显示节点(带 checkbox)
  ↓ 勾选 ≥1 个节点
批量编辑面板显示
  ↓ 填写壳体类型/尺寸 → 点击"应用壳体类型"
    → 计算 spec 字符串 → 遍历选中节点 → upsert 壳体行
  ↓ 每次写入后刷新目录树 + 更新信息栏
```

---

## Architecture

### 文件变更清单

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `Views/Quotation/MergeExcel.cshtml` | 修改 | 新增批量编辑面板 HTML；新增 `<script>` 引用 `quotation-merge-batch.js` |
| `wwwroot/js/quotation-merge.js` | 修改 | `buildTreeFromGrid()` 注入 checkbox；暴露 `hot`/`unitRowMap`/`hasMergedData`/`setMessage` 供 batch 模块使用；在 `clearAllBtn` 事件中通知 batch 模块重置 |
| `wwwroot/js/quotation-merge-batch.js` | 新增 | 批量编辑的全部业务逻辑（独立 IIFE） |
| `wwwroot/css/quotation-merge-batch.css` | 新增 | 批量编辑面板专用样式（`oa-batch-*` 前缀） |

### 模块间通信方案

`quotation-merge.js` 和 `quotation-merge-batch.js` 都是 IIFE，通过 `window.__mergeBridge` 对象实现松耦合通信：

```
window.__mergeBridge = {
  getHot()           → 返回 Handsontable 实例
  getUnitRowMap()    → 返回 unitRowMap（Map<unitNo, rowIndex>）
  isDataLoaded()     → 返回 hasMergedData
  setMessage(msg, isError) → 调用 infoBarEl 更新
  onClearAll(fn)     → 注册"清空数据"回调
  onTreeRebuilt(fn)  → 注册"目录树重建完成"回调
}
```

`quotation-merge.js` 在 IIFE 末尾将该对象挂到 `window`；`quotation-merge-batch.js` 在 `DOMContentLoaded` 后读取该对象完成初始化。

---

## Components and Interfaces

### 1. 修改 `quotation-merge.js`

#### 1.1 暴露 Bridge 对象

在现有 IIFE 末尾追加：

```javascript
window.__mergeBridge = {
  getHot: () => hot,
  getUnitRowMap: () => unitRowMap,
  isDataLoaded: () => hasMergedData,
  setMessage,
  onClearAll: (fn) => _clearAllCallbacks.push(fn),
  onTreeRebuilt: (fn) => _treeRebuiltCallbacks.push(fn),
};
```

内部维护回调数组 `_clearAllCallbacks` 和 `_treeRebuiltCallbacks`，分别在 `clearAllBtn` 事件和 `buildTreeFromGrid()` 末尾遍历调用。

#### 1.2 修改 `buildTreeFromGrid()` — 注入 Checkbox

在生成每个 `<li>` 的代码段中，在现有 `<button>` 前插入 checkbox：

```javascript
// 在创建 li 之后，btn 之前插入 checkbox
const cb = document.createElement('input');
cb.type = 'checkbox';
cb.className = 'oa-unit-checkbox form-check-input me-2 flex-shrink-0';
cb.value = unit.unitNo;
cb.setAttribute('data-unit-no', unit.unitNo);
li.appendChild(cb);
li.appendChild(btn);
```

`li` 需改为 `display: flex; align-items: center` 以保持排版对齐。

#### 1.3 新增"全选/取消全选"按钮区域

`buildTreeFromGrid()` 在渲染节点前，先在 `treeChildrenContainer` 父容器（`#price-tree-pane .oa-card`）的目录树列表上方插入一个按钮行（若已存在则跳过）：

```html
<div id="tree-select-actions" class="d-flex gap-2 mb-2">
  <button id="select-all-units-btn" type="button" class="btn btn-sm btn-outline-secondary">全选</button>
  <button id="deselect-all-units-btn" type="button" class="btn btn-sm btn-outline-secondary">取消全选</button>
</div>
```

按钮事件由 `quotation-merge-batch.js` 中绑定（通过 `onTreeRebuilt` 回调重新绑定）。

---

### 2. 新增 `quotation-merge-batch.js`

整体结构为一个 IIFE，在文档加载完成后通过 `window.__mergeBridge` 获取依赖。

#### 2.1 模块状态（Module State）

```javascript
// 选中的控制柜单元号集合
let selectedUnits = new Set();

// DOM 引用（init() 时赋值）
let bridge, panelEl, shellTypeSelect, customTypeInput,
    wInput, hInput, dInput,
    panelSelectedCount;
```

#### 2.2 核心公开函数

| 函数签名 | 职责 |
|----------|------|
| `init()` | 获取 bridge、缓存 DOM 引用、绑定事件 |
| `onCheckboxChange(e)` | 处理 checkbox change，更新 `selectedUnits`，刷新面板可见性 |
| `onSelectAll()` | 全选所有 `.oa-unit-checkbox` |
| `onDeselectAll()` | 取消全选 |
| `onClearAllReset()` | `selectedUnits.clear()`，隐藏面板 |
| `onTreeRebuilt()` | 重新绑定新 checkbox 的事件，保留已选状态 |
| `applyShellType()` | 读取壳体类型+尺寸 → 验证 → 构造 spec 字符串 → 批量写入壳体行 |
| `buildSpecString(type, w, h, d)` | 纯函数：组合 spec 字符串 |
| `findCabinetBlock(hot, unitNo)` | 纯函数：返回 `{headerRow, endRow}` |
| `upsertShellRow(hot, headerRow, endRow, spec)` | 执行 upsert 壳体行（undo 兼容） |
| `updatePanelVisibility()` | 根据 `selectedUnits.size` 显示/隐藏面板，更新计数 |
| `rebindCheckboxEvents()` | 重新绑定 DOM 中所有 `.oa-unit-checkbox` 的事件 |

---

## Data Models

### Cabinet Block（控制柜块）

运行时数据结构，不持久化：

```
CabinetBlock {
  headerRow: number    // 标题行行号（row[1] 非空）
  endRow:    number    // 块末尾行号（下一标题行前一行，或最后一行）
}
```

通过 `findCabinetBlock(hot, unitNo)` 从当前 `hot.getData()` 实时计算。

### Shell Spec String（壳体规格字符串）

由 `buildSpecString(type, w, h, d)` 纯函数生成，规则：

| type | w/h/d | 结果 |
|------|-------|------|
| 非空 | 三项均有效 | `"{type} {w}W×{h}H×{d}Dmm"` |
| 非空 | 均无效/空 | `"{type}"` |
| 空 | 三项均有效 | `"{w}W×{h}H×{d}Dmm"` |
| 空 | 均无效/空 | `null`（验证拒绝） |

## Handsontable Manipulation Patterns

### 原则

所有表格写操作必须通过 Handsontable 的公开 API，以确保进入内置撤销栈（Ctrl+Z 可逆）：

- 插入行：`hot.alter('insert_row_above', rowIndex, count)`
- 设置单元格：`hot.setDataAtCell(row, col, value)` 或批量 `hot.setDataAtCell([[row, col, val], ...])`
- **禁止**直接修改 `hot.getData()` 返回的数组（不进入撤销栈）

### Pattern A：Upsert 壳体行

```
findCabinetBlock(hot, unitNo)
  → {headerRow, endRow}

// 判断是否已有壳体行（col[2] === "壳体"）
shellRow = null
for r = headerRow+1 to endRow:
  if hot.getDataAtCell(r, 2) === "壳体":
    shellRow = r; break

if shellRow !== null:
  // UPDATE: 只写 col[3]
  hot.setDataAtCell(shellRow, 3, specString)
else:
  // INSERT: 在 headerRow+1 处插入
  hot.alter('insert_row_above', headerRow + 1, 1)
  hot.setDataAtCell([
    [headerRow + 1, 2, "壳体"],
    [headerRow + 1, 3, specString],
    [headerRow + 1, 4, "0.0"],
    [headerRow + 1, 5, "1"],
    [headerRow + 1, 7, "0.0"],
  ])
```

> **重要**：`alter('insert_row_above', ...)` 执行后，原 `headerRow` 以下的所有行号 +1，后续操作需重新调用 `findCabinetBlock()` 或对行号做偏移修正（批量处理时从最后一个控制柜向前处理，避免行号漂移）。

### 批量操作中的行号漂移处理策略

批量遍历 `selectedUnits` 时，**按行号从大到小排序**（即先处理表格靠后的控制柜）。每处理完一个柜块，前面柜块的行号不受影响，后面的行号也不会被破坏，从而消除行号漂移问题。

```javascript
// 按 startRow 降序排列选中节点
const sortedUnits = [...selectedUnits]
  .map(unitNo => ({ unitNo, ...findCabinetBlock(hot, unitNo) }))
  .filter(u => u.headerRow !== -1)
  .sort((a, b) => b.headerRow - a.headerRow);

for (const unit of sortedUnits) {
  upsertShellRow(hot, unit.headerRow, unit.endRow, specString);
}
```

---

## HTML Structure

### MergeExcel.cshtml 修改点

#### 1. 在 `#price-tree-pane .oa-card` 内部追加批量编辑面板

在 `<ul id="tree-children-container">` 和 `<div class="mt-3">（清空按钮）` 之间插入：

```html
<!-- 全选/取消全选 按钮行（由 JS 动态插入，此处仅占位注释） -->

<!-- 批量编辑面板 -->
<div id="batch-edit-panel" class="oa-batch-panel mt-3 d-none">
  <div class="oa-batch-header d-flex align-items-center justify-content-between mb-2">
    <span class="small fw-semibold text-primary">
      <i class="bi bi-pencil-square me-1"></i>批量编辑
      （已选 <span id="batch-selected-count">0</span> 个）
    </span>
  </div>

  <!-- 壳体类型/尺寸 区域 -->
  <div class="oa-batch-section">
    <button type="button" class="oa-batch-section-toggle btn btn-sm btn-link px-0 py-0 mb-1 text-start w-100"
            data-target="shell-section-body" aria-expanded="true">
      <i class="bi bi-chevron-down me-1 oa-toggle-icon"></i>壳体类型 / 尺寸
    </button>
    <div id="shell-section-body" class="oa-batch-section-body">
      <div class="mb-2">
        <label class="form-label small mb-1">壳体类型</label>
        <select id="shell-type-select" class="form-select form-select-sm">
          <option value="">-- 请选择 --</option>
          <option value="操作箱">操作箱</option>
          <option value="控制柜">控制柜</option>
          <option value="操作盒">操作盒</option>
          <option value="操作台">操作台</option>
          <option value="__custom__">其他（自定义）</option>
        </select>
      </div>
      <div id="custom-type-wrapper" class="mb-2 d-none">
        <label class="form-label small mb-1">自定义类型名称（最多10字）</label>
        <input id="custom-type-input" type="text" class="form-control form-control-sm" maxlength="10" placeholder="请输入壳体类型" />
      </div>
      <div class="mb-2">
        <label class="form-label small mb-1">柜体尺寸（mm，可选）</label>
        <div class="d-flex gap-1">
          <input id="dim-w-input" type="number" class="form-control form-control-sm" min="1" max="9999" placeholder="宽W" />
          <input id="dim-h-input" type="number" class="form-control form-control-sm" min="1" max="9999" placeholder="高H" />
          <input id="dim-d-input" type="number" class="form-control form-control-sm" min="1" max="9999" placeholder="深D" />
        </div>
      </div>
      <button id="apply-shell-btn" type="button" class="btn btn-sm btn-primary w-100">
        <i class="bi bi-check2-circle me-1"></i>应用壳体类型
      </button>
    </div>
  </div>
</div>
```

#### 2. 新增 CSS 和 JS 引用

```html
@section Styles {
  <link rel="stylesheet" href="~/css/quotation-merge-batch.css" asp-append-version="true" />
}

@section Scripts {
  <script src="~/lib/handsontable/handsontable.full.min.js"></script>
  <script src="~/js/quotation-merge.js" asp-append-version="true"></script>
  <script src="~/js/quotation-merge-batch.js" asp-append-version="true"></script>
}
```

---

## JS Module Design

### `quotation-merge-batch.js` 详细结构

```javascript
(() => {
  'use strict';

  // ── 状态 ──────────────────────────────────────────
  let selectedUnits = new Set();   // Set<string> of unitNo
  let bridge = null;

  // ── DOM 引用 ──────────────────────────────────────
  let panelEl, batchCountEl,
      shellTypeSelect, customTypeWrapper, customTypeInput,
      dimW, dimH, dimD,
      applyShellBtn;

  // ── 初始化 ────────────────────────────────────────
  function init() {
    bridge = window.__mergeBridge;
    if (!bridge) return;

    // 缓存 DOM
    panelEl           = document.getElementById('batch-edit-panel');
    batchCountEl      = document.getElementById('batch-selected-count');
    shellTypeSelect   = document.getElementById('shell-type-select');
    customTypeWrapper = document.getElementById('custom-type-wrapper');
    customTypeInput   = document.getElementById('custom-type-input');
    dimW              = document.getElementById('dim-w-input');
    dimH              = document.getElementById('dim-h-input');
    dimD              = document.getElementById('dim-d-input');
    applyShellBtn     = document.getElementById('apply-shell-btn');

    if (!panelEl) return;

    // 绑定静态元素事件
    shellTypeSelect.addEventListener('change', onShellTypeChange);
    applyShellBtn.addEventListener('click', applyShellType);

    // 折叠/展开
    document.querySelectorAll('.oa-batch-section-toggle').forEach(btn => {
      btn.addEventListener('click', onSectionToggle);
    });

    // Bridge 回调
    bridge.onClearAll(onClearAllReset);
    bridge.onTreeRebuilt(onTreeRebuilt);
  }

  // ── Checkbox 管理 ────────────────────────────────
  function rebindCheckboxEvents() {
    document.querySelectorAll('.oa-unit-checkbox').forEach(cb => {
      cb.removeEventListener('change', onCheckboxChange);
      cb.addEventListener('change', onCheckboxChange);
      // 恢复已选状态
      cb.checked = selectedUnits.has(cb.value);
    });
    // 重新绑定全选/取消全选按钮
    const selAll   = document.getElementById('select-all-units-btn');
    const deselAll = document.getElementById('deselect-all-units-btn');
    if (selAll)   selAll.onclick   = onSelectAll;
    if (deselAll) deselAll.onclick = onDeselectAll;
  }

  function onCheckboxChange(e) {
    const unitNo = e.target.value;
    if (e.target.checked) selectedUnits.add(unitNo);
    else selectedUnits.delete(unitNo);
    updatePanelVisibility();
  }

  function onSelectAll() {
    document.querySelectorAll('.oa-unit-checkbox').forEach(cb => {
      cb.checked = true;
      selectedUnits.add(cb.value);
    });
    updatePanelVisibility();
  }

  function onDeselectAll() {
    document.querySelectorAll('.oa-unit-checkbox').forEach(cb => {
      cb.checked = false;
    });
    selectedUnits.clear();
    updatePanelVisibility();
  }

  function onClearAllReset() {
    selectedUnits.clear();
    updatePanelVisibility();
  }

  function onTreeRebuilt() {
    rebindCheckboxEvents();
    updatePanelVisibility();
  }

  function updatePanelVisibility() {
    if (!panelEl) return;
    const count = selectedUnits.size;
    panelEl.classList.toggle('d-none', count === 0);
    if (batchCountEl) batchCountEl.textContent = count;
  }

  // ── 折叠/展开 ────────────────────────────────────
  function onSectionToggle(e) {
    const btn = e.currentTarget;
    const targetId = btn.dataset.target;
    const body = document.getElementById(targetId);
    if (!body) return;
    const isExpanded = btn.getAttribute('aria-expanded') === 'true';
    body.classList.toggle('d-none', isExpanded);
    btn.setAttribute('aria-expanded', String(!isExpanded));
    const icon = btn.querySelector('.oa-toggle-icon');
    if (icon) {
      icon.classList.toggle('bi-chevron-down', !isExpanded);
      icon.classList.toggle('bi-chevron-right', isExpanded);
    }
  }

  function expandAllSections() {
    document.querySelectorAll('.oa-batch-section-toggle').forEach(btn => {
      const targetId = btn.dataset.target;
      const body = document.getElementById(targetId);
      if (body) body.classList.remove('d-none');
      btn.setAttribute('aria-expanded', 'true');
      const icon = btn.querySelector('.oa-toggle-icon');
      if (icon) {
        icon.classList.add('bi-chevron-down');
        icon.classList.remove('bi-chevron-right');
      }
    });
  }

  // ── 壳体类型下拉 ─────────────────────────────────
  function onShellTypeChange() {
    const isCustom = shellTypeSelect.value === '__custom__';
    customTypeWrapper.classList.toggle('d-none', !isCustom);
    if (!isCustom) customTypeInput.value = '';
  }

  // ── buildSpecString 纯函数 ───────────────────────
  function buildSpecString(type, w, h, d) {
    const hasType = type && type.trim().length > 0;
    const wv = parseInt(w, 10), hv = parseInt(h, 10), dv = parseInt(d, 10);
    const hasDims = Number.isFinite(wv) && wv >= 1 && wv <= 9999
                 && Number.isFinite(hv) && hv >= 1 && hv <= 9999
                 && Number.isFinite(dv) && dv >= 1 && dv <= 9999;
    if (!hasType && !hasDims) return null;
    if (hasType && hasDims) return `${type.trim()} ${wv}W×${hv}H×${dv}Dmm`;
    if (hasType) return type.trim();
    return `${wv}W×${hv}H×${dv}Dmm`;
  }

  // ── findCabinetBlock 纯函数 ──────────────────────
  function findCabinetBlock(hot, unitNo) {
    const data = hot.getData();
    let headerRow = -1, endRow = -1;
    for (let i = 0; i < data.length; i++) {
      const cell = (data[i][1] || '').trim();
      if (cell === unitNo) { headerRow = i; continue; }
      if (headerRow !== -1 && cell !== '') { endRow = i - 1; return { headerRow, endRow }; }
    }
    if (headerRow !== -1) endRow = data.length - 1;
    return { headerRow, endRow };
  }

  // ── Upsert 壳体行 ────────────────────────────────
  function upsertShellRow(hot, headerRow, endRow, specString) {
    // 查找现有壳体行
    let shellRow = -1;
    for (let r = headerRow + 1; r <= endRow; r++) {
      if ((hot.getDataAtCell(r, 2) || '').trim() === '壳体') {
        shellRow = r; break;
      }
    }
    if (shellRow !== -1) {
      hot.setDataAtCell(shellRow, 3, specString);
    } else {
      const insertAt = headerRow + 1;
      hot.alter('insert_row_above', insertAt, 1);
      hot.setDataAtCell([
        [insertAt, 2, '壳体'],
        [insertAt, 3, specString],
        [insertAt, 4, '0.0'],
        [insertAt, 5, '1'],
        [insertAt, 7, '0.0'],
      ]);
    }
  }

  // ── 应用壳体类型 ─────────────────────────────────
  function applyShellType() {
    if (!bridge || !bridge.isDataLoaded()) {
      bridge && bridge.setMessage('请先合并 Excel 文件', true); return;
    }
    if (selectedUnits.size === 0) {
      bridge.setMessage('请先选中至少一个控制柜节点', true); return;
    }

    // 读取并验证壳体类型
    let shellType = '';
    if (shellTypeSelect.value === '__custom__') {
      shellType = (customTypeInput.value || '').trim();
      if (!shellType) {
        bridge.setMessage('请填写自定义壳体类型名称', true); return;
      }
    } else {
      shellType = shellTypeSelect.value;
    }

    // 读取尺寸（可选）
    const w = dimW.value, h = dimH.value, d = dimD.value;
    // 如果任一尺寸有值，则全部验证
    const anyDim = w || h || d;
    if (anyDim) {
      const wv = parseInt(w, 10), hv = parseInt(h, 10), dv = parseInt(d, 10);
      const valid = (v, el) => {
        const ok = Number.isFinite(v) && v >= 1 && v <= 9999;
        el.classList.toggle('is-invalid', !ok);
        return ok;
      };
      const wOk = valid(wv, dimW), hOk = valid(hv, dimH), dOk = valid(dv, dimD);
      if (!wOk || !hOk || !dOk) {
        bridge.setMessage('尺寸必须为正整数（1–9999）', true); return;
      }
    } else {
      [dimW, dimH, dimD].forEach(el => el.classList.remove('is-invalid'));
    }

    const specString = buildSpecString(shellType, w, h, d);
    if (!specString) {
      bridge.setMessage('请先选择或填写壳体类型', true); return;
    }

    const hot = bridge.getHot();
    // 按行号从大到小排序，避免行号漂移
    const sortedUnits = [...selectedUnits]
      .map(unitNo => ({ unitNo, ...findCabinetBlock(hot, unitNo) }))
      .filter(u => u.headerRow !== -1)
      .sort((a, b) => b.headerRow - a.headerRow);

    if (sortedUnits.length === 0) {
      bridge.setMessage('未找到选中控制柜对应的表格行，请确认数据已合并', true); return;
    }

    try {
      for (const unit of sortedUnits) {
        upsertShellRow(hot, unit.headerRow, unit.endRow, specString);
      }
      bridge.setMessage(`已为 ${sortedUnits.length} 个控制柜写入壳体行（规格：${specString}）`, false);
    } catch (err) {
      bridge.setMessage(`写入壳体行失败：${err.message || err}`, true);
    }
  }

  // ── 启动 ─────────────────────────────────────────
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
```

---

## Data Flow

### Flow 1：壳体类型写入

```
用户操作                          JS 执行
─────────────────────────────────────────────────────
选择壳体类型 + 填写尺寸（可选）
点击"应用壳体类型"
                                 applyShellType()
                                   ↓ 读取 shellType, w, h, d
                                   ↓ buildSpecString() → specString
                                   ↓ [...selectedUnits].sort(desc by headerRow)
                                   ↓ for each unit:
                                       findCabinetBlock(hot, unitNo)
                                       upsertShellRow(hot, headerRow, endRow, specString)
                                         ↓ 查找 col[2]==="壳体" 的行
                                         ↓ 找到 → setDataAtCell(shellRow, 3, spec)
                                         ↓ 未找到 → alter('insert_row_above', headerRow+1)
                                                    → setDataAtCell([...cells])
                                   ↓ bridge.setMessage(摘要)
                                   ↓ (hot afterChange hook) → scheduleTreeRebuild()
                                   ↓ 目录树刷新 → bridge.onTreeRebuilt callbacks
                                   ↓ rebindCheckboxEvents() 恢复选中状态
```

---

## Error Handling

| 场景 | 处理方式 |
|------|----------|
| 未合并数据就点击操作按钮 | 提示"请先合并 Excel 文件"，不执行写入 |
| 未选中任何节点就点击操作按钮 | 提示"请先选中至少一个控制柜节点" |
| 壳体类型和尺寸均为空 | 提示"请先选择或填写壳体类型" |
| 选择"其他"但自定义文本为空 | 提示"请填写自定义壳体类型名称" |
| 尺寸输入非整数或超出 1–9999 | 输入框标红（`is-invalid`），提示"尺寸必须为正整数（1–9999）" |
| `findCabinetBlock` 返回 `headerRow=-1` | 跳过该节点，仅统计有效处理数量；若全部跳过则提示"未找到选中控制柜对应的表格行" |
| JS 运行时异常 | `try/catch` 捕获，提示"写入XX失败：{errorMessage}"，不写入任何行 |

所有错误信息通过 `bridge.setMessage(msg, true)` 显示在页面信息栏（`#page-info-bar`）。

---

## Testing Strategy

### 单元测试（Example-based）

- `buildSpecString()` 各输入组合返回正确字符串（type+dims、type-only、dims-only、均空）
- `findCabinetBlock()` 在各种数据布局下正确返回 `{headerRow, endRow}`
- 验证逻辑：无效尺寸各返回正确错误状态

### 集成测试（Example-based）

- 在模拟 Handsontable 数据上执行 upsert 壳体行，验证结果行位置和内容
- 多柜块同时操作，验证行号漂移处理正确（后处理前面的柜块数据不受影响）

### Property-Based Tests

见下节《Correctness Properties》。

---

## Mobile / Responsive Considerations

- 批量编辑面板宽度继承父容器（`#price-tree-pane`），最小 220px，最大 60vw，无固定像素宽度
- 柜体尺寸三个输入框使用 `d-flex gap-1`，`flex: 1` 均分，`min-width: 0` 防止溢出
- 折叠/展开按钮最小触摸区域 ≥ 44×44px（Bootstrap btn-sm 已满足）
- 不使用绝对定位或 `position: fixed` 浮层，面板随树面板滚动
- `env(safe-area-inset-bottom)` 在 `.oa-batch-panel` 底部 padding 中引用：
  ```css
  .oa-batch-panel { padding-bottom: max(8px, env(safe-area-inset-bottom)); }
  ```

---

## CSS: `quotation-merge-batch.css`

```css
/* 批量编辑面板 */
.oa-batch-panel {
  border-top: 1px solid #dee2e6;
  padding-top: 0.75rem;
  padding-bottom: max(8px, env(safe-area-inset-bottom));
}

.oa-batch-header {
  font-size: 0.85rem;
}

.oa-batch-section + .oa-batch-section {
  border-top: 1px dashed #dee2e6;
  padding-top: 0.5rem;
}

.oa-batch-section-toggle {
  font-size: 0.8rem;
  color: #6c757d;
  text-decoration: none;
}
.oa-batch-section-toggle:hover { color: #0d6efd; }

/* checkbox 与 tree-node-link 同行对齐 */
.oa-unit-checkbox {
  cursor: pointer;
  flex-shrink: 0;
}

/* tree li 改为 flex */
#tree-children-container li {
  display: flex;
  align-items: center;
}

/* 全选/取消全选 按钮区域 */
#tree-select-actions {
  border-bottom: 1px solid #dee2e6;
  padding-bottom: 0.5rem;
  margin-bottom: 0.5rem;
}

@media (max-width: 768px) {
  .oa-batch-panel { padding-bottom: max(12px, env(safe-area-inset-bottom)); }
}
```

---

## Correctness Properties


*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

The following properties focus on the pure-function and data-manipulation logic in `quotation-merge-batch.js`. They are testable with a property-based testing library (e.g., fast-check for JavaScript) without requiring a real Handsontable DOM instance — a lightweight mock that records `alter` / `setDataAtCell` calls is sufficient.

### Property 1: Checkbox toggle is an involution

*For any* `selectedUnits` Set and any `unitNo`, toggling the checkbox twice (check then uncheck) results in `selectedUnits` being identical to its state before either toggle.

**Validates: Requirements 1.2**

---

### Property 2: Select-all achieves complete coverage

*For any* list of N cabinet unit identifiers rendered as checkboxes, calling `onSelectAll()` results in `selectedUnits.size === N` and every checkbox element's `checked` property being `true`.

**Validates: Requirements 1.3, 1.6**

---

### Property 3: Deselect-all empties the selection

*For any* non-empty `selectedUnits` Set (regardless of prior selection state), calling `onDeselectAll()` results in `selectedUnits.size === 0` and every checkbox element's `checked` property being `false`.

**Validates: Requirements 1.7**

---

### Property 4: Panel visibility mirrors selection count

*For any* set of unit checkbox interactions, the batch edit panel is visible (`d-none` absent) if and only if `selectedUnits.size > 0`, and the displayed count text always equals `selectedUnits.size`.

**Validates: Requirements 1.4, 1.5**

---

### Property 5: `buildSpecString` produces correct format for all input combinations

*For any* non-empty type string `t` and any integers `w`, `h`, `d` each in `[1, 9999]`:
- `buildSpecString(t, w, h, d)` returns `"{t} {w}W×{h}H×{d}Dmm"`
- `buildSpecString(t, '', '', '')` returns `"{t}"`
- `buildSpecString('', w, h, d)` returns `"{w}W×{h}H×{d}Dmm"`
- `buildSpecString('', '', '', '')` returns `null`

*For any* dimension value `v` outside `[1, 9999]` (including 0, negative, or non-integer), its inclusion in the dims arguments causes the full-dims branch to not produce a valid spec string.

**Validates: Requirements 2.6, 3.2, 3.3, 3.4, 3.5, 3.6**

---

### Property 6: `upsertShellRow` maintains exactly-one-shell-row invariant

*For any* simulated grid data representing a cabinet block (with or without a pre-existing row where `col[2] === "壳体"`), after calling `upsertShellRow(hot, headerRow, endRow, specString)`:
- There is exactly one row in the block where `col[2] === "壳体"`
- That row is at index `headerRow + 1`
- `col[3]` of that row equals `specString`
- If a shell row already existed, the total row count of the block is unchanged (no new row was inserted)
- If no shell row existed, the total row count of the block increased by exactly 1

**Validates: Requirements 2.3, 2.4, 2.5**

---

### Property 7: Batch shell operation always emits a result message

*For any* valid invocation of `applyShellType()` that does not throw (i.e., passes all input validation, finds at least one cabinet block, and completes row writes), `bridge.setMessage` is called exactly once with `isError = false` and a message string containing the number of affected cabinets.

**Validates: Requirements 4.1**
