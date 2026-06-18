(function () {
    'use strict';

    var cfg = window.purchaseVerify;
    if (!cfg) return;

    function toRow(item) {
        return [
            item.id || 0,
            item.itemName || '',
            item.itemSpec || '',
            item.itemQty,
            item.itemManufacturer || '',
            item.hasCert === true ? '有' : item.hasCert === false ? '无' : '',
            item.hasInspection === true ? '有' : item.hasInspection === false ? '无' : '',
            item.appearanceOk === true ? '合格' : item.appearanceOk === false ? '不合格' : '',
            item.hasAccessories === true ? '有' : item.hasAccessories === false ? '无' : '',
            item.hasDocuments === true ? '有' : item.hasDocuments === false ? '无' : '',
            item.verifyDate ? item.verifyDate.substring(0, 10) : '',
            item.conclusion || '',
            item.verifier || ''
        ];
    }

    function parseBool3(val) {
        if (val === '有' || val === '合格') return true;
        if (val === '无' || val === '不合格') return false;
        return null;
    }

    function fromRow(row) {
        return {
            id: parseInt(row[0], 10) || 0,
            planId: cfg.planId,
            hasCert: parseBool3(row[5]),
            hasInspection: parseBool3(row[6]),
            appearanceOk: row[7] === '合格' ? true : row[7] === '不合格' ? false : null,
            hasAccessories: parseBool3(row[8]),
            hasDocuments: parseBool3(row[9]),
            verifyDate: row[10] || null,
            conclusion: (row[11] || '').trim() || null,
            verifier: (row[12] || '').trim() || null
        };
    }

    var data = (cfg.items || []).map(toRow);
    var container = document.getElementById('hotContainer');
    var hot = new Handsontable(container, {
        data: data,
        licenseKey: '53ea8-c2678-49b80-cb40f-4dad4',
        colHeaders: ['ID', '产品名称', '规格型号', '数量', '制造商', '合格证', '检验报告', '外观', '随机附件', '随机资料', '验证日期', '结论', '验证人'],
        columns: [
            { data: 0, readOnly: true, width: 50 },
            { data: 1, readOnly: true, width: 120 },
            { data: 2, readOnly: true, width: 140 },
            { data: 3, readOnly: true, type: 'numeric', width: 60 },
            { data: 4, readOnly: true, width: 100 },
            { data: 5, type: 'dropdown', source: ['', '有', '无'], width: 60 },
            { data: 6, type: 'dropdown', source: ['', '有', '无'], width: 70 },
            { data: 7, type: 'dropdown', source: ['', '合格', '不合格'], width: 60 },
            { data: 8, type: 'dropdown', source: ['', '有', '无'], width: 70 },
            { data: 9, type: 'dropdown', source: ['', '有', '无'], width: 70 },
            { data: 10, type: 'date', dateFormat: 'YYYY-MM-DD', width: 100 },
            { data: 11, type: 'dropdown', source: ['', '合格', '不合格'], width: 60 },
            { data: 12, width: 80 }
        ],
        hiddenColumns: { columns: [0], indicators: false },
        rowHeaders: true,
        stretchH: 'all',
        height: 480
    });

    function getToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    }

    document.getElementById('btnSave')?.addEventListener('click', function () {
        var payload = hot.getData().map(fromRow);

        fetch(cfg.saveUrl, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getToken()
            },
            body: JSON.stringify(payload)
        })
            .then(function (res) { return res.json(); })
            .then(function (result) {
                alert(result.message || (result.success ? '保存成功' : '保存失败'));
                if (result.success) location.reload();
            })
            .catch(function () { alert('保存失败'); });
    });
})();
