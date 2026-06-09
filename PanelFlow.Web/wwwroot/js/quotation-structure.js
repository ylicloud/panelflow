(() => {
    "use strict";

    const ROOT_CODE = "__ROOT__";

    const cfg = document.getElementById("structure-config");
    if (!cfg) return;

    const searchUrl = cfg.dataset.searchUrl;
    const treeUrl = cfg.dataset.treeUrl;
    const applyUrl = cfg.dataset.applyUrl;
    const dictUrl = cfg.dataset.dictUrl;

    const searchInput = document.getElementById("quotation-search");
    const searchResults = document.getElementById("search-results");
    const quotationInfo = document.getElementById("quotation-info");
    const infoBar = document.getElementById("page-info-bar");
    const treeEl = document.getElementById("structure-tree");
    const refreshBtn = document.getElementById("refresh-tree-btn");
    const expandAllBtn = document.getElementById("expand-all-btn");
    const collapseAllBtn = document.getElementById("collapse-all-btn");
    const selectAllCabsBtn = document.getElementById("select-all-cabs-btn");
    const invertCabsBtn = document.getElementById("invert-cabs-btn");
    const opPanel = document.getElementById("op-panel");
    const opHint = document.getElementById("op-disabled-hint");
    const selectedSummary = document.getElementById("selected-summary");
    const saveReorderBtn = document.getElementById("save-reorder-btn");
    const cabinetCountEl = document.getElementById("cabinet-count");

    let currentFabh = "";
    let treeData = null;
    let canEdit = false;
    let dictL1 = [];
    let dictL2 = [];
    let dictL3 = [];
    const selected = new Set();
    const collapsed = new Set();
    let reorderDirty = false;
    let reorderParentCode = "";
    let reorderOrderedCodes = [];
    let lastClickedCabinet = null;

    const token = () => {
        const el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : "";
    };

    const setInfo = (msg, isError) => {
        if (!infoBar) return;
        infoBar.textContent = msg || "";
        infoBar.classList.toggle("is-error", !!isError);
    };

    const statusText = (s) => ({ 0: "草稿", 1: "(无内容)", 10: "已成立" }[s] ?? String(s));

    const escapeHtml = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({
        "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
    }[c]));

    const levelLabel = (lv, xlx) => {
        if (lv === 1) return "控制柜";
        if (lv === 2) return "属性";
        if (lv === 3) return xlx === 11 ? "元件" : "通用元件";
        return "";
    };

    const getCabinetNodes = () => (treeData?.tree || []);

    const collectCollapsibleCodes = (nodes, result) => {
        nodes.forEach((n) => {
            if (n.children && n.children.length) {
                result.add(n.code);
                collectCollapsibleCodes(n.children, result);
            }
        });
    };

    const defaultCollapseToLevel1 = () => {
        collapsed.clear();
        collectCollapsibleCodes(getCabinetNodes(), collapsed);
    };

    const setTreeToolbarEnabled = (enabled) => {
        [refreshBtn, expandAllBtn, collapseAllBtn, selectAllCabsBtn, invertCabsBtn].forEach((btn) => {
            if (btn) btn.disabled = !enabled;
        });
    };

    const loadDicts = async () => {
        const [r1, r2, r3] = await Promise.all([
            fetch(`${dictUrl}?level=1&includeDisabled=false`).then((r) => r.json()),
            fetch(`${dictUrl}?level=2&includeDisabled=false`).then((r) => r.json()),
            fetch(`${dictUrl}?level=3&includeDisabled=false`).then((r) => r.json())
        ]);
        dictL1 = r1.success ? (r1.items || []) : [];
        dictL2 = r2.success ? (r2.items || []) : [];
        dictL3 = r3.success ? (r3.items || []) : [];
        renderDictChecklist("dict-l1-list", dictL1);
        renderDictChecklist("dict-l2-list", dictL2);
        renderDictChecklist("dict-l3-list", dictL3);
        renderDictChecklist("dict-l2-batch-list", dictL2);
        renderDictChecklist("dict-l3-batch-list", dictL3);
    };

    const renderDictChecklist = (containerId, items) => {
        const el = document.getElementById(containerId);
        if (!el) return;
        if (!items.length) {
            el.innerHTML = '<div class="text-muted small">暂无可用字典项</div>';
            return;
        }
        el.innerHTML = items.map((it) => {
            const spec = it.ggxh ? ` / ${escapeHtml(it.ggxh)}` : "";
            return `<label class="d-flex align-items-center gap-2 small mb-1">
                <input type="checkbox" class="form-check-input oa-dict-cb" value="${it.id}" />
                <span>${escapeHtml(it.name)}${spec} <span class="text-muted">(x_lx=${it.xlx})</span></span>
            </label>`;
        }).join("");
    };

    const getCheckedDictIds = (containerId) => {
        const el = document.getElementById(containerId);
        if (!el) return [];
        return [...el.querySelectorAll(".oa-dict-cb:checked")].map((c) => parseInt(c.value, 10));
    };

    const renderNodeList = (nodes, depth) => {
        if (!nodes || !nodes.length) return "";
        return nodes.map((n) => {
            const hasChildren = n.children && n.children.length > 0;
            const isCollapsed = collapsed.has(n.code);
            const checked = selected.has(n.code) ? "checked" : "";
            const ro = n.isReadOnly ? "is-readonly" : "";
            const lock = n.isLocked ? "is-locked" : "";
            const cbDisabled = !canEdit || n.isReadOnly ? "disabled" : "";
            const toggleIcon = isCollapsed ? "bi-caret-right" : "bi-caret-down";
            const toggleBtn = hasChildren
                ? `<button type="button" class="oa-tree-toggle" data-toggle="${escapeHtml(n.code)}" title="折叠/展开"><i class="bi ${toggleIcon}"></i></button>`
                : `<span class="oa-tree-toggle is-placeholder"></span>`;
            const orderBtns = canEdit && !n.isReadOnly && !n.isLocked && depth > 0
                ? `<span class="oa-order-btns ms-1">
                    <button type="button" class="btn btn-sm btn-outline-secondary oa-sib-up" data-code="${escapeHtml(n.code)}"><i class="bi bi-arrow-up"></i></button>
                    <button type="button" class="btn btn-sm btn-outline-secondary oa-sib-down" data-code="${escapeHtml(n.code)}"><i class="bi bi-arrow-down"></i></button>
                   </span>`
                : "";
            const childrenHtml = hasChildren && !isCollapsed
                ? `<ul>${renderNodeList(n.children, depth + 1)}</ul>` : "";
            return `<li>
                <div class="oa-tree-node ${ro} ${lock}" data-code="${escapeHtml(n.code)}" data-level="${n.level}">
                    ${toggleBtn}
                    <input type="checkbox" class="form-check-input oa-node-cb" data-code="${escapeHtml(n.code)}" data-level="${n.level}" ${checked} ${cbDisabled} />
                    <span class="oa-node-label">
                        <span class="badge text-bg-light me-1">${levelLabel(n.level, n.xlx)}</span>
                        ${n.isLocked ? '<i class="bi bi-lock-fill text-secondary me-1"></i>' : ""}
                        ${escapeHtml(n.name)} <span class="text-muted">(${escapeHtml(n.code)})</span>
                    </span>
                    ${orderBtns}
                </div>
                ${childrenHtml}
            </li>`;
        }).join("");
    };

    const paintTree = () => {
        if (!treeData) {
            treeEl.innerHTML = '<div class="text-muted small">未加载</div>';
            return;
        }

        const rootName = escapeHtml(treeData.quotationName || treeData.fabh || "报价单");
        const rootCollapsed = collapsed.has(ROOT_CODE);
        const rootToggle = rootCollapsed ? "bi-caret-right" : "bi-caret-down";
        const rootChecked = selected.has(ROOT_CODE) ? "checked" : "";
        const rootCbDisabled = !canEdit ? "disabled" : "";
        const cabinetsHtml = !rootCollapsed ? `<ul>${renderNodeList(getCabinetNodes(), 1)}</ul>` : "";

        treeEl.innerHTML = `<ul>
            <li>
                <div class="oa-tree-node is-root" data-code="${ROOT_CODE}" data-level="0">
                    <button type="button" class="oa-tree-toggle" data-toggle="${ROOT_CODE}"><i class="bi ${rootToggle}"></i></button>
                    <input type="checkbox" class="form-check-input oa-node-cb" data-code="${ROOT_CODE}" data-level="0" ${rootChecked} ${rootCbDisabled} />
                    <span class="oa-node-label">
                        <span class="badge text-bg-primary me-1">报价单</span>
                        ${rootName}
                    </span>
                </div>
                ${cabinetsHtml}
            </li>
        </ul>`;
        updateOpPanel();
    };

    const getSelectedNodes = () => {
        if (!treeData || selected.has(ROOT_CODE)) {
            if (selected.has(ROOT_CODE)) {
                return [{ code: ROOT_CODE, name: treeData?.quotationName || "报价单", level: 0 }];
            }
            return [];
        }
        const result = [];
        const walk = (nodes) => {
            nodes.forEach((n) => {
                if (selected.has(n.code)) result.push(n);
                if (n.children) walk(n.children);
            });
        };
        walk(getCabinetNodes());
        return result;
    };

    const getSelectedCabinetCodes = () =>
        getSelectedNodes().filter((n) => n.level === 1).map((n) => n.code);

    const isRootOnlySelection = () =>
        selected.size === 1 && selected.has(ROOT_CODE);

    const isAllCabinetSelection = () => {
        if (selected.has(ROOT_CODE) || selected.size === 0) return false;
        const nodes = getSelectedNodes();
        return nodes.length > 0 && nodes.every((n) => n.level === 1);
    };

    const updateOpPanel = () => {
        const nodes = getSelectedNodes();
        selectedSummary.textContent = nodes.length
            ? nodes.map((n) => n.name).join("、")
            : "无";

        document.querySelectorAll(".op-section").forEach((s) => s.classList.add("d-none"));

        if (!canEdit) return;

        if (isRootOnlySelection()) {
            document.getElementById("op-add-l1").classList.remove("d-none");
            return;
        }

        if (!nodes.length) return;

        const levels = new Set(nodes.map((n) => n.level));
        const allSameLevel = levels.size === 1;
        const lv = allSameLevel ? nodes[0].level : 0;

        if (isAllCabinetSelection()) {
            document.getElementById("op-batch-cabinet").classList.remove("d-none");
            if (cabinetCountEl) cabinetCountEl.textContent = String(nodes.length);
            return;
        }

        if (lv === 2) {
            document.getElementById("op-add-l3").classList.remove("d-none");
            const deletable = nodes.every((n) => !n.isLocked);
            if (deletable) {
                document.getElementById("op-rename").classList.remove("d-none");
                document.getElementById("op-delete").classList.remove("d-none");
            }
            if (nodes.length >= 2 && nodes.every((n) => !n.isLocked)) {
                document.getElementById("op-reorder").classList.remove("d-none");
            }
        }
        if (lv === 3) {
            document.getElementById("op-rename").classList.remove("d-none");
            document.getElementById("op-delete").classList.remove("d-none");
        }
    };

    const loadTree = async (fabh) => {
        currentFabh = fabh;
        selected.clear();
        lastClickedCabinet = null;
        reorderDirty = false;
        saveReorderBtn.disabled = true;
        setInfo("");
        treeEl.innerHTML = '<div class="text-muted small">加载中…</div>';
        try {
            const resp = await fetch(`${treeUrl}?fabh=${encodeURIComponent(fabh)}`);
            const data = await resp.json();
            if (!data.success) {
                setInfo(data.message || "加载失败", true);
                return;
            }
            treeData = data.data;
            canEdit = !!treeData.canEdit;
            defaultCollapseToLevel1();
            quotationInfo.innerHTML = `
                <strong>${escapeHtml(treeData.fabh)}</strong>
                ${escapeHtml(treeData.quotationName)}
                · 报价人：${escapeHtml(treeData.quoter)}
                · 状态：<span class="badge ${treeData.currentStatus === 10 ? "text-bg-secondary" : "text-bg-primary"}">${statusText(treeData.currentStatus)}</span>
                ${treeData.orphanCount > 0 ? ` · <span class="text-warning">游离行 ${treeData.orphanCount}</span>` : ""}`;
            opPanel.classList.toggle("d-none", false);
            opHint.classList.toggle("d-none", true);
            setTreeToolbarEnabled(true);
            if (!canEdit) {
                setInfo(treeData.currentStatus === 10 ? "报价单已成立，结构只读。" : "您无权修改此报价单结构。", false);
            }
            paintTree();
        } catch (e) {
            setInfo("加载失败：" + (e.message || e), true);
        }
    };

    const postApply = async (operations) => {
        try {
            const resp = await fetch(applyUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "RequestVerificationToken": token()
                },
                body: JSON.stringify({ fabh: currentFabh, operations })
            });
            const data = await resp.json();
            if (!data.success) {
                setInfo(data.message || "操作失败", true);
                return false;
            }
            setInfo(data.message || "保存成功");
            await loadTree(currentFabh);
            return true;
        } catch (e) {
            setInfo("操作失败：" + (e.message || e), true);
            return false;
        }
    };

    const findSiblings = (code) => {
        let siblings = null;
        let parentCode = "";
        const walk = (nodes, parent) => {
            for (const n of nodes) {
                if (n.code === code) {
                    siblings = nodes;
                    parentCode = parent ? parent.code : "";
                    return true;
                }
                if (n.children && walk(n.children, n)) return true;
            }
            return false;
        };
        walk(getCabinetNodes(), null);
        return { siblings, parentCode };
    };

    const moveSibling = (code, dir) => {
        const { siblings, parentCode } = findSiblings(code);
        if (!siblings) return;
        const idx = siblings.findIndex((n) => n.code === code);
        const target = idx + dir;
        if (idx < 0 || target < 0 || target >= siblings.length) return;
        if (siblings[idx].isLocked || (target === 0 && siblings[0].isLocked)) return;
        const tmp = siblings[idx];
        siblings[idx] = siblings[target];
        siblings[target] = tmp;
        reorderDirty = true;
        reorderParentCode = parentCode;
        reorderOrderedCodes = siblings.map((n) => n.code);
        saveReorderBtn.disabled = false;
        paintTree();
    };

    const handleCabinetShiftSelect = (code, shiftKey) => {
        const cabs = getCabinetNodes();
        const idx = cabs.findIndex((c) => c.code === code);
        if (idx < 0) return;

        if (shiftKey && lastClickedCabinet != null) {
            const anchor = cabs.findIndex((c) => c.code === lastClickedCabinet);
            if (anchor >= 0) {
                const from = Math.min(anchor, idx);
                const to = Math.max(anchor, idx);
                for (let i = from; i <= to; i++) {
                    selected.add(cabs[i].code);
                }
            }
        } else {
            lastClickedCabinet = code;
        }
    };

    let searchTimer = null;
    searchInput.addEventListener("input", () => {
        clearTimeout(searchTimer);
        const kw = searchInput.value.trim();
        if (!kw) {
            searchResults.classList.add("d-none");
            return;
        }
        searchTimer = setTimeout(async () => {
            try {
                const items = await fetch(`${searchUrl}?keyword=${encodeURIComponent(kw)}`).then((r) => r.json());
                if (!items.length) {
                    searchResults.innerHTML = '<div class="oa-search-item text-muted">无匹配</div>';
                } else {
                    searchResults.innerHTML = items.map((it) => `
                        <div class="oa-search-item" data-fabh="${escapeHtml(it.quotationNo)}">
                            <strong>${escapeHtml(it.quotationNo)}</strong> ${escapeHtml(it.quotationName)}
                            <span class="text-muted"> · ${escapeHtml(it.customerName)} · ${statusText(it.currentStatus)}</span>
                        </div>`).join("");
                }
                searchResults.classList.remove("d-none");
            } catch {
                searchResults.classList.add("d-none");
            }
        }, 300);
    });

    searchResults.addEventListener("click", (e) => {
        const item = e.target.closest(".oa-search-item[data-fabh]");
        if (!item) return;
        searchResults.classList.add("d-none");
        searchInput.value = item.dataset.fabh;
        loadTree(item.dataset.fabh);
    });

    document.addEventListener("click", (e) => {
        if (!searchResults.contains(e.target) && e.target !== searchInput) {
            searchResults.classList.add("d-none");
        }
    });

    refreshBtn.addEventListener("click", () => {
        if (currentFabh) loadTree(currentFabh);
    });

    expandAllBtn.addEventListener("click", () => {
        collapsed.clear();
        paintTree();
    });

    collapseAllBtn.addEventListener("click", () => {
        collapsed.clear();
        collapsed.add(ROOT_CODE);
        collectCollapsibleCodes(getCabinetNodes(), collapsed);
        paintTree();
    });

    selectAllCabsBtn.addEventListener("click", () => {
        if (!canEdit) return;
        selected.clear();
        selected.delete(ROOT_CODE);
        getCabinetNodes().forEach((c) => selected.add(c.code));
        paintTree();
    });

    invertCabsBtn.addEventListener("click", () => {
        if (!canEdit) return;
        selected.delete(ROOT_CODE);
        getCabinetNodes().forEach((c) => {
            if (selected.has(c.code)) selected.delete(c.code);
            else selected.add(c.code);
        });
        paintTree();
    });

    treeEl.addEventListener("change", (e) => {
        const cb = e.target.closest(".oa-node-cb");
        if (!cb) return;
        const code = cb.dataset.code;
        const level = parseInt(cb.dataset.level, 10);
        if (cb.checked) {
            if (code === ROOT_CODE) {
                selected.clear();
                selected.add(ROOT_CODE);
            } else {
                selected.delete(ROOT_CODE);
                selected.add(code);
                if (level === 1) handleCabinetShiftSelect(code, e.shiftKey);
            }
        } else {
            selected.delete(code);
            if (level === 1) lastClickedCabinet = code;
        }
        paintTree();
    });

    treeEl.addEventListener("click", (e) => {
        const toggle = e.target.closest(".oa-tree-toggle[data-toggle]");
        if (toggle) {
            const code = toggle.dataset.toggle;
            if (collapsed.has(code)) collapsed.delete(code);
            else collapsed.add(code);
            paintTree();
            return;
        }
        const up = e.target.closest(".oa-sib-up");
        if (up) { moveSibling(up.dataset.code, -1); return; }
        const down = e.target.closest(".oa-sib-down");
        if (down) { moveSibling(down.dataset.code, 1); return; }
    });

    document.getElementById("btn-add-l1").addEventListener("click", async () => {
        if (!canEdit || !currentFabh) return;
        const ids = getCheckedDictIds("dict-l1-list");
        if (!ids.length) { setInfo("请选择要挂入的第1级扩展项", true); return; }
        await postApply([{ type: "AddLevel1", dictIds: ids, targetCodes: [] }]);
    });

    document.getElementById("btn-batch-add").addEventListener("click", async () => {
        if (!canEdit || !currentFabh) return;
        const targets = getSelectedCabinetCodes();
        if (!targets.length) { setInfo("请先选择控制柜", true); return; }
        const l2Ids = getCheckedDictIds("dict-l2-batch-list");
        const l3Ids = getCheckedDictIds("dict-l3-batch-list");
        if (!l2Ids.length && !l3Ids.length) {
            setInfo("请至少勾选一项第2级或第3级字典", true);
            return;
        }
        const ops = [];
        if (l2Ids.length) ops.push({ type: "AddLevel2", targetCodes: targets, dictIds: l2Ids });
        if (l3Ids.length) ops.push({ type: "AddLevel3", targetCodes: targets, dictIds: l3Ids });
        await postApply(ops);
    });

    document.getElementById("btn-batch-remove").addEventListener("click", async () => {
        if (!canEdit || !currentFabh) return;
        const targets = getSelectedCabinetCodes();
        if (!targets.length) { setInfo("请先选择控制柜", true); return; }
        const l2Ids = getCheckedDictIds("dict-l2-batch-list");
        const l3Ids = getCheckedDictIds("dict-l3-batch-list");
        if (!l2Ids.length && !l3Ids.length) {
            setInfo("请至少勾选一项要移除的第2级或第3级字典", true);
            return;
        }
        if (!window.confirm("确定按字典批量移除所选控制柜下的属性/元件？有元件的第2级属性将被跳过。")) return;
        const ops = [];
        if (l2Ids.length) ops.push({ type: "RemoveLevel2ByDict", targetCodes: targets, dictIds: l2Ids });
        if (l3Ids.length) ops.push({ type: "RemoveLevel3ByDict", targetCodes: targets, dictIds: l3Ids });
        await postApply(ops);
    });

    document.querySelectorAll(".oa-apply-btn").forEach((btn) => {
        btn.addEventListener("click", async () => {
            if (!canEdit || !currentFabh) return;
            const op = btn.dataset.op;
            const targets = [...selected].filter((c) => c !== ROOT_CODE);
            if (!targets.length) { setInfo("请先选择节点", true); return; }

            const ops = [];
            if (op === "AddLevel2") {
                const ids = getCheckedDictIds("dict-l2-list");
                if (!ids.length) { setInfo("请选择要挂入的属性", true); return; }
                ops.push({ type: "AddLevel2", targetCodes: targets, dictIds: ids });
            } else if (op === "AddLevel3") {
                const ids = getCheckedDictIds("dict-l3-list");
                if (!ids.length) { setInfo("请选择要挂入的元件", true); return; }
                ops.push({ type: "AddLevel3", targetCodes: targets, dictIds: ids });
            } else if (op === "Delete") {
                if (!window.confirm("确定删除选中节点？")) return;
                ops.push({ type: "Delete", targetCodes: targets });
            } else if (op === "Rename") {
                const name = document.getElementById("rename-input").value.trim();
                if (!name) { setInfo("请输入新名称", true); return; }
                if (targets.length !== 1) { setInfo("改名仅支持单选", true); return; }
                ops.push({ type: "Rename", targetCodes: targets, newName: name });
            }
            await postApply(ops);
        });
    });

    saveReorderBtn.addEventListener("click", async () => {
        if (!reorderDirty || !reorderOrderedCodes.length) return;
        const reason = window.prompt("调整顺序会影响编码，请填写理由：", "");
        if (reason === null) return;
        if (!reason.trim()) { setInfo("必须填写理由", true); return; }
        await postApply([{
            type: "ReorderSiblings",
            parentCode: reorderParentCode,
            orderedCodes: reorderOrderedCodes,
            reason: reason.trim()
        }]);
        reorderDirty = false;
        saveReorderBtn.disabled = true;
    });

    // 左右面板可拖动分隔线（默认 50% / 50%）
    const treePaneEl = document.getElementById("structure-tree-pane");
    const splitterEl = document.getElementById("structure-splitter");
    const workspaceEl = document.getElementById("structure-workspace");

    if (treePaneEl && splitterEl && workspaceEl) {
        let dragging = false;
        let startX = 0;
        let startWidth = 0;

        const applyHalfWidth = () => {
            const workspaceWidth = workspaceEl.getBoundingClientRect().width;
            if (workspaceWidth <= 0) return;
            const half = Math.max(220, Math.min(workspaceWidth * 0.6, workspaceWidth * 0.5));
            treePaneEl.style.width = `${half}px`;
        };

        applyHalfWidth();
        window.addEventListener("resize", () => {
            if (!treePaneEl.style.width) {
                applyHalfWidth();
            }
        });

        splitterEl.addEventListener("mousedown", (event) => {
            dragging = true;
            startX = event.clientX;
            startWidth = treePaneEl.getBoundingClientRect().width;
            splitterEl.classList.add("is-dragging");
            document.body.style.cursor = "col-resize";
            document.body.style.userSelect = "none";
            event.preventDefault();
        });

        window.addEventListener("mousemove", (event) => {
            if (!dragging) return;
            const workspaceWidth = workspaceEl.getBoundingClientRect().width;
            const minWidth = 220;
            const maxWidth = Math.max(minWidth, workspaceWidth * 0.6);
            const delta = event.clientX - startX;
            const nextWidth = Math.min(maxWidth, Math.max(minWidth, startWidth + delta));
            treePaneEl.style.width = `${nextWidth}px`;
        });

        window.addEventListener("mouseup", () => {
            if (!dragging) return;
            dragging = false;
            splitterEl.classList.remove("is-dragging");
            document.body.style.cursor = "";
            document.body.style.userSelect = "";
        });
    }

    loadDicts();
})();
