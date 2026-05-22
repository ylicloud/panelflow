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

    const BASE_HEADERS_8 = ["序号", "名称", "规格", "单位", "单价", "数量", "报价浮动", "厂家"];
    const BASE_HEADERS_9 = ["序号", "名称", "规格", "单位", "单价", "参考价格", "数量", "报价浮动", "厂家"];

    let summaryMode = false;
    let projectSummaryReadOnly = false;
    let cabinetViewActive = false;
    /** 柜体视图且在单价后插入参考价列 */
    let refPriceColumnVisible = false;
    let currentCabinetUnitCode = "";
    let summaryDirty = false;
    let summaryOriginalRows = [];
    /** 当前柜体视图中每行对应的 x_wzdh（用于历史价格匹配） */
    let currentRowWzdh = [];

    const isRefColumnMode = () => !summaryMode && cabinetViewActive && refPriceColumnVisible;

    const colFloat = () => (isRefColumnMode() ? 7 : 6);
    const colVendor = () => (isRefColumnMode() ? 8 : 7);

    const editableColsForMode = () => {
        if (globalReadOnly) {
            return new Set();
        }
        if (projectSummaryReadOnly) {
            return new Set();
        }
        if (summaryMode) {
            return new Set([3, 4, 6, 7]);
        }
        if (!cabinetViewActive) {
            return new Set();
        }
        return isRefColumnMode()
            ? new Set([3, 4, 7, 8])
            : new Set([3, 4, 6, 7]);
    };

    const setMessage = (message, isError) => {
        if (!infoBarEl) {
            return;
        }
        infoBarEl.textContent = message || "";
        infoBarEl.classList.remove("alert-info", "alert-danger", "alert-success");
        infoBarEl.classList.add(isError ? "alert-danger" : "alert-success");
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
        if (isRefColumnMode() && col === 5) {
            td.style.backgroundColor = "#d1e7dd";
            td.style.color = "#0a3622";
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
        const editable = editableColsForMode();
        const readOnly = projectSummaryReadOnly || !editable.has(col);
        return {
            renderer: errorCellRenderer,
            readOnly
        };
    };

    const applyHotColumnLayout = () => {
        const nine = isRefColumnMode();
        const n = nine ? 9 : 8;
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
        columns: Array.from({ length: 8 }, () => ({ type: "text", renderer: errorCellRenderer })),
        cells: (row, col) => getCellsMeta(row, col),
        stretchH: "all",
        width: "100%",
        height: "100%",
        minSpareRows: 0,
        licenseKey
    });

    hot.addHook("afterGetColHeader", (col, TH) => {
        TH.classList.remove("ht-ref-price-header");
        if (isRefColumnMode() && col === 5) {
            TH.classList.add("ht-ref-price-header");
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
        projectSummaryReadOnly = false;
        cabinetViewActive = false;
        refPriceColumnVisible = false;
        currentCabinetUnitCode = "";
        summaryDirty = false;
        summaryOriginalRows = [];
        hideUsagePanel();
        applyHotColumnLayout();
        hot.render();
        applyButtonStates();
    };

    const parseDecimalOrZero = (value) => {
        const num = Number((value ?? "").toString().trim());
        return Number.isFinite(num) ? num : 0;
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
            (x.x_fdds ?? "").toString(),
            (x.x_sccj ?? "").toString()
        ]));
    };

    const mergeRowsWithRefBj = (rows8, refRows) =>
        rows8.map((row, i) => {
            const refObj = refRows[i];
            const refVal = refObj !== undefined && refObj !== null ? (refObj.refBj ?? refObj.RefBj) : null;
            const refStr = refVal !== null && refVal !== undefined && refVal !== "" ? String(refVal) : "";
            const next = row.slice(0, 5);
            next.push(refStr);
            next.push(row[5], row[6], row[7]);
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

    const loadCabinetGrid = async (unitCode, wantRefColumn) => {
        const target = (unitCode || "").trim();
        const connector = componentsUrl.includes("?") ? "&" : "?";
        const response = await fetch(`${componentsUrl}${connector}unitCode=${encodeURIComponent(target)}`, { method: "GET" });
        const result = await response.json();
        if (!response.ok || !result.success) {
            throw new Error(result.message || "读取柜内元件失败");
        }
        const rows = Array.isArray(result.rows) ? result.rows : [];
        const mapped8 = mapComponentsToRows8(rows);

        if (!wantRefColumn || !cabinetRefBjUrl) {
            refPriceColumnVisible = false;
            applyHotColumnLayout();
            hot.loadData(mapped8);
            return;
        }

        const refRows = await fetchReferenceBjRows(target);
        refPriceColumnVisible = true;
        applyHotColumnLayout();
        hot.loadData(mergeRowsWithRefBj(mapped8, refRows));
    };

    const identityFromHotRow = (rowIndex) => {
        const data = hot.getData();
        const row = Array.isArray(data[rowIndex]) ? data[rowIndex] : [];
        const cf = colFloat();
        const cv = colVendor();
        return {
            name: (row[1] ?? "").toString().trim(),
            spec: (row[2] ?? "").toString().trim(),
            unit: (row[3] ?? "").toString().trim(),
            price: parseDecimalOrZero(row[4]),
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
            const wantRef = !!(showRefPriceCb && showRefPriceCb.checked);
            await loadCabinetGrid(target, wantRef);
            hot.render();
            applyButtonStates();
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
            applyHotColumnLayout();
            hideUsagePanel();
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
                matchKey: (x.matchKey ?? "").toString(),
                name: (x.x_mc ?? "").toString().trim(),
                spec: (x.x_ggxh ?? "").toString().trim(),
                unit: (x.x_dw ?? "").toString().trim(),
                price: Number(x.x_dj ?? 0),
                floatRate: Number(x.x_fdds ?? 0),
                vendor: (x.x_sccj ?? "").toString().trim()
            }));
            hot.loadData(mapped);
            projectSummaryReadOnly = true;
            enterSummaryMode();
            setMessage(`已加载项目元件汇总，共 ${mapped.length} 条。`, false);
        } catch (error) {
            const message = error instanceof Error ? error.message : "读取项目元件汇总失败";
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
        if (summaryMode) {
            loadProjectComponentUsage(r);
        } else if (cabinetViewActive) {
            loadCabinetComponentUsage(r);
        }
    });

    hot.addHook("afterChange", (changes, source) => {
        if (!changes || source === "loadData" || !summaryMode) {
            return;
        }
        summaryDirty = true;
        applyButtonStates();
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

    if (saveSummaryBtn) {
        saveSummaryBtn.addEventListener("click", async () => {
            if (!summaryMode || !saveProjectSummaryUrl) {
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

    // 引用历史报价按钮
    const autoFillPriceBtn = document.getElementById("auto-fill-price-btn");
    if (autoFillPriceBtn) {
        autoFillPriceBtn.addEventListener("click", async () => {
            const url = autoFillPriceBtn.dataset.autoFillUrl;
            const fabh = autoFillPriceBtn.dataset.quotationNo;
            if (!url || !fabh) return;

            if (!cabinetViewActive) {
                setMessage("请先点击左侧控制柜节点加载元件数据", true);
                return;
            }

            autoFillPriceBtn.disabled = true;
            setMessage("正在匹配历史报价...", false);

            try {
                const token = getToken();
                const response = await fetch(url, {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json",
                        ...(token ? { RequestVerificationToken: token } : {})
                    },
                    body: JSON.stringify({ fabh })
                });

                const result = await readJsonResponse(response, "自动填价失败");
                if (!result.success) {
                    setMessage(result.message || "自动填价失败", true);
                    return;
                }

                const prices = result.prices || {};
                const priceKeys = Object.keys(prices);
                const data = hot.getData();
                const changes = [];
                let filledCount = 0;
                const colPrice = 4;
                const colUnit = 3;
                const cv = colVendor();

                for (let r = 0; r < data.length; r++) {
                    const wzdh = currentRowWzdh[r];
                    if (!wzdh) continue;
                    const hist = prices[wzdh];
                    if (!hist) continue;

                    const currentPrice = parseDecimalOrZero(data[r][colPrice]);
                    const currentUnit = (data[r][colUnit] || "").toString().trim();
                    const currentVendorVal = (data[r][cv] || "").toString().trim();

                    if (currentPrice === 0 && hist.price > 0) {
                        changes.push([r, colPrice, hist.price.toString()]);
                        filledCount++;
                    }
                    if (!currentUnit && hist.unit) {
                        changes.push([r, colUnit, hist.unit]);
                    }
                    if (!currentVendorVal && hist.vendor) {
                        changes.push([r, cv, hist.vendor]);
                    }
                }

                if (changes.length > 0) {
                    hot.setDataAtCell(changes);
                }

                const total = currentRowWzdh.filter(w => w).length;
                const matchedInPrices = currentRowWzdh.filter(w => w && prices[w]).length;
                let msg = `历史价格库匹配 ${priceKeys.length} 条，当前柜体 ${total} 个元件中 ${matchedInPrices} 个有历史价`;
                if (filledCount > 0) {
                    msg += `，已填充 ${filledCount} 个单价为0的元件`;
                } else if (matchedInPrices > 0) {
                    msg += `，但所有匹配元件的单价已非0，未做修改`;
                }
                setMessage(msg, filledCount === 0 && matchedInPrices === 0);
            } catch (error) {
                const message = error instanceof Error ? error.message : "自动填价请求失败，请检查网络";
                setMessage(message, true);
            } finally {
                autoFillPriceBtn.disabled = false;
            }
        });
    }

    applyButtonStates();
})();
