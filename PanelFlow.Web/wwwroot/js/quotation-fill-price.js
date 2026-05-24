(() => {
    const container = document.getElementById("hot-container");
    if (!container || typeof Handsontable === "undefined") {
        return;
    }

    const licenseKey = container.dataset.licenseKey || "";
    const componentsUrl = container.dataset.componentsUrl || "";
    const projectSummaryUrl = container.dataset.projectSummaryUrl || "";
    const saveProjectSummaryUrl = container.dataset.saveProjectSummaryUrl || "";
    const cabinetRefBjUrl = container.dataset.cabinetRefBjUrl || "";
    const referencePriceUrl = container.dataset.referencePriceUrl || "";
    const globalReadOnly = (container.dataset.readOnly || "").toLowerCase() === "true";
    const projectUsageUrl = (projectSummaryUrl || "").replace("GetProjectComponentSummary", "GetProjectComponentUsage");
    const infoBarEl = document.getElementById("page-info-bar");
    const treePaneEl = document.getElementById("price-tree-pane");
    const splitterEl = document.getElementById("price-splitter");
    const toggleTreeBtn = document.getElementById("toggle-tree-btn");
    const treeChildrenContainer = document.getElementById("tree-children-container");
    const treeRootNode = document.getElementById("tree-root-node");
    const saveSummaryBtn = document.getElementById("save-summary-btn");
    const saveForm = document.getElementById("price-save-form");
    const fillPriceActionButtons = document.getElementById("fill-price-action-buttons");
    const showRefPriceCb = document.getElementById("show-ref-price-cb");
    const componentUsagePanel = document.getElementById("component-usage-panel");
    const componentUsageTitle = document.getElementById("component-usage-title");
    const componentUsageList = document.getElementById("component-usage-list");
    const addCabinetBtn = document.getElementById("add-cabinet-btn");
    const deleteCabinetBtn = document.getElementById("delete-cabinet-btn");
    const addRowBtn = document.getElementById("add-row-btn");
    const deleteRowBtn = document.getElementById("delete-row-btn");
    const unsavedBadge = document.getElementById("unsaved-badge");

    const BASE_HEADERS_8 = ["序号", "名称", "规格", "单位", "单价", "数量", "金额", "报价浮动", "厂家"];
    const BASE_HEADERS_9 = ["序号", "名称", "规格", "单位", "单价", "参考价格", "数量", "金额", "报价浮动", "厂家"];
    const ROOT_SUMMARY_HEADERS = ["元件名称", "规格型号", "单价", "合计数量", "金额小计"];

    let summaryMode = false;
    let projectSummaryReadOnly = false;
    let cabinetViewActive = false;
    /** 根节点汇总视图模式（Req 15.1-15.6：只读、按 x_mc/x_ggxh/x_dj 分组、底部合计行） */
    let rootSummaryMode = false;
    /** 柜体视图且在单价后插入参考价列 */
    let refPriceColumnVisible = false;
    let currentCabinetUnitCode = "";
    let summaryDirty = false;
    let summaryOriginalRows = [];
    /** 当前柜体视图中每行对应的 x_wzdh（用于历史价格匹配） */
    let currentRowWzdh = [];
    /** 当前柜体视图中每行对应的完整参考价格数据（含 avg/min/max/count，用于 tooltip） */
    let currentReferencePriceData = [];
    /** 前端内存中的控制柜列表 [{code, name}]，用于增删改管理 */
    let cabinetList = [];
    /** 前端内存中每个控制柜对应的元件数据 { code: [[row data]] } */
    let cabinetDataMap = {};
    /** 当前选中的控制柜编码（用于高亮和操作） */
    let selectedCabinetCode = "";
    /** 未保存状态标记 */
    let dirty = false;

    const isRefColumnMode = () => !summaryMode && cabinetViewActive && refPriceColumnVisible;

    const colAmount = () => (isRefColumnMode() ? 7 : 6);
    const colFloat = () => (isRefColumnMode() ? 8 : 7);
    const colVendor = () => (isRefColumnMode() ? 9 : 8);
    const colQty = () => (isRefColumnMode() ? 6 : 5);
    const colPrice = () => 4;

    const editableColsForMode = () => {
        if (globalReadOnly) {
            return new Set();
        }
        if (projectSummaryReadOnly) {
            return new Set();
        }
        if (summaryMode) {
            return new Set([3, 4, 7, 8]);
        }
        if (!cabinetViewActive) {
            return new Set();
        }
        // Req 13.5: 可编辑字段 - 名称(1), 规格型号(2), 单位(3), 单价(4), 数量, 报价浮动, 厂家
        return isRefColumnMode()
            ? new Set([1, 2, 3, 4, 6, 8, 9])
            : new Set([1, 2, 3, 4, 5, 7, 8]);
    };

    const setMessage = (message, isError) => {
        if (!infoBarEl) {
            return;
        }
        infoBarEl.textContent = message || "";
        infoBarEl.classList.remove("alert-info", "alert-danger", "alert-success");
        infoBarEl.classList.add(isError ? "alert-danger" : "alert-success");
    };

    /** 标记为未保存状态 */
    const markDirty = () => {
        dirty = true;
        if (unsavedBadge) {
            unsavedBadge.classList.add("is-visible");
        }
    };

    /** 清除未保存状态 */
    const clearDirty = () => {
        dirty = false;
        if (unsavedBadge) {
            unsavedBadge.classList.remove("is-visible");
        }
    };

    /** 初始化 cabinetList：从 DOM 中读取已有的控制柜节点 */
    const initCabinetListFromDom = () => {
        cabinetList = [];
        if (!treeChildrenContainer) return;
        const buttons = treeChildrenContainer.querySelectorAll("button.tree-node-link[data-unit-no]");
        buttons.forEach((btn) => {
            const code = (btn.getAttribute("data-unit-no") || "").trim();
            const nameEl = btn.childNodes;
            // 提取名称文本（去掉编码部分）
            let name = btn.textContent.trim();
            // 去掉末尾的 (xxxx) 编码
            const codeMatch = name.match(/\(([^)]+)\)\s*$/);
            if (codeMatch) {
                name = name.replace(codeMatch[0], "").trim();
            }
            // 去掉前面的点号图标文本
            if (name.startsWith("•") || name.startsWith("·")) {
                name = name.substring(1).trim();
            }
            if (code) {
                cabinetList.push({ code, name });
            }
        });
    };

    /** 获取下一个可用的 4 位控制柜编码 */
    const getNextCabinetCode = () => {
        let maxCode = 0;
        cabinetList.forEach((cab) => {
            const num = parseInt(cab.code, 10);
            if (!isNaN(num) && num > maxCode) {
                maxCode = num;
            }
        });
        const next = maxCode + 1;
        return next.toString().padStart(4, "0");
    };

    /** 重新渲染目录树子节点 */
    const renderTreeChildren = () => {
        if (!treeChildrenContainer) return;
        treeChildrenContainer.innerHTML = "";
        if (cabinetList.length === 0) {
            const li = document.createElement("li");
            li.className = "text-muted small ms-4";
            li.textContent = "暂无控制柜节点";
            treeChildrenContainer.appendChild(li);
            return;
        }
        cabinetList.forEach((cab, idx) => {
            const li = document.createElement("li");
            li.className = "ms-4 mb-1";
            // Req 14.1: 支持拖拽排序（仅在可编辑模式下）
            if (!globalReadOnly) {
                li.draggable = true;
                li.setAttribute("data-drag-idx", idx.toString());
            }
            const btn = document.createElement("button");
            btn.type = "button";
            btn.className = "tree-node-link";
            btn.setAttribute("data-unit-no", cab.code);
            if (cab.code === selectedCabinetCode) {
                btn.classList.add("tree-node-link-usage");
            }
            btn.innerHTML = `<i class="bi bi-dot"></i>${escapeHtml(cab.name)}<span class="text-muted small ms-1">(${escapeHtml(cab.code)})</span>`;
            li.appendChild(btn);
            treeChildrenContainer.appendChild(li);
        });
    };

    /** 简单 HTML 转义 */
    const escapeHtml = (str) => {
        const div = document.createElement("div");
        div.textContent = str;
        return div.innerHTML;
    };

    // ========== 控制柜拖拽排序 (Req 14.1~14.5) ==========

    /** 当前拖拽的源索引 */
    let dragSourceIdx = -1;

    /** 清除所有拖拽视觉指示 */
    const clearDragIndicators = () => {
        if (!treeChildrenContainer) return;
        treeChildrenContainer.querySelectorAll("li").forEach((li) => {
            li.classList.remove("drag-source", "drag-over-top", "drag-over-bottom");
        });
    };

    /**
     * 拖拽完成后重新分配控制柜编号。
     * Req 14.2: 更新所有控制柜及下属元件的 x_bm 编号，确保按新顺序连续递增。
     */
    const reassignCabinetCodes = () => {
        const newCabinetDataMap = {};
        let newSelectedCode = "";
        cabinetList.forEach((cab, idx) => {
            const newCode = (idx + 1).toString().padStart(4, "0");
            const oldCode = cab.code;
            // 跟踪选中的控制柜新编码
            if (oldCode === selectedCabinetCode) {
                newSelectedCode = newCode;
            }
            // 迁移元件数据到新编码
            if (cabinetDataMap[oldCode] !== undefined) {
                newCabinetDataMap[newCode] = cabinetDataMap[oldCode];
            }
            cab.code = newCode;
        });
        // 替换整个 cabinetDataMap
        Object.keys(cabinetDataMap).forEach((k) => delete cabinetDataMap[k]);
        Object.keys(newCabinetDataMap).forEach((k) => {
            cabinetDataMap[k] = newCabinetDataMap[k];
        });
        // 更新选中状态
        selectedCabinetCode = newSelectedCode;
        if (currentCabinetUnitCode && newSelectedCode) {
            currentCabinetUnitCode = newSelectedCode;
        }
    };

    /** 初始化拖拽事件（使用事件委托绑定到容器） */
    const initTreeDragAndDrop = () => {
        if (!treeChildrenContainer || globalReadOnly) return;

        treeChildrenContainer.addEventListener("dragstart", (e) => {
            const li = e.target.closest("li[data-drag-idx]");
            if (!li) return;
            dragSourceIdx = parseInt(li.getAttribute("data-drag-idx"), 10);
            li.classList.add("drag-source");
            e.dataTransfer.effectAllowed = "move";
            e.dataTransfer.setData("text/plain", dragSourceIdx.toString());
        });

        treeChildrenContainer.addEventListener("dragover", (e) => {
            e.preventDefault();
            e.dataTransfer.dropEffect = "move";
            const li = e.target.closest("li[data-drag-idx]");
            if (!li) return;
            const targetIdx = parseInt(li.getAttribute("data-drag-idx"), 10);
            // Req 14.3: 仅允许同级排序 - 只在同一 ul 内拖拽
            if (targetIdx === dragSourceIdx) return;

            // 清除其他指示线
            treeChildrenContainer.querySelectorAll("li").forEach((el) => {
                el.classList.remove("drag-over-top", "drag-over-bottom");
            });

            // Req 14.5: 显示插入位置指示线
            const rect = li.getBoundingClientRect();
            const midY = rect.top + rect.height / 2;
            if (e.clientY < midY) {
                li.classList.add("drag-over-top");
            } else {
                li.classList.add("drag-over-bottom");
            }
        });

        treeChildrenContainer.addEventListener("dragleave", (e) => {
            const li = e.target.closest("li[data-drag-idx]");
            if (li) {
                li.classList.remove("drag-over-top", "drag-over-bottom");
            }
        });

        treeChildrenContainer.addEventListener("drop", (e) => {
            e.preventDefault();
            const li = e.target.closest("li[data-drag-idx]");
            if (!li) {
                clearDragIndicators();
                return;
            }
            let targetIdx = parseInt(li.getAttribute("data-drag-idx"), 10);
            if (dragSourceIdx === targetIdx || dragSourceIdx < 0) {
                clearDragIndicators();
                return;
            }

            // 判断插入位置：上方还是下方
            const rect = li.getBoundingClientRect();
            const midY = rect.top + rect.height / 2;
            const insertAfter = e.clientY >= midY;

            // 从 cabinetList 中取出拖拽项
            const [draggedItem] = cabinetList.splice(dragSourceIdx, 1);

            // 计算插入位置（splice 后索引可能变化）
            let insertIdx = targetIdx;
            if (dragSourceIdx < targetIdx) {
                insertIdx = insertAfter ? targetIdx : targetIdx - 1;
            } else {
                insertIdx = insertAfter ? targetIdx + 1 : targetIdx;
            }
            cabinetList.splice(insertIdx, 0, draggedItem);

            // Req 14.2: 重新分配编号
            reassignCabinetCodes();

            // 重新渲染树
            renderTreeChildren();

            // Req 14.4: 标记未保存状态
            markDirty();
            setMessage("控制柜顺序已调整，请点击保存以写入数据库。", false);

            clearDragIndicators();
            dragSourceIdx = -1;
        });

        treeChildrenContainer.addEventListener("dragend", (e) => {
            clearDragIndicators();
            dragSourceIdx = -1;
        });
    };

    /** 新增控制柜 */
    const addCabinet = () => {
        const code = getNextCabinetCode();
        const name = `控制柜${code}`;
        cabinetList.push({ code, name });
        cabinetDataMap[code] = [];
        renderTreeChildren();
        markDirty();
        setMessage(`已新增控制柜 ${name}（${code}），请点击保存以写入数据库。`, false);
    };

    /** 删除当前选中的控制柜 */
    const deleteCabinet = () => {
        if (!selectedCabinetCode) {
            setMessage("请先点击左侧目录树选中一个控制柜节点", true);
            return;
        }
        const idx = cabinetList.findIndex((c) => c.code === selectedCabinetCode);
        if (idx === -1) {
            setMessage("未找到选中的控制柜节点", true);
            return;
        }
        const cab = cabinetList[idx];
        if (!confirm(`确定要删除控制柜"${cab.name}（${cab.code}）"及其所有元件吗？`)) {
            return;
        }
        // 从内存中移除控制柜及其元件数据
        cabinetList.splice(idx, 1);
        delete cabinetDataMap[selectedCabinetCode];
        selectedCabinetCode = "";
        cabinetViewActive = false;
        currentCabinetUnitCode = "";
        hot.loadData([]);
        renderTreeChildren();
        markDirty();
        setMessage(`已删除控制柜"${cab.name}（${cab.code}）"及其下属元件，请点击保存以写入数据库。`, false);
    };

    /** 新增元件行（在表格末尾追加空行） */
    const addElementRow = () => {
        if (!cabinetViewActive) {
            setMessage("请先点击左侧控制柜节点加载元件数据", true);
            return;
        }
        const rowCount = hot.countRows();
        const newSeq = (rowCount + 1).toString();
        const colCount = isRefColumnMode() ? 10 : 9;
        const emptyRow = new Array(colCount).fill("");
        emptyRow[0] = newSeq; // 序号
        hot.alter("insert_row_below", rowCount, 1);
        hot.setDataAtRowProp(rowCount, 0, newSeq);
        // 使用 setDataAtCell 设置整行数据
        const changes = emptyRow.map((val, col) => [rowCount, col, val]);
        hot.setDataAtCell(changes, "addRow");
        // 同步 currentRowWzdh
        currentRowWzdh.push("");
        markDirty();
        setMessage(`已在末尾新增第 ${newSeq} 行，请填写元件信息。`, false);
    };

    /** 删除选中的元件行 */
    const deleteElementRows = () => {
        if (!cabinetViewActive) {
            setMessage("请先点击左侧控制柜节点加载元件数据", true);
            return;
        }
        const selected = hot.getSelected();
        if (!selected || selected.length === 0) {
            setMessage("请先在表格中选中要删除的行", true);
            return;
        }
        // 收集所有选中的行索引（去重并排序）
        const rowSet = new Set();
        for (const [r1, , r2] of selected) {
            const minR = Math.min(r1, r2);
            const maxR = Math.max(r1, r2);
            for (let r = minR; r <= maxR; r++) {
                rowSet.add(r);
            }
        }
        const rowsToDelete = Array.from(rowSet).sort((a, b) => b - a); // 从后往前删除
        if (rowsToDelete.length === 0) return;

        if (!confirm(`确定要删除选中的 ${rowsToDelete.length} 行吗？`)) {
            return;
        }

        // 从后往前删除以避免索引偏移
        for (const r of rowsToDelete) {
            hot.alter("remove_row", r, 1);
            currentRowWzdh.splice(r, 1);
        }
        markDirty();
        setMessage(`已删除 ${rowsToDelete.length} 行元件数据，请点击保存以写入数据库。`, false);
    };

    const getToken = () => {
        const tokenInput = saveForm ? saveForm.querySelector("input[name='__RequestVerificationToken']") : null;
        return tokenInput ? tokenInput.value : "";
    };

    const readJsonResponse = async (response, fallbackMessage) => {
        const text = await response.text();
        try {
            return JSON.parse(text);
        } catch {
            throw new Error(response.ok ? fallbackMessage : "请求返回了HTML页面，请检查登录状态或防伪令牌。");
        }
    };

    const syncRefPriceCheckboxUi = () => {
        if (!showRefPriceCb) {
            return;
        }
        // 当 referencePriceUrl 可用时，参考价格列始终显示，checkbox 保持选中且禁用
        if (referencePriceUrl) {
            showRefPriceCb.checked = cabinetViewActive;
            showRefPriceCb.disabled = true;
            return;
        }
        const enable = cabinetViewActive && !projectSummaryReadOnly;
        showRefPriceCb.disabled = !enable;
        if (!enable) {
            showRefPriceCb.checked = false;
        }
    };

    const applyButtonStates = () => {
        if (fillPriceActionButtons) {
            fillPriceActionButtons.classList.toggle("d-none", projectSummaryReadOnly || globalReadOnly);
        }
        if (saveSummaryBtn) {
            saveSummaryBtn.disabled = globalReadOnly || projectSummaryReadOnly || !(summaryMode && summaryDirty);
        }
        // 新增行/删除行按钮仅在柜体视图激活时可用
        if (addRowBtn) {
            addRowBtn.disabled = globalReadOnly || !cabinetViewActive;
        }
        if (deleteRowBtn) {
            deleteRowBtn.disabled = globalReadOnly || !cabinetViewActive;
        }
        syncRefPriceCheckboxUi();
    };

    const clearTreeHighlights = () => {
        if (!treeChildrenContainer) {
            return;
        }
        treeChildrenContainer.querySelectorAll("button.tree-node-link").forEach((btn) => {
            btn.classList.remove("tree-node-link-usage");
        });
    };

    const highlightTreeUnits = (unitCodes) => {
        clearTreeHighlights();
        if (!treeChildrenContainer) {
            return;
        }
        unitCodes.forEach((unitCode) => {
            const btn = treeChildrenContainer.querySelector(`button.tree-node-link[data-unit-no="${unitCode}"]`);
            if (btn) {
                btn.classList.add("tree-node-link-usage");
            }
        });
    };

    const hideUsagePanel = () => {
        clearTreeHighlights();
        if (!componentUsagePanel || !componentUsageTitle || !componentUsageList) {
            return;
        }
        componentUsagePanel.classList.add("d-none");
        componentUsageTitle.textContent = "";
        componentUsageList.innerHTML = "";
    };

    const renderUsagePanel = (summaryRow, usageRows) => {
        if (!componentUsagePanel || !componentUsageTitle || !componentUsageList) {
            return;
        }
        const title = `${summaryRow.name} / ${summaryRow.spec || "无规格"} / ${summaryRow.unit || "无单位"}`;
        componentUsageTitle.textContent = `${title}，共使用于 ${usageRows.length} 个控制柜。`;
        componentUsageList.innerHTML = "";
        if (usageRows.length === 0) {
            const li = document.createElement("li");
            li.className = "text-muted";
            li.textContent = "未找到使用该元件的控制柜。";
            componentUsageList.appendChild(li);
        } else {
            usageRows.forEach((item) => {
                const li = document.createElement("li");
                li.className = "mb-1";
                li.textContent = `${item.unitCode} ${item.unitName}（数量合计：${item.qty}）`;
                componentUsageList.appendChild(li);
            });
        }
        componentUsagePanel.classList.remove("d-none");
        highlightTreeUnits(usageRows.map((x) => x.unitCode));
    };

    const errorCellRenderer = (instance, td, row, col, prop, value, cellProperties) => {
        Handsontable.renderers.TextRenderer(instance, td, row, col, prop, value, cellProperties);
        // Root summary mode: style total row and subtotal column
        if (rootSummaryMode) {
            const totalRowIndex = instance.countRows() - 1;
            if (row === totalRowIndex) {
                // 合计行样式：加粗、浅蓝色背景
                td.style.backgroundColor = "#d6eaf8";
                td.style.color = "#1b4f72";
                td.style.fontWeight = "bold";
            } else if (col === 4) {
                // 金额小计列样式
                td.style.backgroundColor = "#d6eaf8";
                td.style.color = "#1b4f72";
                td.style.fontWeight = "600";
            } else {
                td.style.backgroundColor = "#eef1f4";
                td.style.color = "#495057";
                td.style.fontWeight = "";
            }
            td.style.border = "";
            return;
        }
        if (isRefColumnMode() && col === 5) {
            td.style.backgroundColor = "#d1e7dd";
            td.style.color = "#0a3622";
            td.style.fontWeight = "600";
            td.style.border = "";
            // 加载中状态使用斜体和较浅颜色
            if (value === "加载中...") {
                td.style.fontStyle = "italic";
                td.style.fontWeight = "normal";
                td.style.color = "#6c757d";
            } else {
                td.style.fontStyle = "";
            }
            return;
        }
        if (col === colAmount()) {
            td.style.backgroundColor = "#d6eaf8";
            td.style.color = "#1b4f72";
            td.style.fontWeight = "600";
            td.style.border = "";
            return;
        }
        const editable = editableColsForMode();
        const readOnlySummaryCell = summaryMode && (projectSummaryReadOnly || !editable.has(col));
        const readOnlyCabinetCell = !summaryMode && cabinetViewActive && !editable.has(col);
        if (readOnlySummaryCell || readOnlyCabinetCell) {
            td.style.backgroundColor = "#eef1f4";
            td.style.color = "#6c757d";
        } else {
            td.style.backgroundColor = "";
            td.style.color = "";
            td.style.fontWeight = "";
        }
        td.style.border = "";
    };

    const getCellsMeta = (row, col) => {
        if (rootSummaryMode) {
            return {
                renderer: errorCellRenderer,
                readOnly: true
            };
        }
        const editable = editableColsForMode();
        const readOnly = projectSummaryReadOnly || !editable.has(col);
        return {
            renderer: errorCellRenderer,
            readOnly
        };
    };

    const applyHotColumnLayout = () => {
        if (rootSummaryMode) {
            hot.updateSettings({
                colHeaders: ROOT_SUMMARY_HEADERS,
                columns: Array.from({ length: 5 }, () => ({ type: "text", renderer: errorCellRenderer })),
                cells: (row, col) => ({
                    renderer: errorCellRenderer,
                    readOnly: true
                })
            });
            return;
        }
        const nine = isRefColumnMode();
        const n = nine ? 10 : 9;
        const headers = nine ? BASE_HEADERS_9 : BASE_HEADERS_8;
        hot.updateSettings({
            colHeaders: headers,
            columns: Array.from({ length: n }, () => ({ type: "text", renderer: errorCellRenderer })),
            cells: (row, col) => getCellsMeta(row, col)
        });
    };

    const hot = new Handsontable(container, {
        data: [],
        rowHeaders: true,
        colHeaders: BASE_HEADERS_8,
        columns: Array.from({ length: 9 }, () => ({ type: "text", renderer: errorCellRenderer })),
        cells: (row, col) => getCellsMeta(row, col),
        stretchH: "all",
        width: "100%",
        height: "100%",
        minSpareRows: 0,
        licenseKey
    });

    hot.addHook("afterGetColHeader", (col, TH) => {
        TH.classList.remove("ht-ref-price-header");
        TH.classList.remove("ht-amount-header");
        if (isRefColumnMode() && col === 5) {
            TH.classList.add("ht-ref-price-header");
        }
        if (col === colAmount()) {
            TH.classList.add("ht-amount-header");
        }
    });

    const enterSummaryMode = () => {
        summaryMode = true;
        summaryDirty = false;
        refPriceColumnVisible = false;
        applyHotColumnLayout();
        hot.render();
        applyButtonStates();
    };

    const leaveSummaryMode = () => {
        summaryMode = false;
        rootSummaryMode = false;
        projectSummaryReadOnly = false;
        cabinetViewActive = false;
        refPriceColumnVisible = false;
        currentCabinetUnitCode = "";
        summaryDirty = false;
        summaryOriginalRows = [];
        currentReferencePriceData = [];
        hideUsagePanel();
        applyHotColumnLayout();
        hot.render();
        applyButtonStates();
    };

    const parseDecimalOrZero = (value) => {
        const num = Number((value ?? "").toString().trim());
        return Number.isFinite(num) ? num : 0;
    };

    const calcAmountValue = (price, fdds, qty) => {
        const p = parseDecimalOrZero(price);
        const f = parseDecimalOrZero(fdds);
        const q = parseDecimalOrZero(qty);
        if (p === 0 && f === 0 && q === 0) return "";
        const amount = p * (1 + f / 100) * q;
        return amount.toFixed(2);
    };

    const recalcAmount = (row) => {
        const data = hot.getData();
        if (row < 0 || row >= data.length) return;
        const rowData = data[row];
        if (!Array.isArray(rowData)) return;
        const cPrice = colPrice();
        const cQty = colQty();
        const cFloat = colFloat();
        const cAmt = colAmount();
        const amount = calcAmountValue(rowData[cPrice], rowData[cFloat], rowData[cQty]);
        hot.setDataAtCell(row, cAmt, amount, "recalcAmount");
    };

    const recalcAllAmounts = () => {
        const data = hot.getData();
        const cPrice = colPrice();
        const cQty = colQty();
        const cFloat = colFloat();
        const cAmt = colAmount();
        const changes = [];
        for (let r = 0; r < data.length; r++) {
            const rowData = data[r];
            if (!Array.isArray(rowData)) continue;
            const amount = calcAmountValue(rowData[cPrice], rowData[cFloat], rowData[cQty]);
            changes.push([r, cAmt, amount]);
        }
        if (changes.length > 0) {
            hot.setDataAtCell(changes, "recalcAmount");
        }
    };

    const mapComponentsToRows8 = (rows) => {
        currentRowWzdh = rows.map((x) => (x.x_wzdh ?? "").toString().trim());
        return rows.map((x) => ([
            (x.seq ?? "").toString(),
            (x.x_mc ?? "").toString(),
            (x.x_ggxh ?? "").toString(),
            (x.x_dw ?? "").toString(),
            (x.x_dj ?? "").toString(),
            (x.x_sl ?? "").toString(),
            calcAmountValue(x.x_dj, x.x_fdds, x.x_sl),
            (x.x_fdds ?? "").toString(),
            (x.x_sccj ?? "").toString()
        ]));
    };

    const mergeRowsWithRefBj = (rows8, refRows) =>
        rows8.map((row, i) => {
            const refObj = refRows[i];
            let refStr = "";
            if (refObj !== undefined && refObj !== null) {
                // Support both old format (refBj/RefBj) and new format (lastPrice/LastPrice)
                const refVal = refObj.lastPrice ?? refObj.LastPrice ?? refObj.refBj ?? refObj.RefBj ?? null;
                if (refVal !== null && refVal !== undefined && refVal !== "" && refVal !== 0) {
                    refStr = String(refVal);
                }
            }
            // DEBUG: 打印前3行的合并情况
            if (i < 3) {
                console.log(`[mergeRowsWithRefBj] row[${i}]: refObj=`, refObj, `refStr="${refStr}"`);
            }
            const next = row.slice(0, 5);
            next.push(refStr);
            next.push(row[5], row[6], row[7], row[8]);
            return next;
        });

    const fetchReferenceBjRows = async (unitCode) => {
        if (!cabinetRefBjUrl) {
            throw new Error("缺少参考价格接口地址");
        }
        const conn = cabinetRefBjUrl.includes("?") ? "&" : "?";
        const response = await fetch(
            `${cabinetRefBjUrl}${conn}unitCode=${encodeURIComponent(unitCode)}`,
            { method: "GET" }
        );
        const result = await response.json();
        if (!response.ok || !result.success) {
            throw new Error(result.message || "读取参考价格失败");
        }
        return Array.isArray(result.rows) ? result.rows : [];
    };

    /**
     * 加载参考价格数据（来自 STD_PRICE_HISTORY）。
     * 返回与 GetCabinetComponents 行序一一对应的数组，无匹配记录的位置为 null。
     * 同时将完整数据存储到 currentReferencePriceData 供 tooltip 使用。
     */
    const loadReferencePrice = async (unitCode) => {
        if (!referencePriceUrl) {
            throw new Error("缺少参考价格接口地址");
        }
        const conn = referencePriceUrl.includes("?") ? "&" : "?";
        const response = await fetch(
            `${referencePriceUrl}${conn}unitCode=${encodeURIComponent(unitCode)}`,
            { method: "GET" }
        );
        const result = await response.json();
        if (!response.ok || !result.success) {
            throw new Error(result.message || "读取参考价格失败");
        }
        const rows = Array.isArray(result.rows) ? result.rows : [];
        // DEBUG: 打印 API 返回的参考价格数据
        console.log(`[loadReferencePrice] API 返回 ${rows.length} 行, 非null行数=${rows.filter(r => r !== null).length}`, rows.slice(0, 5));
        // 存储完整参考价格数据供 tooltip 使用
        currentReferencePriceData = rows;
        return rows;
    };

    const loadCabinetGrid = async (unitCode, wantRefColumn) => {
        const target = (unitCode || "").trim();
        console.log(`[loadCabinetGrid] unitCode=${target}, wantRefColumn=${wantRefColumn}, referencePriceUrl="${referencePriceUrl}", cabinetRefBjUrl="${cabinetRefBjUrl}"`);
        const connector = componentsUrl.includes("?") ? "&" : "?";
        const response = await fetch(`${componentsUrl}${connector}unitCode=${encodeURIComponent(target)}`, { method: "GET" });
        const result = await response.json();
        if (!response.ok || !result.success) {
            throw new Error(result.message || "读取柜内元件失败");
        }
        const rows = Array.isArray(result.rows) ? result.rows : [];
        const mapped8 = mapComponentsToRows8(rows);

        if (!wantRefColumn || (!referencePriceUrl && !cabinetRefBjUrl)) {
            refPriceColumnVisible = false;
            currentReferencePriceData = [];
            applyHotColumnLayout();
            hot.loadData(mapped8);
            return;
        }

        // 显示加载状态：先渲染表格，参考价格列显示"加载中..."
        refPriceColumnVisible = true;
        applyHotColumnLayout();
        const loadingRows = mapped8.map((row) => {
            const next = row.slice(0, 5);
            next.push("加载中...");
            next.push(row[5], row[6], row[7], row[8]);
            return next;
        });
        hot.loadData(loadingRows);

        // 请求参考价格数据
        try {
            let refRows;
            if (referencePriceUrl) {
                refRows = await loadReferencePrice(target);
            } else {
                refRows = await fetchReferenceBjRows(target);
                currentReferencePriceData = [];
            }
            // DEBUG: 确认 merge 前的数据
            console.log(`[loadCabinetGrid] mapped8 行数=${mapped8.length}, refRows 行数=${refRows.length}`);
            const merged = mergeRowsWithRefBj(mapped8, refRows);
            console.log(`[loadCabinetGrid] merged 行数=${merged.length}, 第一行列数=${merged[0]?.length}`, merged[0]);
            hot.loadData(merged);
        } catch (err) {
            // 参考价格加载失败时，清除加载状态，显示空列
            console.error("[loadCabinetGrid] 参考价格加载失败:", err);
            currentReferencePriceData = [];
            const emptyRefRows = mapped8.map((row) => {
                const next = row.slice(0, 5);
                next.push("");
                next.push(row[5], row[6], row[7], row[8]);
                return next;
            });
            hot.loadData(emptyRefRows);
        }
    };

    const identityFromHotRow = (rowIndex) => {
        const data = hot.getData();
        const row = Array.isArray(data[rowIndex]) ? data[rowIndex] : [];
        const cf = colFloat();
        const cv = colVendor();
        const cq = colQty();
        return {
            name: (row[1] ?? "").toString().trim(),
            spec: (row[2] ?? "").toString().trim(),
            unit: (row[3] ?? "").toString().trim(),
            price: parseDecimalOrZero(row[4]),
            qty: parseDecimalOrZero(row[cq]),
            floatRate: parseDecimalOrZero(row[cf]),
            vendor: (row[cv] ?? "").toString().trim()
        };
    };

    const fetchComponentUsageByIdentity = async (identity) => {
        if (!projectUsageUrl) {
            return;
        }
        try {
            const connector = projectUsageUrl.includes("?") ? "&" : "?";
            const query = new URLSearchParams({
                name: identity.name,
                spec: identity.spec,
                unit: identity.unit,
                price: identity.price.toString(),
                floatRate: identity.floatRate.toString(),
                vendor: identity.vendor
            });
            const response = await fetch(`${projectUsageUrl}${connector}${query.toString()}`, { method: "GET" });
            const result = await response.json();
            if (!response.ok || !result.success) {
                throw new Error(result.message || "读取元件使用控制柜失败");
            }
            renderUsagePanel(identity, Array.isArray(result.rows) ? result.rows : []);
        } catch (error) {
            const message = error instanceof Error ? error.message : "读取元件使用控制柜失败";
            setMessage(message, true);
        }
    };

    const getSummaryChangedItems = () => {
        const data = hot.getData();
        const changed = [];
        const count = Math.min(data.length, summaryOriginalRows.length);
        for (let i = 0; i < count; i += 1) {
            const row = Array.isArray(data[i]) ? data[i] : [];
            const oldRow = summaryOriginalRows[i];
            if (!oldRow) {
                continue;
            }
            const newUnit = (row[3] ?? "").toString().trim();
            const newPrice = parseDecimalOrZero(row[4]);
            const newFloatRate = parseDecimalOrZero(row[7]);
            const newVendor = (row[8] ?? "").toString().trim();
            const changedFlag = newUnit !== oldRow.unit
                || newPrice !== oldRow.price
                || newFloatRate !== oldRow.floatRate
                || newVendor !== oldRow.vendor;
            if (!changedFlag) {
                continue;
            }
            changed.push({
                matchKey: oldRow.matchKey,
                newUnit,
                newPrice,
                newFloatRate,
                newVendor
            });
        }
        return changed;
    };

    const loadCabinetComponents = async (unitNo) => {
        const target = (unitNo || "").trim();
        if (!target || !componentsUrl) {
            return;
        }
        try {
            setMessage(`正在加载节点 ${target} 的柜内元件...`, false);
            leaveSummaryMode();
            currentCabinetUnitCode = target;
            cabinetViewActive = true;
            // 当 referencePriceUrl 可用时，始终加载参考价格列（Req 6.1, 6.2）
            const wantRef = referencePriceUrl
                ? true
                : !!(showRefPriceCb && showRefPriceCb.checked);
            await loadCabinetGrid(target, wantRef);
            hot.render();
            applyButtonStates();
            // Req 9.6: 数据加载完成后触发价格异常检测着色
            applyPriceAnomalyStyles();
            setMessage(
                `已加载 ${target} 柜内元件清单，共 ${hot.countRows()} 条。`,
                false
            );
        } catch (error) {
            const message = error instanceof Error ? error.message : "读取柜内元件失败";
            setMessage(message, true);
        }
    };

    const loadProjectComponentSummary = async () => {
        if (!projectSummaryUrl) {
            return;
        }
        try {
            cabinetViewActive = false;
            currentCabinetUnitCode = "";
            refPriceColumnVisible = false;
            rootSummaryMode = true;
            applyHotColumnLayout();
            hideUsagePanel();
            setMessage("正在加载项目全部控制柜元件汇总...", false);
            const response = await fetch(projectSummaryUrl, { method: "GET" });
            const result = await response.json();
            if (!response.ok || !result.success) {
                throw new Error(result.message || "读取项目元件汇总失败");
            }
            const rows = Array.isArray(result.rows) ? result.rows : [];

            // Req 15.2: 按 x_mc、x_ggxh、x_dj 分组，显示合计数量
            const groupMap = new Map();
            rows.forEach((x) => {
                const name = (x.x_mc ?? "").toString().trim();
                const spec = (x.x_ggxh ?? "").toString().trim();
                const price = Number(x.x_dj ?? 0);
                const qty = Number(x.x_sl ?? 0);
                const key = `${name}\x00${spec}\x00${price}`;
                if (groupMap.has(key)) {
                    groupMap.get(key).qty += qty;
                } else {
                    groupMap.set(key, { name, spec, price, qty });
                }
            });

            // Req 15.3: 显示列：元件名称、规格型号、单价、合计数量、金额小计
            let totalAmount = 0;
            const mapped = [];
            for (const group of groupMap.values()) {
                const subtotal = group.price * group.qty;
                totalAmount += subtotal;
                mapped.push([
                    group.name,
                    group.spec,
                    group.price === 0 ? "" : group.price.toFixed(4).replace(/\.?0+$/, ""),
                    group.qty === 0 ? "" : group.qty.toFixed(4).replace(/\.?0+$/, ""),
                    subtotal === 0 ? "" : subtotal.toFixed(2)
                ]);
            }

            // Req 15.4: 底部显示总金额合计行
            mapped.push([
                "合计",
                "",
                "",
                "",
                totalAmount === 0 ? "" : totalAmount.toFixed(2)
            ]);

            // 保存原始行数据供 usage panel 使用
            summaryOriginalRows = rows.map((x) => ({
                matchKey: (x.matchKey ?? "").toString(),
                name: (x.x_mc ?? "").toString().trim(),
                spec: (x.x_ggxh ?? "").toString().trim(),
                unit: (x.x_dw ?? "").toString().trim(),
                price: Number(x.x_dj ?? 0),
                floatRate: Number(x.x_fdds ?? 0),
                vendor: (x.x_sccj ?? "").toString().trim()
            }));

            hot.loadData(mapped);
            // Req 15.5: 只读状态
            projectSummaryReadOnly = true;
            enterSummaryMode();
            setMessage(`已加载项目元件汇总，共 ${mapped.length - 1} 组（按名称/规格/单价分组），总金额：¥${totalAmount.toFixed(2)}`, false);
        } catch (error) {
            const message = error instanceof Error ? error.message : "读取项目元件汇总失败";
            rootSummaryMode = false;
            setMessage(message, true);
        }
    };

    const loadProjectComponentUsage = async (rowIndex) => {
        if (!summaryMode || rowIndex < 0 || rowIndex >= summaryOriginalRows.length) {
            return;
        }
        const row = summaryOriginalRows[rowIndex];
        await fetchComponentUsageByIdentity({
            name: row.name,
            spec: row.spec,
            unit: row.unit,
            price: row.price,
            floatRate: row.floatRate,
            vendor: row.vendor
        });
    };

    const loadCabinetComponentUsage = async (rowIndex) => {
        if (!cabinetViewActive || rowIndex < 0 || rowIndex >= hot.countRows()) {
            return;
        }
        const identity = identityFromHotRow(rowIndex);
        await fetchComponentUsageByIdentity(identity);
    };

    if (showRefPriceCb) {
        showRefPriceCb.addEventListener("change", async () => {
            if (!cabinetViewActive || !currentCabinetUnitCode || !componentsUrl) {
                return;
            }
            try {
                if (showRefPriceCb.checked) {
                    setMessage("正在加载参考价格...", false);
                    await loadCabinetGrid(currentCabinetUnitCode, true);
                } else {
                    refPriceColumnVisible = false;
                    await loadCabinetGrid(currentCabinetUnitCode, false);
                }
                cabinetViewActive = true;
                hot.render();
                applyButtonStates();
                setMessage(showRefPriceCb.checked ? "已显示参考价格列。" : "已隐藏参考价格列。", false);
            } catch (error) {
                showRefPriceCb.checked = !showRefPriceCb.checked;
                const message = error instanceof Error ? error.message : "切换参考价格失败";
                setMessage(message, true);
            }
        });
    }

    if (treeChildrenContainer) {
        treeChildrenContainer.addEventListener("click", (event) => {
            const trigger = event.target.closest("[data-unit-no]");
            if (!trigger) {
                return;
            }
            const unitNo = trigger.getAttribute("data-unit-no") || "";
            selectedCabinetCode = unitNo;
            // 高亮选中节点
            treeChildrenContainer.querySelectorAll("button.tree-node-link").forEach((btn) => {
                btn.classList.remove("tree-node-link-usage");
            });
            trigger.classList.add("tree-node-link-usage");
            loadCabinetComponents(unitNo);
        });
    }

    if (treeRootNode) {
        treeRootNode.addEventListener("click", () => {
            loadProjectComponentSummary();
        });
    }

    hot.addHook("afterSelectionEnd", (row, column, row2, column2) => {
        const r2 = row2 !== undefined && row2 !== null ? row2 : row;
        const r = Math.min(row, r2);
        if (!Number.isFinite(r) || r < 0 || r >= hot.countRows()) {
            return;
        }
        if (rootSummaryMode) {
            // Root summary mode: no usage panel (data is grouped differently)
            return;
        }
        if (summaryMode) {
            loadProjectComponentUsage(r);
        } else if (cabinetViewActive) {
            loadCabinetComponentUsage(r);
        }
    });

    hot.addHook("afterChange", (changes, source) => {
        if (!changes || source === "loadData" || source === "recalcAmount" || source === "addRow") {
            return;
        }
        if (summaryMode) {
            summaryDirty = true;
            applyButtonStates();
            return;
        }
        if (!cabinetViewActive) return;

        // Req 13.6: 元件字段编辑时标记未保存
        markDirty();

        const triggerCols = new Set([colPrice(), colQty(), colFloat()]);
        const rowsToRecalc = new Set();
        let priceChanged = false;
        for (const [row, col] of changes) {
            if (triggerCols.has(col)) {
                rowsToRecalc.add(row);
            }
            if (col === colPrice()) {
                priceChanged = true;
            }
        }
        if (rowsToRecalc.size > 0) {
            const cPrice = colPrice();
            const cQty = colQty();
            const cFloat = colFloat();
            const cAmt = colAmount();
            const data = hot.getData();
            const amtChanges = [];
            for (const r of rowsToRecalc) {
                if (r < 0 || r >= data.length) continue;
                const rowData = data[r];
                if (!Array.isArray(rowData)) continue;
                const amount = calcAmountValue(rowData[cPrice], rowData[cFloat], rowData[cQty]);
                amtChanges.push([r, cAmt, amount]);
            }
            if (amtChanges.length > 0) {
                hot.setDataAtCell(amtChanges, "recalcAmount");
            }
        }
        // Req 9.6: 单价单元格值变更后触发价格异常检测着色
        if (priceChanged) {
            applyPriceAnomalyStyles();
        }
    });

    if (treePaneEl && splitterEl && toggleTreeBtn) {
        let dragging = false;
        let startX = 0;
        let startWidth = treePaneEl.getBoundingClientRect().width;
        let collapsed = false;
        const refreshToggleText = () => {
            toggleTreeBtn.innerHTML = collapsed
                ? '<i class="bi bi-layout-sidebar me-1"></i>显示目录树'
                : '<i class="bi bi-layout-sidebar-inset me-1"></i>隐藏目录树';
        };
        const setCollapsed = (nextCollapsed) => {
            collapsed = nextCollapsed;
            treePaneEl.classList.toggle("is-collapsed", collapsed);
            splitterEl.classList.toggle("is-hidden", collapsed);
            refreshToggleText();
            hot.refreshDimensions();
        };
        splitterEl.addEventListener("mousedown", (event) => {
            if (collapsed) {
                return;
            }
            dragging = true;
            startX = event.clientX;
            startWidth = treePaneEl.getBoundingClientRect().width;
            document.body.style.cursor = "col-resize";
            event.preventDefault();
        });
        window.addEventListener("mousemove", (event) => {
            if (!dragging || collapsed) {
                return;
            }
            const delta = event.clientX - startX;
            const workspace = treePaneEl.parentElement;
            if (!workspace) {
                return;
            }
            const workspaceWidth = workspace.getBoundingClientRect().width;
            const minWidth = 220;
            const maxWidth = Math.max(minWidth, workspaceWidth * 0.6);
            const nextWidth = Math.min(maxWidth, Math.max(minWidth, startWidth + delta));
            treePaneEl.style.width = `${nextWidth}px`;
            hot.refreshDimensions();
        });
        window.addEventListener("mouseup", () => {
            if (!dragging) {
                return;
            }
            dragging = false;
            document.body.style.cursor = "";
            hot.refreshDimensions();
        });
        toggleTreeBtn.addEventListener("click", () => setCollapsed(!collapsed));
        refreshToggleText();
    }

    /**
     * 保存前校验：检测负数价格。
     * 遍历所有行，若存在单价 < 0 的元件，返回校验失败并列出负价格元件信息。
     * Req 10.3: 前端在保存前进行客户端校验，阻止提交并在信息栏显示错误提示。
     * @returns {{ valid: boolean, negativeItems: Array<{row: number, name: string, spec: string}> }}
     */
    const validateBeforeSave = () => {
        const data = hot.getData();
        const negativeItems = [];
        const cPrice = colPrice();

        for (let r = 0; r < data.length; r++) {
            const rowData = data[r];
            if (!Array.isArray(rowData)) continue;
            const price = parseDecimalOrZero(rowData[cPrice]);
            if (price < 0) {
                const name = (rowData[1] || "").toString().trim();
                const spec = (rowData[2] || "").toString().trim();
                negativeItems.push({ row: r, name, spec });
            }
        }

        if (negativeItems.length > 0) {
            return { valid: false, negativeItems };
        }
        return { valid: true, negativeItems: [] };
    };

    if (saveSummaryBtn) {
        saveSummaryBtn.addEventListener("click", async () => {
            if (!summaryMode || !saveProjectSummaryUrl) {
                return;
            }
            // Req 10.3: 保存前校验负数价格
            const validation = validateBeforeSave();
            if (!validation.valid) {
                const itemList = validation.negativeItems
                    .map((item) => `${item.name || "未命名"}(第${item.row + 1}行)`)
                    .join("、");
                setMessage(`以下元件单价为负数，无法保存：${itemList}`, true);
                return;
            }

            const changes = getSummaryChangedItems();
            if (changes.length === 0) {
                setMessage("没有可保存的修改。", false);
                summaryDirty = false;
                applyButtonStates();
                return;
            }
            try {
                setMessage("正在保存元件汇总修改...", false);
                const response = await fetch(saveProjectSummaryUrl, {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json",
                        ...(getToken() ? { RequestVerificationToken: getToken() } : {})
                    },
                    body: JSON.stringify({ items: changes })
                });
                const result = await readJsonResponse(response, "保存数据失败");
                if (!response.ok || !result.success) {
                    throw new Error(result.message || "保存数据失败");
                }
                await loadProjectComponentSummary();
                setMessage(result.message || "保存数据成功。", false);
            } catch (error) {
                const message = error instanceof Error ? error.message : "保存数据失败";
                setMessage(message, true);
            }
        });
    }

    /**
     * 价格异常检测着色逻辑。
     * 遍历所有行，根据单价值应用不同背景色：
     * - 单价为 0 或空：浅灰色背景 (#f0f0f0)
     * - 单价为负数：红色背景 (#f8d7da)
     * - 单价偏离历史均价超过 ±20%：橙色背景 (#fff3cd)
     * 优先级：负数(红) > 偏离(橙) > 空/零(灰)
     * Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.6
     */
    const applyPriceAnomalyStyles = () => {
        if (!cabinetViewActive || summaryMode) return;

        const data = hot.getData();
        const cPrice = colPrice();

        for (let r = 0; r < data.length; r++) {
            const rowData = data[r];
            if (!Array.isArray(rowData)) continue;

            const rawValue = rowData[cPrice];
            const priceNum = parseDecimalOrZero(rawValue);

            let className = "";

            if (priceNum < 0) {
                // Req 9.2: 负数 → 红色背景（最高优先级）
                className = "ht-price-negative";
            } else if (priceNum > 0) {
                // 有正价格时检查偏离
                // Req 9.3, 9.4: 偏离历史均价超过 ±20% → 橙色背景
                const refData = currentReferencePriceData[r];
                if (refData) {
                    const avgPrice = parseFloat(refData.avgPrice ?? refData.AvgPrice ?? 0);
                    if (avgPrice > 0) {
                        const deviation = Math.abs(priceNum - avgPrice) / avgPrice;
                        if (deviation > 0.2) {
                            className = "ht-price-deviation";
                        }
                    }
                }
            } else {
                // Req 9.1: 单价为 0 或空 → 浅灰色背景
                className = "ht-price-empty";
            }

            hot.setCellMeta(r, cPrice, "className", className);
        }

        hot.render();
    };

    /**
     * 将历史价格数据应用到当前表格。
     * 遍历所有行，按 x_wzdh 匹配并填充空价格、空单位、空厂家单元格。
     * 填充后自动重算金额列。
     * @param {Object} prices - 价格映射 { x_wzdh: { price, unit, vendor } }
     * @returns {{ matched: number, unmatched: number, total: number, filled: number }}
     */
    const applyPriceToTable = (prices) => {
        const data = hot.getData();
        const changes = [];
        const filledRows = [];
        let matched = 0;
        let unmatched = 0;
        let total = 0;

        const colPrice = 4;
        const colUnit = 3;
        const cv = colVendor();

        for (let r = 0; r < data.length; r++) {
            const wzdh = (currentRowWzdh[r] || "").toString().trim();

            // Req 3.7: x_wzdh 为空或 null 计入未匹配
            if (!wzdh) {
                total++;
                unmatched++;
                continue;
            }

            total++;
            const hist = prices[wzdh];

            if (!hist) {
                unmatched++;
                continue;
            }

            matched++;

            const currentPrice = parseDecimalOrZero(data[r][colPrice]);
            const currentUnit = (data[r][colUnit] || "").toString().trim();
            const currentVendorVal = (data[r][cv] || "").toString().trim();

            // Req 3.5: 保留已有非零价格
            // Req 3.2: 单价为 0/null/空 且匹配到历史价格时填充
            if (currentPrice === 0 && hist.price > 0) {
                changes.push([r, colPrice, hist.price.toString()]);
                filledRows.push(r);
            }

            // Req 3.3: 空单位填充
            if (!currentUnit && hist.unit) {
                changes.push([r, colUnit, hist.unit]);
            }

            // Req 3.4: 空厂家填充
            if (!currentVendorVal && hist.vendor) {
                changes.push([r, cv, hist.vendor]);
            }
        }

        // Req 3.8: 使用 setDataAtCell 触发变更事件
        if (changes.length > 0) {
            hot.setDataAtCell(changes);
        }

        // Req 3.9: 填充价格后自动重算金额列
        filledRows.forEach((row) => recalcAmount(row));

        return { matched, unmatched, total, filled: filledRows.length };
    };

    /**
     * 自动填价核心函数：调用后端接口获取历史价格并填充表格。
     * 包含 30 秒超时处理和完整的状态管理。
     * Req 4.7: 支持重复点击（幂等），每次使用最新结果，仅填充单价为0的行。
     */
    let _fillPriceAbortController = null;

    const autoFillPrice = async () => {
        const autoFillPriceBtnEl = document.getElementById("auto-fill-price-btn");
        const url = autoFillPriceBtnEl ? autoFillPriceBtnEl.dataset.autoFillUrl : "";
        const fabh = autoFillPriceBtnEl ? autoFillPriceBtnEl.dataset.quotationNo : "";
        if (!url || !fabh) return;

        // Req 4.2: 未加载数据时提示
        if (!cabinetViewActive) {
            setMessage("请先点击左侧控制柜节点加载元件数据", true);
            return;
        }

        // Req 4.7: 重复点击时中止上一次未完成的请求
        if (_fillPriceAbortController) {
            _fillPriceAbortController.abort();
            _fillPriceAbortController = null;
        }

        // Req 4.3: 禁用按钮 + 显示加载提示
        if (autoFillPriceBtnEl) autoFillPriceBtnEl.disabled = true;
        setMessage("正在匹配历史报价...", false);

        // Req 4.6: 30 秒超时
        const controller = new AbortController();
        _fillPriceAbortController = controller;
        const timeoutId = setTimeout(() => controller.abort(), 30000);

        try {
            const token = getToken();
            const response = await fetch(url, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    ...(token ? { RequestVerificationToken: token } : {})
                },
                body: JSON.stringify({ fabh }),
                signal: controller.signal
            });

            clearTimeout(timeoutId);

            const result = await readJsonResponse(response, "自动填价失败");
            if (!result.success) {
                setMessage(result.message || "自动填价失败", true);
                return;
            }

            const prices = result.prices || {};

            // 应用价格到表格
            const stats = applyPriceToTable(prices);

            // Req 9.6: 自动填价完成后触发价格异常检测着色
            applyPriceAnomalyStyles();

            // Req 3.6: 显示匹配统计信息
            const msg = `已匹配 ${stats.matched}/${stats.total} 个元件的历史报价，${stats.unmatched} 个元件无历史记录`;
            setMessage(msg, false);
        } catch (error) {
            clearTimeout(timeoutId);
            if (error instanceof Error && error.name === "AbortError") {
                // 区分超时中止和重复点击中止
                if (_fillPriceAbortController !== controller) {
                    // 被新请求取代，不显示错误（新请求会接管状态）
                    return;
                }
                setMessage("自动填价请求超时（30秒），请稍后重试", true);
            } else {
                const message = error instanceof Error ? error.message : "自动填价请求失败，请检查网络";
                setMessage(message, true);
            }
        } finally {
            // Req 4.4/4.5: 恢复按钮可用状态
            if (_fillPriceAbortController === controller) {
                _fillPriceAbortController = null;
            }
            if (autoFillPriceBtnEl) autoFillPriceBtnEl.disabled = false;
        }
    };

    // 引用历史报价按钮
    const autoFillPriceBtn = document.getElementById("auto-fill-price-btn");
    if (autoFillPriceBtn) {
        autoFillPriceBtn.addEventListener("click", () => autoFillPrice());
    }

    // ========== 参考价格 Tooltip (Req 6.5, 6.6) ==========

    /** 创建或获取 tooltip DOM 元素 */
    const getOrCreateTooltipEl = () => {
        let el = document.getElementById("ref-price-tooltip");
        if (!el) {
            el = document.createElement("div");
            el.id = "ref-price-tooltip";
            el.style.cssText = [
                "position:fixed",
                "z-index:9999",
                "background:#fff",
                "border:1px solid #ccc",
                "border-radius:4px",
                "box-shadow:0 2px 8px rgba(0,0,0,0.15)",
                "padding:8px 12px",
                "font-size:13px",
                "line-height:1.6",
                "pointer-events:none",
                "display:none",
                "white-space:pre-line",
                "max-width:220px"
            ].join(";");
            document.body.appendChild(el);
        }
        return el;
    };

    /**
     * 显示参考价格 tooltip。
     * 当鼠标悬停在参考价格列单元格上时，显示均价、最低价、最高价、样本数。
     * 无匹配记录时不显示 tooltip。
     * @param {number} row - 行索引
     * @param {number} col - 列索引
     * @param {HTMLElement} td - 单元格 DOM 元素
     */
    const showPriceTooltip = (row, col, td) => {
        const tooltipEl = getOrCreateTooltipEl();

        // 仅在参考价格列模式下且悬停在参考价格列（col=5）时显示
        if (!isRefColumnMode() || col !== 5) {
            tooltipEl.style.display = "none";
            return;
        }

        // 获取该行的参考价格数据
        const refData = currentReferencePriceData[row];

        // Req 6.6: 无匹配记录时不显示 tooltip
        if (!refData) {
            tooltipEl.style.display = "none";
            return;
        }

        // 格式化 tooltip 内容
        const avgPrice = refData.avgPrice ?? refData.AvgPrice ?? refData.avg_price;
        const minPrice = refData.minPrice ?? refData.MinPrice ?? refData.min_price;
        const maxPrice = refData.maxPrice ?? refData.MaxPrice ?? refData.max_price;
        const avgCount = refData.avgCount ?? refData.AvgCount ?? refData.avg_count;

        const formatPrice = (val) => {
            if (val === null || val === undefined) return "—";
            return "¥" + Number(val).toFixed(2);
        };

        const content = [
            `均价: ${formatPrice(avgPrice)}`,
            `最低价: ${formatPrice(minPrice)}`,
            `最高价: ${formatPrice(maxPrice)}`,
            `样本数: ${avgCount ?? 0}`
        ].join("\n");

        tooltipEl.textContent = content;
        tooltipEl.style.display = "block";

        // 定位 tooltip：在单元格下方偏右
        if (td) {
            const rect = td.getBoundingClientRect();
            tooltipEl.style.left = (rect.left + 4) + "px";
            tooltipEl.style.top = (rect.bottom + 4) + "px";
        }
    };

    /**
     * 显示异常价格 tooltip。
     * 当鼠标悬停在异常标记的单价单元格上时，显示异常原因。
     * - 负数："价格为负数，请修正"
     * - 偏离："偏离历史均价 ¥X.XX 超过 20%"
     * Validates: Requirements 9.5
     * @param {number} row - 行索引
     * @param {number} col - 列索引
     * @param {HTMLElement} td - 单元格 DOM 元素
     * @returns {boolean} 是否显示了 tooltip
     */
    const showAnomalyTooltip = (row, col, td) => {
        const tooltipEl = getOrCreateTooltipEl();

        // 仅在柜体视图下且悬停在单价列（col 4）时处理
        if (!cabinetViewActive || summaryMode || col !== colPrice()) {
            return false;
        }

        // 检查单元格是否有异常 class
        const cellMeta = hot.getCellMeta(row, col);
        const className = (cellMeta.className || "").toString();

        if (className.indexOf("ht-price-negative") !== -1) {
            // Req 9.5: 负数价格 tooltip
            tooltipEl.textContent = "价格为负数，请修正";
            tooltipEl.style.display = "block";
            if (td) {
                const rect = td.getBoundingClientRect();
                tooltipEl.style.left = (rect.left + 4) + "px";
                tooltipEl.style.top = (rect.bottom + 4) + "px";
            }
            return true;
        }

        if (className.indexOf("ht-price-deviation") !== -1) {
            // Req 9.5: 偏离历史均价 tooltip
            const refData = currentReferencePriceData[row];
            if (refData) {
                const avgPrice = parseFloat(refData.avgPrice ?? refData.AvgPrice ?? refData.avg_price ?? 0);
                if (avgPrice > 0) {
                    tooltipEl.textContent = `偏离历史均价 ¥${avgPrice.toFixed(2)} 超过 20%`;
                    tooltipEl.style.display = "block";
                    if (td) {
                        const rect = td.getBoundingClientRect();
                        tooltipEl.style.left = (rect.left + 4) + "px";
                        tooltipEl.style.top = (rect.bottom + 4) + "px";
                    }
                    return true;
                }
            }
        }

        return false;
    };

    /** 隐藏 tooltip */
    const hidePriceTooltip = () => {
        const tooltipEl = document.getElementById("ref-price-tooltip");
        if (tooltipEl) {
            tooltipEl.style.display = "none";
        }
    };

    // 注册 Handsontable 鼠标悬停事件
    hot.addHook("afterOnCellMouseOver", (event, coords, td) => {
        // 先尝试显示异常价格 tooltip（单价列），再尝试参考价格 tooltip（参考价格列）
        if (!showAnomalyTooltip(coords.row, coords.col, td)) {
            showPriceTooltip(coords.row, coords.col, td);
        }
    });

    hot.addHook("afterOnCellMouseOut", (event, coords, td) => {
        hidePriceTooltip();
    });

    // ========== 控制柜和元件增删改 (Req 13.1~13.6) ==========

    // 初始化控制柜列表
    initCabinetListFromDom();

    // 初始化拖拽排序 (Req 14.1~14.5)
    initTreeDragAndDrop();

    // 新增控制柜按钮
    if (addCabinetBtn) {
        addCabinetBtn.addEventListener("click", () => addCabinet());
    }

    // 删除控制柜按钮
    if (deleteCabinetBtn) {
        deleteCabinetBtn.addEventListener("click", () => deleteCabinet());
    }

    // 新增元件行按钮
    if (addRowBtn) {
        addRowBtn.addEventListener("click", () => addElementRow());
    }

    // 删除元件行按钮
    if (deleteRowBtn) {
        deleteRowBtn.addEventListener("click", () => deleteElementRows());
    }

    applyButtonStates();
})();
