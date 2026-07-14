#!/bin/bash
# PanelFlow 一键部署脚本（测试机 hp3）
# 将 Web 项目发布并上传到局域网 Ubuntu 服务器
#
# 运行环境: WSL2（Ubuntu）
# 用法:
#   wsl -d Ubuntu-22.04
#   cd /mnt/d/repos/panelflow
#   chmod +x deploy-test.sh   # 首次
#   ./deploy-test.sh          # 查看步骤说明
#   ./deploy-test.sh 0        # 完整部署
#   ./deploy-test.sh 2        # 仅执行第 2 步

set -euo pipefail

# ==================== 配置区 ====================
SERVER_USER="sunny"
SERVER_HOST="hp3"
SERVER_PATH="/var/www/PanelFlow"
SERVICE_NAME="panelflow"

# WSL 路径（shell / rsync / mkdir）
PROJECT_DIR="/mnt/d/repos/panelflow/PanelFlow.Web"
PUBLISH_DIR="/mnt/d/work/PanelFlow/publish"

# 供 Windows 版 dotnet.exe 使用的路径（正斜杠亦可）
WIN_PROJECT_CSPROJ="D:/repos/panelflow/PanelFlow.Web/PanelFlow.Web.csproj"
WIN_PUBLISH_DIR="D:/work/PanelFlow/publish"

# Windows SDK（WSL 内通常优先走此路径；若已安装 Linux SDK 会优先用 PATH 中的 dotnet）
WIN_DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"

# 服务器应用端口（健康检查用，在远程机本机地址）
APP_URL="http://localhost:7777/"
# ==================== 配置区结束 ====================

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m'

declare -A STEP_DESCRIPTIONS=(
    [1]="准备本地发布目录（创建或清空）"
    [2]="发布项目代码（dotnet publish）"
    [3]="停止服务器上的应用服务"
    [4]="备份服务器上的当前版本"
    [5]="上传发布文件到服务器"
    [6]="启动服务器上的应用服务"
    [7]="检查服务状态"
    [8]="执行快速健康检查"
)

print_usage() {
    echo "用法: $0 [参数]"
    echo ""
    echo "运行环境: WSL2（在 Ubuntu 发行版中执行本脚本）"
    echo ""
    echo "参数说明:"
    echo "  无参数  - 显示每步的功能说明"
    echo "  0       - 执行所有步骤（完整部署）"
    echo "  1-8     - 执行指定的单一步骤"
    echo ""
    echo "步骤说明:"
    for i in {1..8}; do
        echo "  第${i}步: ${STEP_DESCRIPTIONS[$i]}"
    done
}

resolve_dotnet() {
    if command -v dotnet >/dev/null 2>&1; then
        # 排除 Windows 互操作里挂进来的无用 stub，优先真实 Linux/Windows 可执行文件
        local cand
        cand="$(command -v dotnet)"
        if [[ -x "$cand" ]]; then
            echo "$cand"
            return 0
        fi
    fi
    if [[ -x "$WIN_DOTNET" ]]; then
        echo "$WIN_DOTNET"
        return 0
    fi
    return 1
}

