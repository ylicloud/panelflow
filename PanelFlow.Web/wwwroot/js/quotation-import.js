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
     * BJB 字段长度上限（与 BJB 表 char(n) / BjbImportFieldLimits.cs 一致）。
     * 计数口径：trim 后按 GBK(936) 字节数，对齐 SQL Server 中文 char/varchar。
     */
    const BJB_FIELD_LIMITS = {
        xMc: 50,
        xGgxh: 50,
        xSccj: 50
    };

    /**
     * 对齐 SQL Server 非 Unicode <c>char(n)</c>/<c>varchar(n)</c>（中文排序规则）：
     * 按代码页 936(GBK) 字节计长，与 C# Encoding.GetEncoding(936).GetByteCount 一致。
     * ASCII (U+0000–U+007F) = 1 字节；其余常见汉字/全角等 = 2 字节。
     */
    function sqlLen(text) {
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

    function appendLengthError(errors, invalidCells, rowIndex, colIndex, value, maxLen, columnLabel) {
        const len = sqlLen(value);
        if (len > maxLen) {
            errors.push(
                `第 ${rowIndex + 1} 行：第${colIndex + 1}列${columnLabel}超过 ${maxLen} 字节（当前 ${len} 字节，中文约每字 2 字节），请缩短后重试`
            );
            invalidCells.add(`${rowIndex}:${colIndex}`);
        }
    }

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

                const quantityText = (row[5] || "").trim();
                const quantityNumber = Number(quantityText);
                const quantity = Number.isFinite(quantityNumber) && quantityNumber > 0
                    ? Math.floor(quantityNumber)
                    : 1;
                const splitNames = buildSplitNames(unitNo, quantity);
                splitNames.forEach((splitName, idx) => {
                    const splitLen = sqlLen(splitName);
                    if (splitLen > BJB_FIELD_LIMITS.xMc) {
                        errors.push(
                            `第 ${rowNo} 行：单元号拆分后的控制柜名称超过 ${BJB_FIELD_LIMITS.xMc} 字节（第 ${idx + 1} 个：「${splitName}」，当前 ${splitLen} 字节），请缩短单元号或降低拆分数量`
                        );
                        invalidCells.add(`${i}:1`);
                    }
                });
            } else {
                appendLengthError(errors, invalidCells, i, 2, name, BJB_FIELD_LIMITS.xMc, "名称");
                appendLengthError(errors, invalidCells, i, 3, spec, BJB_FIELD_LIMITS.xGgxh, "规格");
                appendLengthError(errors, invalidCells, i, 6, row[6] || "", BJB_FIELD_LIMITS.xSccj, "生产厂家");
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
        module.exports = { validateRows, buildSplitNames, buildPreviewNodes, BJB_FIELD_LIMITS, sqlLen };
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
        /** @type {null | "overwrite" | "append"} */
        importMode: null,
        canExportPlan: false,
        invalidCells: new Set(),
        invalidRows: new Set(),
    };

    const licenseKey = container.dataset.licenseKey || "";
    const uploadUrl = container.dataset.uploadUrl || "";
    const saveUrl = container.dataset.saveUrl || "";
    const savePlanUrl = container.dataset.savePlanUrl || "";
    const exportPlanUrl = container.dataset.exportPlanUrl || "";
    const quotationNo = container.dataset.quotationNo || "";
    const currentStatus = Number.parseInt(container.dataset.currentStatus || "", 10);
    const isEmptyQuotation = currentStatus === 1;
    const hasExistingCabinetsOnLoad = container.dataset.hasExistingCabinets === "1";
    const infoBarEl = document.getElementById("page-info-bar");
    const treePaneEl = document.getElementById("tree-pane");
    const splitterEl = document.getElementById("tree-splitter");
    const toggleTreeBtn = document.getElementById("toggle-tree-btn");
    const treeChildrenContainer = document.getElementById("tree-children-container");
    const openExcelBtn = document.getElementById("open-excel-btn");
    const checkDataBtn = document.getElementById("check-data-btn");
    const previewTreeBtn = document.getElementById("preview-tree-btn");
    const saveExcelBtn = document.getElementById("save-excel-btn");
    const exportPlanExcelBtn = document.getElementById("export-plan-excel-btn");
    const savePlanBtn = document.getElementById("save-plan-btn");
    const excelInput = document.getElementById("excel-file-input");
    const uploadForm = document.getElementById("excel-upload-form");
    const saveBusyEl = document.getElementById("oa-save-busy");
    const saveBusyProgressEl = document.getElementById("oa-save-busy-progress");
    const saveBusyTitleEl = document.getElementById("oa-save-busy-title");
    const previewModeModalEl = document.getElementById("preview-mode-modal");
    const previewModeOverwriteBtn = document.getElementById("preview-mode-overwrite");
    const previewModeAppendBtn = document.getElementById("preview-mode-append");
    const savePlanBtnDefaultHtml = savePlanBtn ? savePlanBtn.innerHTML : "";
    let isSavingPlan = false;
    let cachedOriginalTreeHtml = treeChildrenContainer ? treeChildrenContainer.innerHTML : "";
    state.canExportPlan = hasExistingCabinetsOnLoad || !isEmptyQuotation;

    /** 原方案已有单元号/名称（追加时用于唯一性校验） */
    const collectExistingUnitKeys = (rootEl) => {
        const keys = new Set();
        if (!rootEl) {
            return keys;
        }
        rootEl.querySelectorAll("button.tree-node-link").forEach((btn) => {
            const no = (btn.dataset.unitNo || "").trim();
            const name = (btn.dataset.unitName || "").trim();
            if (no) {
                keys.add(no.toLowerCase());
            }
            if (name) {
                keys.add(name.toLowerCase());
            }
        });
        return keys;
    };
    let existingUnitKeys = collectExistingUnitKeys(
        (() => {
            const tmp = document.createElement("ul");
            tmp.innerHTML = cachedOriginalTreeHtml;
            return tmp;
        })()
    );

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
        infoBarEl.replaceChildren();
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

    /**
     * 跳转到 Handsontable 单元格（入参为界面 1 基行号/列号）。
     * @param {number} row1Based
     * @param {number} [col1Based=1]
     */
    const goToCell = (row1Based, col1Based = 1) => {
        const rowCount = typeof hot.countRows === "function" ? hot.countRows() : 0;
        const colCount = typeof hot.countCols === "function" ? hot.countCols() : 8;
        const row = Math.max(0, Math.min(rowCount - 1, Math.floor(row1Based) - 1));
        const col = Math.max(0, Math.min(colCount - 1, Math.floor(col1Based) - 1));
        if (rowCount <= 0) {
            return;
        }
        hot.selectCell(row, col);
        if (typeof hot.scrollViewportTo === "function") {
            hot.scrollViewportTo(row, col);
        }
        try {
            hot.listen();
        } catch {
            // ignore：部分版本无 listen
        }
    };

    /**
     * 将「第N行 / 第N行：第M列」错误渲染为可点击链接，便于快速定位。
     * @param {string[]} errors
     */
    const setValidationErrors = (errors) => {
        if (!infoBarEl) {
            return;
        }
        infoBarEl.replaceChildren();
        infoBarEl.classList.remove("alert-info", "alert-success", "alert-danger");
        infoBarEl.classList.add("alert-danger");

        infoBarEl.appendChild(document.createTextNode("数据检查未通过："));
        const shown = errors.slice(0, 8);
        shown.forEach((err, index) => {
            if (index > 0) {
                infoBarEl.appendChild(document.createTextNode("；"));
            }
            const match = String(err).match(/^第\s*(\d+)\s*行(?:：第(\d+)列)?/);
            if (match) {
                const btn = document.createElement("button");
                btn.type = "button";
                btn.className = "oa-goto-cell";
                btn.textContent = err;
                const rowNo = Number(match[1]);
                const colNo = match[2] ? Number(match[2]) : 1;
                btn.title = `点击跳转到第 ${rowNo} 行${match[2] ? `第 ${colNo} 列` : ""}（也可按 Ctrl+G 输入行号）`;
                btn.addEventListener("click", () => goToCell(rowNo, colNo));
                infoBarEl.appendChild(btn);
            } else {
                infoBarEl.appendChild(document.createTextNode(err));
            }
        });
        if (errors.length > 8) {
            infoBarEl.appendChild(
                document.createTextNode(`（另有 ${errors.length - 8} 条；按 Ctrl+G 可输入行号跳转）`)
            );
        } else {
            infoBarEl.appendChild(document.createTextNode("（点击错误可跳转；Ctrl+G 输入行号）"));
        }
    };

    /** Ctrl+G / Cmd+G：输入行号或「行,列」跳转 */
    const promptGoToRow = () => {
        const raw = window.prompt("跳转到单元格（行号，或 行,列 如 2482,4）：", "");
        if (raw == null) {
            return;
        }
        const text = String(raw).trim();
        if (!text) {
            return;
        }
        const parts = text.split(/[,，\s]+/).filter(Boolean);
        const rowNo = Number(parts[0]);
        const colNo = parts.length > 1 ? Number(parts[1]) : 1;
        if (!Number.isFinite(rowNo) || rowNo < 1) {
            setInfo("行号无效，请输入正整数（例如 2482 或 2482,4）。", "error");
            return;
        }
        goToCell(rowNo, Number.isFinite(colNo) && colNo >= 1 ? colNo : 1);
        setInfo(`已跳转到第 ${Math.floor(rowNo)} 行第 ${Math.floor(Number.isFinite(colNo) && colNo >= 1 ? colNo : 1)} 列。`, "success");
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
            openExcelBtn.disabled = isSavingPlan ? true : false;
        }
        checkDataBtn.disabled = isSavingPlan || !state.hasLoadedExcelData;
        previewTreeBtn.disabled = isSavingPlan || !(state.hasLoadedExcelData && state.hasCheckPassed);
        saveExcelBtn.disabled = isSavingPlan || !state.hasLoadedExcelData;
        if (exportPlanExcelBtn) {
            exportPlanExcelBtn.disabled = isSavingPlan || !state.canExportPlan;
        }
        savePlanBtn.disabled = isSavingPlan
            || !(state.hasLoadedExcelData && state.hasCheckPassed && state.hasPreviewSucceeded && state.importMode);
    };

    const resetPreviewState = () => {
        state.hasPreviewSucceeded = false;
        state.importMode = null;
    };

    const setSavingUi = (busy) => {
        isSavingPlan = !!busy;
        document.body.classList.toggle("oa-saving", isSavingPlan);
        if (infoBarEl) {
            infoBarEl.classList.toggle("oa-info-busy", isSavingPlan);
        }
        if (saveBusyEl) {
            saveBusyEl.classList.toggle("d-none", !isSavingPlan);
            saveBusyEl.setAttribute("aria-hidden", isSavingPlan ? "false" : "true");
        }
        if (!isSavingPlan && saveBusyProgressEl) {
            saveBusyProgressEl.textContent = "(0/0)";
        }
        if (!isSavingPlan && saveBusyTitleEl) {
            saveBusyTitleEl.textContent = "正在保存方案…";
        }
        if (savePlanBtn) {
            if (isSavingPlan) {
                savePlanBtn.innerHTML =
                    '<span class="spinner-border spinner-border-sm me-1" role="presentation"></span>正在保存…';
            } else {
                savePlanBtn.innerHTML = savePlanBtnDefaultHtml;
            }
        }
        applyButtonStates();
    };

    const updateSaveProgress = (current, total, message) => {
        const cur = Number.isFinite(Number(current)) ? Number(current) : 0;
        const tot = Number.isFinite(Number(total)) ? Number(total) : 0;
        const label = `(${cur}/${tot})`;
        if (saveBusyProgressEl) {
            saveBusyProgressEl.textContent = label;
        }
        if (saveBusyTitleEl) {
            saveBusyTitleEl.textContent = tot > 0
                ? `正在保存方案… ${label}`
                : "正在保存方案…";
        }
        if (savePlanBtn && isSavingPlan) {
            savePlanBtn.innerHTML =
                `<span class="spinner-border spinner-border-sm me-1" role="presentation"></span>保存 ${label}`;
        }
        setInfo(message || `正在保存方案…${label}`, "info");
    };

    /**
     * 读取 SavePlan 的 NDJSON 进度流；校验失败时仍可能返回普通 JSON。
     * @param {Response} response
     * @returns {Promise<{success?: boolean, unitCount?: number, componentCount?: number, message?: string}>}
     */
    const consumeSavePlanResponse = async (response) => {
        const contentType = (response.headers.get("content-type") || "").toLowerCase();
        if (!contentType.includes("ndjson")) {
            const result = await response.json();
            if (!response.ok || !result.success) {
                throw new Error(result.message || "保存方案失败");
            }
            return result;
        }

        if (!response.body) {
            throw new Error("保存方案失败：浏览器不支持进度流");
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = "";
        let doneEvent = null;

        while (true) {
            const { done, value } = await reader.read();
            if (done) {
                break;
            }
            buffer += decoder.decode(value, { stream: true });
            const lines = buffer.split("\n");
            buffer = lines.pop() || "";
            for (const rawLine of lines) {
                const line = rawLine.trim();
                if (!line) {
                    continue;
                }
                let evt;
                try {
                    evt = JSON.parse(line);
                } catch {
                    continue;
                }
                if (evt.type === "progress") {
                    updateSaveProgress(evt.current, evt.total, evt.message);
                } else if (evt.type === "done") {
                    doneEvent = evt;
                } else if (evt.type === "error") {
                    throw new Error(evt.message || "保存方案失败");
                }
            }
        }

        const tail = buffer.trim();
        if (tail) {
            try {
                const evt = JSON.parse(tail);
                if (evt.type === "progress") {
                    updateSaveProgress(evt.current, evt.total, evt.message);
                } else if (evt.type === "done") {
                    doneEvent = evt;
                } else if (evt.type === "error") {
                    throw new Error(evt.message || "保存方案失败");
                }
            } catch (error) {
                if (error instanceof Error && error.message && !error.message.includes("JSON")) {
                    throw error;
                }
            }
        }

        if (!doneEvent || !doneEvent.success) {
            throw new Error((doneEvent && doneEvent.message) || "保存方案失败：未收到完成事件");
        }
        return doneEvent;
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

        // 仅提交本次预览新增节点；追加模式下绝不能把原柜名一并提交
        return Array.from(treeChildrenContainer.querySelectorAll("button.tree-node-link[data-from-preview='1']"))
            .map((btn) => (btn.dataset.unitName || "").trim())
            .filter((name) => !!name);
    };

    const renderPreviewNodes = (previewNodes, { clearFirst }) => {
        if (!treeChildrenContainer) {
            return;
        }

        if (clearFirst) {
            treeChildrenContainer.innerHTML = "";
        } else {
            treeChildrenContainer.querySelectorAll("li.oa-tree-empty, li.text-muted").forEach((el) => el.remove());
        }

        previewNodes.forEach((node) => {
            const li = document.createElement("li");
            li.className = "ms-4 mb-1";
            const btn = document.createElement("button");
            btn.type = "button";
            btn.className = "tree-node-link is-preview-new";
            btn.setAttribute("data-unit-no", node.unitNo);
            btn.setAttribute("data-unit-name", node.displayName);
            btn.setAttribute("data-from-preview", "1");
            btn.innerHTML = `<i class="bi bi-dot"></i>${node.displayName}`;
            li.appendChild(btn);
            treeChildrenContainer.appendChild(li);
        });
    };

    /**
     * @param {"overwrite" | "append"} mode
     * @returns {boolean}
     */
    const previewTreeFromGrid = (mode) => {
        if (!treeChildrenContainer) {
            return false;
        }

        const rows = getRowsForValidation();
        const previewNodes = buildPreviewNodes(rows);

        if (previewNodes.length === 0) {
            if (mode === "overwrite") {
                treeChildrenContainer.innerHTML = "";
                const emptyLi = document.createElement("li");
                emptyLi.className = "text-muted small ms-4";
                emptyLi.textContent = "目录预览为空：表格第2列暂无有效单元号。";
                treeChildrenContainer.appendChild(emptyLi);
            }
            setInfo("目录预览完成：未找到可用单元号。", "error");
            return false;
        }

        if (mode === "append") {
            const conflicts = [];
            const seen = new Set();
            previewNodes.forEach((node) => {
                const unitNo = (node.unitNo || "").trim();
                const displayName = (node.displayName || "").trim();
                [unitNo, displayName].forEach((key) => {
                    if (!key) {
                        return;
                    }
                    const lower = key.toLowerCase();
                    if (existingUnitKeys.has(lower) && !seen.has(lower)) {
                        seen.add(lower);
                        conflicts.push(key);
                    }
                });
            });
            if (conflicts.length > 0) {
                restoreOriginalTree();
                const shown = conflicts.slice(0, 8).join("、");
                const more = conflicts.length > 8 ? ` 等 ${conflicts.length} 项` : "";
                setInfo(
                    `追加失败：Excel 中的单元号/名称与原方案重复（${shown}${more}）。请修改单元号后再追加，或改用覆盖。`,
                    "error"
                );
                return false;
            }

            treeChildrenContainer.innerHTML = cachedOriginalTreeHtml;
            renderPreviewNodes(previewNodes, { clearFirst: false });
            setInfo(`目录预览完成（追加）：保留原柜，新增 ${previewNodes.length} 个节点。`, "success");
        } else {
            renderPreviewNodes(previewNodes, { clearFirst: true });
            setInfo(`目录预览完成（覆盖）：已生成 ${previewNodes.length} 个目录子节点。`, "success");
        }

        return true;
    };

    const restoreOriginalTree = () => {
        if (!treeChildrenContainer) {
            return;
        }
        treeChildrenContainer.innerHTML = cachedOriginalTreeHtml;
    };

    /**
     * 非空报价单：弹出覆盖/追加/取消；空单直接覆盖预览。
     */
    const askPreviewModeAndRun = () => {
        const runMode = (mode) => {
            const success = previewTreeFromGrid(mode);
            state.importMode = success ? mode : null;
            state.hasPreviewSucceeded = success;
            applyButtonStates();
        };

        if (isEmptyQuotation && !hasExistingCabinetsOnLoad) {
            runMode("overwrite");
            return;
        }

        if (!previewModeModalEl || typeof bootstrap === "undefined" || !bootstrap.Modal) {
            // 无 Bootstrap 时回退为 confirm 选择
            const overwrite = window.confirm("当前报价单有数据。确定后覆盖原内容；取消则可在下一提示选择追加。");
            if (overwrite) {
                runMode("overwrite");
                return;
            }
            const append = window.confirm("是否将本次 Excel 追加到原报价单之后？");
            if (append) {
                runMode("append");
                return;
            }
            restoreOriginalTree();
            resetPreviewState();
            applyButtonStates();
            setInfo("已取消目录预览。", "info");
            return;
        }

        const modal = bootstrap.Modal.getOrCreateInstance(previewModeModalEl);
        let chosen = false;
        const onOverwrite = () => {
            chosen = true;
            cleanup();
            modal.hide();
            runMode("overwrite");
        };
        const onAppend = () => {
            chosen = true;
            cleanup();
            modal.hide();
            runMode("append");
        };
        const onHidden = () => {
            cleanup();
            if (!chosen) {
                restoreOriginalTree();
                resetPreviewState();
                applyButtonStates();
                setInfo("已取消目录预览。", "info");
            }
        };
        const cleanup = () => {
            previewModeOverwriteBtn?.removeEventListener("click", onOverwrite);
            previewModeAppendBtn?.removeEventListener("click", onAppend);
            previewModeModalEl.removeEventListener("hidden.bs.modal", onHidden);
        };

        resetPreviewState();
        applyButtonStates();
        previewModeOverwriteBtn?.addEventListener("click", onOverwrite);
        previewModeAppendBtn?.addEventListener("click", onAppend);
        previewModeModalEl.addEventListener("hidden.bs.modal", onHidden);
        modal.show();
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

    /**
     * 重填「序号」列：对有业务数据的行（第2–8列任一带值）按出现顺序写 1、2、3…
     * 纯空行或仅有旧序号的行清空序号，保证插入行后仍连续。
     * @returns {number} 重写后的非空数据行数
     */
    const renumberSerialColumn = () => {
        const data = hot.getData();
        const changes = [];
        let nextSerial = 1;

        for (let rowIndex = 0; rowIndex < data.length; rowIndex += 1) {
            const row = Array.isArray(data[rowIndex]) ? data[rowIndex] : [];
            let hasBusinessData = false;
            for (let col = 1; col < 8; col += 1) {
                const cell = (row[col] ?? "").toString().trim();
                if (cell) {
                    hasBusinessData = true;
                    break;
                }
            }

            const newValue = hasBusinessData ? String(nextSerial) : "";
            if (hasBusinessData) {
                nextSerial += 1;
            }

            const current = (row[0] ?? "").toString().trim();
            if (current !== newValue) {
                changes.push([rowIndex, 0, newValue]);
            }
        }

        if (changes.length > 0) {
            // 自定义 source，避免 afterChange 把本次「数据检查」状态机清掉
            hot.setDataAtCell(changes, "renumberSerial");
        }

        return nextSerial - 1;
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
            const trigger = event.target.closest("[data-unit-no], [data-unit-name]");
            if (!trigger) {
                return;
            }

            // 优先按原始单元号定位（与目录预览约定一致）
            const unitNo = (trigger.getAttribute("data-unit-no") || "").trim();
            const unitName = (trigger.getAttribute("data-unit-name") || "").trim();
            focusUnitBlockInGrid(unitNo || unitName);
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
        if (!changes || source === "loadData" || source === "renumberSerial") {
            return;
        }
        if (state.hasLoadedExcelData) {
            state.hasCheckPassed = false;
            resetPreviewState();
            applyButtonStates();
        }
    });

    hot.addHook("afterCreateRow", () => {
        if (state.hasLoadedExcelData) {
            state.hasCheckPassed = false;
            resetPreviewState();
            applyButtonStates();
        }
    });

    hot.addHook("afterRemoveRow", () => {
        if (state.hasLoadedExcelData) {
            state.hasCheckPassed = false;
            resetPreviewState();
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
            resetPreviewState();
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
        const serialCount = renumberSerialColumn();
        const errors = runValidation();
        if (errors.length === 0) {
            hot.render();
            state.hasCheckPassed = state.hasLoadedExcelData;
            resetPreviewState();
            applyButtonStates();
            setInfo(`数据检查通过。已重排序号 1–${serialCount}。`, "success");
            return;
        }

        hot.render();
        state.hasCheckPassed = false;
        resetPreviewState();
        applyButtonStates();
        setValidationErrors(errors);
    });

    document.addEventListener("keydown", (event) => {
        if (!(event.ctrlKey || event.metaKey) || event.key.toLowerCase() !== "g") {
            return;
        }
        const tag = (event.target && event.target.tagName) || "";
        if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") {
            return;
        }
        event.preventDefault();
        promptGoToRow();
    });

    previewTreeBtn.addEventListener("click", () => {
        askPreviewModeAndRun();
    });

    savePlanBtn.addEventListener("click", async () => {
        if (isSavingPlan) {
            return;
        }

        if (!state.importMode) {
            setInfo("请先执行目录预览并选择覆盖或追加。", "error");
            return;
        }

        const newUnitCount = getTreeNodeNamesForSave().length;
        if (state.importMode === "overwrite" && !isEmptyQuotation) {
            const ok = window.confirm(
                `将覆盖原报价单全部内容，仅保留本次 Excel 中的 ${newUnitCount} 个控制柜。是否继续？`
            );
            if (!ok) {
                return;
            }
        } else if (state.importMode === "append") {
            const ok = window.confirm(
                `将向原报价单追加 ${newUnitCount} 个控制柜（原有内容保留，新柜码续编）。是否继续？`
            );
            if (!ok) {
                return;
            }
        }

        if (!savePlanUrl) {
            setInfo("保存方案失败：缺少保存接口地址。", "error");
            return;
        }

        if (state.importMode !== "overwrite" && state.importMode !== "append") {
            setInfo("保存方案失败：未选择覆盖或追加模式。", "error");
            return;
        }

        const antiForgeryToken = getToken();
        setSavingUi(true);
        updateSaveProgress(0, newUnitCount, `正在保存方案…（0/${newUnitCount}）`);
        try {
            const response = await fetch(savePlanUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    ...(antiForgeryToken ? { RequestVerificationToken: antiForgeryToken } : {})
                },
                body: JSON.stringify({
                    fabh: quotationNo,
                    tableJson: getRowsForValidation(),
                    treeNodeNames: getTreeNodeNamesForSave(),
                    saveMode: state.importMode === "append" ? "append" : "overwrite"
                })
            });

            // 校验失败仍返回普通 JSON；成功路径为 NDJSON 进度流
            if (!response.ok) {
                const contentType = (response.headers.get("content-type") || "").toLowerCase();
                if (contentType.includes("json") && !contentType.includes("ndjson")) {
                    const fail = await response.json();
                    throw new Error(fail.message || "保存方案失败");
                }
                throw new Error(`保存方案失败（HTTP ${response.status}）`);
            }

            const result = await consumeSavePlanResponse(response);
            const unitCount = Number(result.unitCount);
            const componentCount = Number(result.componentCount);
            if (Number.isFinite(unitCount) && unitCount > 0) {
                state.canExportPlan = true;
            }
            // 保存成功后，当前树视为新的「原树」，便于同页再次追加
            if (treeChildrenContainer) {
                treeChildrenContainer.querySelectorAll("button.tree-node-link[data-from-preview='1']")
                    .forEach((btn) => {
                        btn.removeAttribute("data-from-preview");
                        btn.classList.remove("is-preview-new");
                        btn.classList.add("is-existing");
                    });
                cachedOriginalTreeHtml = treeChildrenContainer.innerHTML;
                existingUnitKeys = collectExistingUnitKeys(treeChildrenContainer);
            }
            resetPreviewState();
            applyButtonStates();

            if (Number.isFinite(unitCount) && Number.isFinite(componentCount)) {
                setInfo(result.message || `保存成功：共 ${unitCount} 个单元，${componentCount} 个元件。`, "success");
            } else {
                setInfo(result.message || "保存方案成功。", "success");
            }
        } catch (error) {
            const message = error instanceof Error ? error.message : "保存方案失败";
            setInfo(message, "error");
        } finally {
            setSavingUi(false);
        }
    });

    const parseDownloadFileName = (disposition, fallback) => {
        let fileName = fallback;
        const utf8Match = disposition.match(/filename\*=UTF-8''([^;\s]+)/i);
        if (utf8Match) {
            fileName = decodeURIComponent(utf8Match[1]);
        } else {
            const plainMatch = disposition.match(/filename="([^"]+)"/i)
                || disposition.match(/filename=([^;\s]+)/i);
            if (plainMatch) {
                fileName = plainMatch[1];
            }
        }
        return fileName;
    };

    saveExcelBtn.addEventListener("click", async () => {
        if (!saveUrl) {
            setInfo("另存表格失败：缺少保存接口地址。", "error");
            return;
        }

        const antiForgeryToken = getToken();
        try {
            setInfo("正在生成表格 Excel（仅当前页面表格）…", "info");
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
            const fileName = parseDownloadFileName(
                disposition,
                `报价元件表_${quotationNo}.xlsx`
            );
            const downloadUrl = window.URL.createObjectURL(blob);
            const a = document.createElement("a");
            a.href = downloadUrl;
            a.download = fileName;
            document.body.appendChild(a);
            a.click();
            a.remove();
            window.URL.revokeObjectURL(downloadUrl);
            setInfo("表格 Excel 已生成（仅当前页面表格），请在浏览器下载中选择保存位置。", "success");
        } catch (error) {
            const message = error instanceof Error ? error.message : "另存表格失败";
            setInfo(message, "error");
        }
    });

    if (exportPlanExcelBtn) {
        exportPlanExcelBtn.addEventListener("click", async () => {
            if (!exportPlanUrl) {
                setInfo("导出方案失败：缺少导出接口地址。", "error");
                return;
            }

            try {
                setInfo("正在从数据库导出方案 Excel…", "info");
                const response = await fetch(exportPlanUrl, { method: "GET" });
                if (!response.ok) {
                    const contentType = (response.headers.get("content-type") || "").toLowerCase();
                    if (contentType.includes("json")) {
                        const fail = await response.json();
                        throw new Error(fail.message || "导出方案失败");
                    }
                    throw new Error(`导出方案失败（HTTP ${response.status}）`);
                }

                const blob = await response.blob();
                const disposition = response.headers.get("Content-Disposition") || "";
                const fileName = parseDownloadFileName(
                    disposition,
                    `方案元件表_${quotationNo}.xlsx`
                );
                const downloadUrl = window.URL.createObjectURL(blob);
                const a = document.createElement("a");
                a.href = downloadUrl;
                a.download = fileName;
                document.body.appendChild(a);
                a.click();
                a.remove();
                window.URL.revokeObjectURL(downloadUrl);
                setInfo("方案 Excel 已生成（库内整单），请在浏览器下载中选择保存位置。", "success");
            } catch (error) {
                const message = error instanceof Error ? error.message : "导出方案失败";
                setInfo(message, "error");
            }
        });
    }

    applyButtonStates();
})();
