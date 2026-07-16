(function () {
    const cfg = window.quotationSummaryConfig;
    if (!cfg) return;

    const token = document.querySelector('#summary-antiforgery input[name="__RequestVerificationToken"]')?.value || '';
    const progressPanel = document.getElementById('summary-progress-panel');
    const progressCurrent = document.getElementById('summary-progress-current');
    const progressSteps = document.getElementById('summary-progress-steps');
    const runBtn = document.getElementById('run-summary-btn');
    const grids = {};

    function getAntiForgeryHeaders() {
        return token ? { 'RequestVerificationToken': token } : {};
    }

    async function fetchJson(url) {
        const res = await fetch(url, { headers: { 'Accept': 'application/json' } });
        return res.json();
    }

    function trimVal(v) {
        return (v == null ? '' : String(v)).trim();
    }

    function setRunning(running) {
        if (runBtn) runBtn.disabled = running;
        if (progressPanel) progressPanel.classList.toggle('d-none', !running);
    }

    function resetProgress() {
        if (progressSteps) progressSteps.innerHTML = '';
        if (progressCurrent) progressCurrent.textContent = '正在执行汇总…';
    }

    function appendProgressStep(message) {
        if (!progressSteps || !message) return;
        const li = document.createElement('li');
        li.textContent = message;
        progressSteps.appendChild(li);
        li.scrollIntoView({ block: 'nearest' });
        if (progressCurrent) progressCurrent.textContent = message;
    }

    async function readNdjsonStream(response, onEvent) {
        if (!response.body) {
            const text = await response.text();
            text.split('\n').forEach(line => {
                if (!line.trim()) return;
                onEvent(JSON.parse(line));
            });
            return;
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';

        while (true) {
            const { value, done } = await reader.read();
            if (done) break;
            buffer += decoder.decode(value, { stream: true });
            const lines = buffer.split('\n');
            buffer = lines.pop() || '';
            for (const line of lines) {
                if (!line.trim()) continue;
                onEvent(JSON.parse(line));
            }
        }

        if (buffer.trim()) {
            onEvent(JSON.parse(buffer));
        }
    }

    function initGrid(elId, columns, data) {
        const el = document.getElementById(elId);
        if (!el || typeof Handsontable === 'undefined') return null;
        if (grids[elId]) {
            grids[elId].loadData(data);
            return grids[elId];
        }
        grids[elId] = new Handsontable(el, {
            data,
            columns,
            rowHeaders: true,
            readOnly: true,
            licenseKey: '53ea8-c2678-49b80-cb40f-4dad4',
            stretchH: 'all',
            height: 420,
            manualColumnResize: true
        });
        return grids[elId];
    }

    async function loadHzb() {
        const json = await fetchJson(cfg.urls.hzb);
        if (!json.success) return;
        const rows = (json.data || []).map(r => ({
            x_bm: trimVal(r.x_bm ?? r.Xbm),
            x_mc: trimVal(r.x_mc ?? r.Xmc),
            x_sm: trimVal(r.x_sm ?? r.Xsm),
            x_lx: r.x_lx ?? r.Xlx,
            x_sl: r.x_sl ?? r.Xsl,
            x_zj1: r.x_zj1 ?? r.Xzj1,
            x_zj2: r.x_zj2 ?? r.Xzj2,
            x_zj3: r.x_zj3 ?? r.Xzj3,
            x_zj4: r.x_zj4 ?? r.Xzj4,
            x_zj5: r.x_zj5 ?? r.Xzj5,
            x_ggxh: trimVal(r.x_ggxh ?? r.Xggxh),
            x_sccj: trimVal(r.x_sccj ?? r.Xsccj)
        }));
        initGrid('grid-hzb', [
            { data: 'x_bm', title: '编码' },
            { data: 'x_mc', title: '名称' },
            { data: 'x_sm', title: '柜号' },
            { data: 'x_lx', title: '类型', type: 'numeric' },
            { data: 'x_sl', title: '数量', type: 'numeric' },
            { data: 'x_zj1', title: '材料', type: 'numeric' },
            { data: 'x_zj2', title: '辅料', type: 'numeric' },
            { data: 'x_zj3', title: '壳体', type: 'numeric' },
            { data: 'x_zj4', title: '安装', type: 'numeric' },
            { data: 'x_zj5', title: '包装', type: 'numeric' },
            { data: 'x_ggxh', title: '规格' },
            { data: 'x_sccj', title: '厂家' }
        ], rows);
    }

    async function loadXmyjb() {
        const json = await fetchJson(cfg.urls.xmyjb);
        if (!json.success) return;
        const rows = (json.data || []).map(r => ({
            x_dyh: trimVal(r.x_dyh),
            x_dymc: trimVal(r.x_dymc),
            x_qjmc: trimVal(r.x_qjmc),
            x_ggxh: trimVal(r.x_ggxh),
            x_sccj: trimVal(r.x_sccj),
            x_lx: r.x_lx,
            x_zsl: r.x_zsl,
            x_zje: r.x_zje,
            x_bcg_sl: r.x_bcg_sl,
            x_zxm_sl: r.x_zxm_sl,
            x_zxm_je: r.x_zxm_je
        }));
        initGrid('grid-xmyjb', [
            { data: 'x_dyh', title: '柜号' },
            { data: 'x_dymc', title: '单元名称' },
            { data: 'x_qjmc', title: '元件名称' },
            { data: 'x_ggxh', title: '规格' },
            { data: 'x_sccj', title: '厂家' },
            { data: 'x_lx', title: '类型', type: 'numeric' },
            { data: 'x_zsl', title: '数量', type: 'numeric' },
            { data: 'x_zje', title: '金额', type: 'numeric' },
            { data: 'x_bcg_sl', title: '不采购量', type: 'numeric' },
            { data: 'x_zxm_sl', title: '项目总量', type: 'numeric' },
            { data: 'x_zxm_je', title: '项目总金额', type: 'numeric' }
        ], rows);
    }

    async function loadXmyjhz() {
        const json = await fetchJson(cfg.urls.xmyjhz);
        if (!json.success) return;
        const rows = (json.data || []).map(r => ({
            x_flbh: trimVal(r.x_flbh),
            x_mc: trimVal(r.x_mc),
            x_ggxh: trimVal(r.x_ggxh),
            x_sccj: trimVal(r.x_sccj),
            x_sl: r.x_sl,
            x_je: r.x_je,
            x_bcg_sl: r.x_bcg_sl,
            x_hzjb: trimVal(r.x_hzjb)
        }));
        initGrid('grid-xmyjhz', [
            { data: 'x_flbh', title: '分类编号' },
            { data: 'x_mc', title: '名称' },
            { data: 'x_ggxh', title: '规格' },
            { data: 'x_sccj', title: '厂家' },
            { data: 'x_sl', title: '数量', type: 'numeric' },
            { data: 'x_je', title: '金额', type: 'numeric' },
            { data: 'x_bcg_sl', title: '不采购量', type: 'numeric' },
            { data: 'x_hzjb', title: '汇总级别' }
        ], rows);
    }

    async function loadXmhz() {
        const json = await fetchJson(cfg.urls.xmhz);
        if (!json.success) return;
        const rows = (json.data || []).map(r => ({
            x_mc: trimVal(r.x_mc),
            x_hzjb: trimVal(r.x_hzjb),
            x_sl: r.x_sl,
            x_je: r.x_je,
            x_bcg_sl: r.x_bcg_sl
        }));
        initGrid('grid-xmhz', [
            { data: 'x_mc', title: '分类名称' },
            { data: 'x_hzjb', title: '汇总级别' },
            { data: 'x_sl', title: '数量', type: 'numeric' },
            { data: 'x_je', title: '金额', type: 'numeric' },
            { data: 'x_bcg_sl', title: '不采购量', type: 'numeric' }
        ], rows);
    }

    async function refreshStatus() {
        const json = await fetchJson(cfg.urls.status);
        if (!json.success || !json.data) return;
        const s = json.data;
        const set = (id, val) => { const el = document.getElementById(id); if (el) el.textContent = val; };
        set('stat-hzb', s.hzbCount ?? s.HzbCount ?? 0);
        set('stat-xmyjb', s.xmyjbCount ?? s.XmyjbCount ?? 0);
        set('stat-xmyjhz', s.xmyjhzCount ?? s.XmyjhzCount ?? 0);
        set('stat-xmhz', s.xmhzCount ?? s.XmhzCount ?? 0);
    }

    async function reloadAllGrids() {
        await Promise.all([loadHzb(), loadXmyjb(), loadXmyjhz(), loadXmhz()]);
        await refreshStatus();
    }

    async function runSummarize(ignoreWarning) {
        resetProgress();
        setRunning(true);

        let finalResult = null;
        try {
            const body = new URLSearchParams();
            body.set('ignoreEmptyComponentWarning', ignoreWarning ? 'true' : 'false');
            const res = await fetch(cfg.urls.summarize, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded',
                    'Accept': 'application/x-ndjson',
                    ...getAntiForgeryHeaders()
                },
                body: body.toString()
            });

            await readNdjsonStream(res, evt => {
                if (evt.type === 'progress') {
                    appendProgressStep(evt.message);
                } else if (evt.type === 'result') {
                    finalResult = evt;
                }
            });
        } catch (err) {
            window.alert('汇总请求失败：' + (err?.message || err));
            return;
        } finally {
            setRunning(false);
        }

        if (!finalResult) {
            window.alert('汇总未返回结果');
            return;
        }

        if (finalResult.needsConfirm) {
            if (window.confirm(finalResult.message)) {
                await runSummarize(true);
            }
            return;
        }

        if (!finalResult.success) {
            window.alert(finalResult.message || '汇总失败');
            return;
        }

        if (progressCurrent) {
            progressCurrent.textContent = finalResult.message || '汇总成功';
        }
        window.alert(finalResult.message || '汇总成功');
        await reloadAllGrids();
    }

    document.getElementById('run-summary-btn')?.addEventListener('click', () => runSummarize(false));

    reloadAllGrids();
})();