is_windows_dotnet() {
    local exe="$1"
    [[ "$exe" == *.exe ]] || [[ "$exe" == /mnt/c/* ]] || [[ "$exe" == /mnt/C/* ]]
}

ensure_prereqs() {
    if [[ ! -d /mnt/d ]]; then
        echo -e "${RED}错误: 未挂载 /mnt/d，请确认在 WSL2 中运行，且已启用驱动器挂载${NC}"
        exit 1
    fi
    if [[ ! -d "$PROJECT_DIR" ]]; then
        echo -e "${RED}错误: 项目目录不存在: $PROJECT_DIR${NC}"
        exit 1
    fi
    if ! command -v ssh >/dev/null 2>&1; then
        echo -e "${RED}错误: 未找到 ssh，请执行: sudo apt install openssh-client${NC}"
        exit 1
    fi
    if ! command -v rsync >/dev/null 2>&1; then
        echo -e "${RED}错误: 未找到 rsync，请执行: sudo apt install rsync${NC}"
        exit 1
    fi
    if ! resolve_dotnet >/dev/null; then
        echo -e "${RED}错误: 未找到 dotnet。可安装 WSL 版 SDK，或确认 Windows 已安装: $WIN_DOTNET${NC}"
        exit 1
    fi
}

step_1() {
    echo -e "${YELLOW}第1步: 准备本地发布目录${NC}"
    if [ ! -d "$PUBLISH_DIR" ]; then
        mkdir -p "$PUBLISH_DIR"
        echo -e "${GREEN}第1步: 发布目录已创建 ($PUBLISH_DIR)${NC}"
    else
        rm -rf "${PUBLISH_DIR:?}"/*
        echo -e "${GREEN}第1步: 发布目录已清空 ($PUBLISH_DIR)${NC}"
    fi
}

step_2() {
    echo -e "${YELLOW}第2步: 发布 PanelFlow.Web 项目${NC}"
    local dotnet_exe
    dotnet_exe="$(resolve_dotnet)"
    echo "使用: $dotnet_exe"

    if is_windows_dotnet "$dotnet_exe"; then
        # Windows 进程不能可靠使用 /mnt/d 路径，改用盘符路径
        "$dotnet_exe" publish "$WIN_PROJECT_CSPROJ" -c Release -o "$WIN_PUBLISH_DIR" || {
            echo -e "${RED}第2步: 发布失败${NC}"
            exit 1
        }
    else
        "$dotnet_exe" publish "$PROJECT_DIR/PanelFlow.Web.csproj" -c Release -o "$PUBLISH_DIR" || {
            echo -e "${RED}第2步: 发布失败${NC}"
            exit 1
        }
    fi
    echo -e "${GREEN}第2步: 项目发布完成${NC}"
}

step_3() {
    echo -e "${YELLOW}第3步: 停止服务器上的 $SERVICE_NAME 服务${NC}"
    ssh "$SERVER_USER@$SERVER_HOST" "sudo systemctl stop $SERVICE_NAME" || {
        echo -e "${RED}第3步: 服务停止失败${NC}"
        exit 1
    }
    echo -e "${GREEN}第3步: 服务已停止${NC}"
}

step_4() {
    echo -e "${YELLOW}第4步: 备份当前版本${NC}"
    local backup_dir="${SERVER_PATH}_backup_$(date +%Y%m%d_%H%M%S)"
    ssh "$SERVER_USER@$SERVER_HOST" "sudo cp -r $SERVER_PATH $backup_dir"
    echo -e "${GREEN}第4步: 已备份至 $backup_dir${NC}"
}

step_5() {
    echo -e "${YELLOW}第5步: 上传发布文件到服务器（跳过 keys/ 与 appsettings.json）${NC}"
    rsync -avz --exclude='keys/' --exclude='appsettings.json' -e ssh \
        "$PUBLISH_DIR/" "$SERVER_USER@$SERVER_HOST:$SERVER_PATH/" || {
        echo -e "${RED}第5步: 上传失败${NC}"
        exit 1
    }
    echo -e "${GREEN}第5步: 上传完成${NC}"
}

step_6() {
    echo -e "${YELLOW}第6步: 启动 $SERVICE_NAME 服务${NC}"
    ssh "$SERVER_USER@$SERVER_HOST" "sudo systemctl start $SERVICE_NAME" || {
        echo -e "${RED}第6步: 服务启动失败${NC}"
        exit 1
    }
    echo -e "${GREEN}第6步: 服务已启动${NC}"
}

step_7() {
    echo -e "${YELLOW}第7步: 检查服务状态${NC}"
    # systemctl status 在 inactive/failed 时非 0，不因此中断脚本
    ssh "$SERVER_USER@$SERVER_HOST" "sudo systemctl status $SERVICE_NAME --no-pager" || true
    echo -e "${GREEN}第7步: 状态检查完成${NC}"
}

step_8() {
    echo -e "${YELLOW}第8步: 执行快速健康检查${NC}"
    sleep 5
    if ssh "$SERVER_USER@$SERVER_HOST" "curl -sf -o /dev/null '$APP_URL'"; then
        echo -e "${GREEN}成功: 健康检查通过${NC}"
    else
        echo -e "${YELLOW}警告: 健康检查失败，请手动验证服务${NC}"
    fi
}

run_all_steps() {
    echo -e "${GREEN}========== PanelFlow 开始部署（测试机 $SERVER_HOST） ==========${NC}"
    ensure_prereqs
    step_1
    step_2
    step_3
    step_4
    step_5
    step_6
    step_7
    step_8
    echo -e "${GREEN}========== PanelFlow 部署完成 ==========${NC}"
}

if [ $# -eq 0 ]; then
    print_usage
elif [ "$1" == "0" ]; then
    run_all_steps
elif [[ "$1" =~ ^[1-8]$ ]]; then
    ensure_prereqs
    "step_$1"
else
    echo -e "${RED}错误: 无效的参数 '$1'${NC}"
    print_usage
    exit 1
fi
