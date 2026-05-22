(() => {
    // ============================================================
    // 纯函数（无 DOM 副作用），按 design.md「模块边界与契约」表
    //   validateRows(rows)        → { errors, invalidCells, invalidRows }
    //   buildSplitNames(baseName, n /* 1..99 */) → string[] 长度 = n
    //   buildPreviewNodes(rows)   → { unitNo, displayName }[]
    // 这些函数被放在 IIFE 顶部以便：
    //   1) 浏览器加载时即使 Handsontable 未注入（下方 early-return）
    //      也已被定义，可通过 module.exports 暴露给 Vitest（jsdom 环境）；
    //   2) 浏览器侧后续逻辑可直接复用，不重复实现。
    // 注意：当前实现保持与旧版 UI 行为完全一致；
    //      tasks 10.3–10.5 会在 JS 重写阶段进一步打磨函数体。
    // ============================================================

    /**
     * 验证元件表格行集，输出错误摘要与无效单元格/行集合。
     *
     * 输入约定（design.md「模块边界与契约」）：
     *   - rows: string[][]，已 trim 后的二维字符串数组，每行 8 列；
     *   - 空行定义：8 列全部 trim 后为空字符串；
     *   - 仅对非空行执行五项验证（requirements 3.1–3.6）。
     *
     * 输出（纯函数，无 DOM 副作用）：
     *   - errors: string[]，错误摘要（文案严格匹配需求文案，便于 PBT 关键字断言）
     *   - invalidCells: Set<"r:c">，二维定位的无效单元格集合
     *   - invalidRows: Set<number>，仅"连续 5 空行后非空行"使用的整行无效集合
     *
     * @param {string[][]} rows
     * @returns {{ errors: string[], invalidCells: Set<string>, invalidRows: Set<number> }}
     */
    function validateRows(rows) {
        const errors = [];
        const invalidCells = new Set();
        const invalidRows = new Set();
        // 记录首次出现的 1 基行号，便于重复检测错误消息引用首次出现位置。
        const firstSeenRowNo = new Map();
        const safeRows = Array.isArray(rows) ? rows : [];

        // requirement 3.6 配套：连续空行计数；非空行处理后归零。
        // 阈值采用 ">= 5"（前置已有 5 个连续空行 + 当前非空行），与 requirement 3.6 文案一致。
        const QUANTITY_MAX = 999_999_999.99;
        const isRowEmpty = (row) => row.every((cell) => !cell);

        let consecutiveEmptyRows = 0;
        for (let i = 0; i < safeRows.length; i += 1) {
            const row = Array.isArray(safeRows[i]) ? safeRows[i] : [];
            const rowNo = i + 1;
            if (isRowEmpty(row)) {
                consecutiveEmptyRows += 1;
                continue;
            }

            // requirement 3.6：连续 5+ 空行后再次出现非空行 → 标记当前行
            if (consecutiveEmptyRows >= 5) {
                errors.push(`第 ${rowNo} 行：连续空行超过 5 行后不能再有数据`);
                invalidRows.add(i);
            }
            consecutiveEmptyRows = 0;

            // requirement 3.2 / 3.3：第 6 列（数量）必须为有限正数，区间 (0, 999_999_999.99]
            const quantity = row[5] || "";
            if (!quantity) {
                errors.push(`第 ${rowNo} 行：第6列数量不能为空`);
                invalidCells.add(`${i}:5`);
            } else {
                const num = Number(quantity);
                if (!Number.isFinite(num) || num <= 0 || num > QUANTITY_MAX) {
                    errors.push(`第 ${rowNo} 行：第6列数量必须为正数`);
                    invalidCells.add(`${i}:5`);
                }
            }

            // requirement 3.4：单元号 / 名称 / 规格不得同时为空
            const unitNo = row[1] || "";
            const name = row[2] || "";
            const spec = row[3] || "";
            if (!unitNo && !name && !spec) {
                errors.push(`第 ${rowNo} 行：单元号、名称、规格不能同时为空`);
                invalidCells.add(`${i}:1`);
                invalidCells.add(`${i}:2`);
                invalidCells.add(`${i}:3`);
            }

            // requirement 3.5：UnitCode（非空）重复检测；空单元号不参与重复检测
            if (unitNo) {
                const firstRowNo = firstSeenRowNo.get(unitNo);
                if (firstRowNo) {
                    errors.push(`第 ${rowNo} 行：第2列单元号"${unitNo}"与第 ${firstRowNo} 行重复`);
                    invalidCells.add(`${i}:1`);
                    invalidCells.add(`${firstRowNo - 1}:1`);
                } else {
                    firstSeenRowNo.set(unitNo, rowNo);
                }
            }
        }

        return { errors, invalidCells, invalidRows };
    }

    /**
     * 按基础名称生成 N 个拆分节点名称。
     *
     * 输入约定（design.md「模块边界与契约」 + requirements 4.3–4.6）：
     *   - baseName: string（先 trim()，再做后续判断）
     *   - n: number，期望范围 1..99；超出范围按以下容错规则处理：
     *       - 非有限数 / 非整数 / <= 1 → 视为 1，返回 [trimmed]
     *       - > 99 → 截断为 99
     *
     * 输出（纯函数，无副作用）：
     *   - string[]，长度严格等于截断后的 N
     *
     * 命名规则（requirements 4.5 / 4.6）：
     *   1) baseName 末尾存在数字段：保留原始 baseName 作为首元素，
     *      其余元素以原始数字段为基数依次 +1，宽度按原始字符宽度左侧补零；
     *      当递增结果的自然宽度超过原始宽度时允许溢出（例：RH99 → RH100）。
     *   2) baseName 末尾无数字段：在 trimmed 后追加从 1 开始的递增序号
     *      （例：AB 且 N=2 → ["AB1", "AB2"]）。
     *
     * @param {string} baseName
     * @param {number} n
     * @returns {string[]}
     */
    function buildSplitNames(baseName, n) {
        const trimmed = (baseName == null ? "" : String(baseName)).trim();

        // 容错：非整数/<=1 → N=1；超过 99 → 截断
        let safeCount;
        if (!Number.isFinite(n)) {
            safeCount = 1;
        } else {
            safeCount = Math.floor(n);
            if (safeCount < 1) safeCount = 1;
            if (safeCount > 99) safeCount = 99;
        }
        if (safeCount === 1) {
            return [trimmed];
        }

        // 末尾任意位数的连续数字段（不限 1–2 位），保留原始字符宽度。
        const match = trimmed.match(/^(.*?)(\d+)$/);
        if (match) {
            const prefix = match[1];
            const rawNumber = match[2];
            const width = rawNumber.length;
            let current = Number(rawNumber);
            const names = [trimmed];
            for (let i = 1; i < safeCount; i += 1) {
                current += 1;
                // padStart：当 current 自然宽度 < width 时左补零；超出 width 时不会截断。
                names.push(`${prefix}${String(current).padStart(width, "0")}`);
            }
            return names;
        }

        // 无数字段：从 1 开始追加序号（首元素也是 trimmed + "1"，与 requirement 4.6 示例一致）
        const names = [];
        for (let i = 1; i <= safeCount; i += 1) {
            names.push(`${trimmed}${i}`);
        }
        return names;
    }

    /**
     * 根据表格行集生成目录预览节点列表。
     *
     * 输入约定（design.md「模块边界与契约」 + requirements 4.2–4.6）：
     *   - rows: string[][]，元件表格的二维字符串数组（每行 8 列）
     *   - 仅处理第 2 列（UnitCode，索引 1）trim 后非空的行；其余行跳过。
     *
     * 输出（纯函数，无副作用）：
     *   - { unitNo, displayName }[]：unitNo 为 trim 后的原始单元号；
     *     displayName 为 buildSplitNames 展开后的具体节点名称。
     *
     * 数量解析（requirement 4.4）：
     *   - 第 6 列（Quantity，索引 5）解析为整数 N；
     *   - 解析失败 / NaN / <= 0 → N = 1；
     *   - > 99 由 buildSplitNames 内部截断为 99（双保险）。
     *
     * @param {string[][]} rows
     * @returns {{ unitNo: string, displayName: string }[]}
     */
    function buildPreviewNodes(rows) {
        const safeRows = Array.isArray(rows) ? rows : [];
        const previewNodes = [];
        for (let i = 0; i < safeRows.length; i += 1) {
            const row = Array.isArray(safeRows[i]) ? safeRows[i] : [];
            const unitNo = (row[1] || "").trim();
            if (!unitNo) {
                continue;
            }
            const quantityText = (row[5] || "").trim();
            const quantityNumber = Number(quantityText);
            const quantity = Number.isFinite(quantityNumber) && quantityNumber > 0
                ? Math.floor(quantityNumber)
                : 1;
            // buildSplitNames 内部已对 N 做 1..99 截断，此处无需再 clamp。
            const names = buildSplitNames(unitNo, quantity);
            names.forEach((name) => {
                previewNodes.push({ unitNo, displayName: name });
            });
        }
        return previewNodes;
    }

    // Node 兼容导出（仅当存在 CommonJS 环境时生效；浏览器中 typeof module === "undefined"）。
    // 测试侧（Vitest + jsdom）通过 require("../quotation-import.js") 拿到三个纯函数。
    if (typeof module !== "undefined" && module.exports) {
        module.exports = { validateRows, buildSplitNames, buildPreviewNodes };
    }

    // ============================================================
    // 浏览器引导：依赖 Handsontable 与 DOM，未就绪时直接返回。
    // 注意：Vitest（jsdom）下 #hot-container 不存在，会在此处早返；
    //      上方 module.exports 已先一步导出纯函数，不影响测试。
    // ============================================================
    const container = document.getElementById("hot-container");
    if (!container || typeof Handsontable === "undefined") {
        return;
    }

    // ============================================================
    // 状态对象（task 10.1）：取代旧版散落的局部变量
    //   - hasLoadedExcelData / hasCheckPassed / hasPreviewSucceeded：
    //     按 design.md「Mermaid 状态机」推进 Idle → Loaded → Checked → Previewed
    //   - invalidCells / invalidRows：errorCellRenderer 渲染依据
    // 通过 applyButtonStates() 单一入口同步 5 个按钮的 disabled。
    // ============================================================
    const state = {
        hasLoadedExcelData: false,
        hasCheckPassed: false,
        hasPreviewSucceeded: false,
        invalidCells: new Set(),
        invalidRows: new Set(),
    };

    const licenseKey = container.dataset.licenseKey || "";
    const uploadUrl = container.dataset.uploadUrl || "";
    const saveUrl = container.dataset.saveUrl || "";
    const savePlanUrl = container.dataset.savePlanUrl || "";
    const quotationNo = container.dataset.quotationNo || "";
    const currentStatus = Number.parseInt(container.dataset.currentStatus || "", 10);
    const isEmptyQuotation = currentStatus === 1;
    const infoBarEl = document.getElementById("page-info-bar");
    const treePaneEl = document.getElementById("tree-pane");
    const splitterEl = document.getElementById("tree-splitter");
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

    /**
     * InfoBar 写入（task 10.2）：替换旧版 setMessage(msg, isError)
     * @param {string} message
     * @param {"info"|"success"|"error"} level
     *
     * 设计要点（design.md / requirements 8.3）：
     *   - InfoBar 始终可见，不自动消失；
     *   - level 决定 alert-info / alert-success / alert-danger；
     *   - 任何非 "success" / "error" 的 level 都视为 "info"，避免静默失败。
     */
    const setInfo = (message, level) => {
        if (!infoBarEl) {
            return;
        }
        infoBarEl.textContent = message || "";
        infoBarEl.classList.remove("alert-info", "alert-success", "alert-danger");
        if (level === "success") {
            infoBarEl.classList.add("alert-success");
        } else if (level === "error") {
            infoBarEl.classList.add("alert-danger");
        } else {
            infoBarEl.classList.add("alert-info");
        }
    };

    const clearInvalidMarks = () => {
        state.invalidCells.clear();
        state.invalidRows.clear();
    };

    /**
     * Handsontable 单元格渲染器（task 10.6）：从 state.invalidCells / state.invalidRows
     * 读取无效标记，应用红底 + 红边；有效格清除背景与边框。
     * 必须先委托给 Handsontable.renderers.TextRenderer 完成文本渲染。
     */
    const errorCellRenderer = (instance, td, row, col, prop, value, cellProperties) => {
        Handsontable.renderers.TextRenderer(instance, td, row, col, prop, value, cellProperties);
        const isInvalid = state.invalidRows.has(row) || state.invalidCells.has(`${row}:${col}`);
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

    /**
     * 单一按钮状态闭环（task 10.1）：依据 state 同步 5 个按钮 disabled。
     * 映射规则（design.md 状态机）：
     *   - open-excel-btn   : 始终启用
     *   - check-data-btn   : hasLoadedExcelData
     *   - preview-tree-btn : hasLoadedExcelData && hasCheckPassed
     *   - save-excel-btn   : hasLoadedExcelData
     *   - save-plan-btn    : hasLoadedExcelData && hasCheckPassed && hasPreviewSucceeded
     */
    const applyButtonStates = () => {
        if (openExcelBtn) {
            openExcelBtn.disabled = false;
        }
        checkDataBtn.disabled = !state.hasLoadedExcelData;
        previewTreeBtn.disabled = !(state.hasLoadedExcelData && state.hasCheckPassed);
        saveExcelBtn.disabled = !state.hasLoadedExcelData;
        savePlanBtn.disabled = !(state.hasLoadedExcelData && state.hasCheckPassed && state.hasPreviewSucceeded);
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

    const previewTreeFromGrid = () => {
        if (!treeChildrenContainer) {
            return false;
        }

        const rows = getRowsForValidation();
        const previewNodes = buildPreviewNodes(rows);

        treeChildrenContainer.innerHTML = "";
        if (previewNodes.length === 0) {
            const emptyLi = document.createElement("li");
            emptyLi.className = "text-muted small ms-4";
            emptyLi.textContent = "目录预览为空：表格第2列暂无有效单元号。";
            treeChildrenContainer.appendChild(emptyLi);
            setInfo("目录预览完成：未找到可用单元号。", "error");
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

        setInfo(`目录预览完成：已生成 ${previewNodes.length} 个目录子节点。`, "success");
        return true;
    };

    // 调用顶部的纯函数 validateRows(rows)，并把结果同步到 state
    // （errorCellRenderer 依赖 state.invalidCells / state.invalidRows）。
    const runValidation = () => {
        const rows = getRowsForValidation();
        const result = validateRows(rows);
        clearInvalidMarks();
        result.invalidCells.forEach((key) => state.invalidCells.add(key));
        result.invalidRows.forEach((row) => state.invalidRows.add(row));
        return result.errors;
    };

    const focusUnitBlockInGrid = (unitName) => {
        const target = (unitName || "").trim();
        if (!target) {
            setInfo("目录节点未包含有效控制柜名称。", "error");
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
            setInfo(`未在导入表第2列找到控制柜：${target}`, "error");
            return;
        }

        hot.selectCell(rowIndex, 1);
        if (typeof hot.scrollViewportTo === "function") {
            hot.scrollViewportTo(rowIndex, 1);
        }
        setInfo(`已定位到导入表中的控制柜 ${target}（第 ${rowIndex + 1} 行）。`, "success");
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

    openExcelBtn.addEventListener("click", () => excelInput.click());

    // afterChange / afterCreateRow / afterRemoveRow（task 10.1 第 3 条）：
    // 任意编辑/插入/删除一旦发生，都立刻使下游阶段失效，
    // 强制用户重新执行「数据检查」与「目录预览」，避免基于旧数据的目录树被保存。
    hot.addHook("afterChange", (changes, source) => {
        if (!changes || source === "loadData") {
            return;
        }
        if (state.hasLoadedExcelData) {
            state.hasCheckPassed = false;
            state.hasPreviewSucceeded = false;
            applyButtonStates();
        }
    });

    hot.addHook("afterCreateRow", () => {
        if (state.hasLoadedExcelData) {
            state.hasCheckPassed = false;
            state.hasPreviewSucceeded = false;
            applyButtonStates();
        }
    });

    hot.addHook("afterRemoveRow", () => {
        if (state.hasLoadedExcelData) {
            state.hasCheckPassed = false;
            state.hasPreviewSucceeded = false;
            applyButtonStates();
        }
    });

    excelInput.addEventListener("change", async () => {
        const file = excelInput.files && excelInput.files[0];
        if (!file) {
            return;
        }

        setInfo("正在读取 Excel...", "info");
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
            state.hasLoadedExcelData = rows.length > 0;
            state.hasCheckPassed = false;
            state.hasPreviewSucceeded = false;
            applyButtonStates();
            const limitHint = result.reachedLimit ? "（已达到 5000 行上限）" : "";
            setInfo(`已加载 ${rows.length} 行数据${limitHint}`, "success");
        } catch (error) {
            const message = error instanceof Error ? error.message : "Excel 读取失败";
            setInfo(message, "error");
        } finally {
            excelInput.value = "";
        }
    });

    checkDataBtn.addEventListener("click", () => {
        const errors = runValidation();
        if (errors.length === 0) {
            hot.render();
            state.hasCheckPassed = state.hasLoadedExcelData;
            state.hasPreviewSucceeded = false;
            applyButtonStates();
            setInfo("数据检查通过。", "success");
            return;
        }

        hot.render();
        state.hasCheckPassed = false;
        state.hasPreviewSucceeded = false;
        applyButtonStates();
        const preview = errors.slice(0, 8).join("；");
        const suffix = errors.length > 8 ? `（另有 ${errors.length - 8} 条）` : "";
        setInfo(`数据检查未通过：${preview}${suffix}`, "error");
    });

    previewTreeBtn.addEventListener("click", () => {
        const success = previewTreeFromGrid();
        state.hasPreviewSucceeded = success;
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
            setInfo("保存方案失败：缺少保存接口地址。", "error");
            return;
        }

        const antiForgeryToken = getToken();
        try {
            setInfo("正在保存方案...", "info");
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
            setInfo(result.message || "保存方案成功。", "success");
        } catch (error) {
            const message = error instanceof Error ? error.message : "保存方案失败";
            setInfo(message, "error");
        }
    });

    saveExcelBtn.addEventListener("click", async () => {
        if (!saveUrl) {
            setInfo("另存excel失败：缺少保存接口地址。", "error");
            return;
        }

        const antiForgeryToken = getToken();
        try {
            setInfo("正在生成 Excel 文件...", "info");
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
            setInfo("Excel 已生成，请在浏览器下载中选择保存位置。", "success");
        } catch (error) {
            const message = error instanceof Error ? error.message : "另存excel失败";
            setInfo(message, "error");
        }
    });

    applyButtonStates();
})();
