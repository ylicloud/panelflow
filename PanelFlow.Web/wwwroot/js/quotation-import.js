(() => {
    const container = document.getElementById("hot-container");
    if (!container || typeof Handsontable === "undefined") {
        return;
    }

    const licenseKey = container.dataset.licenseKey || "";
    const uploadUrl = container.dataset.uploadUrl || "";
    const saveUrl = container.dataset.saveUrl || "";
    const savePlanUrl = container.dataset.savePlanUrl || "";
    const quotationNo = container.dataset.quotationNo || "";
    const currentStatus = Number.parseInt(container.dataset.currentStatus || "", 10);
    const isEmptyQuotation = currentStatus === 1;
    const infoBarEl = document.getElementById("page-info-bar");
    const treePaneEl = document.getElementById("price-tree-pane");
    const splitterEl = document.getElementById("price-splitter");
    const toggleTreeBtn = document.getElementById("toggle-tree-btn");
    const treeChildrenContainer = document.getElementById("tree-children-container");
    const openExcelBtn = document.getElementById("open-excel-btn");
    const checkDataBtn = document.getElementById("check-data-btn");
    const previewTreeBtn = document.getElementById("preview-tree-btn");
    const saveExcelBtn = document.getElementById("save-excel-btn");
    const savePlanBtn = document.getElementById("save-plan-btn");
    const excelInput = document.getElementById("excel-file-input");
    const uploadForm = document.getElementById("excel-upload-form");

    if (!openExcelBtn || !excelInput || !uploadForm || !uploadUrl) {
        return;
    }

    const colHeaders = ["序号", "单元号", "名称", "规格", "单价", "数量", "生产厂家", "总价"];
    const invalidCellSet = new Set();
    const invalidRowSet = new Set();
    let hasLoadedExcelData = false;
    let canPreviewTree = false;
    let canSavePlan = false;

    const setMessage = (message, isError) => {
        if (!infoBarEl) {
            return;
        }

        infoBarEl.textContent = message || "";
        infoBarEl.classList.remove("alert-info", "alert-danger", "alert-success");
        infoBarEl.classList.add(isError ? "alert-danger" : "alert-success");
    };

    const markInvalidCell = (row, col) => invalidCellSet.add(`${row}:${col}`);
    const markInvalidRow = (row) => invalidRowSet.add(row);
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
        } else {
            td.style.backgroundColor = "";
            td.style.border = "";
        }
    };

    const hot = new Handsontable(container, {
        data: [],
        rowHeaders: true,
        colHeaders,
        columns: Array.from({ length: 8 }, () => ({ type: "text", renderer: errorCellRenderer })),
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
                cut: { name: "剪切" }
            }
        },
        licenseKey
    });

    const applyButtonStates = () => {
        checkDataBtn.disabled = !hasLoadedExcelData;
        saveExcelBtn.disabled = !hasLoadedExcelData;
        previewTreeBtn.disabled = !canPreviewTree;
        savePlanBtn.disabled = !canSavePlan;
    };

    const invalidatePreviewAndSave = () => {
        canPreviewTree = false;
        canSavePlan = false;
        applyButtonStates();
    };

    const getToken = () => {
        const tokenInput = uploadForm.querySelector("input[name='__RequestVerificationToken']");
        return tokenInput ? tokenInput.value : "";
    };

    const getRowsForValidation = () => {
        const data = hot.getData();
        return data.map((row) => (Array.isArray(row) ? row.map((cell) => (cell ?? "").toString().trim()) : []));
    };

    const getTreeNodeNamesForSave = () => {
        if (!treeChildrenContainer) {
            return [];
        }

        return Array.from(treeChildrenContainer.querySelectorAll("button.tree-node-link"))
            .map((btn) => (btn.dataset.unitName || btn.textContent || "").trim())
            .filter((name) => !!name);
    };

    const isRowEmpty = (row) => row.every((cell) => !cell);

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

            const names = buildSplitNames(unitNo, quantity);
            names.forEach((name) => {
                const count = (nodeNameCount.get(name) || 0) + 1;
                nodeNameCount.set(name, count);
                previewNodes.push({ unitNo, displayName: name });
            });
        }

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
            btn.setAttribute("data-unit-no", node.unitNo);
            btn.setAttribute("data-unit-name", node.displayName);
            btn.innerHTML = `<i class="bi bi-dot"></i>${node.displayName}`;
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
            }
        }

        return errors;
    };

    const focusUnitBlockInGrid = (unitName) => {
        const target = (unitName || "").trim();
        if (!target) {
            setMessage("目录节点未包含有效控制柜名称。", true);
            return;
        }

        const rows = getRowsForValidation();
        let rowIndex = -1;
        for (let i = 0; i < rows.length; i += 1) {
            if ((rows[i][1] || "").trim() === target) {
                rowIndex = i;
                break;
            }
        }

        if (rowIndex < 0) {
            setMessage(`未在导入表第2列找到控制柜：${target}`, true);
            return;
        }

        hot.selectCell(rowIndex, 1);
        if (typeof hot.scrollViewportTo === "function") {
            hot.scrollViewportTo(rowIndex, 1);
        }
        setMessage(`已定位到导入表中的控制柜 ${target}（第 ${rowIndex + 1} 行）。`, false);
    };

    if (treeChildrenContainer) {
        treeChildrenContainer.addEventListener("click", (event) => {
            const trigger = event.target.closest("[data-unit-name]");
            if (!trigger) {
                return;
            }

            const unitName = trigger.getAttribute("data-unit-name") || "";
            focusUnitBlockInGrid(unitName);
        });
    }

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

        toggleTreeBtn.addEventListener("click", () => setCollapsed(!collapsed));
        refreshToggleText();
    }

    openExcelBtn.addEventListener("click", () => excelInput.click());

    hot.addHook("afterChange", (changes, source) => {
        if (!changes || source === "loadData") {
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
                headers: antiForgeryToken ? { RequestVerificationToken: antiForgeryToken } : {},
                body: formData
            });
            const result = await response.json();
            if (!response.ok || !result.success) {
                throw new Error(result.message || "Excel 读取失败");
            }
            const rows = Array.isArray(result.rows) ? result.rows : [];
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

    previewTreeBtn.addEventListener("click", () => {
        const success = previewTreeFromGrid();
        canSavePlan = success;
        applyButtonStates();
    });

    savePlanBtn.addEventListener("click", async () => {
        if (!isEmptyQuotation) {
            const firstConfirm = window.confirm("当前报价单有数据,导入时会清空原来数据,是否继续?");
            if (!firstConfirm) {
                return;
            }

            const secondConfirm = window.confirm("确定要覆盖当前报价单吗?");
            if (!secondConfirm) {
                return;
            }
        }

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

    applyButtonStates();
})();
