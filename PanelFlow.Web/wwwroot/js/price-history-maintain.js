(() => {
    "use strict";

    const cfg = document.getElementById("price-history-config");
    if (!cfg) return;

    const listUrl = cfg.dataset.listUrl;
    const sourceRowsUrl = cfg.dataset.sourceRowsUrl;
    const excludeUrl = cfg.dataset.excludeUrl;
    const removeExclusionUrl = cfg.dataset.removeExclusionUrl;
    const exclusionsUrl = cfg.dataset.exclusionsUrl;
    const refreshUrl = cfg.dataset.refreshUrl;
    const updateUrl = cfg.dataset.updateUrl;
    const batchUpdateUrl = cfg.dataset.batchUpdateUrl;

    const infoBar = document.getElementById("page-info-bar");
    const exclusionsTbody = document.getElementById("exclusions-tbody");
    const historyPanel = document.getElementById("history-panel");
    const exclusionsPanel = document.getElementById("exclusions-panel");
    const historyToolbar = document.getElementById("history-toolbar");
    const batchToolbar = document.getElementById("batch-toolbar");
    const searchInput = document.getElementById("search-input");
    const onlySuspect = document.getElementById("only-suspect");
    const refreshSpBtn = document.getElementById("refresh-sp-btn");
    const prevPageBtn = document.getElementById("prev-page-btn");
    const nextPageBtn = document.getElementById("next-page-btn");
    const pagerInfo = document.getElementById("history-pager-info");
    const mainTabs = document.getElementById("main-tabs");
    const hotContainer = document.getElementById("history-hot-container");
    const batchDwInput = document.getElementById("batch-dw-input");
    const batchSccjInput = document.getElementById("batch-sccj-input");
    const batchApplyBtn = document.getElementById("batch-apply-btn");

    const sourceModalEl = document.getElementById("source-modal");
    const sourceModal = sourceModalEl ? new bootstrap.Modal(sourceModalEl) : null;
    const sourceModalTitle = document.getElementById("source-modal-title");
    const sourceTbody = document.getElementById("source-tbody");
    const excludeReason = document.getElementById("exclude-reason");
    const excludeWholeBtn = document.getElementById("exclude-whole-btn");
    const excludeSelectedBtn = document.getElementById("exclude-selected-btn");
    const sourceSelectAll = document.getElementById("source-select-all");
    const sourceChartWrap = document.getElementById("source-chart-wrap");
    const sourceChart = document.getElementById("source-chart");
    const sourceChartLegend = document.getElementById("source-chart-legend");

    let currentPage = 1;
    const pageSize = 30;
    let sortBy = "ggxh";
    let sortAsc = true;
    let currentWzdh = "";
    let sourceRows = [];
    let searchTimer = null;
    let historyRows = [];
    let hot = null;
    let totalCount = 0;
    const deviationWarnPct = 20;

    const pendingUpdates = new Map();
    let saveTimer = null;
    let isSaving = false;

    const COL_VIEW = 0;
    const COL_X_DW = 3;
    const COL_DEVIATION = 6;
    const COL_X_SCCJ = 11;

    const columnMeta = [
        { sortKey: null },
        { sortKey: "x_mc" },
        { sortKey: "ggxh" },
        { sortKey: "x_dw" },
        { sortKey: "last_price" },
        { sortKey: "avg_price" },
        { sortKey: "deviation" },
        { sortKey: "min_price" },
        { sortKey: "max_price" },
        { sortKey: "avg_count" },
        { sortKey: "last_fabh" },
        { sortKey: "x_sccj" }
    ];

    const token = () => {
        const el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : "";
    };

    const setInfo = (msg, isError) => {
        if (!infoBar) return;
        infoBar.textContent = msg || "";
        infoBar.classList.toggle("is-error", !!isError);
    };

    const fmtMoney = (v) => {
        if (v === null || v === undefined || v === "") return "—";
        const n = Number(v);
        if (Number.isNaN(n)) return "—";
        return "¥" + n.toFixed(2);
    };

    const fmtDate = (v) => {
        if (!v) return "—";
        const d = new Date(v);
        if (Number.isNaN(d.getTime())) return "—";
        return d.toLocaleString("zh-CN", { hour12: false });
    };

    const fmtShortDate = (v) => {
        if (!v) return "";
        const d = new Date(v);
        if (Number.isNaN(d.getTime())) return "";
        return d.toLocaleDateString("zh-CN", { year: "2-digit", month: "2-digit", day: "2-digit" });
    };

    const escapeHtml = (s) => {
        if (s === null || s === undefined) return "";
        return String(s)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;");
    };

    const ellipsisRenderer = (instance, td, row, col, prop, value, cellProperties) => {
        Handsontable.renderers.TextRenderer(instance, td, row, col, prop, value, cellProperties);
        td.style.overflow = "hidden";
        td.style.textOverflow = "ellipsis";
        td.style.whiteSpace = "nowrap";
        const text = value === null || value === undefined ? "" : String(value);
        if (text) {
            td.title = text;
        }
    };

    const moneyRenderer = (instance, td, row, col, prop, value, cellProperties) => {
        Handsontable.renderers.TextRenderer(instance, td, row, col, prop, fmtMoney(value), cellProperties);
        td.style.textAlign = "right";
    };

    const deviationRenderer = (instance, td, row, col, prop, value, cellProperties) => {
        const rowData = historyRows[row];
        const pct = rowData?.deviationPercent;
        const isSuspect = rowData?.isSuspect;
        let html = "—";
        if (pct !== null && pct !== undefined && !Number.isNaN(Number(pct))) {
            const n = Number(pct);
            const sign = n > 0 ? "+" : "";
            const text = sign + n.toFixed(1) + "%";
            if (isSuspect || Math.abs(n) > deviationWarnPct) {
                html = `<span class="oa-ph-deviation-warn">${text}</span>`;
            } else {
                html = text;
            }
        }
        Handsontable.renderers.HtmlRenderer(instance, td, row, col, prop, html, cellProperties);
        td.style.textAlign = "right";
        if (rowData?.suspectReason) {
            td.title = rowData.suspectReason;
        }
    };

    const viewBtnRenderer = (instance, td, row, col, prop, value, cellProperties) => {
        Handsontable.renderers.HtmlRenderer(
            instance, td, row, col, prop,
            '<button type="button" class="btn btn-sm btn-outline-secondary oa-ph-view-btn py-0 px-1">查看</button>',
            cellProperties
        );
        td.style.textAlign = "center";
        td.style.verticalAlign = "middle";
    };

    const postForm = async (url, data) => {
        const body = new URLSearchParams();
        body.append("__RequestVerificationToken", token());
        Object.keys(data).forEach((k) => {
            const v = data[k];
            if (v !== undefined && v !== null) {
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

    const postJson = async (url, payload) => {
        const headers = { "Content-Type": "application/json" };
        const t = token();
        if (t) {
            headers.RequestVerificationToken = t;
        }
        const resp = await fetch(url, {
            method: "POST",
            headers,
            body: JSON.stringify(payload)
        });
        return resp.json();
    };

    const toHotRow = (row) => ({
        x_mc: row.x_mc || "",
        ggxh: row.ggxh || "",
        x_dw: row.x_dw || "",
        last_price: row.last_price,
        avg_price: row.avg_price,
        deviationPercent: row.deviationPercent,
        min_price: row.min_price,
        max_price: row.max_price,
        avg_count: row.avg_count ?? 0,
        last_fabh: row.last_fabh || "",
        x_sccj: row.x_sccj || ""
    });

    const flushPendingSaves = async () => {
        clearTimeout(saveTimer);
        saveTimer = null;
        if (pendingUpdates.size === 0) {
            return true;
        }
        if (isSaving) {
            return new Promise((resolve) => {
                const check = () => {
                    if (!isSaving) {
                        resolve(flushPendingSaves());
                    } else {
                        setTimeout(check, 100);
                    }
                };
                check();
            });
        }

        const items = Array.from(pendingUpdates.values());
        pendingUpdates.clear();
        isSaving = true;

        try {
            const data = await postJson(updateUrl, items);
            if (!data.success) {
                setInfo(data.message || "保存失败", true);
                return false;
            }
            return true;
        } catch (err) {
            setInfo("保存失败：" + err.message, true);
            return false;
        } finally {
            isSaving = false;
        }
    };

    const queueSave = (id, x_dw, x_sccj) => {
        pendingUpdates.set(id, { id, x_dw, x_sccj });
        clearTimeout(saveTimer);
        saveTimer = setTimeout(() => {
            flushPendingSaves();
        }, 500);
    };

    const initHot = () => {
        if (!hotContainer || typeof Handsontable === "undefined") return;

        const licenseKey = hotContainer.dataset.licenseKey || "";

        const hotHeight = () => Math.max(360, hotContainer.clientHeight || 400);

        hot = new Handsontable(hotContainer, {
            licenseKey,
            data: [],
            rowHeaders: false,
            height: hotHeight(),
            colHeaders: [
                "查看", "元件名称", "规格型号", "单位", "最新价", "均价", "偏离",
                "最低", "最高", "样本", "最新来源", "厂商"
            ],
            columns: [
                { data: "_view", readOnly: true, width: 56, renderer: viewBtnRenderer },
                { data: "x_mc", readOnly: true, width: 120, renderer: ellipsisRenderer },
                { data: "ggxh", readOnly: true, width: 160, renderer: ellipsisRenderer },
                { data: "x_dw", readOnly: false, width: 55 },
                { data: "last_price", readOnly: true, width: 80, renderer: moneyRenderer },
                { data: "avg_price", readOnly: true, width: 80, renderer: moneyRenderer },
                { data: "deviationPercent", readOnly: true, width: 70, renderer: deviationRenderer },
                { data: "min_price", readOnly: true, width: 75, renderer: moneyRenderer },
                { data: "max_price", readOnly: true, width: 75, renderer: moneyRenderer },
                { data: "avg_count", readOnly: true, width: 55, className: "htRight" },
                { data: "last_fabh", readOnly: true, width: 100, renderer: ellipsisRenderer },
                { data: "x_sccj", readOnly: false, width: 110, renderer: ellipsisRenderer }
            ],
            stretchH: "all",
            manualColumnResize: true,
            columnSorting: false,
            renderAllRows: false,
            copyPaste: true,
            contextMenu: {
                items: {
                    copy: { name: "复制" }
                }
            },
            cells(row) {
                const cp = {};
                if (historyRows[row]?.isSuspect) {
                    cp.className = "oa-ph-row-suspect";
                }
                return cp;
            },
            afterGetColHeader(col, TH) {
                const meta = columnMeta[col];
                TH.classList.remove("oa-ph-sortable", "oa-ph-sort-active");
                delete TH.dataset.sortDir;
                if (!meta?.sortKey) return;
                TH.classList.add("oa-ph-sortable");
                if (meta.sortKey === sortBy) {
                    TH.classList.add("oa-ph-sort-active");
                    TH.dataset.sortDir = sortAsc ? "asc" : "desc";
                }
            },
            afterChange(changes, source) {
                if (!changes || source === "loadData") return;
                changes.forEach(([row, prop, , newValue]) => {
                    if (prop !== "x_dw" && prop !== "x_sccj") return;
                    const src = historyRows[row];
                    if (!src) return;
                    const val = newValue === null || newValue === undefined ? "" : String(newValue).trim();
                    if (prop === "x_dw") {
                        src.x_dw = val;
                    } else {
                        src.x_sccj = val;
                    }
                    queueSave(src.id, src.x_dw || null, src.x_sccj || null);
                });
            }
        });

        hot.addHook("afterOnCellMouseDown", (event, coords) => {
            if (coords.row === -1) {
                const meta = columnMeta[coords.col];
                if (!meta?.sortKey) return;
                event.stopImmediatePropagation();
                if (sortBy === meta.sortKey) {
                    sortAsc = !sortAsc;
                } else {
                    sortBy = meta.sortKey;
                    sortAsc = true;
                }
                currentPage = 1;
                loadHistory();
                return;
            }

            if (coords.col === COL_VIEW) {
                const src = historyRows[coords.row];
                if (src?.x_wzdh) {
                    event.stopImmediatePropagation();
                    openSourceModal(src.x_wzdh);
                }
            }
        });
    };

    const renderHot = (items) => {
        historyRows = items || [];
        const data = historyRows.map(toHotRow);
        if (hot) {
            hot.loadData(data);
            hot.render();
        }
    };

    const loadHistory = async () => {
        const saved = await flushPendingSaves();
        if (!saved) return;

        if (hot) {
            hot.loadData([]);
        }

        const params = new URLSearchParams({
            page: String(currentPage),
            pageSize: String(pageSize),
            onlySuspect: onlySuspect?.checked ? "true" : "false",
            sortBy,
            sortAsc: sortAsc ? "true" : "false"
        });
        const kw = searchInput?.value?.trim();
        if (kw) params.set("keyword", kw);

        try {
            const resp = await fetch(`${listUrl}?${params.toString()}`);
            const data = await resp.json();
            if (!data.success) {
                setInfo(data.message || "加载失败", true);
                renderHot([]);
                return;
            }

            const result = data.result;
            totalCount = result.totalCount || 0;
            renderHot(result.items || []);
            updatePager(result);
            setInfo(`共 ${totalCount} 条记录`);
        } catch (err) {
            setInfo("加载失败：" + err.message, true);
            renderHot([]);
        }
    };

    const updatePager = (result) => {
        const total = result.totalCount || 0;
        const totalPages = Math.max(1, Math.ceil(total / pageSize));
        if (pagerInfo) {
            pagerInfo.textContent = `第 ${result.page} / ${totalPages} 页，共 ${total} 条`;
        }
        if (prevPageBtn) prevPageBtn.disabled = result.page <= 1;
        if (nextPageBtn) nextPageBtn.disabled = result.page >= totalPages;
    };

    const loadExclusions = async () => {
        if (!exclusionsTbody) return;
        exclusionsTbody.innerHTML = '<tr><td colspan="7" class="text-center text-muted py-3">加载中…</td></tr>';

        try {
            const resp = await fetch(exclusionsUrl);
            const data = await resp.json();
            if (!data.success) {
                exclusionsTbody.innerHTML = '<tr><td colspan="7" class="text-center text-danger py-3">加载失败</td></tr>';
                return;
            }
            renderExclusions(data.items || []);
        } catch (err) {
            exclusionsTbody.innerHTML = `<tr><td colspan="7" class="text-center text-danger py-3">${escapeHtml(err.message)}</td></tr>`;
        }
    };

    const renderExclusions = (items) => {
        if (!exclusionsTbody) return;
        if (!items.length) {
            exclusionsTbody.innerHTML = '<tr><td colspan="7" class="text-center text-muted py-3">暂无剔除记录</td></tr>';
            return;
        }

        exclusionsTbody.innerHTML = items.map((row) => {
            const scope = row.isWholeQuotation ? "整单" : "按型号";
            return `<tr>
                <td>${escapeHtml(row.fabh)}</td>
                <td>${scope}</td>
                <td class="small">${escapeHtml(row.x_wzdh || "—")}</td>
                <td class="small">${escapeHtml(row.reason || "—")}</td>
                <td>${escapeHtml(row.created_by || "—")}</td>
                <td class="small">${fmtDate(row.created_at)}</td>
                <td>
                    <button type="button" class="btn btn-sm btn-outline-secondary oa-ph-restore-btn" data-id="${row.id}">恢复</button>
                </td>
            </tr>`;
        }).join("");
    };

    const openSourceModal = async (wzdh) => {
        currentWzdh = wzdh;
        if (sourceModalTitle) {
            sourceModalTitle.textContent = "价格来源 — " + wzdh;
        }
        if (sourceTbody) {
            sourceTbody.innerHTML = '<tr><td colspan="9" class="text-center text-muted py-3">加载中…</td></tr>';
        }
        if (sourceChart) sourceChart.innerHTML = "";
        sourceChartWrap?.classList.add("d-none");
        if (excludeReason) excludeReason.value = "";
        if (sourceSelectAll) sourceSelectAll.checked = false;
        sourceModal?.show();

        try {
            const resp = await fetch(`${sourceRowsUrl}?xWzdh=${encodeURIComponent(wzdh)}`);
            const text = await resp.text();
            let data;
            try {
                data = JSON.parse(text);
            } catch {
                throw new Error(resp.ok ? "服务器返回格式异常" : (text.slice(0, 120) || `HTTP ${resp.status}`));
            }
            if (!data.success) {
                const msg = data.message || "加载失败";
                setInfo(msg, true);
                if (sourceTbody) {
                    sourceTbody.innerHTML = `<tr><td colspan="9" class="text-center text-danger py-3">${escapeHtml(msg)}</td></tr>`;
                }
                return;
            }
            sourceRows = data.items || [];
            renderSourceRows();
            renderPriceTrendChart();
        } catch (err) {
            setInfo("加载来源失败：" + err.message, true);
            if (sourceTbody) {
                sourceTbody.innerHTML = `<tr><td colspan="9" class="text-center text-danger py-3">${escapeHtml(err.message)}</td></tr>`;
            }
        }
    };

    const renderSourceRows = () => {
        if (!sourceTbody) return;
        if (!sourceRows.length) {
            sourceTbody.innerHTML = '<tr><td colspan="9" class="text-center text-muted py-3">无来源记录</td></tr>';
            return;
        }

        sourceTbody.innerHTML = sourceRows.map((row, idx) => {
            const classes = [];
            if (row.isExcluded) classes.push("oa-ph-source-excluded");
            if (row.isSuspect) classes.push("oa-ph-source-suspect");
            const status = row.isExcluded
                ? '<span class="badge text-bg-secondary">已剔除</span>'
                : (row.isSuspect
                    ? `<span class="badge text-bg-warning" title="${escapeHtml(row.suspectReason || "")}">疑似异常</span>`
                    : '<span class="text-muted">正常</span>');
            const disabled = row.isExcluded ? "disabled" : "";
            return `<tr class="${classes.join(" ")}">
                <td><input type="checkbox" class="oa-ph-source-cb" data-idx="${idx}" ${disabled} /></td>
                <td>${escapeHtml(row.fabh)}</td>
                <td class="small">${escapeHtml(row.famc || "—")}</td>
                <td>${escapeHtml(row.x_mc || "—")}</td>
                <td class="small">${escapeHtml(row.x_ggxh || "—")}</td>
                <td class="text-end">${fmtMoney(row.x_bj_dj)}</td>
                <td class="text-end">${row.x_sl ?? "—"}</td>
                <td class="small">${fmtDate(row.x_bjb_datetime)}</td>
                <td>${status}</td>
            </tr>`;
        }).join("");
    };

    const renderPriceTrendChart = () => {
        if (!sourceChartWrap || !sourceChart) return;

        const points = sourceRows
            .filter((r) => r.x_bjb_datetime && r.x_bj_dj != null && Number(r.x_bj_dj) > 0)
            .map((r) => ({
                date: new Date(r.x_bjb_datetime),
                price: Number(r.x_bj_dj),
                fabh: r.fabh || ""
            }))
            .filter((p) => !Number.isNaN(p.date.getTime()))
            .sort((a, b) => a.date - b.date);

        if (points.length < 2) {
            sourceChartWrap.classList.add("d-none");
            sourceChart.innerHTML = "";
            if (sourceChartLegend) sourceChartLegend.textContent = "";
            return;
        }

        sourceChartWrap.classList.remove("d-none");

        const w = 800;
        const h = 200;
        const padL = 52;
        const padR = 16;
        const padT = 16;
        const padB = 36;
        const chartW = w - padL - padR;
        const chartH = h - padT - padB;

        const prices = points.map((p) => p.price);
        const minP = Math.min(...prices);
        const maxP = Math.max(...prices);
        const margin = (maxP - minP) * 0.1 || maxP * 0.1 || 1;
        const yMin = Math.max(0, minP - margin);
        const yMax = maxP + margin;
        const yRange = yMax - yMin || 1;

        const minT = points[0].date.getTime();
        const maxT = points[points.length - 1].date.getTime();
        const tRange = maxT - minT || 1;

        const toX = (t) => padL + ((t - minT) / tRange) * chartW;
        const toY = (price) => padT + chartH - ((price - yMin) / yRange) * chartH;

        const polyline = points.map((p) => `${toX(p.date.getTime()).toFixed(1)},${toY(p.price).toFixed(1)}`).join(" ");
        const circles = points.map((p) => {
            const cx = toX(p.date.getTime()).toFixed(1);
            const cy = toY(p.price).toFixed(1);
            const title = `${fmtShortDate(p.date)} ¥${p.price.toFixed(2)} ${escapeHtml(p.fabh)}`;
            return `<circle cx="${cx}" cy="${cy}" r="4" fill="#0d6efd" stroke="#fff" stroke-width="1"><title>${title}</title></circle>`;
        }).join("");

        const avg = prices.reduce((s, v) => s + v, 0) / prices.length;
        const avgY = toY(avg).toFixed(1);

        const yTicks = [yMin, (yMin + yMax) / 2, yMax];
        const yLabels = yTicks.map((v) => {
            const y = toY(v).toFixed(1);
            return `<text x="${padL - 6}" y="${y}" text-anchor="end" dominant-baseline="middle" font-size="10" fill="#6c757d">¥${v.toFixed(0)}</text>`;
        }).join("");

        const xLabelIndices = [0, Math.floor((points.length - 1) / 2), points.length - 1];
        const xLabels = [...new Set(xLabelIndices)].map((i) => {
            const p = points[i];
            const x = toX(p.date.getTime()).toFixed(1);
            return `<text x="${x}" y="${h - 8}" text-anchor="middle" font-size="10" fill="#6c757d">${fmtShortDate(p.date)}</text>`;
        }).join("");

        sourceChart.innerHTML = `<svg viewBox="0 0 ${w} ${h}" preserveAspectRatio="none" role="img" aria-label="价格趋势折线图">
            <line x1="${padL}" y1="${padT}" x2="${padL}" y2="${padT + chartH}" stroke="#dee2e6"/>
            <line x1="${padL}" y1="${padT + chartH}" x2="${padL + chartW}" y2="${padT + chartH}" stroke="#dee2e6"/>
            <line x1="${padL}" y1="${avgY}" x2="${padL + chartW}" y2="${avgY}" stroke="#fd7e14" stroke-dasharray="4 3" opacity="0.8"/>
            <text x="${padL + 4}" y="${Number(avgY) - 4}" font-size="9" fill="#fd7e14">均价</text>
            ${yLabels}
            ${xLabels}
            <polyline points="${polyline}" fill="none" stroke="#0d6efd" stroke-width="2" stroke-linejoin="round"/>
            ${circles}
        </svg>`;

        if (sourceChartLegend) {
            sourceChartLegend.textContent = `样本 ${points.length} 条 · 均价 ¥${avg.toFixed(2)}`;
        }
    };

    const getSelectedSourceRows = () => {
        const checks = document.querySelectorAll(".oa-ph-source-cb:checked");
        return Array.from(checks).map((cb) => sourceRows[Number(cb.dataset.idx)]).filter(Boolean);
    };

    const doExclude = async (fabh, wzdh) => {
        const reason = excludeReason?.value?.trim();
        if (!reason) {
            setInfo("请填写剔除理由", true);
            return;
        }

        const data = await postForm(excludeUrl, {
            fabh,
            xWzdh: wzdh || "",
            reason
        });

        if (!data.success) {
            setInfo(data.message || "剔除失败", true);
            return;
        }

        setInfo(data.message || "剔除成功");
        await openSourceModal(currentWzdh);
        await loadHistory();
    };

    const doRefresh = async () => {
        if (!refreshSpBtn) return;
        if (!confirm("将执行 SP_RefreshPriceHistory 重新聚合历史价格，可能需要数分钟。是否继续？")) {
            return;
        }

        const saved = await flushPendingSaves();
        if (!saved) return;

        refreshSpBtn.disabled = true;
        setInfo("正在重新生成历史价格，请稍候…");

        try {
            const data = await postForm(refreshUrl, {});
            setInfo(data.message || (data.success ? "完成" : "失败"), !data.success);
            if (data.success) {
                currentPage = 1;
                await loadHistory();
            }
        } catch (err) {
            setInfo("重新生成失败：" + err.message, true);
        } finally {
            refreshSpBtn.disabled = false;
        }
    };

    const doBatchApply = async () => {
        const dw = batchDwInput?.value?.trim() ?? "";
        const sccj = batchSccjInput?.value?.trim() ?? "";
        if (!dw && !sccj) {
            setInfo("请至少填写单位或厂商", true);
            return;
        }

        const saved = await flushPendingSaves();
        if (!saved) return;

        const kw = searchInput?.value?.trim() || "";
        const suspectOnly = onlySuspect?.checked ?? false;
        const filterDesc = [
            kw ? `关键词「${kw}」` : "无关键词（全部记录）",
            suspectOnly ? "仅疑似异常" : ""
        ].filter(Boolean).join("，");

        const countParams = new URLSearchParams({
            page: "1",
            pageSize: "1",
            onlySuspect: suspectOnly ? "true" : "false",
            sortBy,
            sortAsc: sortAsc ? "true" : "false"
        });
        if (kw) countParams.set("keyword", kw);

        let matchCount = totalCount;
        try {
            const resp = await fetch(`${listUrl}?${countParams.toString()}`);
            const data = await resp.json();
            if (data.success) {
                matchCount = data.result?.totalCount ?? 0;
            }
        } catch {
            // use cached totalCount
        }

        if (matchCount === 0) {
            setInfo("当前筛选条件下没有匹配记录", true);
            return;
        }

        const warnAll = !kw && !suspectOnly;
        const msg = warnAll
            ? `将对全部 ${matchCount} 条历史价格记录批量设置单位/厂商，是否继续？`
            : `将对当前筛选的 ${matchCount} 条记录（${filterDesc}）批量设置，是否继续？`;
        if (!confirm(msg)) return;

        if (batchApplyBtn) batchApplyBtn.disabled = true;

        try {
            const payload = {
                keyword: kw || null,
                onlySuspect: suspectOnly,
                x_dw: dw || null,
                x_sccj: sccj || null
            };
            const data = await postJson(batchUpdateUrl, payload);
            setInfo(data.message || (data.success ? "完成" : "失败"), !data.success);
            if (data.success) {
                await loadHistory();
            }
        } catch (err) {
            setInfo("批量设置失败：" + err.message, true);
        } finally {
            if (batchApplyBtn) batchApplyBtn.disabled = false;
        }
    };

    const switchTab = (tab) => {
        const isHistory = tab === "history";
        historyPanel?.classList.toggle("d-none", !isHistory);
        exclusionsPanel?.classList.toggle("d-none", isHistory);
        historyToolbar?.classList.toggle("d-none", !isHistory);
        batchToolbar?.classList.toggle("d-none", !isHistory);

        mainTabs?.querySelectorAll(".nav-link").forEach((btn) => {
            btn.classList.toggle("active", btn.dataset.tab === tab);
        });

        if (isHistory) {
            loadHistory();
        } else {
            flushPendingSaves();
            loadExclusions();
        }
    };

    // Events
    mainTabs?.addEventListener("click", (e) => {
        const btn = e.target.closest("[data-tab]");
        if (!btn) return;
        switchTab(btn.dataset.tab);
    });

    searchInput?.addEventListener("input", () => {
        clearTimeout(searchTimer);
        searchTimer = setTimeout(() => {
            currentPage = 1;
            loadHistory();
        }, 350);
    });

    onlySuspect?.addEventListener("change", () => {
        currentPage = 1;
        loadHistory();
    });

    prevPageBtn?.addEventListener("click", async () => {
        if (currentPage <= 1) return;
        const saved = await flushPendingSaves();
        if (!saved) return;
        currentPage--;
        loadHistory();
    });

    nextPageBtn?.addEventListener("click", async () => {
        const saved = await flushPendingSaves();
        if (!saved) return;
        currentPage++;
        loadHistory();
    });

    refreshSpBtn?.addEventListener("click", doRefresh);
    batchApplyBtn?.addEventListener("click", doBatchApply);

    excludeWholeBtn?.addEventListener("click", async () => {
        const selected = getSelectedSourceRows();
        if (!selected.length) {
            setInfo("请先勾选要整单剔除的方案来源行", true);
            return;
        }
        const fabhs = [...new Set(selected.map((r) => r.fabh))];
        if (fabhs.length > 1) {
            setInfo("整单剔除一次只能选择一个方案编号", true);
            return;
        }
        if (!confirm(`确认整单剔除方案 ${fabhs[0]}？`)) return;
        await doExclude(fabhs[0], "");
    });

    excludeSelectedBtn?.addEventListener("click", async () => {
        const selected = getSelectedSourceRows();
        if (!selected.length) {
            setInfo("请先勾选来源行", true);
            return;
        }
        for (const row of selected) {
            await doExclude(row.fabh, currentWzdh);
        }
    });

    sourceSelectAll?.addEventListener("change", () => {
        const checked = sourceSelectAll.checked;
        document.querySelectorAll(".oa-ph-source-cb:not(:disabled)").forEach((cb) => {
            cb.checked = checked;
        });
    });

    exclusionsTbody?.addEventListener("click", async (e) => {
        const btn = e.target.closest(".oa-ph-restore-btn");
        if (!btn) return;
        if (!confirm("确认恢复该剔除记录？恢复后需重新生成历史价格才会生效。")) return;

        const data = await postForm(removeExclusionUrl, { id: btn.dataset.id });
        setInfo(data.message || (data.success ? "已恢复" : "失败"), !data.success);
        if (data.success) {
            await loadExclusions();
        }
    });

    initHot();
    switchTab("history");
})();
