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

    shellTypeSelect.addEventListener('change', onShellTypeChange);
    applyShellBtn.addEventListener('click', applyShellType);

    document.querySelectorAll('.oa-batch-section-toggle').forEach(btn => {
      btn.addEventListener('click', onSectionToggle);
    });

    const dimPreset = document.getElementById('dim-preset-select');
    if (dimPreset) {
      dimPreset.addEventListener('change', () => {
        const val = dimPreset.value;
        if (!val) return;
        const [w, h, d] = val.split(',');
        dimW.value = w;
        dimH.value = h;
        dimD.value = d;
        [dimW, dimH, dimD].forEach(el => el.classList.remove('is-invalid'));
        dimPreset.value = '';
      });
    }

    bridge.onClearAll(onClearAllReset);
    bridge.onTreeRebuilt(onTreeRebuilt);
  }

  // ── Checkbox 管理 ────────────────────────────────
  function rebindCheckboxEvents() {
    document.querySelectorAll('.oa-unit-checkbox').forEach(cb => {
      cb.removeEventListener('change', onCheckboxChange);
      cb.addEventListener('change', onCheckboxChange);
      cb.checked = selectedUnits.has(cb.value);
    });
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

  // ── 壳体类型下拉 ─────────────────────────────────
  function onShellTypeChange() {
    const isCustom = shellTypeSelect.value === '__custom__';
    customTypeWrapper.classList.toggle('d-none', !isCustom);
    if (!isCustom) customTypeInput.value = '';
  }

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

  function upsertShellRow(hot, headerRow, endRow, specString) {
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

  function applyShellType() {
    if (!bridge || !bridge.isDataLoaded()) {
      bridge && bridge.setMessage('请先合并 Excel 文件', true); return;
    }
    if (selectedUnits.size === 0) {
      bridge.setMessage('请先选中至少一个控制柜节点', true); return;
    }

    let shellType = '';
    if (shellTypeSelect.value === '__custom__') {
      shellType = (customTypeInput.value || '').trim();
      if (!shellType) {
        bridge.setMessage('请填写自定义壳体类型名称', true); return;
      }
    } else {
      shellType = shellTypeSelect.value;
    }

    const w = dimW.value, h = dimH.value, d = dimD.value;
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

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
