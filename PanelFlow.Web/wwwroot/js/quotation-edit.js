(() => {
    const config = window.quotationEditConfig || {};
    const keywordInput = document.getElementById("customerKeyword");
    const searchBtn = document.getElementById("customerSearchBtn");
    const hint = document.getElementById("customerSearchHint");
    const resultBox = document.getElementById("customerSearchResult");
    const companyNoInput = document.getElementById("CustomerNo");
    const companyNameInput = document.getElementById("CustomerName");
    const aliasInput = document.getElementById("CustomerAlias");
    const renameForm = document.getElementById("renameFabhForm");

    function setHint(text, isError = false) {
        if (!hint) return;
        hint.textContent = text;
        hint.className = isError ? "small text-danger mt-1" : "small text-muted mt-1";
    }

    function clearResult() {
        if (!resultBox) return;
        resultBox.innerHTML = "";
        resultBox.classList.add("d-none");
    }

    function chooseCustomer(item) {
        if (companyNoInput) companyNoInput.value = item.companyNo || "";
        if (companyNameInput) companyNameInput.value = item.companyName || "";
        if (aliasInput) aliasInput.value = item.alias || "";
        clearResult();
        setHint(`已选择：${companyNameInput?.value || ""} (${aliasInput?.value || ""})`);
    }

    async function search() {
        if (!keywordInput) return;
        const keyword = (keywordInput.value || "").trim();
        clearResult();
        if (!keyword) {
            setHint("请输入搜索关键字", true);
            return;
        }

        setHint("搜索中...");
        try {
            const url = `${config.searchCustomersUrl}?keyword=${encodeURIComponent(keyword)}`;
            const response = await fetch(url, { method: "GET" });
            if (!response.ok) {
                setHint("搜索失败，请稍后重试", true);
                return;
            }

            const list = await response.json();
            if (!Array.isArray(list) || list.length === 0) {
                setHint("未找到匹配客户", true);
                return;
            }

            list.forEach(item => {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "list-group-item list-group-item-action";
                button.textContent = `${item.companyName} (${item.alias}) [${item.companyNo}]`;
                button.addEventListener("click", () => chooseCustomer(item));
                resultBox.appendChild(button);
            });
            resultBox.classList.remove("d-none");
            setHint(`找到 ${list.length} 条，请点击选择`);
        } catch {
            setHint("搜索异常，请稍后重试", true);
        }
    }

    if (searchBtn) searchBtn.addEventListener("click", search);
    if (keywordInput) {
        keywordInput.addEventListener("keydown", e => {
            if (e.key === "Enter") {
                e.preventDefault();
                search();
            }
        });
    }

    if (renameForm) {
        renameForm.addEventListener("submit", e => {
            const input = document.getElementById("newQuotationNo");
            const submitBtn = document.getElementById("renameFabhSubmitBtn");
            const newNo = (input?.value || "").trim();
            if (!newNo) {
                e.preventDefault();
                alert("请输入新方案编号");
                return;
            }
            if (!confirm("改号后无法通过本页面撤销，是否继续？")) {
                e.preventDefault();
                return;
            }
            if (submitBtn && !submitBtn.disabled) {
                submitBtn.disabled = true;
                submitBtn.innerHTML =
                    '<span class="spinner-border spinner-border-sm me-1" role="status" aria-hidden="true"></span>正在修改...';
            }
        });
    }
})();
