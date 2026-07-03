# Windows 开发命令行环境配置指南

> 适用场景：Windows 11 开发机 + 多台 Ubuntu 服务器，希望本地命令行习惯与 Linux 一致。  
> 目标组合：**Windows Terminal**（终端） + **WSL2 + Ubuntu**（日常开发） + **PowerShell 7**（Windows 本机管理） + **Coreutils for Windows**（可选，在 Windows 原生侧使用 Linux 风格命令）。

---

## 1. 概念速查

| 名称 | 类型 | 作用 |
|------|------|------|
| **cmd** | Shell | 老式 Windows 命令行，仅遗留脚本时使用 |
| **PowerShell 5.1** | Shell | Windows 内置，管本机够用 |
| **PowerShell 7（pwsh）** | Shell | 跨平台、现代语法，推荐作为 Windows 管理主力 |
| **WSL2** | 虚拟化层 | 在 Windows 上运行真实 Linux 内核 |
| **Ubuntu（WSL）** | Linux 发行版 | WSL 里的完整 Linux 环境，与服务器最接近 |
| **Git Bash** | Shell | 轻量 Unix 命令，适合 Git；不能替代 WSL |
| **Coreutils for Windows** | 命令工具集 | 微软维护的 Linux 风格原生工具（`ls`、`grep`、`find` 等），**不是 Shell** |
| **Windows Terminal** | 终端应用 | 多标签、分屏、主题；**不是 Shell**，只负责显示 |

**推荐分工：**

```
日常写代码、跑 Linux 工具、对齐服务器环境  →  WSL2 + Ubuntu + bash
管理 Windows 本机（服务、注册表、安装软件）  →  PowerShell 7
在 Windows 原生路径用 ls/grep/find 等       →  Coreutils for Windows（可选）
连远程 Ubuntu 服务器                        →  WSL 或 PowerShell 里 ssh 均可
```

