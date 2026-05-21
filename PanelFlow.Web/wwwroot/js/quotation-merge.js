(() => {
    const container = document.getElementById("hot-container");
    if (!container || typeof Handsontable === "undefined") {
        return;
    }

    const licenseKey = container.dataset.licenseKey || "";
    const mergeUrl = container.dataset.mergeUrl || "";
    const exportUrl = container.dataset.exportUrl || "";
    const sheetCountUrl = container.dataset.sheetCountUrl || "";
    const multiSheetMergeUrl = container.dataset.multiSheetMergeUrl || "";
    const infoBarEl = document.getElementById("page-info-bar");
    const treePaneEl = document.getElementById("price-tree-pane");
    const splitterEl = document.getElementById("price-splitter");
    const toggleTreeBtn = document.getElementById("toggle-tree-btn");
    const treeChildrenContainer = document.getElementById("tree-children-container");
    const openExcelBtn = document.getElementById("open-excel-btn");
    const mergeExcelBtn = document.getElementById("merge-excel-btn");
    const exportExcelBtn = document.getElementById("export-excel-btn");
    const clearAllBtn = document.getElementById("clear-all-btn");
    const excelInput = document.getElementById("excel-file-input");
    const uploadForm = document.getElementById("excel-upload-form");

    if (!openExcelBtn || !excelInput || !uploadForm || !mergeUrl) {
        return;
    }

    const colHeaders = ["序号", "单元号", "名称", "规格", "单价", "数量", "生产厂家", "总价"];
    let selectedFiles = [];
    let hasMergedData = false;
    let hasExported = false;  // 是否已导出
    // 单元号 → 表格行索引（从合并结果记录）
    let unitRowMap = new Map();

    // Bridge 回调数组（供 batch 模块注册）
    const _clearAllCallbacks = [];
    const _treeRebuiltCallbacks = [];

    const setMessage = (message, isError) => {
        if (!infoBarEl) return;
        infoBarEl.textContent = message || "";
        infoBarEl.classList.remove("alert-info", "alert-danger", "alert-success");
        infoBarEl.classList.add(isError ? "alert-danger" : "alert-success");
    };

    const hot = new Handsontable(container, {
        data: [],
        rowHeaders: true,
        colHeaders,
        columns: Array.from({ length: 8 }, () => ({ type: "text" })),
        stretchH: "all",
        width: "100%",
        height: "100%",
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
        mergeExcelBtn.disabled = selectedFiles.length === 0;
        exportExcelBtn.disabled = !hasMergedData;
    };

    const getToken = () => {
        const tokenInput = uploadForm.querySelector("input[name='__RequestVerificationToken']");
        return tokenInput ? tokenInput.value : "";
    };

    const getRowsForExport = () => {
        const data = hot.getData();
        return data.map((row) => (Array.isArray(row) ? row.map((cell) => (cell ?? "").toString().trim()) : []));
    };

    const updateTreeFromFiles = () => {
        if (!treeChildrenContainer) return;

        treeChildrenContainer.innerHTML = "";
        if (selectedFiles.length === 0) {
            const li = document.createElement("li");
            li.className = "text-muted small ms-4";
            li.textContent = "暂无文件";
            treeChildrenContainer.appendChild(li);
            return;
        }

        selectedFiles.forEach((file) => {
            const li = document.createElement("li");
            li.className = "ms-4 mb-1";
            li.innerHTML = `<i class="bi bi-file-earmark-excel text-success me-1"></i>${file.name}`;
            treeChildrenContainer.appendChild(li);
        });
    };

    // 根据合并后的表格数据生成目录树（带重复/错误标记）
    const buildTreeFromGrid = () => {
        if (!treeChildrenContainer) return;

        const rows = getRowsForExport();
        treeChildrenContainer.innerHTML = "";
        unitRowMap = new Map();

        // 第一轮：找出每个单元号的范围（开始行 → 结束行）
        const units = [];
        let currentUnit = null;
        for (let i = 0; i < rows.length; i++) {
            const unitNo = (rows[i][1] || "").trim();
            if (unitNo) {
                if (currentUnit) {
                    currentUnit.endRow = i - 1;
                    units.push(currentUnit);
                }
                currentUnit = { unitNo, startRow: i, endRow: i };
                if (!unitRowMap.has(unitNo)) {
                    unitRowMap.set(unitNo, i);
                }
            }
        }
        if (currentUnit) {
            currentUnit.endRow = rows.length - 1;
            units.push(currentUnit);
        }

        // 统计每个单元号出现次数（用于检测重复）
        const unitNameCount = new Map();
        units.forEach((u) => {
            unitNameCount.set(u.unitNo, (unitNameCount.get(u.unitNo) || 0) + 1);
        });

        if (units.length === 0) {
            const li = document.createElement("li");
            li.className = "text-muted small ms-4";
            li.textContent = "暂无单元数据";
            treeChildrenContainer.appendChild(li);
            _treeRebuiltCallbacks.forEach(fn => fn());
            return;
        }

        // 插入"全选/取消全选"按钮区域（若不存在）
        if (!document.getElementById("tree-select-actions")) {
            const actionsDiv = document.createElement("div");
            actionsDiv.id = "tree-select-actions";
            actionsDiv.className = "d-flex gap-2 mb-2";
            actionsDiv.innerHTML = `
                <button id="select-all-units-btn" type="button" class="btn btn-sm btn-outline-secondary">全选</button>
                <button id="deselect-all-units-btn" type="button" class="btn btn-sm btn-outline-secondary">取消全选</button>
            `;
            treeChildrenContainer.parentNode.insertBefore(actionsDiv, treeChildrenContainer);
        }

        // 第二轮：为每个单元生成节点（包括错误检查）
        units.forEach((unit) => {
            const isDuplicate = (unitNameCount.get(unit.unitNo) || 0) > 1;

            // 检查该单元下的元件行是否有规格为空或数量问题
            let hasError = false;
            const errorReasons = [];
            for (let r = unit.startRow + 1; r <= unit.endRow; r++) {
                const row = rows[r];
                if (!row) continue;
                const name = (row[2] || "").trim();
                const spec = (row[3] || "").trim();
                const qty = (row[5] || "").trim();
                // 只校验有内容的元件行
                if (name || spec || qty) {
                    if (!spec) {
                        hasError = true;
                        if (!errorReasons.includes("规格为空")) errorReasons.push("规格为空");
                    }
                    const qtyNum = Number(qty);
                    if (!qty) {
                        hasError = true;
                        if (!errorReasons.includes("数量为空")) errorReasons.push("数量为空");
                    } else if (!Number.isFinite(qtyNum) || qtyNum <= 0) {
                        hasError = true;
                        if (!errorReasons.includes("数量无效")) errorReasons.push("数量无效");
                    }
                } else if (!name && !spec && !qty) {
                    // 完全空行（名称、规格、数量都为空）在有内容行之间出现
                    // 不单独标记，跳过
                }
            }

            const li = document.createElement("li");
            li.className = "ms-4 mb-1";

            const btn = document.createElement("button");
            btn.type = "button";
            btn.className = "tree-node-link";
            btn.setAttribute("data-unit-no", unit.unitNo);
            btn.setAttribute("data-row-index", unit.startRow.toString());

            const icon = isDuplicate
                ? '<i class="bi bi-exclamation-triangle-fill me-1 text-danger"></i>'
                : '<i class="bi bi-dot"></i>';

            const nameSpan = isDuplicate
                ? `<span class="text-danger fw-semibold">${unit.unitNo}</span>`
                : unit.unitNo;

            const tags = [];
            if (isDuplicate) tags.push('<span class="badge bg-danger ms-1" style="font-size:10px;">重复</span>');
            if (hasError) {
                const tooltip = errorReasons.join("、");
                tags.push(`<span class="badge bg-warning text-dark ms-1" style="font-size:10px;cursor:help;" title="${tooltip}">错误</span>`);
            }

            btn.innerHTML = `${icon}${nameSpan}${tags.join("")}`;
            // 注入 checkbox（批量编辑用）
            const cb = document.createElement("input");
            cb.type = "checkbox";
            cb.className = "oa-unit-checkbox form-check-input me-2 flex-shrink-0";
            cb.value = unit.unitNo;
            cb.setAttribute("data-unit-no", unit.unitNo);
            li.appendChild(cb);
            li.appendChild(btn);
            treeChildrenContainer.appendChild(li);
        });

        // 通知 batch 模块目录树已重建
        _treeRebuiltCallbacks.forEach(fn => fn());
    };

    // 目录树点击事件：跳转到对应的单元号行
    if (treeChildrenContainer) {
        treeChildrenContainer.addEventListener("click", (event) => {
            const trigger = event.target.closest("[data-unit-no]");
            if (!trigger) return;

            const rowIndexStr = trigger.getAttribute("data-row-index");
            const unitNo = trigger.getAttribute("data-unit-no") || "";
            const rowIndex = Number.parseInt(rowIndexStr || "-1", 10);

            if (rowIndex >= 0) {
                hot.selectCell(rowIndex, 1);
                if (typeof hot.scrollViewportTo === "function") {
                    hot.scrollViewportTo(rowIndex, 1);
                }
                setMessage(`已定位到单元号：${unitNo}（第 ${rowIndex + 1} 行）`, false);
            }
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
            if (collapsed) return;
            dragging = true;
            startX = event.clientX;
            startWidth = treePaneEl.getBoundingClientRect().width;
            document.body.style.cursor = "col-resize";
            event.preventDefault();
        });

        window.addEventListener("mousemove", (event) => {
            if (!dragging || collapsed) return;
            const delta = event.clientX - startX;
            const workspace = treePaneEl.parentElement;
            if (!workspace) return;
            const workspaceWidth = workspace.getBoundingClientRect().width;
            const minWidth = 220;
            const maxWidth = Math.max(minWidth, workspaceWidth * 0.6);
            const nextWidth = Math.min(maxWidth, Math.max(minWidth, startWidth + delta));
            treePaneEl.style.width = `${nextWidth}px`;
        });

        window.addEventListener("mouseup", () => {
            if (!dragging) return;
            dragging = false;
            document.body.style.cursor = "";
            hot.render();
        });

        toggleTreeBtn.addEventListener("click", () => setCollapsed(!collapsed));
        refreshToggleText();
    }

    openExcelBtn.addEventListener("click", () => excelInput.click());

    // 表格修改后刷新目录树
    let renderTimer = null;
    const scheduleTreeRebuild = () => {
        if (!hasMergedData) return;
        if (renderTimer) clearTimeout(renderTimer);
        renderTimer = setTimeout(() => buildTreeFromGrid(), 200);
    };

    hot.addHook("afterChange", (changes, source) => {
        if (!changes || source === "loadData") return;
        scheduleTreeRebuild();
    });

    hot.addHook("afterCreateRow", scheduleTreeRebuild);
    hot.addHook("afterRemoveRow", scheduleTreeRebuild);

    excelInput.addEventListener("change", () => {
        const files = excelInput.files;
        if (!files || files.length === 0) return;

        for (let i = 0; i < files.length; i++) {
            selectedFiles.push(files[i]);
        }

        updateTreeFromFiles();
        applyButtonStates();
        setMessage(`已选择 ${selectedFiles.length} 个文件，点击"合并"按钮开始合并。`, false);

        excelInput.value = "";
    });

    clearAllBtn.addEventListener("click", () => {
        selectedFiles = [];
        hot.loadData([]);
        hasMergedData = false;
        updateTreeFromFiles();
        applyButtonStates();
        setMessage("已清空所有数据。", false);
        // 通知 batch 模块重置
        _clearAllCallbacks.forEach(fn => fn());
    });

    // 多 Sheet 信息栏格式化
    const formatMultiSheetMessage = (response) => {
        let msg = `读取了 1 个文件，${response.totalSheets} 个 sheet 页，共 ${response.importedSheets} 个控制柜/操作箱`;

        if (response.ignoredSheets > 0 && Array.isArray(response.ignoredSheetNames) && response.ignoredSheetNames.length > 0) {
            const reasons = Array.isArray(response.ignoredSheetReasons) ? response.ignoredSheetReasons : [];
            const details = response.ignoredSheetNames.map((name, idx) => {
                const reason = reasons[idx] ? `[${reasons[idx]}]` : "";
                return `${name}${reason}`;
            });
            msg += `（忽略 ${response.ignoredSheets} 个 sheet：${details.join("、")}）`;
        }

        if (response.reachedLimit === true) {
            msg += `（已达到 5000 行上限，后续 sheet 未完整读取）`;
        }

        return msg;
    };

    // 多 Sheet 合并处理函数
    const handleMultiSheetMerge = async (file) => {
        setMessage("正在合并多 Sheet 文件...", false);
        const antiForgeryToken = getToken();
        const formData = new FormData();
        formData.append("file", file);
        formData.append("startSeqNo", "0");

        try {
            const response = await fetch(multiSheetMergeUrl, {
                method: "POST",
                headers: antiForgeryToken ? { RequestVerificationToken: antiForgeryToken } : {},
                body: formData
            });

            if (!response.ok) {
                setMessage("合并请求失败，请检查网络或稍后重试", true);
                return;
            }

            const result = await response.json();

            if (!result.success) {
                setMessage(result.message || "合并失败", true);
                return;
            }

            const rows = Array.isArray(result.rows) ? result.rows : [];
            if (rows.length === 0) {
                setMessage("所有 Sheet 均缺少必需列，无有效数据导入", true);
                return;
            }

            hot.loadData(rows);
            hasMergedData = true;
            hasExported = false;
            buildTreeFromGrid();
            applyButtonStates();
            setMessage(formatMultiSheetMessage(result), false);
        } catch (error) {
            setMessage("合并请求失败，请检查网络或稍后重试", true);
        }
    };

    // 单文件处理：通过 SingleFileApi 合并（现有逻辑提取）
    const handleSingleFileMerge = async (file) => {
        const antiForgeryToken = getToken();
        const formData = new FormData();
        formData.append("file", file);
        formData.append("startSeqNo", "0");

        const response = await fetch(mergeUrl, {
            method: "POST",
            headers: antiForgeryToken ? { RequestVerificationToken: antiForgeryToken } : {},
            body: formData
        });

        const text = await response.text();
        if (!text) {
            throw new Error("服务器返回空响应");
        }

        let result;
        try {
            result = JSON.parse(text);
        } catch (parseError) {
            throw new Error(`响应不是有效 JSON（${text.substring(0, 100)}）`);
        }

        if (result.ignored === true) {
            setMessage(`选择 1 个文件，导入 0 个文件，忽略 1 个文件`, true);
            return;
        }

        if (!response.ok || !result.success) {
            throw new Error(`文件 ${file.name} 读取失败：${result.message || "读取失败"}`);
        }

        const rows = Array.isArray(result.rows) ? result.rows : [];
        if (rows.length > 0) {
            hot.loadData(rows);
            hasMergedData = true;
            hasExported = false;
            buildTreeFromGrid();
        }
        applyButtonStates();
        setMessage(`选择 1 个文件，导入 1 个文件，忽略 0 个文件`, false);
    };

    mergeExcelBtn.addEventListener("click", async () => {
        if (selectedFiles.length === 0) {
            setMessage("请先选择 Excel 文件。", true);
            return;
        }

        // 单文件模式：先检测 Sheet 数量再路由
        if (selectedFiles.length === 1) {
            const file = selectedFiles[0];
            setMessage("正在检测文件 Sheet 信息...", false);

            const antiForgeryToken = getToken();
            const formData = new FormData();
            formData.append("file", file);

            try {
                const controller = new AbortController();
                const timeoutId = setTimeout(() => controller.abort(), 10000);

                const response = await fetch(sheetCountUrl, {
                    method: "POST",
                    headers: antiForgeryToken ? { RequestVerificationToken: antiForgeryToken } : {},
                    body: formData,
                    signal: controller.signal
                });

                clearTimeout(timeoutId);

                if (!response.ok) {
                    setMessage("无法读取文件 Sheet 信息，请重试", true);
                    return;
                }

                const result = await response.json();

                if (!result.success) {
                    setMessage("无法读取文件 Sheet 信息，请重试", true);
                    return;
                }

                if (result.sheetCount >= 2) {
                    await handleMultiSheetMerge(file);
                } else {
                    // sheetCount === 1，走现有 SingleFileApi 逻辑
                    setMessage("正在合并文件...", false);
                    await handleSingleFileMerge(file);
                }
            } catch (error) {
                // 超时（AbortError）或网络异常
                setMessage("无法读取文件 Sheet 信息，请重试", true);
            }
            return;
        }

        // 多文件模式（selectedFiles.length >= 2）：保持现有逻辑不变
        setMessage(`正在合并 ${selectedFiles.length} 个文件...`, false);
        const antiForgeryToken = getToken();
        const allRows = [];
        let currentSeqNo = 0;
        let totalCount = 0;
        let reachedLimit = false;
        let importedCount = 0;
        let ignoredCount = 0;
        let errorMessages = [];

        try {
            for (let i = 0; i < selectedFiles.length; i++) {
                const file = selectedFiles[i];
                const formData = new FormData();
                formData.append("file", file);
                formData.append("startSeqNo", currentSeqNo.toString());

                let result;
                try {
                    const response = await fetch(mergeUrl, {
                        method: "POST",
                        headers: antiForgeryToken ? { RequestVerificationToken: antiForgeryToken } : {},
                        body: formData
                    });

                    const text = await response.text();
                    if (!text) {
                        errorMessages.push(`文件 ${file.name} 读取失败：服务器返回空响应`);
                        break;
                    }

                    try {
                        result = JSON.parse(text);
                    } catch (parseError) {
                        errorMessages.push(`文件 ${file.name} 读取失败：响应不是有效 JSON`);
                        break;
                    }

                    // 缺少必需列：忽略
                    if (result.ignored === true) {
                        ignoredCount++;
                        continue;
                    }

                    if (!response.ok || !result.success) {
                        errorMessages.push(`文件 ${file.name} 读取失败：${result.message || "读取失败"}`);
                        break;
                    }
                } catch (fetchError) {
                    errorMessages.push(`文件 ${file.name} 读取失败：${fetchError.message}`);
                    break;
                }

                const rows = Array.isArray(result.rows) ? result.rows : [];
                allRows.push(...rows);
                totalCount += result.rowCount || rows.length;
                currentSeqNo = result.lastSeqNo || currentSeqNo;
                importedCount++;

                if (result.reachedLimit || allRows.length >= 5000) {
                    reachedLimit = true;
                    break;
                }
            }

            if (allRows.length === 0 && ignoredCount === 0) {
                const errorDetail = errorMessages.length > 0 ? `（${errorMessages.join("；")}）` : "";
                throw new Error(`没有成功读取任何数据${errorDetail}`);
            }

            if (allRows.length > 0) {
                hot.loadData(allRows);
                hasMergedData = true;
                hasExported = false;
                buildTreeFromGrid();
            }
            applyButtonStates();

            if (errorMessages.length > 0) {
                setMessage(errorMessages[0], true);
            } else {
                const limitHint = reachedLimit ? "（已达到 5000 行上限）" : "";
                setMessage(
                    `选择 ${selectedFiles.length} 个文件，导入 ${importedCount} 个文件，忽略 ${ignoredCount} 个文件${limitHint}`,
                    false
                );
            }
        } catch (error) {
            const message = error instanceof Error ? error.message : "Excel 合并失败";
            setMessage(message, true);
        }
    });

    exportExcelBtn.addEventListener("click", async () => {
        const rows = getRowsForExport();
        if (rows.length === 0) {
            setMessage("没有可导出的数据。", true);
            return;
        }

        // 导出期间临时禁用离开检查
        suppressLeaveCheck = true;

        try {
            setMessage("正在生成 Excel 文件...", false);
            const antiForgeryToken = getToken();
            const response = await fetch(exportUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    ...(antiForgeryToken ? { RequestVerificationToken: antiForgeryToken } : {})
                },
                body: JSON.stringify({ rows })
            });

            if (!response.ok) {
                throw new Error("导出失败，请稍后重试");
            }

            const blob = await response.blob();
            const disposition = response.headers.get("Content-Disposition") || "";
            const match = disposition.match(/filename\*=UTF-8''([^;]+)|filename="?([^"]+)"?/i);
            const fileName = decodeURIComponent((match && (match[1] || match[2])) || `合并元件表_${new Date().toISOString().slice(0, 10)}.xlsx`);
            const downloadUrl = window.URL.createObjectURL(blob);
            const a = document.createElement("a");
            a.href = downloadUrl;
            a.download = fileName;
            document.body.appendChild(a);
            a.click();
            a.remove();
            window.URL.revokeObjectURL(downloadUrl);
            hasExported = true;
            setMessage("Excel 已生成，请在浏览器下载中选择保存位置。", false);
        } catch (error) {
            const message = error instanceof Error ? error.message : "导出失败";
            setMessage(message, true);
        } finally {
            // 短暂延时后恢复检查（确保下载触发完成）
            setTimeout(() => { suppressLeaveCheck = false; }, 1000);
        }
    });

    applyButtonStates();

    // 离开页面提示：合并后未导出时拦截导航
    let suppressLeaveCheck = false;  // 临时禁用离开检查（如导出时）
    const isUnsaved = () => hasMergedData && !hasExported && !suppressLeaveCheck;

    // 浏览器关闭/刷新时提示
    window.addEventListener("beforeunload", (event) => {
        if (isUnsaved()) {
            event.preventDefault();
            event.returnValue = "请及时导出保存合并后excel文件";
            return event.returnValue;
        }
    });

    // 拦截站内菜单点击（侧边栏链接）
    document.addEventListener("click", (event) => {
        if (!isUnsaved()) return;

        const link = event.target.closest("a[href]");
        if (!link) return;

        const href = link.getAttribute("href") || "";
        // 跳过锚点、外部链接、空链接
        if (!href || href.startsWith("#") || href.startsWith("javascript:")) return;
        // 跳过新窗口打开
        if (link.getAttribute("target") === "_blank") return;
        // 跳过下载链接（download 属性）
        if (link.hasAttribute("download")) return;
        // 只拦截真正会导航离开的链接（以 / 开头或包含完整 URL）
        if (!href.startsWith("/") && !href.startsWith("http")) return;
        // 跳过当前页面链接
        const currentPath = window.location.pathname;
        if (href === currentPath || href.endsWith(currentPath)) return;

        const confirmed = window.confirm("请及时导出保存合并后excel文件。确认离开吗？");
        if (!confirmed) {
            event.preventDefault();
            event.stopPropagation();
        } else {
            // 用户确认离开：清除标志避免 beforeunload 再次提示
            hasMergedData = false;
        }
    }, true);

    // 暴露 Bridge 对象供 batch 模块使用
    window.__mergeBridge = {
        getHot: () => hot,
        getUnitRowMap: () => unitRowMap,
        isDataLoaded: () => hasMergedData,
        setMessage,
        onClearAll: (fn) => _clearAllCallbacks.push(fn),
        onTreeRebuilt: (fn) => _treeRebuiltCallbacks.push(fn),
    };
})();