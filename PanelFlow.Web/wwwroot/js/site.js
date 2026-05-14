// 侧边栏折叠控制
(function () {
    const sidebar = document.getElementById('sidebar');
    const main = document.querySelector('.oa-main');
    const toggleBtn = document.getElementById('sidebarToggle');
    if (!toggleBtn || !sidebar || !main) return;

    toggleBtn.addEventListener('click', function () {
        const isMobile = window.innerWidth <= 768;
        if (isMobile) {
            sidebar.classList.toggle('show');
        } else {
            sidebar.classList.toggle('collapsed');
            main.classList.toggle('expanded');
        }
    });

    // 移动端点击内容区关闭侧边栏（排除切换按钮本身，避免事件冒泡导致刚打开就被关闭）
    main.addEventListener('click', function (e) {
        if (window.innerWidth <= 768 && sidebar.classList.contains('show')
            && !toggleBtn.contains(e.target)) {
            sidebar.classList.remove('show');
        }
    });
})();