> **关于 Coreutils for Windows：** 微软在 [Build 2026](https://blogs.windows.com/windowsdeveloper/2026/06/02/build-2026-furthering-windows-as-the-trusted-platform-for-development/) 发布，基于开源 [uutils](https://github.com/uutils/coreutils)（Rust 重写的 GNU coreutils）。在 **Windows 原生环境**（不进入 WSL）即可使用与 Linux/macOS 一致的 `ls`、`cp`、`grep`、`find` 等命令，方便脚本跨平台迁移。当前仍为 **Preview** 阶段。

---

## 2. 当前环境自检

在 **PowerShell** 中依次执行：

```powershell
# WSL 状态（应显示默认发行版、WSL 版本为 2）
wsl --status
wsl -l -v

# PowerShell 7（未安装时会报「找不到命令」）
pwsh --version

# Windows Terminal（已安装时会有输出）
wt --version
# 或
Get-AppxPackage Microsoft.WindowsTerminal

# Coreutils for Windows（未安装时会报「找不到命令」）
ls --version
grep --version
# 或
coreutils-manager --help
```

**本机已知状态（2026-06-22）：**

- WSL2：已启用
- 默认发行版：`Ubuntu-22.04`（WSL 2）
- 另有 `docker-desktop` 发行版（Docker Desktop 自动创建，勿删）
- PowerShell 7：未安装
- Coreutils for Windows：未安装（可选，见第 6 节）
- Windows Terminal：建议安装（见第 4 节）

---

## 3. WSL2 + Ubuntu 配置

### 3.1 首次安装（若尚未安装）

以**管理员身份**打开 PowerShell，执行：

```powershell
# 启用 WSL 与虚拟化组件（Win11 22H2+ 通常已内置，可跳过）
wsl --install

# 指定 Ubuntu 22.04（与常见服务器 LTS 一致）
wsl --install -d Ubuntu-22.04
```

安装完成后**重启**电脑。首次启动 Ubuntu 会要求创建 Linux 用户名和密码（与 Windows 密码无关，自行设定）。

### 3.2 确认 WSL 版本为 2

```powershell
wsl -l -v
```

若某发行版 VERSION 为 1，升级为 2：

```powershell
wsl --set-version Ubuntu-22.04 2
wsl --set-default-version 2
```

### 3.3 设置默认发行版

```powershell
wsl --set-default Ubuntu-22.04
```

### 3.4 Ubuntu 首次初始化（在 WSL 终端中执行）

```bash
# 更新包索引
sudo apt update && sudo apt upgrade -y

# 常用开发工具
sudo apt install -y build-essential git curl wget unzip zip \
  ca-certificates gnupg lsb-release openssh-client rsync

# 可选：Node.js（若项目需要，也可用 nvm）
# curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash -
# sudo apt install -y nodejs
```

### 3.5 Windows 与 WSL 文件路径

| 位置 | 路径示例 |
|------|----------|
| 在 WSL 中访问 Windows D 盘 | `/mnt/d/repos/panelflow` |
| 在 Windows 中访问 WSL 家目录 | `\\wsl$\Ubuntu-22.04\home\<用户名>` |
| 在资源管理器地址栏 | 输入 `\\wsl$\Ubuntu-22.04` |

**性能建议：**

- 项目在 WSL 内开发（如 `~/repos/panelflow`）→ I/O 最快，最贴近 Linux 服务器
- 项目必须在 Windows 盘（如 `D:\repos\...`）→ 可在 WSL 用 `/mnt/d/...` 访问，但大量小文件 I/O 会慢一些

对本仓库（`D:\repos\panelflow`，.NET / ASP.NET MVC）：

- 编译、调试在 **Windows + PowerShell / Cursor 集成终端** 更直接
- 脚本、SSH、rsync 等可用 **WSL** 侧操作

### 3.6 WSL 常用维护命令（Windows 侧）

```powershell
# 关闭所有 WSL 实例
wsl --shutdown

# 进入默认 Ubuntu
wsl

# 以指定用户进入
wsl -d Ubuntu-22.04 -u root
```

---

## 4. 安装 Windows Terminal

Windows Terminal 是**终端应用**，可同时开 PowerShell、WSL、cmd 等多个标签。

### 4.1 安装方式（任选其一）

**方式 A：Microsoft Store（推荐）**

1. 打开 Microsoft Store
2. 搜索 **Windows Terminal**
3. 安装

**方式 B：winget**

```powershell
winget install --id Microsoft.WindowsTerminal -e
```

**方式 C：GitHub Release**

从 [Windows Terminal Releases](https://github.com/microsoft/terminal/releases) 下载 `.msixbundle` 并安装。

### 4.2 设为默认终端（Win11）

1. 打开 **设置 → 隐私和安全性 → 开发者选项**（或 **设置 → 系统 → 开发者选项**）
2. 找到 **终端** → 默认终端应用 → 选择 **Windows Terminal**

或在 Windows Terminal 设置中：**启动 → 默认终端应用程序 → Windows Terminal**。

### 4.3 推荐 Terminal 配置

打开 Windows Terminal → **设置（Ctrl+,）**：

1. **启动**
   - 默认配置文件：`Ubuntu-22.04`（日常开发）
   - 默认终端应用：Windows Terminal

2. **配置文件 → Ubuntu-22.04**
   - 起始目录：`\\wsl$\Ubuntu-22.04\home\<你的Linux用户名>`
   - 字体：Cascadia Code、JetBrains Mono 等等宽字体

3. **配置文件 → PowerShell**（安装 pwsh 后会出现 PowerShell 7 条目）
   - 用于 Windows 本机管理时切换到此标签

4. **新建配置文件**（可选）：添加 **Git Bash** 若已安装 Git for Windows

**settings.json 片段示例**（合并到 Terminal 设置 JSON 中，按实际 GUID 调整）：

```json
{
  "defaultProfile": "{Ubuntu-22.04 的 profile guid}",
  "profiles": {
    "list": [
      {
        "name": "Ubuntu-22.04",
        "source": "Windows.Terminal.Wsl",
        "startingDirectory": "//wsl$/Ubuntu-22.04/home/你的用户名",
        "font": {
          "face": "Cascadia Code",
          "size": 12
        }
      }
    ]
  }
}
```

---

## 5. 安装 PowerShell 7

PowerShell 7 与 Windows 内置的 5.1 **并存**，命令为 `pwsh`（5.1 仍为 `powershell`）。

### 5.1 安装

```powershell
winget install --id Microsoft.PowerShell -e
```

安装后**新开终端**，验证：

```powershell
pwsh --version
# 期望类似：PowerShell 7.x.x
```

### 5.2 常用场景

```powershell
# 查看 Windows 服务
Get-Service | Where-Object Status -eq 'Running'

# 管理环境变量（用户级）
[Environment]::GetEnvironmentVariable('PATH', 'User')

# 本机 .NET 项目编译（panelflow）
cd D:\repos\panelflow
dotnet build
```

### 5.3 可选：模块与执行策略

```powershell
# 允许本地脚本运行（按需）
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned

# 常用模块
Install-Module -Name PSReadLine -Scope CurrentUser -Force
```

---

## 6. Coreutils for Windows（可选）

若你习惯 Linux 命令、但当前在 **Windows 原生路径**（如 `D:\repos\...`）工作、不想切进 WSL，可以安装 Coreutils for Windows。

### 6.1 它是什么、不是什么

| | Coreutils for Windows | WSL2 + Ubuntu | Git Bash |
|---|----------------------|---------------|----------|
| 运行方式 | Windows 原生 exe | 完整 Linux 内核 | MSYS 模拟层 |
| 提供内容 | 约 100+ 个命令工具 | 完整 Linux 环境 | 部分 Unix 工具 + bash |
| 适合场景 | 在 PS7/cmd 里用 `ls`、`grep` 管道 | 与 Ubuntu 服务器完全一致 | 主要配合 Git |
| 能否替代 WSL | **不能**（无 apt、无完整 syscall） | — | **不能** |

实现方式：单个 `coreutils.exe` 多调用二进制，安装时为每个命令创建 NTFS 硬链接（如 `ls.exe` → `coreutils.exe`）。

### 6.2 安装

```powershell
winget install --id Microsoft.Coreutils -e
```

安装后**新开终端**，验证：

```powershell
ls --version
grep --version
find --version
```

默认安装路径：`C:\Program Files\coreutils\`

### 6.3 前置要求与 Shell 冲突

- **需要 PowerShell 7.4+**（推荐 7.6+，支持 `~` 路径展开）
- 安装程序会通过 **PSReadLine** 集成到交互式 PowerShell，改善引号与通配符行为
- 部分命令与 cmd/PowerShell **内置命令同名**，在 PowerShell 中可能仍解析为内置别名：

| 命令 | cmd | PowerShell 7.4+ | 说明 |
|------|:---:|:---------------:|------|
| `ls` `cp` `mv` `rm` `cat` | ✅ | ⚠️ 与别名冲突 | 管道场景需注意 |
| `find` `grep` `hostname` | ✅ | ✅ | 通常可直接用 |
| `dir` `more` `whoami` `kill` | 🛑 未提供 | 🛑 未提供 | 避免与 Windows 内置冲突 |

若某命令行为不对，可用 `coreutils-manager` 管理：

```powershell
coreutils-manager --help
coreutils-manager disable ls    # 禁用单个工具，恢复 Windows 默认行为
```

**PowerShell 别名提示：** 不要用 `Set-Alias ll ls` 等方式覆盖，会导致管道二进制流兼容问题（`xargs`、`find` 等会异常）。

### 6.4 Windows 与 Linux 的差异（官方说明）

| 差异 | 说明 |
|------|------|
| 行尾 | Windows 常用 CRLF，字节级操作可能受 `\r` 影响 |
| 空设备 | 用 `NUL` 代替 `/dev/null` |
| 信号 | 无 POSIX 信号（`kill` 未提供）；`Ctrl+C` 正常 |
| 路径 | `/` 与 `\` 均可，部分输出仍用 `\` |
| 权限 | Windows ACL，非 POSIX 权限位 |
| 符号链接 | 读取无需提权；**创建**需开发者模式或管理员终端 |

PowerShell 中写复杂 `find` 表达式时，转义符仍是 PowerShell 的 `` ` ``，不是 bash 的 `\`：

```powershell
# bash:  find . \( -name '*.txt' -o -name '*.md' \)
# pwsh:  find . `( -name '*.txt' -o -name '*.md' `)
```

### 6.5 与 WSL 如何搭配

```
需要完整 Linux（apt、docker、与服务器一致）     →  WSL2 + Ubuntu
在 D:\ 盘 dotnet 项目里偶尔 ls/grep/管道脚本   →  PowerShell 7 + Coreutils
已有 bash 脚本要在 Windows 原生侧跑             →  可试 Coreutils，复杂脚本仍建议 WSL
```

对本仓库 panelflow：**主力仍建议 PowerShell 7 + `dotnet`**；Coreutils 作为在 Windows 侧使用 Linux 习惯命令的**补充**，不必替代 WSL。

### 6.6 常用命令示例

```powershell
# 在 PowerShell 7 中
ls -la D:\repos\panelflow
grep -r "IPermissionService" D:\repos\panelflow --include="*.cs"
find D:\repos\panelflow -name "*.csproj"
cat README.md | grep -i docker
```

---

## 7. Cursor / VS Code 终端配置

### 7.1 打开设置 JSON

Cursor：**Ctrl+Shift+P** → `Preferences: Open User Settings (JSON)`

配置文件路径：

```
C:\Users\<用户名>\AppData\Roaming\Cursor\User\settings.json
```

### 7.2 推荐配置（按场景选择）

**方案 A：默认 WSL Ubuntu（偏 Linux / 与服务器一致）**

```json
{
  "terminal.integrated.defaultProfile.windows": "Ubuntu-22.04",
  "terminal.integrated.profiles.windows": {
    "Ubuntu-22.04": {
      "path": "C:\\Windows\\System32\\wsl.exe",
      "args": ["-d", "Ubuntu-22.04"]
    },
    "PowerShell 7": {
      "path": "C:\\Program Files\\PowerShell\\7\\pwsh.exe",
      "icon": "terminal-powershell"
    },
    "PowerShell": {
      "source": "PowerShell",
      "icon": "terminal-powershell"
    },
    "Command Prompt": {
      "path": "${env:windir}\\System32\\cmd.exe",
      "icon": "terminal-cmd"
    }
  }
}
```

**方案 B：默认 PowerShell 7（偏 .NET / Windows 路径项目，如 panelflow）**

```json
{
  "terminal.integrated.defaultProfile.windows": "PowerShell 7",
  "terminal.integrated.profiles.windows": {
    "PowerShell 7": {
      "path": "C:\\Program Files\\PowerShell\\7\\pwsh.exe",
      "icon": "terminal-powershell"
    },
    "Ubuntu-22.04": {
      "path": "C:\\Windows\\System32\\wsl.exe",
      "args": ["-d", "Ubuntu-22.04"]
    }
  }
}
```

### 7.3 终端内切换 Shell

- 点击终端面板右上角 **+** 旁的下拉箭头 → 选择配置文件
- 或 **Ctrl+Shift+P** → `Terminal: Select Default Profile`

### 7.4 在 WSL 中打开 Windows 项目

```bash
cd /mnt/d/repos/panelflow
code .    # 若已在 WSL 中安装 code/cursor CLI
```

在 Cursor 中：**文件 → 打开文件夹** → 选 `D:\repos\panelflow` 即可，终端可按方案 B 用 PowerShell 7 跑 `dotnet`。

---

## 8. SSH 连接 Ubuntu 服务器

### 8.1 在 WSL 中配置（推荐，与服务器环境一致）

```bash
# 生成密钥（若还没有）
ssh-keygen -t ed25519 -C "your_email@example.com"

# 复制公钥到服务器
ssh-copy-id user@your-server-ip

# 连接
ssh user@your-server-ip
```

**`~/.ssh/config` 示例：**

```
Host prod-web
    HostName 192.168.1.100
    User deploy
    IdentityFile ~/.ssh/id_ed25519
    ServerAliveInterval 60

Host staging
    HostName staging.example.com
    User ubuntu
    IdentityFile ~/.ssh/id_ed25519
```

之后可直接：`ssh prod-web`

### 8.2 在 Windows PowerShell 中配置（可选）

密钥路径：`C:\Users\<用户名>\.ssh\`

PowerShell 7 同样支持 OpenSSH，用法与 Linux 类似：

```powershell
ssh user@your-server-ip
```

### 8.3 WSL 与 Windows 共用 SSH 密钥（可选）

若希望两边用同一套密钥，可在 WSL `~/.ssh/config` 中指向 Windows 密钥：

```
Host *
    IdentityFile /mnt/c/Users/你的Windows用户名/.ssh/id_ed25519
```

注意私钥文件权限：WSL 内私钥应为 `600`。

---

## 9. 日常推荐工作流

```
┌─────────────────────────────────────────────────────────┐
│  Windows Terminal                                        │
│  ┌──────────────────┬──────────────────┐                │
│  │  Tab: Ubuntu     │  Tab: PowerShell 7│                │
│  │  写脚本/git/ssh  │  dotnet build     │                │
│  │  docker (Linux)  │  Windows 服务     │                │
│  └──────────────────┴──────────────────┘                │
└─────────────────────────────────────────────────────────┘
         │                              │
         ▼                              ▼
   WSL2 Ubuntu 22.04              Windows 本机
   （对齐 Ubuntu 服务器）          （.NET / IIS / 本机工具）
```

| 任务 | 推荐环境 |
|------|----------|
| `dotnet build` / 调试 panelflow | PowerShell 7 或 Cursor 默认 PS7 |
| `git`、bash 脚本、Makefile | WSL Ubuntu |
| `ls` / `grep` / `find` 在 Windows 盘 | PowerShell 7 + Coreutils（或 WSL） |
| `ssh` / `rsync` / `scp` 到服务器 | WSL Ubuntu |
| Docker（Linux 容器） | Docker Desktop + WSL2 后端（已装 docker-desktop） |
| 改 Windows 环境变量、服务 | PowerShell 7 |

---

## 10. 安装检查清单

完成配置后，逐项确认：

- [ ] `wsl -l -v` → Ubuntu-22.04 为 **VERSION 2**，且带 `*`
- [ ] `wsl` 能进入 Ubuntu，sudo 可用
- [ ] `pwsh --version` → 显示 PowerShell 7.x
- [ ] （可选）`ls --version` → Coreutils 已安装
- [ ] `wt` 或开始菜单能打开 **Windows Terminal**
- [ ] Terminal 默认标签为 Ubuntu（或按你偏好设置）
- [ ] Cursor 终端能切换 Ubuntu / PowerShell 7
- [ ] `ssh user@server` 能连上 Ubuntu 服务器
- [ ] （可选）`agent --version` Cursor Agent CLI 正常

---

## 11. 常见问题

### Q1：`wsl` 启动慢或卡住

```powershell
wsl --shutdown
# 再重新 wsl
```

检查 Docker Desktop 是否占用过多 WSL 资源；可在 Docker Desktop → Settings → Resources 中限 CPU/内存。

### Q2：WSL 访问 `/mnt/d` 很慢

- 尽量把高频 I/O 项目放在 WSL 家目录 `~/repos`
- 或 Windows 侧用 PowerShell 7 开发，WSL 只做 SSH/脚本

### Q3：`pwsh` 找不到命令

安装 PowerShell 7 后需**新开终端**；若仍不行，检查 PATH 是否包含：

```
C:\Program Files\PowerShell\7\
```

### Q4：Cursor 终端默认仍是 PowerShell 5.1

确认 `settings.json` 中 `defaultProfile` 指向 **PowerShell 7** 或 **Ubuntu-22.04**，且路径正确。

### Q5：`agent` 报 No version directories found

Cursor Agent CLI 启动脚本与版本目录命名不一致时的已知问题。可运行：

```powershell
& "$env:LOCALAPPDATA\cursor-agent\agent.ps1" --version
```

若直接运行正常而 `agent` 失败，检查 `%LOCALAPPDATA%\cursor-agent\versions\` 下目录名格式，或重新在 Cursor 中安装 Agent CLI。

### Q6：WSL 与 Docker Desktop 冲突

确保 Docker Desktop → Settings → General → **Use the WSL 2 based engine** 已勾选；  
Settings → Resources → WSL Integration → 启用 **Ubuntu-22.04**。

### Q7：Coreutils 的 `ls` 在 PowerShell 里仍是别名行为

Coreutils 与 PowerShell 内置别名可能冲突。确认已装 **PowerShell 7.4+**，并新开终端让 PSReadLine 集成生效；仍异常时用 `coreutils-manager disable ls` 或改用 `Get-ChildItem`。复杂管道脚本建议放 WSL 执行。

---

## 12. 本机待办（按优先级）

根据当前环境，建议按顺序完成：

1. **安装 PowerShell 7**（`winget install Microsoft.PowerShell`）
2. **安装 Windows Terminal**（若尚未安装）
3. **（可选）安装 Coreutils for Windows**（`winget install Microsoft.Coreutils`，需先完成第 1 步）
4. **Ubuntu 内** `apt update && apt upgrade`，安装 `git`、`openssh-client` 等
5. **配置 SSH** 密钥与 `~/.ssh/config`
6. **Cursor** `settings.json` 添加终端配置（第 7.2 节，方案 A 或 B）
7. 验证 `dotnet build`（PowerShell 7）与 `ssh`（WSL）均正常

---

## 13. 参考链接

- [Coreutils for Windows（GitHub）](https://github.com/microsoft/coreutils)
- [Coreutils 命令列表（Microsoft Learn）](https://learn.microsoft.com/en-us/windows/core-utils/commands)
- [Build 2026 发布公告](https://blogs.windows.com/windowsdeveloper/2026/06/02/build-2026-furthering-windows-as-the-trusted-platform-for-development/)
- [WSL 官方文档](https://learn.microsoft.com/zh-cn/windows/wsl/)
- [Windows Terminal 文档](https://learn.microsoft.com/zh-cn/windows/terminal/)
- [PowerShell 7 安装](https://learn.microsoft.com/zh-cn/powershell/scripting/install/installing-powershell-on-windows)
- [Cursor 文档 - 集成终端](https://docs.cursor.com/)

---

*文档版本：2026-06-22（含 Coreutils for Windows） | 维护：开发环境变更时同步更新第 2、12 节*
