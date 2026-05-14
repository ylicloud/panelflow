#!/bin/bash
# PanelFlow 一键部署脚本
# 将 Web 项目发布并上传到局域网 Ubuntu 服务器
# 使用方法: ./deploy.sh 0 执行所有步骤
# 运行环境: Cygwin

# ==================== 配置区 ====================
SERVER_USER="sunny"
SERVER_HOST="hp1"
SERVER_PATH="/var/www/PanelFlow"
SERVICE_NAME="panelflow"

# 本地路径：Cygwin 格式（用于 shell 操作）和 Windows 格式（用于 dotnet 命令）
PROJECT_DIR="/cygdrive/f/OneDrive/source/PanelFlow/PanelFlow.Web"
PUBLISH_DIR="/cygdrive/d/work/PanelFlow/publish"
WIN_PROJECT_DIR="f:\\OneDrive\\source\\PanelFlow\\PanelFlow.Web"
WIN_PUBLISH_DIR="D:\\work\\PanelFlow\\publish"

# 服务器应用端口（健康检查用）
APP_URL="http://localhost:8000/"
# ==================== 配置区结束 ====================

# 颜色输出
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m'

# 步骤描述
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

# 第1步：准备本地发布目录
step_1() {
    echo -e "${YELLOW}第1步: 准备本地发布目录${NC}"
    if [ ! -d "$PUBLISH_DIR" ]; then
        mkdir -p "$PUBLISH_DIR"
        echo -e "${GREEN}第1步: 发布目录已创建${NC}"
    else
        rm -rf "${PUBLISH_DIR:?}"/*
        echo -e "${GREEN}第1步: 发布目录已清空${NC}"
    fi
}

# 第2步：发布项目代码
step_2() {
    echo -e "${YELLOW}第2步: 发布 PanelFlow.Web 项目${NC}"
    # dotnet 是 Windows 程序，必须使用 Windows 格式路径
    cd "$PROJECT_DIR" && dotnet publish -c Release -o "$WIN_PUBLISH_DIR" || {
        echo -e "${RED}第2步: 发布失败${NC}"
        exit 1
    }
    echo -e "${GREEN}第2步: 项目发布完成${NC}"
}

# 第3步：停止服务
step_3() {
    echo -e "${YELLOW}第3步: 停止服务器上的 $SERVICE_NAME 服务${NC}"
    ssh "$SERVER_USER@$SERVER_HOST" "sudo systemctl stop $SERVICE_NAME" || {
        echo -e "${RED}第3步: 服务停止失败${NC}"
        exit 1
    }
    echo -e "${GREEN}第3步: 服务已停止${NC}"
}

# 第4步：备份当前版本
step_4() {
    echo -e "${YELLOW}第4步: 备份当前版本${NC}"
    BACKUP_DIR="${SERVER_PATH}_backup_$(date +%Y%m%d_%H%M%S)"
    ssh "$SERVER_USER@$SERVER_HOST" "sudo cp -r $SERVER_PATH $BACKUP_DIR"
    echo -e "${GREEN}第4步: 已备份至 $BACKUP_DIR${NC}"
}

# 第5步：上传发布文件
step_5() {
    echo -e "${YELLOW}第5步: 上传发布文件到服务器${NC},跳过配置文件复制"
    rsync -avz --exclude='keys/' --exclude='appsettings.json' -e ssh "$PUBLISH_DIR/" "$SERVER_USER@$SERVER_HOST:$SERVER_PATH/" || {
        echo -e "${RED}第5步: 上传失败${NC}"
        exit 1
    }
    echo -e "${GREEN}第5步: 上传完成${NC}"
}

# 第6步：启动服务
step_6() {
    echo -e "${YELLOW}第6步: 启动 $SERVICE_NAME 服务${NC}"
    ssh "$SERVER_USER@$SERVER_HOST" "sudo systemctl start $SERVICE_NAME" || {
        echo -e "${RED}第6步: 服务启动失败${NC}"
        exit 1
    }
    echo -e "${GREEN}第6步: 服务已启动${NC}"
}

# 第7步：检查服务状态
step_7() {
    echo -e "${YELLOW}第7步: 检查服务状态${NC}"
    ssh "$SERVER_USER@$SERVER_HOST" "sudo systemctl status $SERVICE_NAME --no-pager"
    echo -e "${GREEN}第7步: 状态检查完成${NC}"
}

# 第8步：健康检查
# step_8() {
#     echo -e "${YELLOW}第8步: 等待应用启动并执行健康检查${NC}"
#     sleep 5
#     if ssh "$SERVER_USER@$SERVER_HOST" "curl -sf -o /dev/null -w '%{http_code}' $APP_URL" | grep -q "200\|302"; then
#         echo -e "${GREEN}第8步: 健康检查通过${NC}"
#     else
#         echo -e "${YELLOW}第8步: 健康检查未通过（可能是登录重定向），请手动验证${NC}"
#     fi
# }
step_8() {
    echo -e "${YELLOW}第8步: 执行快速健康检查${NC}"
    sleep 5 # 给应用一点启动时间
    ssh "$SERVER_USER@$SERVER_HOST" "curl -f $APP_URL" || echo -e "${YELLOW}警告: 健康检查失败，请手动验证服务${NC}"
    ssh "$SERVER_USER@$SERVER_HOST" "curl -f $APP_URL" && echo -e "${GREEN}成功: 健康检查通过${NC}"
}

# 执行所有步骤
run_all_steps() {
    echo -e "${GREEN}========== PanelFlow 开始部署 ==========${NC}"
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

# 主逻辑
if [ $# -eq 0 ]; then
    print_usage
elif [ "$1" == "0" ]; then
    run_all_steps
elif [[ "$1" =~ ^[1-8]$ ]]; then
    step_$1
else
    echo -e "${RED}错误: 无效的参数 '$1'${NC}"
    print_usage
    exit 1
fi
