(() => {
    const container = document.getElementById("hot-container");
    if (!container || typeof Handsontable === "undefined") {
        return;
    }

    const licenseKey = container.dataset.licenseKey || "";
    const isImport = (container.dataset.isImport || "").toLowerCase() === "true";
    const uploadUrl = container.dataset.uploadUrl || "";
    const saveUrl = container.dataset.saveUrl || "";
    const savePlanUrl = container.dataset.savePlanUrl || "";
    const componentsUrl = container.dataset.componentsUrl || "";
    const projectSummaryUrl = container.dataset.projectSummaryUrl || "";
    const saveProjectSummaryUrl = container.dataset.saveProjectSummaryUrl || "";
    const projectUsageUrl = (projectSummaryUrl || "").replace("GetProjectComponentSummary", "GetProjectComponentUsage");
    const quotationNo = container.dataset.quotationNo || "";
    const infoBarEl = document.getElementById("page-info-bar");
    const treePaneEl = document.getElementById("price-tree-pane");
    const splitterEl = document.getElementById("price-splitter");
    const toggleTreeBtn = document.getElementById("toggle-tree-btn");
    const treeChildrenContainer = document.getElementById("tree-children-container");
    const treeRootNode = document.getElementById("tree-root-node");
    const openExcelBtn = document.getElementById("open-excel-btn");
    const checkDataBtn = document.getElementById("check-data-btn");
    const previewTreeBtn = document.getElementById("preview-tree-btn");
    const saveExcelBtn = document.getElementById("save-excel-btn");
    const savePlanBtn = document.getElementById("save-plan-btn");
    const saveSummaryBtn = document.getElementById("save-summary-btn");
    const excelInput = document.getElementById("excel-file-input");
    const uploadForm = document.getElementById("excel-upload-form");
    const componentUsagePanel = document.getElementById("component-usage-panel");
    const componentUsageTitle = document.getElementById("component-usage-title");
    const componentUsageList = document.getElementById("component-usage-list");

    const defaultHeaders = ["序号", "单元号", "名称", "规格", "单价", "数量", "生产厂家", "总价"];
    const componentHeaders = ["序号", "名称", "规格", "单位", "单价", "数量", "报价浮动", "厂家"];
    const summaryEditableCols = new Set([3, 4, 6, 7]);
    const invalidCellSet = new Set();
    const invalidRowSet = new Set();

    /** 与 BJB char(n) / BjbImportFieldLimits 一致（按 GBK 字节） */
    const BJB_FIELD_LIMITS = { xMc: 50, xGgxh: 50, xSccj: 50 };

    /** 对齐 SQL Server 中文 char/varchar：按 GBK(936) 字节计长 */
    function sqlByteLen(text) {
        const s = (text ?? "").trim();
        let bytes = 0;
        for (let i = 0; i < s.length; i += 1) {
            const code = s.charCodeAt(i);
            if (code >= 0xDC00 && code <= 0xDFFF) {
                continue;
            }
            if (code <= 0x7F) {
                bytes += 1;
            } else if (code >= 0xD800 && code <= 0xDBFF) {
                bytes += 2;
                if (i + 1 < s.length) {
                    const low = s.charCodeAt(i + 1);
                    if (low >= 0xDC00 && low <= 0xDFFF) {
                        i += 1;
                    }
                }
            } else {
                bytes += 2;
            }
        }
        return bytes;
    }

    function appendByteLengthError(errors, rowIndex, colIndex, value, maxLen, columnLabel) {
        const len = sqlByteLen(value);
        if (len > maxLen) {
            errors.push(
                `第 ${rowIndex + 1} 行：第${colIndex + 1}列“${columnLabel}”超过 ${maxLen} 字节（当前 ${len} 字节，中文约每字 2 字节），请缩短后重试`
            );
            markInvalidCell(rowIndex, colIndex);
        }
    }

    let hasLoadedExcelData = false;
    let canPreviewTree = false;
    let canSavePlan = false;
    let summaryMode = false;
    let summaryDirty = false;
    let summaryOriginalRows = [];
    let highlightedUnitCodes = [];

    const applyButtonStates = () => {
        if (checkDataBtn) {
            checkDataBtn.disabled = !hasLoadedExcelData;
        }
        if (saveExcelBtn) {
            saveExcelBtn.disabled = !hasLoadedExcelData;
        }
        if (previewTreeBtn) {
            previewTreeBtn.disabled = !canPreviewTree;
        }
        if (savePlanBtn) {
            savePlanBtn.disabled = !canSavePlan;
        }
        if (saveSummaryBtn) {
            saveSummaryBtn.disabled = !(summaryMode && summaryDirty);
        }
    };

    const clearTreeHighlights = () => {
        highlightedUnitCodes = [];
        if (!treeChildrenContainer) {
            return;
        }

        treeChildrenContainer.querySelectorAll("button.tree-node-link").forEach((btn) => {
            btn.classList.remove("tree-node-link-usage");
        });
    };

    const highlightTreeUnits = (unitCodes) => {
        clearTreeHighlights();
        highlightedUnitCodes = Array.isArray(unitCodes) ? unitCodes : [];
        if (!treeChildrenContainer) {
            return;
        }

        highlightedUnitCodes.forEach((unitCode) => {
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

    const markInvalidCell = (row, col) => {
        invalidCellSet.add(`${row}:${col}`);
    };

    const markInvalidRow = (row) => {
        invalidRowSet.add(row);
    };

    const clearInvalidMarks = () => {
        invalidCellSet.clear();
        invalidRowSet.clear();
    };

    const errorCellRenderer = (instance, td, row, col, prop, value, cellProperties) => {
        Handsontable.renderers.TextRenderer(instance, td, row, col, prop, value, cellProperties);
        const isInvalid = invalidRowSet.has(row) || invalidCellSet.has(`${row}:${col}`);
        if (isInvalid) {
            td.style.backgroundColor = "#ffe3e3";
            td.style.border = "1px solid #dc3545";
            td.style.color = "";
        } else if (summaryMode && !summaryEditableCols.has(col)) {
            td.style.backgroundColor = "#eef1f4";
            td.style.border = "";
            td.style.color = "#6c757d";
        } else {
            td.style.backgroundColor = "";
            td.style.border = "";
            td.style.color = "";
        }
    };

    const columns = Array.from({ length: 8 }, () => ({ type: "text", renderer: errorCellRenderer }));

    const hot = new Handsontable(container, {
        data: [],
        rowHeaders: true,
        colHeaders: defaultHeaders,
        columns,
        cells: (row, col) => ({
            renderer: errorCellRenderer,
            readOnly: summaryMode ? !summaryEditableCols.has(col) : false
        }),
        stretchH: "all",
        width: "100%",
        height: 420,
        minSpareRows: 0,
        contextMenu: {
            items: {
                row_above: { name: "在上方插入行" },
                row_below: { name: "在下方插入行" },
                col_left: { name: "在左侧插入列" },
                col_right: { name: "在右侧插入列" },
                remove_row: { name: "删除行" },
                remove_col: { name: "删除列" },
                undo: { name: "撤销" },
                redo: { name: "重做" },
                copy: { name: "复制" },
                cut: { name: "剪切" },
                make_read_only: { name: "切换只读" }
            }
        },
        licenseKey
    });

    const setMessage = (message, isError) => {
        if (!infoBarEl) {
            return;
        }

        infoBarEl.textContent = message || "";
        infoBarEl.classList.remove("alert-info", "alert-danger", "alert-success");
        infoBarEl.classList.add(isError ? "alert-danger" : "alert-success");
    };

    const applyHeaders = (headers) => {
        hot.updateSettings({
            colHeaders: headers
        });
    };

    const enterSummaryMode = () => {
        summaryMode = true;
        summaryDirty = false;
        hot.render();
        applyButtonStates();
    };

    const leaveSummaryMode = () => {
        summaryMode = false;
        summaryDirty = false;
        summaryOriginalRows = [];
        hideUsagePanel();
        hot.render();
        applyButtonStates();
    };

    const markReadOnlyView = () => {
        leaveSummaryMode();
        applyHeaders(componentHeaders);
        if (isImport) {
            hasLoadedExcelData = false;
            invalidatePreviewAndSave();
            applyButtonStates();
        }
    };

    const loadCabinetComponents = async (unitNo) => {
        const target = (unitNo || "").trim();
        if (!target) {
            setMessage("目录节点未包含有效单元号。", true);
            return;
        }

        if (!componentsUrl) {
            setMessage("缺少柜内元件读取接口地址。", true);
            return;
        }

        try {
            setMessage(`正在加载节点 ${target} 的柜内元件...`, false);
            const connector = componentsUrl.includes("?") ? "&" : "?";
            const response = await fetch(`${componentsUrl}${connector}unitCode=${encodeURIComponent(target)}`, {
                method: "GET"
            });
            const result = await response.json();
            if (!response.ok || !result.success) {
                throw new Error(result.message || "读取柜内元件失败");
            }

            const rows = Array.isArray(result.rows) ? result.rows : [];
            const mapped = rows.map((x) => ([
                (x.seq ?? "").toString(),
                (x.x_mc ?? "").toString(),
                (x.x_ggxh ?? "").toString(),
                (x.x_dw ?? "").toString(),
                (x.x_dj ?? "").toString(),
                (x.x_sl ?? "").toString(),
                (x.x_fdds ?? "").toString(),
                (x.x_sccj ?? "").toString()
            ]));
            markReadOnlyView();
            hot.loadData(mapped);
            const count = mapped.length;
            setMessage(`已加载 ${target} 柜内元件清单，共 ${count} 条。`, false);
        } catch (error) {
            const message = error instanceof Error ? error.message : "读取柜内元件失败";
            setMessage(message, true);
        }

    };

    const loadProjectComponentSummary = async () => {
        if (!projectSummaryUrl) {
            setMessage("缺少项目元件汇总接口地址。", true);
            return;
        }

        try {
            setMessage("正在加载项目全部控制柜元件汇总...", false);
            const response = await fetch(projectSummaryUrl, { method: "GET" });
            const result = await response.json();
            if (!response.ok || !result.success) {
                throw new Error(result.message || "读取项目元件汇总失败");
            }

            const rows = Array.isArray(result.rows) ? result.rows : [];
            const mapped = rows.map((x) => ([
                (x.seq ?? "").toString(),
                (x.x_mc ?? "").toString(),
                (x.x_ggxh ?? "").toString(),
                (x.x_dw ?? "").toString(),
                (x.x_dj ?? "").toString(),
                (x.x_sl ?? "").toString(),
                (x.x_fdds ?? "").toString(),
                (x.x_sccj ?? "").toString()
            ]));
            summaryOriginalRows = rows.map((x) => ({
                name: (x.x_mc ?? "").toString().trim(),
                spec: (x.x_ggxh ?? "").toString().trim(),
                unit: (x.x_dw ?? "").toString().trim(),
                price: Number(x.x_dj ?? 0),
                floatRate: Number(x.x_fdds ?? 0),
                vendor: (x.x_sccj ?? "").toString().trim()
            }));
            applyHeaders(componentHeaders);
            hot.loadData(mapped);
            enterSummaryMode();
            setMessage(`已加载项目元件汇总，共 ${mapped.length} 条。`, false);
        } catch (error) {
            const message = error instanceof Error ? error.message : "读取项目元件汇总失败";
            setMessage(message, true);
        }
    };

    const loadProjectComponentUsage = async (rowIndex) => {
        if (!summaryMode || !projectUsageUrl || rowIndex < 0 || rowIndex >= summaryOriginalRows.length) {
            return;
        }

        const row = summaryOriginalRows[rowIndex];
        try {
            const connector = projectUsageUrl.includes("?") ? "&" : "?";
            const query = new URLSearchParams({
                name: row.name,
                spec: row.spec,
                unit: row.unit,
                price: row.price.toString(),
                floatRate: row.floatRate.toString(),
                vendor: row.vendor
            });
            const response = await fetch(`${projectUsageUrl}${connector}${query.toString()}`, { method: "GET" });
            const result = await response.json();
            if (!response.ok || !result.success) {
                throw new Error(result.message || "读取元件使用控制柜失败");
            }

            const usageRows = Array.isArray(result.rows) ? result.rows : [];
            renderUsagePanel(row, usageRows);
        } catch (error) {
            const message = error instanceof Error ? error.message : "读取元件使用控制柜失败";
            setMessage(message, true);
        }
    };

    if (treeChildrenContainer) {
        treeChildrenContainer.addEventListener("click", (event) => {
            const trigger = event.target.closest("[data-unit-no]");
            if (!trigger) {
                return;
            }

            const unitNo = trigger.getAttribute("data-unit-no") || "";
            loadCabinetComponents(unitNo);
        });
    }

    if (treeRootNode) {
        treeRootNode.addEventListener("click", () => {
            loadProjectComponentSummary();
        });
    }

    hot.addHook("afterSelectionEnd", (row) => {
        if (!summaryMode) {
            return;
        }

        loadProjectComponentUsage(row);
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
            hot.render();
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
        });

        window.addEventListener("mouseup", () => {
            if (!dragging) {
                return;
            }

            dragging = false;
            document.body.style.cursor = "";
            hot.render();
        });

        toggleTreeBtn.addEventListener("click", () => {
            setCollapsed(!collapsed);
        });

        refreshToggleText();
    }

    if (!isImport) {
        return;
    }

    if (!openExcelBtn || !excelInput || !uploadForm || !uploadUrl) {
        return;
    }

    const getToken = () => {
        const tokenInput = uploadForm.querySelector("input[name='__RequestVerificationToken']");
        return tokenInput ? tokenInput.value : "";
    };

    const getRowsForValidation = () => {
        const data = hot.getData();
        return data.map((row) => (Array.isArray(row) ? row.map((cell) => (cell ?? "").toString().trim()) : []));
    };

    const isRowEmpty = (row) => row.every((cell) => !cell);

    const invalidatePreviewAndSave = () => {
        canPreviewTree = false;
        canSavePlan = false;
        applyButtonStates();
    };

    const getTreeNodeNamesForSave = () => {
        if (!treeChildrenContainer) {
            return [];
        }

        return Array.from(treeChildrenContainer.querySelectorAll("button.tree-node-link"))
            .map((btn) => (btn.textContent || "").trim())
            .filter((name) => !!name);
    };

    const parseDecimalOrZero = (value) => {
        const num = Number((value ?? "").toString().trim());
        return Number.isFinite(num) ? num : 0;
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
            const newFloatRate = parseDecimalOrZero(row[6]);
            const newVendor = (row[7] ?? "").toString().trim();

            const changedFlag = newUnit !== oldRow.unit
                || newPrice !== oldRow.price
                || newFloatRate !== oldRow.floatRate
                || newVendor !== oldRow.vendor;
            if (!changedFlag) {
                continue;
            }

            changed.push({
                name: oldRow.name,
                spec: oldRow.spec,
                oldUnit: oldRow.unit,
                oldPrice: oldRow.price,
                oldFloatRate: oldRow.floatRate,
                oldVendor: oldRow.vendor,
                newUnit,
                newPrice,
                newFloatRate,
                newVendor
            });
        }

        return changed;
    };

    const buildSplitNames = (baseName, count) => {
        const safeCount = Number.isFinite(count) ? Math.max(1, Math.floor(count)) : 1;
        if (safeCount === 1) {
            return [baseName];
        }

        const match = baseName.match(/^(.*?)(\d{1,2})$/);
        if (match) {
            const prefix = match[1];
            const rawNumber = match[2];
            const width = rawNumber.length;
            let current = Number(rawNumber);
            const names = [baseName];
            for (let i = 1; i < safeCount; i += 1) {
                current += 1;
                names.push(`${prefix}${String(current).padStart(width, "0")}`);
            }
            return names;
        }

        const names = [];
        for (let i = 1; i <= safeCount; i += 1) {
            names.push(`${baseName}${i}`);
        }
        return names;
    };

    const previewTreeFromGrid = () => {
        if (!treeChildrenContainer) {
            return false;
        }

        const rows = getRowsForValidation();
        const previewNodes = [];
        const nodeNameCount = new Map();
        for (let i = 0; i < rows.length; i += 1) {
            const unitNo = (rows[i][1] || "").trim();
            if (!unitNo) {
                continue;
            }

            const quantityText = (rows[i][5] || "").trim();
            const quantityNumber = Number(quantityText);
            const quantity = Number.isFinite(quantityNumber) && quantityNumber > 0
                ? Math.floor(quantityNumber)
                : 1;

            const isSplit = quantity > 1;
            const names = buildSplitNames(unitNo, quantity);
            names.forEach((name) => {
                const count = (nodeNameCount.get(name) || 0) + 1;
                nodeNameCount.set(name, count);
                previewNodes.push({
                    unitNo,
                    displayName: name,
                    isSplit,
                    isDuplicate: false
                });
            });
        }

        previewNodes.forEach((node) => {
            if ((nodeNameCount.get(node.displayName) || 0) > 1) {
                node.isDuplicate = true;
            }
        });

        treeChildrenContainer.innerHTML = "";
        if (previewNodes.length === 0) {
            const emptyLi = document.createElement("li");
            emptyLi.className = "text-muted small ms-4";
            emptyLi.textContent = "目录预览为空：表格第2列暂无有效单元号。";
            treeChildrenContainer.appendChild(emptyLi);
            setMessage("目录预览完成：未找到可用单元号。", true);
            return false;
        }

        previewNodes.forEach((node) => {
            const li = document.createElement("li");
            li.className = "ms-4 mb-1";
            const btn = document.createElement("button");
            btn.type = "button";
            btn.className = "tree-node-link";
            if (node.isDuplicate) {
                btn.classList.add("tree-node-link-duplicate");
            } else if (node.isSplit) {
                btn.classList.add("tree-node-link-split");
            }
            btn.setAttribute("data-unit-no", node.unitNo);
            if (node.isDuplicate) {
                btn.title = "错误:名称重复";
            } else if (node.isSplit) {
                btn.title = "已拆分";
            }

            const icon = document.createElement("i");
            icon.className = "bi bi-dot";
            btn.appendChild(icon);
            btn.append(node.displayName);

            li.appendChild(btn);
            treeChildrenContainer.appendChild(li);
        });
        setMessage(`目录预览完成：已生成 ${previewNodes.length} 个目录子节点。`, false);
        return true;
    };

    const validateRows = () => {
        const rows = getRowsForValidation();
        const errors = [];
        const unitNoMap = new Map();
        clearInvalidMarks();

        let consecutiveEmptyRows = 0;
        for (let i = 0; i < rows.length; i += 1) {
            const row = rows[i];
            const rowNo = i + 1;
            const empty = isRowEmpty(row);

            if (empty) {
                consecutiveEmptyRows += 1;
                continue;
            }

            if (consecutiveEmptyRows > 5) {
                errors.push(`第 ${rowNo} 行：连续空行超过 5 行后不能再有数据（中间最多容忍 5 个空行）。`);
                markInvalidRow(i);
            }
            consecutiveEmptyRows = 0;

            const quantity = row[5] || "";
            if (!quantity) {
                errors.push(`第 ${rowNo} 行：第6列“数量”不能为空，且必须为正数。`);
                markInvalidCell(i, 5);
            } else {
                const num = Number(quantity);
                if (!Number.isFinite(num) || num <= 0) {
                    errors.push(`第 ${rowNo} 行：第6列“数量”必须为正数。`);
                    markInvalidCell(i, 5);
                }
            }

            const name = row[2] || "";
            const spec = row[3] || "";
            const unitNo = row[1] || "";
            if (!name && !spec && !unitNo) {
                errors.push(`第 ${rowNo} 行：当第3列“名称”和第4列“规格”同时为空时，第2列“单元号”必填。`);
                markInvalidCell(i, 1);
                markInvalidCell(i, 2);
                markInvalidCell(i, 3);
            }

            if (unitNo) {
                const firstRowNo = unitNoMap.get(unitNo);
                if (firstRowNo) {
                    errors.push(`第 ${rowNo} 行：第2列“单元号”与第 ${firstRowNo} 行重复（${unitNo}）。`);
                    markInvalidCell(i, 1);
                    markInvalidCell(firstRowNo - 1, 1);
                } else {
                    unitNoMap.set(unitNo, rowNo);
                }

                const quantityText = (row[5] || "").trim();
                const quantityNumber = Number(quantityText);
                const quantity = Number.isFinite(quantityNumber) && quantityNumber > 0
                    ? Math.floor(quantityNumber)
                    : 1;
                const splitNames = buildSplitNames(unitNo, quantity);
                splitNames.forEach((splitName, idx) => {
                    const splitLen = sqlByteLen(splitName);
                    if (splitLen > BJB_FIELD_LIMITS.xMc) {
                        errors.push(
                            `第 ${rowNo} 行：单元号拆分后的控制柜名称超过 ${BJB_FIELD_LIMITS.xMc} 字节（第 ${idx + 1} 个：「${splitName}」，当前 ${splitLen} 字节），请缩短单元号或降低拆分数量`
                        );
                        markInvalidCell(i, 1);
                    }
                });
            } else {
                appendByteLengthError(errors, i, 2, name, BJB_FIELD_LIMITS.xMc, "名称");
                appendByteLengthError(errors, i, 3, spec, BJB_FIELD_LIMITS.xGgxh, "规格");
                appendByteLengthError(errors, i, 6, row[6] || "", BJB_FIELD_LIMITS.xSccj, "生产厂家");
            }
        }

        return errors;
    };

    openExcelBtn.addEventListener("click", () => {
        excelInput.click();
    });

    hot.addHook("afterChange", (changes, source) => {
        if (!changes || source === "loadData") {
            return;
        }

        if (summaryMode) {
            summaryDirty = true;
            applyButtonStates();
            return;
        }

        if (hasLoadedExcelData) {
            invalidatePreviewAndSave();
        }
    });

    hot.addHook("afterCreateRow", () => {
        if (hasLoadedExcelData) {
            invalidatePreviewAndSave();
        }
    });

    hot.addHook("afterRemoveRow", () => {
        if (hasLoadedExcelData) {
            invalidatePreviewAndSave();
        }
    });

    excelInput.addEventListener("change", async () => {
        const file = excelInput.files && excelInput.files[0];
        if (!file) {
            return;
        }

        setMessage("正在读取 Excel...", false);

        const formData = new FormData();
        formData.append("file", file);
        const antiForgeryToken = getToken();

        try {
            const response = await fetch(uploadUrl, {
                method: "POST",
                headers: antiForgeryToken
                    ? { RequestVerificationToken: antiForgeryToken }
                    : {},
                body: formData
            });

            const result = await response.json();
            if (!response.ok || !result.success) {
                throw new Error(result.message || "Excel 读取失败");
            }

            const rows = Array.isArray(result.rows) ? result.rows : [];
            leaveSummaryMode();
            applyHeaders(defaultHeaders);
            hot.loadData(rows);
            hasLoadedExcelData = rows.length > 0;
            invalidatePreviewAndSave();
            applyButtonStates();

            const limitHint = result.reachedLimit ? "（已达到 5000 行上限）" : "";
            setMessage(`已加载 ${rows.length} 行数据${limitHint}`, false);
        } catch (error) {
            const message = error instanceof Error ? error.message : "Excel 读取失败";
            setMessage(message, true);
        } finally {
            excelInput.value = "";
        }
    });

    if (checkDataBtn) {
        checkDataBtn.addEventListener("click", () => {
            const errors = validateRows();
            if (errors.length === 0) {
                hot.render();
                canPreviewTree = hasLoadedExcelData;
                canSavePlan = false;
                applyButtonStates();
                setMessage("数据检查通过。", false);
                return;
            }

            hot.render();
            canPreviewTree = false;
            canSavePlan = false;
            applyButtonStates();
            const preview = errors.slice(0, 8).join("；");
            const suffix = errors.length > 8 ? `（另有 ${errors.length - 8} 条）` : "";
            setMessage(`数据检查未通过：${preview}${suffix}`, true);
        });
    }

    if (previewTreeBtn) {
        previewTreeBtn.addEventListener("click", () => {
            const success = previewTreeFromGrid();
            canSavePlan = success;
            applyButtonStates();
        });
    }

    if (savePlanBtn) {
        savePlanBtn.addEventListener("click", async () => {
            if (!savePlanUrl) {
                setMessage("保存方案失败：缺少保存接口地址。", true);
                return;
            }

            const antiForgeryToken = getToken();
            try {
                setMessage("正在保存方案...", false);
                const response = await fetch(savePlanUrl, {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json",
                        ...(antiForgeryToken ? { RequestVerificationToken: antiForgeryToken } : {})
                    },
                    body: JSON.stringify({
                        fabh: quotationNo,
                        tableJson: getRowsForValidation(),
                        treeNodeNames: getTreeNodeNamesForSave()
                    })
                });
                const result = await response.json();
                if (!response.ok || !result.success) {
                    throw new Error(result.message || "保存方案失败");
                }
                setMessage(result.message || "保存方案成功。", false);
            } catch (error) {
                const message = error instanceof Error ? error.message : "保存方案失败";
                setMessage(message, true);
            }
        });
    }

    if (saveSummaryBtn) {
        saveSummaryBtn.addEventListener("click", async () => {
            if (!summaryMode) {
                setMessage("请先点击目录根节点进入项目元件汇总后再保存。", true);
                return;
            }
            if (!saveProjectSummaryUrl) {
                setMessage("保存数据失败：缺少汇总保存接口地址。", true);
                return;
            }

            const changes = getSummaryChangedItems();
            if (changes.length === 0) {
                setMessage("没有可保存的修改。", false);
                summaryDirty = false;
                applyButtonStates();
                return;
            }

            const antiForgeryToken = getToken();
            try {
                setMessage("正在保存元件汇总修改...", false);
                const response = await fetch(saveProjectSummaryUrl, {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json",
                        ...(antiForgeryToken ? { RequestVerificationToken: antiForgeryToken } : {})
                    },
                    body: JSON.stringify({
                        items: changes
                    })
                });
                const result = await response.json();
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

    if (saveExcelBtn) {
        saveExcelBtn.addEventListener("click", async () => {
            if (!saveUrl) {
                setMessage("另存excel失败：缺少保存接口地址。", true);
                return;
            }

            const antiForgeryToken = getToken();
            try {
                setMessage("正在生成 Excel 文件...", false);
                const response = await fetch(saveUrl, {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json",
                        ...(antiForgeryToken ? { RequestVerificationToken: antiForgeryToken } : {})
                    },
                    body: JSON.stringify({
                        quotationNo,
                        rows: getRowsForValidation()
                    })
                });

                if (!response.ok) {
                    throw new Error("导出失败，请稍后重试");
                }

                const blob = await response.blob();
                const disposition = response.headers.get("Content-Disposition") || "";
                const match = disposition.match(/filename\*=UTF-8''([^;]+)|filename="?([^"]+)"?/i);
                const fileName = decodeURIComponent((match && (match[1] || match[2])) || `报价元件表_${quotationNo}.xlsx`);

                const downloadUrl = window.URL.createObjectURL(blob);
                const a = document.createElement("a");
                a.href = downloadUrl;
                a.download = fileName;
                document.body.appendChild(a);
                a.click();
                a.remove();
                window.URL.revokeObjectURL(downloadUrl);
                setMessage("Excel 已生成，请在浏览器下载中选择保存位置。", false);
            } catch (error) {
                const message = error instanceof Error ? error.message : "另存excel失败";
                setMessage(message, true);
            }
        });
    }

    applyButtonStates();
})();
