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
    const saveSummaryBtn = document.getElementById("save-summary-btn"); // 已移除，保留 null 兼容旧代码
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
    const colorLegendEl = document.getElementById("color-legend");
    const currentNodeLabelEl = document.getElementById("current-node-label");
    const currentNodeTotalEl = document.getElementById("current-node-total");
    const quotationName = (document.getElementById("tree-root-node")?.textContent || "").trim();
    // fill-progress-dashboard spec / B-T5: 进度看板徽章 + 问题抽屉 DOM
    const fillProgressBadgeEl = document.getElementById("fill-progress-badge");
    const progressFilledEl = document.getElementById("progress-filled");
    const progressTotalEl = document.getElementById("progress-total");
    const progressPercentEl = document.getElementById("progress-percent");
    const anomalyCountEl = document.getElementById("anomaly-count");
    const problemListToggleEl = document.getElementById("problem-list-toggle");
    const problemListDrawerEl = document.getElementById("problem-list-drawer");
    const problemListBodyEl = document.getElementById("problem-list-body");
    const problemListSummaryEl = document.getElementById("problem-list-summary");
    const problemListCloseEl = document.getElementById("problem-list-close");
    const fillProgressUrl = (fillProgressBadgeEl?.dataset.progressUrl || "").trim();
    const wzdhSyncStatsUrl = (container.dataset.wzdhSyncStatsUrl || "").trim();
    const applyPriceByWzdhUrl = (container.dataset.applyPriceByWzdhUrl || "").trim();

    const BASE_HEADERS_8 = ["序号", "名称", "规格", "单位", "单价", "数量", "金额", "报价浮动", "厂家"];
    const BASE_HEADERS_9 = ["序号", "名称", "规格", "单位", "单价", "参考价格", "数量", "金额", "报价浮动", "厂家"];
    const ROOT_SUMMARY_HEADERS = ["元件名称", "规格型号", "单价", "合计数量", "金额小计"];
    const ROOT_COL_PRICE = 2;
    const ROOT_COL_QTY = 3;
    const ROOT_COL_SUBTOTAL = 4;

    let summaryMode = false;
    let projectSummaryReadOnly = false;
    let cabinetViewActive = false;
    /** 柜体视图右侧展示附加费用项（Level 2）而非元件明细 */
    let additionalViewActive = false;
    let hotAdditional = null;
    let currentAdditionalData = [];
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
                btn.classList.add("tree-node-link-selected");
            }
            btn.innerHTML = `<i class="bi bi-dot"></i>${escapeHtml(cab.name)}<span class="ms-1">(${escapeHtml(cab.code)})</span>`;
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
        setSelectedTreeNode("");
        updateCabinetStatusBar("尚未选择");
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
        recalcTotalAmount();
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

    /**
     * 清除"元件使用柜"高亮（保留"当前选中柜"高亮不被清掉）。
     */
    const clearTreeHighlights = () => {
        if (!treeChildrenContainer) {
            return;
        }
        treeChildrenContainer.querySelectorAll("button.tree-node-link").forEach((btn) => {
            btn.classList.remove("tree-node-link-usage");
        });
    };

    /**
     * 设置"当前选中柜"高亮：用户主动点击的节点，持久显示。
     * 与 highlightTreeUnits 使用不同的 CSS 类，互不干扰。
     */
    const setSelectedTreeNode = (unitCode) => {
        if (!treeChildrenContainer) return;
        treeChildrenContainer.querySelectorAll("button.tree-node-link").forEach((btn) => {
            btn.classList.remove("tree-node-link-selected");
        });
        if (treeRootNode) {
            treeRootNode.classList.remove("tree-node-link-selected");
        }
        if (!unitCode) {
            return;
        }
        if (unitCode === "__ROOT__" && treeRootNode) {
            treeRootNode.classList.add("tree-node-link-selected");
            return;
        }
        const btn = treeChildrenContainer.querySelector(`button.tree-node-link[data-unit-no="${unitCode}"]`);
        if (btn) {
            btn.classList.add("tree-node-link-selected");
        }
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

    /**
     * 计算并更新当前节点的"合计金额"。
     * - 柜体视图：累加金额列（colAmount()）。
     * - 根节点汇总视图：累加最后一行外的"金额小计"列（col 4）；最后一行本身已是合计，跳过。
     */
    const recalcTotalAmount = () => {
        if (!currentNodeTotalEl) return;
        const data = hot.getData();
        let total = 0;
        if (rootSummaryMode) {
            // 最后一行是 "合计" 自身，跳过
            for (let r = 0; r < data.length - 1; r += 1) {
                const row = data[r];
                if (!Array.isArray(row)) continue;
                total += parseDecimalOrZero(row[4]);
            }
        } else if (additionalViewActive) {
            currentAdditionalData.forEach((row) => {
                const price = parseFloat(row.x_bj_dj) || 0;
                const qty = parseFloat(row.x_sl) || 0;
                const fdds = parseFloat(row.x_fdds) || 0;
                total += price * (1 + fdds / 100) * qty;
            });
        } else if (cabinetViewActive) {
            const cAmt = colAmount();
            for (let r = 0; r < data.length; r += 1) {
                const row = data[r];
                if (!Array.isArray(row)) continue;
                total += parseDecimalOrZero(row[cAmt]);
            }
        }
        currentNodeTotalEl.textContent = `¥${total.toFixed(2)}`;
    };

    const isRootSummaryTotalRow = (rowIndex, instance) => {
        if (!rootSummaryMode || rowIndex < 0) return false;
        const inst = instance || (typeof hot !== "undefined" ? hot : null);
        if (!inst || typeof inst.getData !== "function") return false;
        const data = inst.getData();
        const row = Array.isArray(data[rowIndex]) ? data[rowIndex] : null;
        if (!row) return false;
        return (row[0] ?? "").toString().trim() === "合计";
    };

    /** 根节点汇总：单价变更后重算该行金额小计 */
    const recalcRootSummaryRow = (rowIndex) => {
        if (!rootSummaryMode || isRootSummaryTotalRow(rowIndex)) return;
        const row = hot.getData()[rowIndex];
        if (!Array.isArray(row)) return;
        const price = parseDecimalOrZero(row[ROOT_COL_PRICE]);
        const qty = parseDecimalOrZero(row[ROOT_COL_QTY]);
        const subtotal = price * qty;
        hot.setDataAtCell(
            rowIndex,
            ROOT_COL_SUBTOTAL,
            subtotal === 0 ? "" : subtotal.toFixed(2),
            "recalcAmount"
        );
    };

    /** 根节点汇总：刷新底部"合计"行与状态栏总金额 */
    const recalcRootSummaryGrandTotal = () => {
        if (!rootSummaryMode) return;
        const data = hot.getData();
        let total = 0;
        for (let r = 0; r < data.length; r += 1) {
            if (isRootSummaryTotalRow(r)) continue;
            const row = data[r];
            if (!Array.isArray(row)) continue;
            total += parseDecimalOrZero(row[ROOT_COL_SUBTOTAL]);
        }
        const lastIdx = data.length - 1;
        if (lastIdx >= 0 && isRootSummaryTotalRow(lastIdx)) {
            hot.setDataAtCell(
                lastIdx,
                ROOT_COL_SUBTOTAL,
                total === 0 ? "" : total.toFixed(2),
                "recalcAmount"
            );
        }
        if (currentNodeTotalEl) {
            currentNodeTotalEl.textContent = `¥${total.toFixed(2)}`;
        }
    };

    /**
     * 更新状态栏（柜名 + 合计），同时显隐颜色图例。
     */
    const updateCabinetStatusBar = (label) => {
        if (currentNodeLabelEl) {
            currentNodeLabelEl.textContent = label || "尚未选择";
        }
        if (colorLegendEl) {
            // 仅在柜体视图（含参考价格列）展示颜色图例
            colorLegendEl.classList.toggle("d-none", !cabinetViewActive);
        }
        recalcTotalAmount();
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

    /**
     * 渲染"元件使用控制柜"面板。
     * @param {{name:string, spec:string, unit:string}} displayInfo 用于面板 title 展示
     * @param {Array<{unitCode:string, unitName:string, qty:number, priceMin?:number, priceMax?:number, vendors?:string[]}>} usageRows
     * @param {string} [message] 后端附带消息（如"该元件未填规格型号..."）
     */
    const renderUsagePanel = (displayInfo, usageRows, message) => {
        if (!componentUsagePanel || !componentUsageTitle || !componentUsageList) {
            return;
        }
        const safe = displayInfo || { name: "", spec: "", unit: "" };
        const title = `${safe.name || "未命名"} / ${safe.spec || "无规格"} / ${safe.unit || "无单位"}`;
        if (message) {
            componentUsageTitle.textContent = `${title}：${message}`;
        } else {
            componentUsageTitle.textContent = `${title}，共使用于 ${usageRows.length} 个控制柜。`;
        }
        componentUsageList.innerHTML = "";
        if (usageRows.length === 0) {
            const li = document.createElement("li");
            li.className = "text-muted";
            li.textContent = message ? "（仅当前柜内使用，或型号未填无法识别）" : "未找到使用该元件的控制柜。";
            componentUsageList.appendChild(li);
        } else {
            usageRows.forEach((item) => {
                const li = document.createElement("li");
                li.className = "mb-1";
                const priceText = formatUsagePriceRange(item.priceMin, item.priceMax);
                const vendorText = Array.isArray(item.vendors) && item.vendors.length > 0
                    ? `；厂家：${item.vendors.join("、")}`
                    : "";
                li.textContent = `${item.unitCode} ${item.unitName}（数量合计：${item.qty}${priceText}${vendorText}）`;
                componentUsageList.appendChild(li);
            });
        }
        componentUsagePanel.classList.remove("d-none");
        highlightTreeUnits(usageRows.map((x) => x.unitCode));
    };

    /** 格式化某柜下该型号的价格区间——单一价格直接显示，区间用"~"连接 */
    const formatUsagePriceRange = (priceMin, priceMax) => {
        const hasMin = priceMin !== null && priceMin !== undefined;
        const hasMax = priceMax !== null && priceMax !== undefined;
        if (!hasMin && !hasMax) return "";
        const min = Number(priceMin);
        const max = Number(priceMax);
        if (!Number.isFinite(min) && !Number.isFinite(max)) return "";
        if (min === max) return `；单价：¥${min.toFixed(2)}`;
        return `；单价：¥${min.toFixed(2)} ~ ¥${max.toFixed(2)}`;
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
            } else if (col === ROOT_COL_SUBTOTAL) {
                // 金额小计列样式
                td.style.backgroundColor = "#d6eaf8";
                td.style.color = "#1b4f72";
                td.style.fontWeight = "600";
            } else if (col === ROOT_COL_PRICE && !cellProperties.readOnly) {
                td.style.backgroundColor = "";
                td.style.color = "";
                td.style.fontWeight = "";
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

    const getCellsMeta = (instance, row, col) => {
        if (rootSummaryMode) {
            const totalRow = isRootSummaryTotalRow(row, instance);
            return {
                renderer: errorCellRenderer,
                readOnly: globalReadOnly || totalRow || col !== ROOT_COL_PRICE
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
                cells: function (row, col) {
                    return getCellsMeta(this, row, col);
                }
            });
            return;
        }
        const nine = isRefColumnMode();
        const n = nine ? 10 : 9;
        const headers = nine ? BASE_HEADERS_9 : BASE_HEADERS_8;
        hot.updateSettings({
            colHeaders: headers,
            columns: Array.from({ length: n }, () => ({ type: "text", renderer: errorCellRenderer })),
            cells: function (row, col) {
                return getCellsMeta(this, row, col);
            }
        });
    };

    const hot = new Handsontable(container, {
        data: [],
        rowHeaders: true,
        colHeaders: BASE_HEADERS_8,
        columns: Array.from({ length: 9 }, () => ({ type: "text", renderer: errorCellRenderer })),
        cells: function (row, col) {
            return getCellsMeta(this, row, col);
        },
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

    // B-T7: F3 快捷键 — 在柜体视图下跳到当前柜的下一个未填价行（单价单元格）
    // 仅在 Handsontable 持焦时拦截，不影响浏览器其它页面的 F3 搜索行为
    hot.addHook("beforeKeyDown", (event) => {
        if (event.key !== "F3" && event.keyCode !== 114) return;
        if (!cabinetViewActive || summaryMode) return;
        event.preventDefault();
        event.stopImmediatePropagation();

        const cPrice = colPrice();
        const selected = hot.getSelected();
        const startRow = (selected && selected.length > 0) ? Math.max(0, selected[0][0] + 1) : 0;
        const total = hot.countRows();
        let found = -1;
        for (let r = startRow; r < total; r++) {
            const v = hot.getDataAtCell(r, cPrice);
            const num = Number((v ?? "").toString().trim());
            if (!Number.isFinite(num) || num <= 0) {
                found = r;
                break;
            }
        }
        if (found < 0) {
            // 从开头再扫一遍（环绕），防止用户已经在末尾
            for (let r = 0; r < startRow; r++) {
                const v = hot.getDataAtCell(r, cPrice);
                const num = Number((v ?? "").toString().trim());
                if (!Number.isFinite(num) || num <= 0) {
                    found = r;
                    break;
                }
            }
        }
        if (found < 0) {
            setMessage("本柜已全部填价。按 ESC 关闭抽屉，或切换到其它柜继续。", false);
            return;
        }
        hot.selectCell(found, cPrice);
        hot.scrollViewportTo(found, cPrice);
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
        additionalViewActive = false;
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

    /**
     * 从当前柜体视图的某一行提取"显示信息"（用于面板标题）。
     * 注意：identity 不再参与查询匹配，仅用于面板 title 友好展示；
     * 真正的匹配口径是 x_wzdh（标准化指纹），由 currentRowWzdh[rowIndex] 提供。
     */
    const displayInfoFromHotRow = (rowIndex) => {
        const data = hot.getData();
        const row = Array.isArray(data[rowIndex]) ? data[rowIndex] : [];
        return {
            name: (row[1] ?? "").toString().trim(),
            spec: (row[2] ?? "").toString().trim(),
            unit: (row[3] ?? "").toString().trim()
        };
    };

    /**
     * 根据 x_wzdh（标准化指纹）查询元件使用控制柜（Req 17）。
     * - wzdh 为空时：直接展示"该元件未填规格型号，无法识别使用情况"，不发起请求
     * - 后端返回的 rows 形如 [{ unitCode, unitName, qty, priceMin, priceMax, vendors[] }]
     */
    const fetchComponentUsageByWzdh = async (wzdh, displayInfo) => {
        if (!projectUsageUrl) {
            return;
        }

        const targetWzdh = (wzdh || "").toString().trim();
        if (!targetWzdh) {
            renderUsagePanel(displayInfo, [], "该元件未填规格型号，无法识别使用情况");
            return;
        }

        try {
            const connector = projectUsageUrl.includes("?") ? "&" : "?";
            const query = new URLSearchParams({ wzdh: targetWzdh });
            const response = await fetch(`${projectUsageUrl}${connector}${query.toString()}`, { method: "GET" });
            const result = await response.json();
            if (!response.ok || !result.success) {
                throw new Error(result.message || "读取元件使用控制柜失败");
            }
            renderUsagePanel(
                displayInfo,
                Array.isArray(result.rows) ? result.rows : [],
                result.message || ""
            );
        } catch (error) {
            const message = error instanceof Error ? error.message : "读取元件使用控制柜失败";
            setMessage(message, true);
        }
    };

    const getSummaryChangedItems = () => {
        const data = hot.getData();
        const changed = [];
        if (rootSummaryMode) {
            for (let i = 0; i < summaryOriginalRows.length; i += 1) {
                const row = Array.isArray(data[i]) ? data[i] : [];
                if (isRootSummaryTotalRow(i)) break;
                const oldRow = summaryOriginalRows[i];
                if (!oldRow) continue;
                const newPrice = parseDecimalOrZero(row[ROOT_COL_PRICE]);
                if (newPrice === oldRow.price) continue;
                changed.push({
                    matchKey: oldRow.matchKey,
                    newUnit: oldRow.unit,
                    newPrice,
                    newFloatRate: oldRow.floatRate,
                    newVendor: oldRow.vendor
                });
            }
            return changed;
        }
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
            // leaveSummaryMode 会清空选中态，这里需要恢复
            selectedCabinetCode = target;
            setSelectedTreeNode(target);
            currentCabinetUnitCode = target;
            additionalViewActive = false;
            cabinetViewActive = true;
            document.getElementById("additional-items-section")?.classList.remove("is-primary-panel");
            document.getElementById("additional-items-section")?.classList.add("d-none");
            document.getElementById("hot-container")?.classList.remove("is-hidden-panel");
            document.getElementById("fill-price-action-buttons")?.style.removeProperty("display");
            // 当 referencePriceUrl 可用时，始终加载参考价格列（Req 6.1, 6.2）
            const wantRef = referencePriceUrl
                ? true
                : !!(showRefPriceCb && showRefPriceCb.checked);
            await loadCabinetGrid(target, wantRef);
            hot.render();
            applyButtonStates();
            // Req 9.6: 数据加载完成后触发价格异常检测着色
            applyPriceAnomalyStyles();
            // 状态栏更新：柜名 + 总金额（Req 16.1, 16.3）
            const cab = cabinetList.find((c) => c.code === target);
            const label = cab && cab.name
                ? `${cab.name}（${target}）`
                : `控制柜 ${target}`;
            updateCabinetStatusBar(label);
            // B-T5: 切柜后刷新全局进度看板 + 显示徽章
            updateProgressVisibility();
            refreshProgress();
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
            additionalViewActive = false;
            currentCabinetUnitCode = "";
            refPriceColumnVisible = false;
            rootSummaryMode = true;
            document.getElementById("additional-items-section")?.classList.remove("is-primary-panel");
            document.getElementById("additional-items-section")?.classList.add("d-none");
            document.getElementById("hot-container")?.classList.remove("is-hidden-panel");
            applyHotColumnLayout();
            hideUsagePanel();
            setMessage("正在加载项目全部控制柜元件汇总...", false);
            const response = await fetch(projectSummaryUrl, { method: "GET" });
            const result = await response.json();
            if (!response.ok || !result.success) {
                throw new Error(result.message || "读取项目元件汇总失败");
            }
            const rows = Array.isArray(result.rows) ? result.rows : [];

            // Req 15.2: 按 x_mc、x_ggxh、单价(x_bj_dj，API 字段名仍为 x_dj) 分组
            const groupMap = new Map();
            rows.forEach((x) => {
                const name = (x.x_mc ?? "").toString().trim();
                const spec = (x.x_ggxh ?? "").toString().trim();
                const price = Number(x.x_dj ?? 0);
                const qty = Number(x.x_sl ?? 0);
                const floatRate = Number(x.x_fdds ?? 0);
                const lineAmount = x.amount != null && x.amount !== undefined
                    ? Number(x.amount)
                    : price * (1 + floatRate / 100) * qty;
                const key = `${name}\x00${spec}\x00${price}`;
                if (groupMap.has(key)) {
                    const g = groupMap.get(key);
                    g.qty += qty;
                    g.amountSum += lineAmount;
                } else {
                    groupMap.set(key, {
                        name,
                        spec,
                        price,
                        qty,
                        amountSum: lineAmount,
                        matchKey: (x.matchKey ?? "").toString(),
                        unit: (x.x_dw ?? "").toString().trim(),
                        floatRate,
                        vendor: (x.x_sccj ?? "").toString().trim()
                    });
                }
            });

            // Req 15.3: 显示列：元件名称、规格型号、单价、合计数量、金额小计
            let totalAmount = 0;
            const mapped = [];
            summaryOriginalRows = [];
            for (const group of groupMap.values()) {
                const subtotal = group.amountSum;
                totalAmount += subtotal;
                mapped.push([
                    group.name,
                    group.spec,
                    group.price === 0 ? "" : group.price.toFixed(4).replace(/\.?0+$/, ""),
                    group.qty === 0 ? "" : group.qty.toFixed(4).replace(/\.?0+$/, ""),
                    subtotal === 0 ? "" : subtotal.toFixed(2)
                ]);
                summaryOriginalRows.push({
                    matchKey: group.matchKey,
                    name: group.name,
                    spec: group.spec,
                    unit: group.unit,
                    price: group.price,
                    floatRate: group.floatRate,
                    vendor: group.vendor
                });
            }

            // Req 15.4: 底部显示总金额合计行
            mapped.push([
                "合计",
                "",
                "",
                "",
                totalAmount === 0 ? "" : totalAmount.toFixed(2)
            ]);

            hot.loadData(mapped);
            // 有编辑权限时允许改单价并保存；只读账号仍保持全表只读
            projectSummaryReadOnly = globalReadOnly;
            enterSummaryMode();
            // 状态栏更新：根节点 + 总金额（Req 16.1, 16.3）
            updateCabinetStatusBar(quotationName ? `${quotationName}（项目汇总）` : "项目汇总");
            // B-T5: 进入项目汇总视图后隐藏进度徽章组（Req B-1.6/B-3.6）
            updateProgressVisibility();
            const editHint = globalReadOnly
                ? ""
                : "；可直接修改「单价」列，金额小计与合计将自动重算，修改后请点击「保存数据」。";
            setMessage(
                `已加载项目元件汇总，共 ${mapped.length - 1} 组（按名称/规格/单价分组），总金额：¥${totalAmount.toFixed(2)}${editHint}`,
                false
            );
        } catch (error) {
            const message = error instanceof Error ? error.message : "读取项目元件汇总失败";
            rootSummaryMode = false;
            setMessage(message, true);
        }
    };

    const loadCabinetComponentUsage = async (rowIndex) => {
        if (!cabinetViewActive || rowIndex < 0 || rowIndex >= hot.countRows()) {
            return;
        }
        const wzdh = (currentRowWzdh[rowIndex] || "").toString().trim();
        const displayInfo = displayInfoFromHotRow(rowIndex);
        await fetchComponentUsageByWzdh(wzdh, displayInfo);
    };

    // ====================================================================
    // 填价进度看板（spec: fill-progress-dashboard / Req B-1, B-2, B-3）
    // ====================================================================

    const ISSUE_LABEL = {
        negative: "负数",
        zero_price: "零价",
        deviation: "偏离",
        missing_spec: "缺规格"
    };

    /** 进度看板状态 */
    const progressState = {
        data: null,
        refreshing: false,
        debounceTimer: 0
    };

    /**
     * 设置徽章可见性。规则（Req B-1.6 / B-3.6）：
     *   - 项目根汇总视图（summaryMode 真）下整组隐藏，因为汇总本身就是全景
     *   - 退出汇总视图时恢复显示
     */
    const updateProgressVisibility = () => {
        if (!fillProgressBadgeEl) return;
        const shouldHide = summaryMode || rootSummaryMode;
        fillProgressBadgeEl.classList.toggle("d-none", shouldHide);
        if (shouldHide && problemListDrawerEl && !problemListDrawerEl.classList.contains("d-none")) {
            problemDrawer.close();
        }
    };

    /**
     * 把后端 DTO 渲染到徽章。空数据时显示 0/0（–）。
     * @param {Object|null} dto
     */
    const renderProgressBadge = (dto) => {
        if (!fillProgressBadgeEl) return;
        if (!dto) {
            if (progressFilledEl) progressFilledEl.textContent = "—";
            if (progressTotalEl) progressTotalEl.textContent = "—";
            if (progressPercentEl) progressPercentEl.textContent = "统计失败";
            if (anomalyCountEl) anomalyCountEl.textContent = "—";
            if (problemListToggleEl) problemListToggleEl.disabled = true;
            return;
        }
        const total = Number(dto.totalRows || 0);
        const filled = Number(dto.filledRows || 0);
        const anomalyTotal = Number(dto.anomalies?.total ?? 0);

        if (progressFilledEl) progressFilledEl.textContent = String(filled);
        if (progressTotalEl) progressTotalEl.textContent = String(total);
        if (progressPercentEl) {
            progressPercentEl.textContent = total > 0
                ? `${Math.round((filled / total) * 100)}%`
                : "–";
        }
        if (anomalyCountEl) anomalyCountEl.textContent = String(anomalyTotal);
        if (problemListToggleEl) problemListToggleEl.disabled = anomalyTotal === 0;
    };

    /**
     * 进度看板核心：从后端取数据 + 渲染徽章 + 同步抽屉内容（若打开）。
     * 设计上不阻塞表格编辑：失败仅在徽章显示"统计失败"。
     */
    const refreshProgress = async () => {
        if (!fillProgressUrl || !fillProgressBadgeEl) return;
        if (progressState.refreshing) return;
        progressState.refreshing = true;
        try {
            const response = await fetch(fillProgressUrl, { method: "GET" });
            const result = await response.json();
            if (!response.ok || !result || !result.success) {
                throw new Error(result?.message || "进度查询失败");
            }
            progressState.data = result.data || null;
            renderProgressBadge(progressState.data);
            if (problemListDrawerEl && !problemListDrawerEl.classList.contains("d-none")) {
                problemDrawer.render(progressState.data);
            }
        } catch (_err) {
            renderProgressBadge(null);
        } finally {
            progressState.refreshing = false;
        }
    };

    /**
     * 防抖刷新（编辑触发时使用，避免连击造成请求风暴）。
     */
    const refreshProgressDebounced = () => {
        clearTimeout(progressState.debounceTimer);
        progressState.debounceTimer = setTimeout(refreshProgress, 500);
    };

    // ====================================================================
    // 问题清单抽屉（B-T6）
    // ====================================================================

    const problemDrawer = {
        open: () => {
            if (!problemListDrawerEl) return;
            problemListDrawerEl.classList.remove("d-none");
            problemListDrawerEl.setAttribute("aria-hidden", "false");
            problemDrawer.render(progressState.data);
        },
        close: () => {
            if (!problemListDrawerEl) return;
            problemListDrawerEl.classList.add("d-none");
            problemListDrawerEl.setAttribute("aria-hidden", "true");
        },
        toggle: () => {
            if (!problemListDrawerEl) return;
            if (problemListDrawerEl.classList.contains("d-none")) {
                problemDrawer.open();
            } else {
                problemDrawer.close();
            }
        },
        render: (dto) => {
            if (!problemListBodyEl) return;
            const problems = Array.isArray(dto?.problems) ? dto.problems : [];
            if (problems.length === 0) {
                problemListBodyEl.innerHTML = '<div class="text-muted small text-center py-4">当前无异常项 🎉</div>';
                if (problemListSummaryEl) problemListSummaryEl.textContent = "共 0 条";
                return;
            }
            // 按柜分组
            const groups = new Map();
            for (const p of problems) {
                const key = p.cabinetCode || "";
                if (!groups.has(key)) {
                    groups.set(key, { name: p.cabinetName || key, items: [] });
                }
                groups.get(key).items.push(p);
            }
            // 渲染分组（首屏限制 200 条总量，B-T5/Req B-5.2）
            const RENDER_LIMIT = 200;
            let rendered = 0;
            const parts = [];
            for (const [cabCode, group] of groups) {
                parts.push(
                    `<div class="problem-list-group" data-cab-code="${escapeHtml(cabCode)}">` +
                    `<div class="group-header">` +
                    `<span><i class="bi bi-box-seam me-1"></i>${escapeHtml(group.name)} <span class="text-muted small">(${group.items.length})</span></span>` +
                    `<i class="bi bi-chevron-down"></i>` +
                    `</div>` +
                    `<ul class="group-items">`
                );
                for (const item of group.items) {
                    if (rendered >= RENDER_LIMIT) break;
                    rendered++;
                    const issueLabel = ISSUE_LABEL[item.issueType] || item.issueType;
                    const priceStr = formatNumber(item.currentPrice);
                    const avgStr = item.avgPrice != null ? `（均价 ¥${formatNumber(item.avgPrice)}）` : "";
                    parts.push(
                        `<li class="problem-list-item" data-cab-code="${escapeHtml(cabCode)}" data-row-seq="${item.rowSeq}">` +
                        `<span class="issue-badge issue-${item.issueType}">${issueLabel}</span>` +
                        `<span>${escapeHtml(item.name || "(无名)")} · ${escapeHtml(item.spec || "(无规格)")} · ¥${priceStr}${avgStr}</span>` +
                        `</li>`
                    );
                }
                parts.push(`</ul></div>`);
                if (rendered >= RENDER_LIMIT) break;
            }
            const more = problems.length > RENDER_LIMIT
                ? `<div class="text-muted small text-center py-2">还有 ${problems.length - RENDER_LIMIT} 条未显示，建议先处理已显示的问题。</div>`
                : "";
            problemListBodyEl.innerHTML = parts.join("") + more;
            if (problemListSummaryEl) {
                problemListSummaryEl.textContent = `共 ${problems.length} 条`
                    + (problems.length > RENDER_LIMIT ? `（显示前 ${RENDER_LIMIT}）` : "");
            }
        },
        /**
         * 跳转到指定柜+行，并临时高亮 2 秒。
         * 同柜直接 selectCell；跨柜则先加载柜数据。
         */
        jumpTo: async (cabinetCode, rowSeq) => {
            const targetCab = (cabinetCode || "").trim();
            const rowIdx = Math.max(0, (Number(rowSeq) || 1) - 1);
            if (!targetCab) return;
            try {
                if (currentCabinetUnitCode !== targetCab) {
                    await loadCabinetComponents(targetCab);
                }
                const cPrice = colPrice();
                if (rowIdx < hot.countRows()) {
                    hot.selectCell(rowIdx, cPrice);
                    hot.scrollViewportTo(rowIdx, cPrice);
                    flashRow(rowIdx);
                }
            } catch (err) {
                setMessage(err instanceof Error ? err.message : "跳转失败", true);
            }
        }
    };

    /** 给指定行的所有单元格临时加 .hot-row-flash 类，触发 CSS 动画 */
    const flashRow = (rowIdx) => {
        try {
            const colCount = hot.countCols();
            for (let c = 0; c < colCount; c++) {
                const td = hot.getCell(rowIdx, c);
                if (!td) continue;
                td.classList.remove("hot-row-flash");
                // 强制 reflow 后再加 class，保证连续点击同一行也会重播动画
                void td.offsetWidth;
                td.classList.add("hot-row-flash");
                setTimeout(() => td.classList.remove("hot-row-flash"), 2100);
            }
        } catch (_err) {
            // 不阻塞跳转
        }
    };

    /** 数字格式化（保留 2 位小数；0 时显示 0.00）；escapeHtml 复用文件前部已有定义 */
    const formatNumber = (n) => {
        const num = Number(n);
        if (!Number.isFinite(num)) return "0.00";
        return num.toFixed(2);
    };

    // 抽屉事件绑定（顶层一次性绑定）
    if (problemListToggleEl) {
        problemListToggleEl.addEventListener("click", () => {
            if (problemListToggleEl.disabled) return;
            problemDrawer.toggle();
        });
    }
    if (problemListCloseEl) {
        problemListCloseEl.addEventListener("click", () => problemDrawer.close());
    }
    if (problemListDrawerEl) {
        // overlay 点击关闭
        problemListDrawerEl.addEventListener("click", (event) => {
            if (event.target.classList.contains("drawer-overlay")) {
                problemDrawer.close();
            }
        });
        // 委托：点击问题项跳转
        problemListBodyEl?.addEventListener("click", (event) => {
            const item = event.target.closest(".problem-list-item");
            if (!item) return;
            const cabCode = item.getAttribute("data-cab-code") || "";
            const rowSeq = item.getAttribute("data-row-seq") || "1";
            problemDrawer.jumpTo(cabCode, rowSeq);
        });
    }
    // ESC 关闭抽屉
    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && problemListDrawerEl && !problemListDrawerEl.classList.contains("d-none")) {
            problemDrawer.close();
        }
    });

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

    if (treeRootNode) {
        treeRootNode.addEventListener("click", () => {
            selectedCabinetCode = "";
            setSelectedTreeNode("__ROOT__");
            loadProjectComponentSummary();
        });
    }

    // ========== 按 x_wzdh 全项目统一单价 ==========
    let syncPriceDebounceTimer = 0;
    let syncPriceInFlight = false;

    const parseCodesFromMatchKey = (matchKey) => {
        const raw = (matchKey || "").trim();
        if (!raw.toLowerCase().startsWith("codes:")) {
            return [];
        }
        return raw.slice(6).split(",").map((s) => s.trim()).filter(Boolean);
    };

    const fetchWzdhSyncStats = async (wzdh) => {
        if (!wzdhSyncStatsUrl) {
            throw new Error("同步统计接口未配置");
        }
        const connector = wzdhSyncStatsUrl.includes("?") ? "&" : "?";
        const response = await fetch(`${wzdhSyncStatsUrl}${connector}wzdh=${encodeURIComponent(wzdh)}`, { method: "GET" });
        const result = await response.json();
        if (!response.ok || !result.success) {
            throw new Error(result.message || "查询元件使用统计失败");
        }
        return result;
    };

    const postApplyPriceSync = async ({ wzdh, newPrice, codes }) => {
        if (!applyPriceByWzdhUrl) {
            throw new Error("同步单价接口未配置");
        }
        const response = await fetch(applyPriceByWzdhUrl, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                ...(getToken() ? { RequestVerificationToken: getToken() } : {})
            },
            body: JSON.stringify({
                wzdh: wzdh || "",
                newPrice,
                codes: codes || []
            })
        });
        return readJsonResponse(response, "同步单价失败");
    };

    /** 柜体视图：把当前表格内同 wzdh 行的单价与金额列同步为 newPrice */
    const applyLocalCabinetRowsByWzdh = (wzdh, newPrice) => {
        const wzdhKey = wzdh.toLowerCase();
        const cPrice = colPrice();
        const cQty = colQty();
        const cFloat = colFloat();
        const cAmt = colAmount();
        const changes = [];
        for (let r = 0; r < hot.countRows(); r++) {
            const rowWzdh = (currentRowWzdh[r] || "").trim().toLowerCase();
            if (rowWzdh !== wzdhKey) {
                continue;
            }
            const priceText = newPrice === 0 ? "" : String(newPrice);
            changes.push([r, cPrice, priceText]);
            const rowData = hot.getData()[r];
            if (Array.isArray(rowData)) {
                const amount = calcAmountValue(newPrice, rowData[cFloat], rowData[cQty]);
                changes.push([r, cAmt, amount]);
            }
        }
        if (changes.length > 0) {
            hot.setDataAtCell(changes, "syncByWzdh");
            applyPriceAnomalyStyles();
            recalcTotalAmount();
        }
    };

    const revertCabinetPriceCell = (rowIndex, oldPrice) => {
        const cPrice = colPrice();
        const cQty = colQty();
        const cFloat = colFloat();
        const cAmt = colAmount();
        const priceText = oldPrice === 0 ? "" : String(oldPrice);
        hot.setDataAtCell(rowIndex, cPrice, priceText, "rollbackPrice");
        const rowData = hot.getData()[rowIndex];
        if (Array.isArray(rowData)) {
            const amount = calcAmountValue(oldPrice, rowData[cFloat], rowData[cQty]);
            hot.setDataAtCell(rowIndex, cAmt, amount, "rollbackPrice");
        }
    };

    const runCabinetPriceSync = async (rowIndex, newPrice, oldPrice) => {
        if (syncPriceInFlight || globalReadOnly || !cabinetViewActive) {
            return;
        }
        const wzdh = (currentRowWzdh[rowIndex] || "").trim();
        const display = displayInfoFromHotRow(rowIndex);
        const label = `${display.name} ${display.spec}`.trim() || "当前元件";

        if (!wzdh) {
            setMessage(`「${label}」未填规格型号，无法识别为同一型号，仅保留当前行修改（不会同步到其它控制柜）。`, false);
            return;
        }

        try {
            const stats = await fetchWzdhSyncStats(wzdh);
            const totalRows = Number(stats.totalRows || 0);
            const cabinetCount = Number(stats.cabinetCount || 0);
            const cabinets = Array.isArray(stats.cabinets) ? stats.cabinets : [];

            if (totalRows <= 1) {
                syncPriceInFlight = true;
                const result = await postApplyPriceSync({ wzdh, newPrice, codes: [] });
                applyLocalCabinetRowsByWzdh(wzdh, newPrice);
                setMessage(result.message || `已更新「${label}」单价。`, false);
                refreshProgressDebounced();
                return;
            }

            const cabNames = cabinets
                .map((c) => (c.unitName || c.unitCode || "").toString().trim())
                .filter(Boolean)
                .join("、");
            const confirmed = window.confirm(
                `「${label}」在本项目共有 ${totalRows} 处使用（${cabinetCount} 个控制柜${cabNames ? "：" + cabNames : ""}）。\n\n是否将全部统一更新为 ¥${newPrice.toFixed(2)}？`
            );
            if (!confirmed) {
                revertCabinetPriceCell(rowIndex, oldPrice);
                setMessage("已取消同步，仅保留您刚才输入前的价格。", false);
                return;
            }

            syncPriceInFlight = true;
            const result = await postApplyPriceSync({ wzdh, newPrice, codes: [] });
            applyLocalCabinetRowsByWzdh(wzdh, newPrice);
            const unitCodes = cabinets.map((c) => (c.unitCode || "").toString().trim()).filter(Boolean);
            if (unitCodes.length > 0) {
                highlightTreeUnits(unitCodes);
            }
            setMessage(result.message || `已同步 ${totalRows} 处单价。`, false);
            refreshProgressDebounced();
        } catch (error) {
            const message = error instanceof Error ? error.message : "同步单价失败";
            setMessage(message, true);
        } finally {
            syncPriceInFlight = false;
        }
    };

    const runRootSummaryPriceSync = async (rowIndex, newPrice, oldPrice) => {
        if (syncPriceInFlight || globalReadOnly || !rootSummaryMode || isRootSummaryTotalRow(rowIndex)) {
            return;
        }
        const meta = summaryOriginalRows[rowIndex];
        if (!meta) {
            return;
        }
        const codes = parseCodesFromMatchKey(meta.matchKey);
        const label = `${meta.name} ${meta.spec}`.trim() || "当前分组";

        if (codes.length === 0) {
            setMessage(`「${label}」无法解析元件编码，请使用「保存数据」写入修改。`, false);
            return;
        }

        if (codes.length <= 1) {
            syncPriceInFlight = true;
            try {
                const result = await postApplyPriceSync({ wzdh: "", newPrice, codes });
                recalcRootSummaryRow(rowIndex);
                recalcRootSummaryGrandTotal();
                summaryDirty = false;
                meta.price = newPrice;
                setMessage(result.message || `已更新「${label}」单价。`, false);
                refreshProgressDebounced();
            } catch (error) {
                const message = error instanceof Error ? error.message : "同步单价失败";
                setMessage(message, true);
            } finally {
                syncPriceInFlight = false;
            }
            return;
        }

        const confirmed = window.confirm(
            `「${label}」在本项目共有 ${codes.length} 处元件记录。\n\n是否将全部统一更新为 ¥${newPrice.toFixed(2)}？`
        );
        if (!confirmed) {
            hot.setDataAtCell(
                rowIndex,
                ROOT_COL_PRICE,
                oldPrice === 0 ? "" : String(oldPrice),
                "rollbackPrice"
            );
            recalcRootSummaryRow(rowIndex);
            recalcRootSummaryGrandTotal();
            setMessage("已取消同步。", false);
            return;
        }

        syncPriceInFlight = true;
        try {
            const result = await postApplyPriceSync({ wzdh: "", newPrice, codes });
            recalcRootSummaryRow(rowIndex);
            recalcRootSummaryGrandTotal();
            summaryDirty = false;
            meta.price = newPrice;
            setMessage(result.message || `已同步 ${codes.length} 处单价。`, false);
            refreshProgressDebounced();
        } catch (error) {
            const message = error instanceof Error ? error.message : "同步单价失败";
            setMessage(message, true);
        } finally {
            syncPriceInFlight = false;
        }
    };

    hot.addHook("afterSelectionEnd", (row, column, row2, column2) => {
        const r2 = row2 !== undefined && row2 !== null ? row2 : row;
        const r = Math.min(row, r2);
        if (!Number.isFinite(r) || r < 0 || r >= hot.countRows()) {
            return;
        }
        if (rootSummaryMode || summaryMode) {
            // 汇总视图按指纹归并展示，元件使用查询仅在柜体视图启用
            return;
        }
        if (cabinetViewActive) {
            loadCabinetComponentUsage(r);
        }
    });

    hot.addHook("afterChange", (changes, source) => {
        if (!changes || source === "loadData" || source === "recalcAmount" || source === "addRow"
            || source === "syncByWzdh" || source === "rollbackPrice") {
            return;
        }
        if (rootSummaryMode) {
            let priceChanged = false;
            for (const [row, col, oldVal, newVal] of changes) {
                if (col === ROOT_COL_PRICE && !isRootSummaryTotalRow(row)) {
                    priceChanged = true;
                    recalcRootSummaryRow(row);
                    const oldPrice = parseDecimalOrZero(oldVal);
                    const newPrice = parseDecimalOrZero(newVal);
                    if (oldPrice !== newPrice) {
                        clearTimeout(syncPriceDebounceTimer);
                        syncPriceDebounceTimer = setTimeout(
                            () => runRootSummaryPriceSync(row, newPrice, oldPrice),
                            500
                        );
                    }
                }
            }
            if (priceChanged) {
                recalcRootSummaryGrandTotal();
                summaryDirty = true;
                applyButtonStates();
            }
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
            // Req 16.3: 单价/数量/浮动变更后重算合计金额
            recalcTotalAmount();
        }
        // Req 9.6: 单价单元格值变更后触发价格异常检测着色
        if (priceChanged) {
            applyPriceAnomalyStyles();
            // B-T5: 价格变更后防抖刷新进度看板（避免连击造成请求风暴）
            refreshProgressDebounced();
            for (const [row, col, oldVal, newVal] of changes) {
                if (col !== colPrice()) {
                    continue;
                }
                const oldPrice = parseDecimalOrZero(oldVal);
                const newPrice = parseDecimalOrZero(newVal);
                if (oldPrice === newPrice) {
                    continue;
                }
                clearTimeout(syncPriceDebounceTimer);
                syncPriceDebounceTimer = setTimeout(
                    () => runCabinetPriceSync(row, newPrice, oldPrice),
                    500
                );
            }
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
        const priceCol = rootSummaryMode ? ROOT_COL_PRICE : colPrice();
        const nameCol = rootSummaryMode ? 0 : 1;
        const specCol = rootSummaryMode ? 1 : 2;

        for (let r = 0; r < data.length; r++) {
            if (rootSummaryMode && isRootSummaryTotalRow(r)) continue;
            const rowData = data[r];
            if (!Array.isArray(rowData)) continue;
            const price = parseDecimalOrZero(rowData[priceCol]);
            if (price < 0) {
                const name = (rowData[nameCol] || "").toString().trim();
                const spec = (rowData[specCol] || "").toString().trim();
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
                // B-T5: 保存成功后刷新进度看板（即便项目汇总视图徽章被隐藏，也保持数据新鲜以备退出汇总时立即可见）
                refreshProgress();
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
        // Req 16.3: 自动填价后同步刷新合计金额
        recalcTotalAmount();

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
            // B-T5: 自动填价完成后立即刷新进度看板
            refreshProgress();

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

    applyButtonStates();
    // B-T5: 页面加载完成后立即取一次进度（无论用户是否点击柜节点）
    updateProgressVisibility();
    refreshProgress();

    // ═══════════════════════════════════════════════════════════════════════
    // ██  一、二级叶节点填价扩展
    // ═══════════════════════════════════════════════════════════════════════

    // ── DOM 引用 ──
    const additionalItemsSection = document.getElementById("additional-items-section");
    const additionalItemsToggle = document.getElementById("additional-items-toggle");
    const additionalItemsBody = document.getElementById("additional-items-body");
    const additionalItemsChevron = document.getElementById("additional-items-chevron");
    const additionalItemsCount = document.getElementById("additional-items-count");
    const hotAdditionalContainer = document.getElementById("hot-additional-container");
    const hotAttrContainer = document.getElementById("hot-attr-container");
    const batchFillBtn = document.getElementById("batch-fill-btn");
    const attrNodeLabelEl = document.getElementById("attr-node-label");
    const attrNodeTotalEl = document.getElementById("attr-node-total");
    const viewCabinetBtn = document.getElementById("view-cabinet-btn");
    const viewAttrBtn = document.getElementById("view-attr-btn");
    const cabinetTablePane = document.getElementById("cabinet-table-pane");
    const attrTablePane = document.getElementById("attr-table-pane");
    const attrTreePane = document.getElementById("attr-tree-pane");
    const cabinetTreePane = document.getElementById("price-tree-pane");
    const priceWorkspace = document.getElementById("price-workspace");
    const hotContainerEl = document.getElementById("hot-container");

    const calcHandsontableHeight = (rowCount, minRows = 4, maxRows = 25) => {
        const header = 28;
        const rowH = 24;
        const rows = Math.max(rowCount, minRows);
        return Math.min(header + rows * rowH, header + maxRows * rowH);
    };

    const calcAttrTableHeight = () => {
        if (!hotAttrContainer) return 400;
        const pane = attrTablePane?.querySelector(".oa-card");
        const bar = document.getElementById("attr-status-bar");
        const footerHint = hotAttrContainer.nextElementSibling;
        const paneH = pane?.clientHeight || 500;
        const barH = bar?.offsetHeight || 40;
        const footH = footerHint?.offsetHeight || 24;
        return Math.max(200, paneH - barH - footH - 24);
    };

    const additionalItemsUrl = (hotAdditionalContainer?.dataset.additionalUrl || "").trim();
    const attributeItemsUrl = (hotAttrContainer?.dataset.attributeUrl || "").trim();

    /** id 在路径中时（/Quotation/Action/FABH001）须用 ? 连接首个查询参数，不能用 & */
    const appendQueryParam = (baseUrl, key, value) => {
        const base = (baseUrl || "").trim();
        if (!base) return "";
        const connector = base.includes("?") ? "&" : "?";
        return `${base}${connector}${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`;
    };
    const saveLeafItemsUrl = (hotAdditionalContainer?.dataset.saveLeafUrl || "").trim();
    const leafQuotationNo = (hotAdditionalContainer?.dataset.quotationNo || "").trim();
    const leafLicenseKey = (hotAdditionalContainer?.dataset.licenseKey || "").trim();
    const attrLicenseKey = (hotAttrContainer?.dataset.licenseKey || "").trim();

    // CSRF token helper
    const getAntiForgeryToken = () => {
        const form = document.getElementById("price-save-form");
        if (!form) return "";
        const input = form.querySelector("input[name='__RequestVerificationToken']");
        return input ? input.value : "";
    };

    // ── 附加费用项 Handsontable ──
    let additionalSaveTimer = 0;

    const showAdditionalPanel = () => {
        additionalItemsSection?.classList.remove("d-none");
        additionalItemsSection?.classList.add("is-primary-panel");
        additionalItemsBody?.classList.remove("is-collapsed");
        if (additionalItemsChevron) additionalItemsChevron.className = "bi bi-chevron-down";
        hotContainerEl?.classList.add("is-hidden-panel");
        componentUsagePanel?.classList.add("d-none");
        document.getElementById("fill-price-action-buttons")?.style.setProperty("display", "none");
    };

    const showComponentPanel = () => {
        additionalItemsSection?.classList.remove("is-primary-panel");
        additionalItemsSection?.classList.add("d-none");
        hotContainerEl?.classList.remove("is-hidden-panel");
        document.getElementById("fill-price-action-buttons")?.style.removeProperty("display");
    };

    const refreshHotAdditionalLayout = (rowCount) => {
        if (!hotAdditionalContainer || !hotAdditional) return;
        const inPrimary = additionalItemsSection?.classList.contains("is-primary-panel");
        let height;
        if (inPrimary) {
            const card = cabinetTablePane?.querySelector(".oa-card");
            const statusBar = document.getElementById("cabinet-status-bar");
            const cardH = card?.clientHeight || 500;
            const barH = statusBar?.offsetHeight || 40;
            height = Math.max(200, cardH - barH - 20);
        } else {
            height = calcHandsontableHeight(rowCount);
        }
        hotAdditional.updateSettings({ height });
        hotAdditional.render();
    };

    const loadCabinetAdditionalView = async (unitNo, nodeType) => {
        const target = (unitNo || "").trim();
        if (!target) return;
        try {
            leaveSummaryMode();
            selectedCabinetCode = target;
            setSelectedTreeNode(target);
            currentCabinetUnitCode = target;
            additionalViewActive = true;
            cabinetViewActive = false;
            refPriceColumnVisible = false;
            applyHotColumnLayout();
            hideUsagePanel();

            setMessage(`正在加载节点 ${target} 的附加费用项...`, false);
            showAdditionalPanel();
            await loadAdditionalItems(target, nodeType);

            const cab = cabinetList.find((c) => c.code === target);
            const label = cab && cab.name
                ? `${cab.name}（${target}）`
                : (nodeType === "leaf" ? `费用项 ${target}` : `控制柜 ${target}`);
            updateCabinetStatusBar(label);
            updateProgressVisibility();
            refreshProgress();
            setMessage(
                `已加载 ${target} 附加费用项，共 ${currentAdditionalData.length} 条。`,
                false
            );
            applyButtonStates();
        } catch (error) {
            const message = error instanceof Error ? error.message : "读取附加费用项失败";
            setMessage(message, true);
        }
    };

    const ADDITIONAL_COLS = [
        { data: "x_mc", title: "名称", readOnly: true, width: 80 },
        { data: "x_ggxh", title: "规格型号", width: 160 },
        { data: "x_dw", title: "单位", width: 55 },
        { data: "x_bj_dj", title: "单价", type: "numeric", numericFormat: { pattern: "0.00" }, width: 80 },
        { data: "x_sl", title: "数量", type: "numeric", numericFormat: { pattern: "0.##" }, width: 55 },
        {
            data: "_amount", title: "金额", readOnly: true, width: 90,
            renderer: (instance, td, row, col, prop, value) => {
                const rowData = instance.getSourceDataAtRow(row) || {};
                const price = parseFloat(rowData.x_bj_dj) || 0;
                const qty = parseFloat(rowData.x_sl) || 0;
                const fdds = parseFloat(rowData.x_fdds) || 0;
                const amount = price * (1 + fdds / 100) * qty;
                td.textContent = "¥" + amount.toFixed(2);
                td.style.backgroundColor = "#dbeafe";
                td.style.textAlign = "right";
                return td;
            }
        },
        { data: "x_fdds", title: "浮动%", type: "numeric", numericFormat: { pattern: "0.##" }, width: 55 },
        { data: "x_sccj", title: "厂家", width: 100 }
    ];

    const initHotAdditional = (rowCount = 4) => {
        if (!hotAdditionalContainer || typeof Handsontable === "undefined") return;
        if (hotAdditional) { hotAdditional.destroy(); hotAdditional = null; }

        const initialHeight = calcHandsontableHeight(rowCount);
        hotAdditional = new Handsontable(hotAdditionalContainer, {
            licenseKey: leafLicenseKey,
            data: [],
            columns: ADDITIONAL_COLS,
            colHeaders: ADDITIONAL_COLS.map(c => c.title),
            rowHeaders: true,
            stretchH: "all",
            width: "100%",
            height: initialHeight,
            contextMenu: false,
            manualRowMove: false,
            manualColumnResize: true,
            cells(row, col) {
                const rowData = this.instance.getSourceDataAtRow(row);
                if (globalReadOnly) return { readOnly: true };
                const meta = {};
                if (col === 3 && rowData) {
                    const price = parseFloat(rowData.x_bj_dj) || 0;
                    if (price <= 0) meta.className = "ht-price-empty";
                }
                return meta;
            },
            afterChange(changes, source) {
                if (!changes || source === "loadData") return;
                clearTimeout(additionalSaveTimer);
                additionalSaveTimer = setTimeout(() => saveAdditionalItems(), 600);
                updateAdditionalTotal();
            }
        });
    };

    const loadAdditionalItems = async (unitCode, nodeType) => {
        if (!additionalItemsUrl || !unitCode) return;
        const url = appendQueryParam(additionalItemsUrl, "unitCode", unitCode);
        const resp = await fetch(url);
        const result = await resp.json();
        if (!result.success) throw new Error(result.message || "加载附加费用项失败");
        currentAdditionalData = Array.isArray(result.rows) ? result.rows : [];

        if (!hotAdditional) {
            initHotAdditional(currentAdditionalData.length);
        }
        hotAdditional.loadData(currentAdditionalData);
        requestAnimationFrame(() => refreshHotAdditionalLayout(currentAdditionalData.length));

        if (additionalItemsCount) {
            additionalItemsCount.textContent = `${currentAdditionalData.length} 项`;
        }
        updateAdditionalTotal();
    };

    const saveAdditionalItems = async () => {
        if (!saveLeafItemsUrl || !hotAdditional) return;
        const items = hotAdditional.getSourceData().map(row => ({
            code: row.x_bm,
            spec: row.x_ggxh || null,
            unit: row.x_dw || null,
            price: parseFloat(row.x_bj_dj) || 0,
            qty: parseFloat(row.x_sl) || 0,
            floatRate: parseFloat(row.x_fdds) || 0,
            vendor: row.x_sccj || null
        })).filter(r => r.code);

        if (items.length === 0) return;
        const token = getAntiForgeryToken();
        try {
            await fetch(saveLeafItemsUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    ...(token ? { RequestVerificationToken: token } : {})
                },
                body: JSON.stringify({ quotationNo: leafQuotationNo, items })
            });
            updateAdditionalTotal();
        } catch (_) { /* 静默失败，下次触发重试 */ }
    };

    const updateAdditionalTotal = () => {
        if (hotAdditional) {
            currentAdditionalData = hotAdditional.getSourceData();
        }
        recalcTotalAmount();
    };

    // 折叠/展开附加费用项区块
    if (additionalItemsToggle) {
        additionalItemsToggle.addEventListener("click", () => {
            const isCollapsed = additionalItemsBody.classList.toggle("is-collapsed");
            if (additionalItemsChevron) {
                additionalItemsChevron.className = isCollapsed ? "bi bi-chevron-right" : "bi bi-chevron-down";
            }
        });
    }

    // ── 柜体视图：折叠 + 树节点点击（Level 1 / Level 2）──
    if (treeChildrenContainer) {
        treeChildrenContainer.addEventListener("click", async (event) => {
            const expandBtn = event.target.closest(".tree-expand-btn");
            if (expandBtn && expandBtn.tagName === "BUTTON") {
                event.stopPropagation();
                const li = expandBtn.closest("li");
                const l2List = li?.querySelector(".tree-level2-list");
                const expanded = expandBtn.getAttribute("aria-expanded") === "true";
                const nextExpanded = !expanded;
                expandBtn.setAttribute("aria-expanded", nextExpanded ? "true" : "false");
                l2List?.classList.toggle("tree-level2-collapsed", !nextExpanded);
                const icon = expandBtn.querySelector("i");
                if (icon) icon.className = nextExpanded ? "bi bi-chevron-down" : "bi bi-chevron-right";
                return;
            }

            const l2Btn = event.target.closest("[data-level2-code]");
            if (l2Btn) {
                const l2Code = l2Btn.getAttribute("data-level2-code") || "";
                const parentCode = l2Btn.getAttribute("data-parent-code") || "";
                const hasChildren = l2Btn.getAttribute("data-has-children") === "true";

                if (hasChildren) {
                    await loadCabinetComponents(parentCode);
                } else {
                    await loadCabinetAdditionalView(parentCode, "cabinet");
                    if (hotAdditional) {
                        const rowIdx = hotAdditional.getSourceData()
                            .findIndex((r) => (r.x_bm || "").trim() === l2Code.trim());
                        if (rowIdx >= 0) {
                            hotAdditional.scrollViewportTo({ row: rowIdx });
                            hotAdditional.selectRows(rowIdx);
                        }
                    }
                }
                return;
            }

            const l1Btn = event.target.closest("[data-unit-no]");
            if (!l1Btn) return;

            const unitNo = l1Btn.getAttribute("data-unit-no") || "";
            const nodeType = l1Btn.getAttribute("data-node-type") || "cabinet";
            await loadCabinetAdditionalView(unitNo, nodeType);
        });
    }

    // ── 视图切换 ──
    const switchToCabinetView = () => {
        priceWorkspace?.classList.remove("view-mode-attr");
        priceWorkspace?.classList.add("view-mode-cabinet");
        if (viewCabinetBtn) { viewCabinetBtn.className = "btn btn-sm btn-primary"; }
        if (viewAttrBtn) { viewAttrBtn.className = "btn btn-sm btn-outline-primary"; }
    };

    const switchToAttrView = () => {
        priceWorkspace?.classList.remove("view-mode-cabinet");
        priceWorkspace?.classList.add("view-mode-attr");
        if (viewCabinetBtn) { viewCabinetBtn.className = "btn btn-sm btn-outline-primary"; }
        if (viewAttrBtn) { viewAttrBtn.className = "btn btn-sm btn-primary"; }

        requestAnimationFrame(() => {
            if (hotAttr) {
                hotAttr.updateSettings({ height: calcAttrTableHeight() });
                hotAttr.render();
            }
            const selected = attrTreePane?.querySelector(".attr-tree-node-link-selected");
            const first = attrTreePane?.querySelector(".attr-tree-node-link");
            if (!selected && first) {
                first.click();
            }
        });
    };

    if (viewCabinetBtn) viewCabinetBtn.addEventListener("click", switchToCabinetView);
    if (viewAttrBtn) viewAttrBtn.addEventListener("click", switchToAttrView);

    // ── 属性视图 Handsontable ──
    let hotAttr = null;
    let currentAttrData = [];
    let currentAttrXlx = -1;
    let attrSaveTimer = 0;

    const ATTR_COLS = [
        {
            data: "cabinetName", title: "控制柜", readOnly: true, width: 110,
            renderer(instance, td, row) {
                const rowData = instance.getSourceDataAtRow(row) || {};
                if (rowData.isPlaceholder) {
                    td.textContent = "（无此项）";
                    td.className = "ht-placeholder-row";
                } else {
                    td.textContent = rowData.cabinetName || rowData.cabinetCode || "";
                }
                return td;
            }
        },
        { data: "x_ggxh", title: "规格型号", width: 180 },
        { data: "x_dw", title: "单位", width: 55 },
        { data: "x_bj_dj", title: "单价", type: "numeric", numericFormat: { pattern: "0.00" }, width: 85 },
        { data: "x_sl", title: "数量", type: "numeric", numericFormat: { pattern: "0.##" }, width: 55 },
        {
            data: "_amount", title: "金额", readOnly: true, width: 90,
            renderer: (instance, td, row) => {
                const rowData = instance.getSourceDataAtRow(row) || {};
                if (rowData.isPlaceholder) { td.textContent = ""; td.style.backgroundColor = "#f8f9fa"; return td; }
                const price = parseFloat(rowData.x_bj_dj) || 0;
                const qty = parseFloat(rowData.x_sl) || 0;
                const fdds = parseFloat(rowData.x_fdds) || 0;
                const amount = price * (1 + fdds / 100) * qty;
                td.textContent = "¥" + amount.toFixed(2);
                td.style.backgroundColor = "#dbeafe";
                td.style.textAlign = "right";
                return td;
            }
        },
        { data: "x_fdds", title: "浮动%", type: "numeric", numericFormat: { pattern: "0.##" }, width: 55 },
        { data: "x_sccj", title: "厂家", width: 110 }
    ];

    const initHotAttr = () => {
        if (!hotAttrContainer || typeof Handsontable === "undefined") return;
        if (hotAttr) { hotAttr.destroy(); hotAttr = null; }

        hotAttr = new Handsontable(hotAttrContainer, {
            licenseKey: attrLicenseKey,
            data: [],
            columns: ATTR_COLS,
            colHeaders: ATTR_COLS.map(c => c.title),
            rowHeaders: true,
            stretchH: "all",
            width: "100%",
            height: calcAttrTableHeight(),
            contextMenu: false,
            manualRowMove: false,
            manualColumnResize: true,
            cells(row, col) {
                const rowData = this.instance.getSourceDataAtRow(row);
                if (globalReadOnly || !rowData || rowData.isPlaceholder || !rowData.x_bm) {
                    return { readOnly: true, className: "ht-placeholder-row" };
                }
                if (col === 3) {
                    const price = parseFloat(rowData.x_bj_dj) || 0;
                    if (price <= 0) return { className: "ht-price-empty" };
                }
                return {};
            },
            afterChange(changes, source) {
                if (!changes || source === "loadData") return;
                clearTimeout(attrSaveTimer);
                attrSaveTimer = setTimeout(() => saveAttrItems(), 600);
                updateAttrTotal();
            }
        });
    };

    const loadAttributeItems = async (xlx, attrName) => {
        if (!attributeItemsUrl) return;
        currentAttrXlx = xlx;
        const url = appendQueryParam(attributeItemsUrl, "xlx", xlx);
        try {
            const resp = await fetch(url);
            const result = await resp.json();
            if (!result.success) throw new Error(result.message || "加载属性数据失败");
            currentAttrData = Array.isArray(result.rows) ? result.rows : [];
            if (!hotAttr) initHotAttr();
            hotAttr.loadData(currentAttrData);
            requestAnimationFrame(() => {
                hotAttr.updateSettings({ height: calcAttrTableHeight() });
                hotAttr.render();
            });
            if (attrNodeLabelEl) attrNodeLabelEl.textContent = attrName || `属性 ${xlx}`;
            updateAttrTotal();
            if (batchFillBtn) batchFillBtn.classList.remove("d-none");
        } catch (e) {
            if (attrNodeLabelEl) attrNodeLabelEl.textContent = "加载失败";
        }
    };

    const saveAttrItems = async () => {
        if (!saveLeafItemsUrl || !hotAttr) return;
        const attrQuotationNo = hotAttrContainer?.dataset.quotationNo || leafQuotationNo;
        const items = hotAttr.getSourceData()
            .filter(row => row && !row.isPlaceholder && row.x_bm)
            .map(row => ({
                code: row.x_bm,
                spec: row.x_ggxh || null,
                unit: row.x_dw || null,
                price: parseFloat(row.x_bj_dj) || 0,
                qty: parseFloat(row.x_sl) || 0,
                floatRate: parseFloat(row.x_fdds) || 0,
                vendor: row.x_sccj || null
            }));

        if (items.length === 0) return;
        const token = getAntiForgeryToken();
        try {
            await fetch(saveLeafItemsUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    ...(token ? { RequestVerificationToken: token } : {})
                },
                body: JSON.stringify({ quotationNo: attrQuotationNo, items })
            });
        } catch (_) { /* 静默失败 */ }
    };

    const updateAttrTotal = () => {
        if (!hotAttr || !attrNodeTotalEl) return;
        let total = 0;
        hotAttr.getSourceData().forEach(row => {
            if (!row || row.isPlaceholder) return;
            const price = parseFloat(row.x_bj_dj) || 0;
            const qty = parseFloat(row.x_sl) || 0;
            const fdds = parseFloat(row.x_fdds) || 0;
            total += price * (1 + fdds / 100) * qty;
        });
        attrNodeTotalEl.textContent = "¥" + total.toFixed(2);
    };

    // ── 批量填写按钮 ──
    if (batchFillBtn) {
        batchFillBtn.addEventListener("click", () => {
            if (!hotAttr) return;
            const sourceData = hotAttr.getSourceData();

            // 找到首个有规格且单价>0的非占位行作为模板
            const template = sourceData.find(row =>
                row && !row.isPlaceholder && row.x_bm
                && (row.x_ggxh || "").trim() !== ""
                && (parseFloat(row.x_bj_dj) || 0) > 0
            );
            if (!template) {
                alert("未找到可用模板行（需有规格型号且单价 > 0）");
                return;
            }

            let filledCount = 0;
            const changes = [];
            sourceData.forEach((row, rowIdx) => {
                if (!row || row.isPlaceholder || !row.x_bm) return;
                const specEmpty = !(row.x_ggxh || "").trim();
                const priceZero = (parseFloat(row.x_bj_dj) || 0) === 0;
                if (specEmpty || priceZero) {
                    if (specEmpty) changes.push([rowIdx, "x_ggxh", template.x_ggxh]);
                    if (priceZero) changes.push([rowIdx, "x_bj_dj", template.x_bj_dj]);
                    if (!(row.x_dw || "").trim()) changes.push([rowIdx, "x_dw", template.x_dw]);
                    if (!(row.x_sccj || "").trim()) changes.push([rowIdx, "x_sccj", template.x_sccj]);
                    filledCount++;
                }
            });

            if (changes.length === 0) {
                alert("所有行均已填写，无需批量填充。");
                return;
            }

            hotAttr.setDataAtRowProp(changes, "batchFill");
            clearTimeout(attrSaveTimer);
            attrSaveTimer = setTimeout(() => saveAttrItems(), 600);
            updateAttrTotal();
            if (infoBarEl) {
                infoBarEl.textContent = `已批量填写 ${filledCount} 行`;
                infoBarEl.className = "alert alert-success py-2 px-3 small mb-2";
            }
        });
    }

    // ── 属性视图树节点点击 ──
    const attrTreeContainer = attrTreePane?.querySelector("ul");
    if (attrTreeContainer) {
        attrTreeContainer.addEventListener("click", (event) => {
            const btn = event.target.closest(".attr-tree-node-link");
            if (!btn) return;

            // 更新选中样式
            attrTreeContainer.querySelectorAll(".attr-tree-node-link").forEach(b =>
                b.classList.remove("attr-tree-node-link-selected"));
            btn.classList.add("attr-tree-node-link-selected");

            const xlx = parseInt(btn.dataset.xlx ?? "-1", 10);
            const attrName = btn.dataset.attrName || "";
            loadAttributeItems(xlx, attrName);
        });
    }

})();
