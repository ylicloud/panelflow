(() => {
    "use strict";

    const cfg = document.getElementById("element-dict-config");
    if (!cfg) return;

    const getUrl = cfg.dataset.getUrl;
    const createUrl = cfg.dataset.createUrl;
    const updateUrl = cfg.dataset.updateUrl;
    const toggleUrl = cfg.dataset.toggleUrl;
    const reorderUrl = cfg.dataset.reorderUrl;

    const tbody = document.getElementById("dict-tbody");
    const infoBar = document.getElementById("page-info-bar");
    const addBtn = document.getElementById("add-item-btn");
    const saveOrderBtn = document.getElementById("save-order-btn");
    const levelTabs = document.getElementById("level-tabs");

    const modalEl = document.getElementById("item-modal");
    const modal = modalEl ? new bootstrap.Modal(modalEl) : null;
    const fId = document.getElementById("f-id");
    const fLevel = document.getElementById("f-level");
    const fName = document.getElementById("f-name");
    const fXlx = document.getElementById("f-xlx");
    const fAmount = document.getElementById("f-amount");
    const fGgxh = document.getElementById("f-ggxh");
    const fUnit = document.getElementById("f-unit");
    const fScope = document.getElementById("f-scope");
    const fDefault = document.getElementById("f-default");
    const fEnabled = document.getElementById("f-enabled");
    const fRemark = document.getElementById("f-remark");
    const saveItemBtn = document.getElementById("save-item-btn");

    let currentLevel = 2;
    let items = [];          // 当前级别字典项
    let orderDirty = false;  // 顺序是否被改动

    const token = () => {
        const el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : "";
    };

    const setInfo = (msg, isError) => {
        if (!infoBar) return;
        infoBar.textContent = msg || "";
        infoBar.classList.toggle("is-error", !!isError);
    };

    const postForm = async (url, data) => {
        const body = new URLSearchParams();
        body.append("__RequestVerificationToken", token());
        Object.keys(data).forEach((k) => {
            const v = data[k];
            if (Array.isArray(v)) {
                v.forEach((item) => body.append(k, item));
            } else if (v !== undefined && v !== null) {
                body.append(k, v);
            }
        });
        const resp = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/x-www-form-urlencoded" },
            body
        });
        return resp.json();
    };

    /** 各级新增时的类型/数量默认值（沿用 PB 历史规则）。 */
    const defaultXlxForLevel = (level) => {
        if (level === 1) return 1;
        if (level === 3) return 11;
        return "";
    };

    const defaultAmountForLevel = () => 1;

    const formatAmount = (v) => {
        const n = Number(v);
        return Number.isFinite(n) ? n.toFixed(2) : "1.00";
    };

    const escapeHtml = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({
        "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
    }[c]));

    const renderTable = () => {
        document.querySelectorAll(".oa-col-l2").forEach((el) => el.classList.toggle("d-none", currentLevel !== 2));
        document.querySelectorAll(".oa-col-l3").forEach((el) => el.classList.toggle("d-none", currentLevel !== 3));

        if (!items.length) {
            tbody.innerHTML = '<tr><td colspan="10" class="text-center text-muted py-3">暂无数据，点击右上角“新增”。</td></tr>';
            return;
        }

        const rows = items.map((it, idx) => {
            const lockIcon = it.isLocked ? '<i class="bi bi-lock-fill text-secondary me-1" title="锁定项"></i>' : "";
            const upDisabled = idx === 0 || it.isLocked ? "disabled" : "";
            const downDisabled = idx === items.length - 1 || (idx === 0 && it.isLocked) ? "disabled" : "";
            const l2Cell = currentLevel === 2
                ? `<td class="oa-col-l2">${it.isDefaultOnImport ? '<span class="badge text-bg-info">是</span>' : '<span class="text-muted">否</span>'}</td>`
                : "";
            const l3Cell = currentLevel === 3
                ? `<td class="oa-col-l3">${escapeHtml(it.targetParentScope || "0001")}</td>`
                : "";
            const delBtn = it.isLocked
                ? ""
                : `<button type="button" class="btn btn-sm btn-outline-danger oa-toggle-btn" data-id="${it.id}" data-enabled="${it.isEnabled}">${it.isEnabled ? "停用" : "启用"}</button>`;
            return `<tr class="${it.isEnabled ? "" : "oa-dict-disabled"}" data-id="${it.id}">
                <td>
                    <div class="oa-order-btns">
                        <button type="button" class="btn btn-sm btn-outline-secondary oa-up-btn" ${upDisabled}><i class="bi bi-arrow-up"></i></button>
                        <button type="button" class="btn btn-sm btn-outline-secondary oa-down-btn" ${downDisabled}><i class="bi bi-arrow-down"></i></button>
                    </div>
                </td>
                <td>${lockIcon}${escapeHtml(it.name)}</td>
                <td>${it.xlx}</td>
                <td>${formatAmount(it.amount)}</td>
                <td>${escapeHtml(it.ggxh || "")}</td>
                <td>${escapeHtml(it.defaultUnit || "")}</td>
                ${l3Cell}
                ${l2Cell}
                <td>${it.isEnabled ? '<span class="badge text-bg-success">启用</span>' : '<span class="badge text-bg-secondary">停用</span>'}</td>
                <td>
                    <button type="button" class="btn btn-sm btn-outline-primary oa-edit-btn" data-id="${it.id}">编辑</button>
                    ${delBtn}
                </td>
            </tr>`;
        });
        tbody.innerHTML = rows.join("");
    };

    const loadLevel = async (level) => {
        currentLevel = level;
        orderDirty = false;
        saveOrderBtn.disabled = true;
        setInfo("");
        tbody.innerHTML = '<tr><td colspan="10" class="text-center text-muted py-3">加载中…</td></tr>';
        try {
            const resp = await fetch(`${getUrl}?level=${level}&includeDisabled=true`);
            const data = await resp.json();
            if (!data.success) { setInfo(data.message || "加载失败", true); return; }
            items = data.items || [];
            renderTable();
        } catch (e) {
            setInfo("加载失败：" + (e.message || e), true);
        }
    };

    const moveItem = (id, dir) => {
        const idx = items.findIndex((x) => x.id === id);
        const target = idx + dir;
        if (idx < 0 || target < 0 || target >= items.length) return;
        // 锁定项固定首位：不允许把锁定项移动，也不允许其它项移到首位之前
        if (items[idx].isLocked) return;
        if (target === 0 && items[0].isLocked) return;
        const tmp = items[idx];
        items[idx] = items[target];
        items[target] = tmp;
        orderDirty = true;
        saveOrderBtn.disabled = false;
        renderTable();
    };

    const openModal = (item) => {
        const isEdit = !!item;
        document.getElementById("item-modal-title").textContent = isEdit ? "编辑通用项" : "新增通用项";
        fId.value = isEdit ? item.id : "";
        fLevel.value = currentLevel;
        fName.value = isEdit ? item.name : "";
        fXlx.value = isEdit ? item.xlx : defaultXlxForLevel(currentLevel);
        fAmount.value = isEdit ? formatAmount(item.amount) : formatAmount(defaultAmountForLevel());
        fGgxh.value = isEdit ? (item.ggxh || "") : "";
        fUnit.value = isEdit ? (item.defaultUnit || "") : "";
        fScope.value = isEdit ? (item.targetParentScope || "") : "";
        fDefault.checked = isEdit ? item.isDefaultOnImport : false;
        fEnabled.checked = isEdit ? item.isEnabled : true;
        fRemark.value = isEdit ? (item.remark || "") : "";
        document.querySelectorAll(".oa-field-l2").forEach((el) => el.classList.toggle("d-none", currentLevel !== 2));
        document.querySelectorAll(".oa-field-l3").forEach((el) => el.classList.toggle("d-none", currentLevel !== 3));
        if (modal) modal.show();
    };

    const saveItem = async () => {
        const name = fName.value.trim();
        if (!name) { setInfo("名称不能为空", true); return; }
        const xlx = parseInt(fXlx.value, 10);
        if (!Number.isInteger(xlx) || xlx < 0) { setInfo("类型 x_lx 必须为非负整数", true); return; }
        const amount = parseFloat(fAmount.value);
        if (!Number.isFinite(amount) || amount < 0) { setInfo("数量必须为非负数", true); return; }

        const dto = {
            Id: fId.value || 0,
            Level: currentLevel,
            Name: name,
            Xlx: xlx,
            Amount: amount,
            Ggxh: fGgxh.value.trim(),
            DefaultUnit: fUnit.value.trim(),
            TargetParentScope: currentLevel === 3 ? fScope.value.trim() : "",
            IsDefaultOnImport: currentLevel === 2 ? fDefault.checked : false,
            IsEnabled: fEnabled.checked,
            Remark: fRemark.value.trim()
        };
        const url = fId.value ? updateUrl : createUrl;
        try {
            const data = await postForm(url, dto);
            if (!data.success) { setInfo(data.message || "保存失败", true); return; }
            if (modal) modal.hide();
            setInfo(data.message || "保存成功");
            await loadLevel(currentLevel);
        } catch (e) {
            setInfo("保存失败：" + (e.message || e), true);
        }
    };

    const toggleEnable = async (id, currentlyEnabled) => {
        try {
            const data = await postForm(toggleUrl, { id, enabled: !currentlyEnabled });
            if (!data.success) { setInfo(data.message || "操作失败", true); return; }
            setInfo(data.message);
            await loadLevel(currentLevel);
        } catch (e) {
            setInfo("操作失败：" + (e.message || e), true);
        }
    };

    const saveOrder = async () => {
        const reason = window.prompt("调整顺序会影响后续报价单的属性顺序，请填写调整理由：", "");
        if (reason === null) return;
        if (!reason.trim()) { setInfo("调整顺序必须填写理由", true); return; }
        try {
            const data = await postForm(reorderUrl, {
                level: currentLevel,
                orderedIds: items.map((x) => x.id),
                reason: reason.trim()
            });
            if (!data.success) { setInfo(data.message || "保存失败", true); return; }
            orderDirty = false;
            saveOrderBtn.disabled = true;
            setInfo(data.message || "顺序已保存");
            await loadLevel(currentLevel);
        } catch (e) {
            setInfo("保存失败：" + (e.message || e), true);
        }
    };

    // 事件绑定
    levelTabs.addEventListener("click", (e) => {
        const btn = e.target.closest("button[data-level]");
        if (!btn) return;
        if (orderDirty && !window.confirm("顺序尚未保存，切换将丢失改动，继续？")) return;
        levelTabs.querySelectorAll(".nav-link").forEach((b) => b.classList.remove("active"));
        btn.classList.add("active");
        loadLevel(parseInt(btn.dataset.level, 10));
    });

    tbody.addEventListener("click", (e) => {
        const editBtn = e.target.closest(".oa-edit-btn");
        if (editBtn) { openModal(items.find((x) => x.id === parseInt(editBtn.dataset.id, 10))); return; }
        const toggleBtn = e.target.closest(".oa-toggle-btn");
        if (toggleBtn) { toggleEnable(parseInt(toggleBtn.dataset.id, 10), toggleBtn.dataset.enabled === "true"); return; }
        const upBtn = e.target.closest(".oa-up-btn");
        if (upBtn) { moveItem(parseInt(upBtn.closest("tr").dataset.id, 10), -1); return; }
        const downBtn = e.target.closest(".oa-down-btn");
        if (downBtn) { moveItem(parseInt(downBtn.closest("tr").dataset.id, 10), 1); return; }
    });

    addBtn.addEventListener("click", () => openModal(null));
    saveItemBtn.addEventListener("click", saveItem);
    saveOrderBtn.addEventListener("click", saveOrder);

    loadLevel(currentLevel);
})();
