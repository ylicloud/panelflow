(() => {
  'use strict';

  // ── 状态 ──────────────────────────────────────────
  let selectedUnits = new Set();   // Set<string> of unitNo
  let bridge = null;

  // ── DOM 引用 ──────────────────────────────────────
  let panelEl, batchCountEl,
      shellTypeSelect, customTypeWrapper, customTypeInput,
      dimW, dimH, dimD,
      applyShellBtn,
      componentEditorBody, addComponentRowBtn, applyComponentsBtn;

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
    componentEditorBody   = document.getElementById('component-editor-body');
    addComponentRowBtn    = document.getElementById('add-component-row-btn');
    applyComponentsBtn    = document.getElementById('apply-components-btn');

    if (!panelEl) return;

    // 绑定静态元素事件
    shellTypeSelect.addEventListener('change', onShellTypeChange);
    applyShellBtn.addEventListener('click', applyShellType);
    addComponentRowBtn.addEventListener('click', addComponentEditorRow);
    applyComponentsBtn.addEventListener('click', applyComponents);

    // 折叠/展开
    document.querySelectorAll('.oa-batch-section-toggle').forEach(btn => {
      btn.addEventListener('click', onSectionToggle);
    });

    // 尺寸预设联动
    const dimPreset = document.getElementById('dim-preset-select');
    if (dimPreset) {
      dimPreset.addEventListener('change', () => {
        const val = dimPreset.value;
        if (!val) return;
        const [w, h, d] = val.split(',');
        dimW.value = w;
        dimH.value = h;
        dimD.value = d;
        // 清除 is-invalid 状态
        [dimW, dimH, dimD].forEach(el => el.classList.remove('is-invalid'));
        // 回置选项，使其可再次选同一预设
        dimPreset.value = '';
      });
    }

    // Bridge 回调
    bridge.onClearAll(onClearAllReset);
    bridge.onTreeRebuilt(onTreeRebuilt);

    // 添加初始元件行
    addComponentEditorRow();
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

  // ── Append 元件行 ────────────────────────────────
  function appendComponentRows(hot, endRow, components) {
    let insertAt = endRow + 1;
    for (const comp of components) {
      hot.alter('insert_row_above', insertAt, 1);
      const price = comp.price || '0.0';
      const qty   = comp.qty   || '1';
      // 总价 = 单价 × 数量（均为数字时计算，否则留空）
      const priceNum = parseFloat(price);
      const qtyNum   = parseFloat(qty);
      const total = Number.isFinite(priceNum) && Number.isFinite(qtyNum)
        ? (priceNum * qtyNum).toFixed(1)
        : '';
      hot.setDataAtCell([
        [insertAt, 2, comp.name],
        [insertAt, 3, comp.spec],
        [insertAt, 4, price],
        [insertAt, 5, qty],
        [insertAt, 6, comp.vendor || ''],
        [insertAt, 7, total],
      ]);
      insertAt++;
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

  // ── 通用元件编辑器 ───────────────────────────────
  function addComponentEditorRow() {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td><input type="text" class="form-control form-control-sm comp-name" list="comp-name-presets" placeholder="名称" autocomplete="off" /></td>
      <td><input type="text" class="form-control form-control-sm comp-spec" list="comp-spec-presets" placeholder="规格" autocomplete="off" /></td>
      <td><input type="number" class="form-control form-control-sm comp-qty" min="0" placeholder="数量" value="1" /></td>
      <td><input type="text" class="form-control form-control-sm comp-price" placeholder="单价" value="0.0" /></td>
      <td><input type="text" class="form-control form-control-sm comp-vendor" placeholder="厂家" /></td>
      <td>
        <button type="button" class="btn btn-sm btn-outline-danger oa-del-comp-row" title="删除此行">
          <i class="bi bi-x"></i>
        </button>
      </td>`;
    tr.querySelector('.oa-del-comp-row').addEventListener('click', () => tr.remove());
    componentEditorBody.appendChild(tr);
  }

  function readComponentRows() {
    const rows = [];
    componentEditorBody.querySelectorAll('tr').forEach(tr => {
      const name   = tr.querySelector('.comp-name')?.value.trim() || '';
      const spec   = tr.querySelector('.comp-spec')?.value.trim() || '';
      const qty    = tr.querySelector('.comp-qty')?.value.trim() || '';
      const price  = tr.querySelector('.comp-price')?.value.trim() || '';
      const vendor = tr.querySelector('.comp-vendor')?.value.trim() || '';
      rows.push({ name, spec, qty, price, vendor, _el: tr });
    });
    return rows;
  }

  function applyComponents() {
    if (!bridge || !bridge.isDataLoaded()) {
      bridge && bridge.setMessage('请先合并 Excel 文件', true); return;
    }
    if (selectedUnits.size === 0) {
      bridge.setMessage('请先选中至少一个控制柜节点', true); return;
    }

    const allRows = readComponentRows();
    // 过滤空行
    const validRows = allRows.filter(r => r.name || r.spec);

    if (validRows.length === 0) {
      bridge.setMessage('没有有效的元件可添加', true); return;
    }

    // 验证数量
    let hasError = false;
    for (const row of validRows) {
      if (row.qty === '') {
        bridge.setMessage('元件数量不能为空', true); hasError = true; break;
      }
      const qv = Number(row.qty);
      if (!Number.isFinite(qv) || qv < 0) {
        bridge.setMessage('元件数量必须为非负数', true); hasError = true; break;
      }
    }
    if (hasError) return;

    const hot = bridge.getHot();
    const sortedUnits = [...selectedUnits]
      .map(unitNo => ({ unitNo, ...findCabinetBlock(hot, unitNo) }))
      .filter(u => u.headerRow !== -1)
      .sort((a, b) => b.headerRow - a.headerRow);

    if (sortedUnits.length === 0) {
      bridge.setMessage('未找到选中控制柜对应的表格行', true); return;
    }

    try {
      for (const unit of sortedUnits) {
        appendComponentRows(hot, unit.endRow, validRows);
      }
      bridge.setMessage(
        `已为 ${sortedUnits.length} 个控制柜追加 ${validRows.length} 条通用元件行`,
        false
      );
    } catch (err) {
      bridge.setMessage(`追加元件行失败：${err.message || err}`, true);
    }
  }

  // ── 启动 ─────────────────────────────────────────
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
