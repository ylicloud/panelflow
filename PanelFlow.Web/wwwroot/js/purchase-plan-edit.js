(function () {
    'use strict';

    var cfg = window.purchasePlanEdit;
    if (!cfg) return;

    var changeTypeLabels = { 0: '正常', 1: '变更', 2: '新增', 3: '删除' };

    function toRow(item) {
        return [
            item.id || 0,
            item.sortNo || 0,
            item.itemName || '',
            item.itemSpec || '',
            item.itemUnit || '',
            item.itemQty,
            item.itemManufacturer || '',
            item.needDate ? item.needDate.substring(0, 10) : '',
            item.remark || '',
            changeTypeLabels[item.changeType] || '正常',
            item.changeRemark || ''
        ];
    }

    function fromRow(row, index) {
        var typeMap = { '正常': 0, '变更': 1, '新增': 2, '删除': 3 };
        return {
            id: parseInt(row[0], 10) || 0,
            planId: cfg.planId,
            sortNo: index + 1,
            itemName: (row[2] || '').trim(),
            itemSpec: (row[3] || '').trim(),
            itemUnit: (row[4] || '').trim() || null,
            itemQty: parseFloat(row[5]) || 0,
            itemNoBuyQty: 0,
            itemManufacturer: (row[6] || '').trim() || null,
            needDate: row[7] ? row[7] : null,
            remark: (row[8] || '').trim() || null,
            changeType: typeMap[row[9]] !== undefined ? typeMap[row[9]] : 0,
            changeRemark: (row[10] || '').trim() || null
        };
    }

    var data = (cfg.items || []).map(toRow);
    var container = document.getElementById('hotContainer');
    var hot = new Handsontable(container, {
        data: data,
        licenseKey: '53ea8-c2678-49b80-cb40f-4dad4',
        colHeaders: ['ID', '序', '产品名称', '规格型号', '单位', '数量', '生产厂', '需要日期', '备注', '变更类型', '变更说明'],
        columns: [
            { data: 0, readOnly: true, width: 50 },
            { data: 1, readOnly: true, width: 40 },
            { data: 2, width: 120 },
            { data: 3, width: 160 },
            { data: 4, width: 50 },
            { data: 5, type: 'numeric', width: 60 },
            { data: 6, width: 100 },
            { data: 7, type: 'date', dateFormat: 'YYYY-MM-DD', width: 100 },
            { data: 8, width: 100 },
            { data: 9, type: 'dropdown', source: ['正常', '变更', '新增', '删除'], width: 70 },
            { data: 10, width: 100 }
        ],
        hiddenColumns: { columns: [0, 1], indicators: false },
        rowHeaders: true,
        stretchH: 'all',
        height: 480,
        readOnly: !cfg.canEdit,
        contextMenu: cfg.canEdit
    });

    function getToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    }

    document.getElementById('btnSave')?.addEventListener('click', function () {
        var rows = hot.getData().filter(function (r) { return (r[2] || '').trim(); });
        var payload = rows.map(function (r, i) { return fromRow(r, i); });

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

    document.getElementById('btnAddRow')?.addEventListener('click', function () {
        hot.alter('insert_row_below', hot.countRows());
        var last = hot.countRows() - 1;
        hot.setDataAtCell(last, 9, '新增');
    });
})();
