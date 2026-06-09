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

    const infoBar = document.getElementById("page-info-bar");
    const historyTbody = document.getElementById("history-tbody");
    const exclusionsTbody = document.getElementById("exclusions-tbody");
    const historyPanel = document.getElementById("history-panel");
    const exclusionsPanel = document.getElementById("exclusions-panel");
    const historyToolbar = document.getElementById("history-toolbar");
    const searchInput = document.getElementById("search-input");
    const onlySuspect = document.getElementById("only-suspect");
    const refreshSpBtn = document.getElementById("refresh-sp-btn");
    const prevPageBtn = document.getElementById("prev-page-btn");
    const nextPageBtn = document.getElementById("next-page-btn");
    const pagerInfo = document.getElementById("history-pager-info");
    const mainTabs = document.getElementById("main-tabs");

    const sourceModalEl = document.getElementById("source-modal");
    const sourceModal = sourceModalEl ? new bootstrap.Modal(sourceModalEl) : null;
    const sourceModalTitle = document.getElementById("source-modal-title");
    const sourceTbody = document.getElementById("source-tbody");
    const excludeReason = document.getElementById("exclude-reason");
    const excludeWholeBtn = document.getElementById("exclude-whole-btn");
    const excludeSelectedBtn = document.getElementById("exclude-selected-btn");
    const sourceSelectAll = document.getElementById("source-select-all");
    const historyTheadRow = document.getElementById("history-thead-row");
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
    const deviationWarnPct = 20;

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

    const fmtDeviation = (pct, isSuspect) => {
        if (pct === null || pct === undefined || Number.isNaN(Number(pct))) return "—";
        const n = Number(pct);
        const sign = n > 0 ? "+" : "";
        const text = sign + n.toFixed(1) + "%";
        if (isSuspect || Math.abs(n) > deviationWarnPct) {
            return `<span class="oa-ph-deviation-warn">${text}</span>`;
        }
        return text;
    };

    const updateSortHeaders = () => {
        historyTheadRow?.querySelectorAll(".oa-ph-sortable").forEach((th) => {
            const active = th.dataset.sort === sortBy;
            th.classList.toggle("oa-ph-sort-active", active);
            if (active) {
                th.dataset.sortDir = sortAsc ? "asc" : "desc";
            } else {
                delete th.dataset.sortDir;
            }
        });
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

    const loadHistory = async () => {
        if (!historyTbody) return;
        historyTbody.innerHTML = '<tr><td colspan="10" class="text-center text-muted py-3">加载中…</td></tr>';

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
                historyTbody.innerHTML = '<tr><td colspan="10" class="text-center text-danger py-3">加载失败</td></tr>';
                return;
            }

            const result = data.result;
            renderHistory(result.items || []);
            updatePager(result);
            updateSortHeaders();
            setInfo(`共 ${result.totalCount} 条记录`);
        } catch (err) {
            setInfo("加载失败：" + err.message, true);
            historyTbody.innerHTML = '<tr><td colspan="10" class="text-center text-danger py-3">加载失败</td></tr>';
        }
    };

    const renderHistory = (items) => {
        if (!historyTbody) return;
        if (!items.length) {
            historyTbody.innerHTML = '<tr><td colspan="10" class="text-center text-muted py-3">无数据</td></tr>';
            return;
        }

        historyTbody.innerHTML = items.map((row) => {
            const suspectBadge = row.isSuspect
                ? `<span class="badge text-bg-warning oa-ph-badge-suspect" title="${escapeHtml(row.suspectReason || "")}">异常</span>`
                : '<span class="text-muted">—</span>';
            const trClass = row.isSuspect ? "oa-ph-row-suspect" : "";
            return `<tr class="${trClass} oa-ph-history-row" data-wzdh="${escapeHtml(row.x_wzdh)}" style="cursor:pointer">
                <td>${escapeHtml(row.x_mc || "—")}</td>
                <td class="small">${escapeHtml(row.ggxh || "—")}</td>
                <td class="text-end">${fmtMoney(row.last_price)}</td>
                <td class="text-end">${fmtMoney(row.avg_price)}</td>
                <td class="text-end">${fmtDeviation(row.deviationPercent, row.isSuspect)}</td>
                <td class="text-end">${fmtMoney(row.min_price)}</td>
                <td class="text-end">${fmtMoney(row.max_price)}</td>
                <td class="text-end">${row.avg_count ?? 0}</td>
                <td class="small">${escapeHtml(row.last_fabh || "—")}</td>
                <td>${suspectBadge}</td>
            </tr>`;
        }).join("");
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

    const escapeHtml = (s) => {
        if (s === null || s === undefined) return "";
        return String(s)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;");
    };

    const switchTab = (tab) => {
        const isHistory = tab === "history";
        historyPanel?.classList.toggle("d-none", !isHistory);
        exclusionsPanel?.classList.toggle("d-none", isHistory);
        historyToolbar?.classList.toggle("d-none", !isHistory);

        mainTabs?.querySelectorAll(".nav-link").forEach((btn) => {
            btn.classList.toggle("active", btn.dataset.tab === tab);
        });

        if (isHistory) {
            loadHistory();
        } else {
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

    prevPageBtn?.addEventListener("click", () => {
        if (currentPage > 1) {
            currentPage--;
            loadHistory();
        }
    });

    nextPageBtn?.addEventListener("click", () => {
        currentPage++;
        loadHistory();
    });

    refreshSpBtn?.addEventListener("click", doRefresh);

    historyTheadRow?.addEventListener("click", (e) => {
        const th = e.target.closest(".oa-ph-sortable");
        if (!th) return;
        const col = th.dataset.sort;
        if (!col) return;
        if (sortBy === col) {
            sortAsc = !sortAsc;
        } else {
            sortBy = col;
            sortAsc = true;
        }
        currentPage = 1;
        updateSortHeaders();
        loadHistory();
    });

    historyTbody?.addEventListener("click", (e) => {
        const row = e.target.closest(".oa-ph-history-row");
        if (!row) return;
        openSourceModal(row.dataset.wzdh);
    });

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

    updateSortHeaders();
    switchTab("history");
})();
