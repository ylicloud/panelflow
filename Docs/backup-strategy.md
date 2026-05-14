# 数据库备份方案

## 目标

支持任意时刻的数据恢复（Point-in-Time Recovery, PITR），在数据库损坏、误操作等场景下可将数据库恢复到任意历史时间点。

---

## 基础信息

| 项目 | 值 |
|------|-----|
| 服务器 | hp1 (192.168.8.3) |
| 操作系统 | Ubuntu 22.04 |
| 数据库引擎 | SQL Server 2022 |
| 数据库名称 | DKZX_MIS_MSSQL |
| 当前大小 | ~7 GB |
| 每日增量 | ~几十 MB |

---

## 方案概述

SQL Server 的时间点恢复依赖三种备份类型的组合：

```
完整备份（Full）
    └── 差异备份（Differential）
            └── 事务日志备份（Log）× N
```

恢复到任意时刻 = 最近一次完整备份 + 最近一次差异备份 + 中间所有事务日志备份。

### 备份策略

| 备份类型 | 执行频率 | 保留期 | 预估单次大小 |
|----------|---------|--------|------------|
| 完整备份 | 每周日 02:00 | 保留 4 周 | ~3–4 GB（含压缩） |
| 差异备份 | 每天 02:00（周日除外） | 保留 2 周 | ~100–500 MB |
| 事务日志备份 | 每 15 分钟 | 保留 7 天 | ~5–50 MB/次 |

> **前提条件**：数据库必须设置为 **FULL 恢复模式**，否则事务日志会自动截断，无法做时间点恢复。

---

## 第一步：设置完整恢复模式

在 SQL Server 中执行以下 T-SQL（只需执行一次）：

```sql
USE master;
ALTER DATABASE DKZX_MIS_MSSQL SET RECOVERY FULL;
GO

-- 立即做一次完整备份，激活日志链
BACKUP DATABASE DKZX_MIS_MSSQL
TO DISK = '/var/opt/mssql/backup/DKZX_MIS_MSSQL_init.bak'
WITH COMPRESSION, STATS = 10;
GO
```

验证当前恢复模式：

```sql
SELECT name, recovery_model_desc FROM sys.databases WHERE name = 'DKZX_MIS_MSSQL';
```

---

## 第二步：创建备份目录

在 hp1 服务器上执行：

```bash
sudo mkdir -p /var/opt/mssql/backup/{full,diff,log}
sudo chown -R mssql:mssql /var/opt/mssql/backup
sudo chmod -R 750 /var/opt/mssql/backup
```

---

## 第三步：部署备份脚本

### 3.1 完整备份脚本

创建文件 `/opt/mssql-backup/backup_full.sh`：

```bash
#!/bin/bash
# 完整备份脚本

DB_NAME="DKZX_MIS_MSSQL"
SA_PASSWORD="666666"
BACKUP_DIR="/var/opt/mssql/backup/full"
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
BACKUP_FILE="${BACKUP_DIR}/${DB_NAME}_FULL_${TIMESTAMP}.bak"
LOG_FILE="/var/log/mssql-backup/backup_full.log"
RETENTION_DAYS=28  # 保留 4 周

mkdir -p "$(dirname "$LOG_FILE")"
echo "[$(date '+%Y-%m-%d %H:%M:%S')] 开始完整备份: $BACKUP_FILE" >> "$LOG_FILE"

/opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$SA_PASSWORD" -C \
  -No \
  -Q "BACKUP DATABASE [$DB_NAME]
      TO DISK = N'$BACKUP_FILE'
      WITH COMPRESSION, CHECKSUM, STATS = 10;
      GO" \
  >> "$LOG_FILE" 2>&1

EXIT_CODE=$?
if [ $EXIT_CODE -eq 0 ]; then
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] 完整备份成功: $BACKUP_FILE ($(du -sh "$BACKUP_FILE" | cut -f1))" >> "$LOG_FILE"
else
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] 完整备份失败！退出码: $EXIT_CODE" >> "$LOG_FILE"
    # 发送告警（见第六步）
fi

# 清理过期备份
find "$BACKUP_DIR" -name "*.bak" -mtime +$RETENTION_DAYS -delete
echo "[$(date '+%Y-%m-%d %H:%M:%S')] 已清理 ${RETENTION_DAYS} 天前的完整备份" >> "$LOG_FILE"
```

### 3.2 差异备份脚本

创建文件 `/opt/mssql-backup/backup_diff.sh`：

```bash
#!/bin/bash
# 差异备份脚本

DB_NAME="DKZX_MIS_MSSQL"
SA_PASSWORD="666666"
BACKUP_DIR="/var/opt/mssql/backup/diff"
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
BACKUP_FILE="${BACKUP_DIR}/${DB_NAME}_DIFF_${TIMESTAMP}.bak"
LOG_FILE="/var/log/mssql-backup/backup_diff.log"
RETENTION_DAYS=14  # 保留 2 周

mkdir -p "$(dirname "$LOG_FILE")"
echo "[$(date '+%Y-%m-%d %H:%M:%S')] 开始差异备份: $BACKUP_FILE" >> "$LOG_FILE"

/opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$SA_PASSWORD" \
  -No \
  -Q "BACKUP DATABASE [$DB_NAME]
      TO DISK = N'$BACKUP_FILE'
      WITH DIFFERENTIAL, COMPRESSION, CHECKSUM, STATS = 10;
      GO" \
  >> "$LOG_FILE" 2>&1

EXIT_CODE=$?
if [ $EXIT_CODE -eq 0 ]; then
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] 差异备份成功: $BACKUP_FILE ($(du -sh "$BACKUP_FILE" | cut -f1))" >> "$LOG_FILE"
else
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] 差异备份失败！退出码: $EXIT_CODE" >> "$LOG_FILE"
fi

find "$BACKUP_DIR" -name "*.bak" -mtime +$RETENTION_DAYS -delete
```

### 3.3 事务日志备份脚本

创建文件 `/opt/mssql-backup/backup_log.sh`：

```bash
#!/bin/bash
# 事务日志备份脚本

DB_NAME="DKZX_MIS_MSSQL"
SA_PASSWORD="666666"
BACKUP_DIR="/var/opt/mssql/backup/log"
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
BACKUP_FILE="${BACKUP_DIR}/${DB_NAME}_LOG_${TIMESTAMP}.bak"
LOG_FILE="/var/log/mssql-backup/backup_log.log"
RETENTION_DAYS=7  # 保留 7 天

mkdir -p "$(dirname "$LOG_FILE")"

/opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$SA_PASSWORD" \
  -No \
  -Q "BACKUP LOG [$DB_NAME]
      TO DISK = N'$BACKUP_FILE'
      WITH COMPRESSION, CHECKSUM;
      GO" \
  >> "$LOG_FILE" 2>&1

EXIT_CODE=$?
if [ $EXIT_CODE -ne 0 ]; then
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] 日志备份失败！退出码: $EXIT_CODE" >> "$LOG_FILE"
fi

find "$BACKUP_DIR" -name "*.bak" -mtime +$RETENTION_DAYS -delete
```

赋予执行权限：

```bash
sudo mkdir -p /opt/mssql-backup
sudo chmod +x /opt/mssql-backup/backup_full.sh
sudo chmod +x /opt/mssql-backup/backup_diff.sh
sudo chmod +x /opt/mssql-backup/backup_log.sh
```

---

## 第四步：配置 Cron 定时任务

以 root 用户编辑 crontab：

```bash
sudo crontab -e
```

添加以下内容：

```cron
# SQL Server 备份任务
# 每周日 02:00 执行完整备份
0 2 * * 0 /opt/mssql-backup/backup_full.sh

# 周一到周六 02:00 执行差异备份
0 2 * * 1-6 /opt/mssql-backup/backup_diff.sh

# 每 15 分钟执行一次事务日志备份
*/15 * * * * /opt/mssql-backup/backup_log.sh
```

---

## 第五步：配置异地备份（强烈建议）

本地磁盘损坏时备份也会丢失，必须将备份同步到另一台机器。

### 方案 A：同步到测试服务器 hp3

```bash
# 安装 rsync（如果未安装）
sudo apt install rsync -y

# 配置 SSH 免密登录
ssh-keygen -t ed25519 -C "mssql-backup"
ssh-copy-id root@192.168.8.7
```

创建异地同步脚本 `/opt/mssql-backup/sync_remote.sh`：

```bash
#!/bin/bash
# 将备份同步到 hp3

BACKUP_SRC="/var/opt/mssql/backup"
REMOTE_DST="root@192.168.8.7:/mnt/backup/hp1-mssql"
LOG_FILE="/var/log/mssql-backup/sync_remote.log"

echo "[$(date '+%Y-%m-%d %H:%M:%S')] 开始同步备份到 hp3..." >> "$LOG_FILE"

rsync -avz --delete \
  --exclude="*.tmp" \
  "$BACKUP_SRC/" "$REMOTE_DST/" \
  >> "$LOG_FILE" 2>&1

echo "[$(date '+%Y-%m-%d %H:%M:%S')] 同步完成，退出码: $?" >> "$LOG_FILE"
```

在 crontab 中添加每小时同步：

```cron
# 每小时整点同步备份到 hp3
5 * * * * /opt/mssql-backup/sync_remote.sh
```

### 方案 B：挂载 NAS/SMB 共享目录

```bash
# 挂载 NAS
sudo mount -t cifs //nas-server/backup /mnt/nas-backup \
  -o username=backupuser,password=xxx,uid=mssql,gid=mssql

# 或写入 /etc/fstab 实现开机自动挂载
```

---

## 第六步：备份验证（每月执行）

备份文件存在不等于可以恢复，必须定期验证：

```sql
-- 验证备份文件完整性（不实际恢复，只校验 CHECKSUM）
RESTORE VERIFYONLY
FROM DISK = '/var/opt/mssql/backup/full/DKZX_MIS_MSSQL_FULL_20260417_020000.bak'
WITH CHECKSUM;
```

---

## 第七步：恢复操作（发生故障时）

### 场景：恢复到 2026-04-17 14:30:00

**步骤 1**：找到所需的备份文件

```bash
# 查看可用的完整备份
ls -lh /var/opt/mssql/backup/full/

# 查看差异备份（选最近一次，且在目标时间之前）
ls -lh /var/opt/mssql/backup/diff/

# 查看日志备份（选目标时间之前的所有文件）
ls -lh /var/opt/mssql/backup/log/
```

**步骤 2**：断开所有连接，开始恢复

```sql

-- 强制断开所有连接
ALTER DATABASE DKZX_MIS_MSSQL SET SINGLE_USER WITH ROLLBACK IMMEDIATE;

-- 第 1 步：还原完整备份（不恢复，WITH NORECOVERY）
RESTORE DATABASE DKZX_MIS_MSSQL
FROM DISK = '/var/opt/mssql/backup/full/DKZX_MIS_MSSQL_FULL_20260413_020000.bak'
WITH NORECOVERY, REPLACE, STATS = 10;

-- 第 2 步：还原最近的差异备份（不恢复）
RESTORE DATABASE DKZX_MIS_MSSQL
FROM DISK = '/var/opt/mssql/backup/diff/DKZX_MIS_MSSQL_DIFF_20260417_020000.bak'
WITH NORECOVERY, STATS = 10;

-- 第 3 步：逐一还原日志备份，直到目标时间点之前的最后一个（不恢复）
RESTORE LOG DKZX_MIS_MSSQL
FROM DISK = '/var/opt/mssql/backup/log/DKZX_MIS_MSSQL_LOG_20260417_120000.bak'
WITH NORECOVERY;

RESTORE LOG DKZX_MIS_MSSQL
FROM DISK = '/var/opt/mssql/backup/log/DKZX_MIS_MSSQL_LOG_20260417_121500.bak'
WITH NORECOVERY;

-- ... 继续还原更多日志文件 ...

-- 第 4 步：还原最后一个包含目标时间点的日志文件，指定 STOPAT 时间点（WITH RECOVERY 完成恢复）
RESTORE LOG DKZX_MIS_MSSQL
FROM DISK = '/var/opt/mssql/backup/log/DKZX_MIS_MSSQL_LOG_20260417_143000.bak'
WITH RECOVERY, STOPAT = '2026-04-17 14:30:00';

-- 第 5 步：恢复多用户模式
ALTER DATABASE DKZX_MIS_MSSQL SET MULTI_USER;


-- 以测试服务器hp3为例
-- 登录数据库
sqlcmd -U sa -P sa -C
-- 强制断开所有连接
ALTER DATABASE DKZX_MIS_MSSQL SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
-- 恢复数据库
RESTORE DATABASE DKZX_MIS_MSSQL
FROM DISK = '/mnt/backup/hp1-mssql/full/DKZX_MIS_MSSQL_FULL_20260426_020001.bak'
WITH NORECOVERY, REPLACE, STATS = 10;
-- 恢复差异备份(不再恢复日志,到此为止,恢复完成)
RESTORE DATABASE DKZX_MIS_MSSQL
FROM DISK = '/mnt/backup/hp1-mssql/diff/DKZX_MIS_MSSQL_DIFF_20260428_020001.bak'
WITH RECOVERY, STATS = 10;
```
---

## 磁盘空间估算

| 备份类型 | 单次大小 | 保留数量 | 合计 |
|----------|---------|---------|------|
| 完整备份 | ~3.5 GB | 4 个 | ~14 GB |
| 差异备份 | ~300 MB | 14 个 | ~4 GB |
| 事务日志 | ~20 MB/次 × 96次/天 | 7天 | ~13 GB |
| **总计** | | | **~31 GB** |

> 建议备份磁盘空间 ≥ **60 GB**（预留 2 倍余量）。

---

## 注意事项

1. **密码安全**：生产环境中不要将 `sa` 密码明文写在脚本里，建议使用环境变量或 `.sqlpass` 文件（chmod 600）。
2. **日志链不能断**：不要手动将数据库切换为 SIMPLE 恢复模式，否则日志链断裂，之前的日志备份将无法用于时间点恢复。
3. **taillog 备份**：发生故障后，如果日志文件未损坏，在开始恢复前先做一次 `BACKUP LOG ... WITH NO_TRUNCATE` 尾日志备份，可以最大化减少数据丢失。
4. **sqlcmd 路径**：Ubuntu 上 mssql-tools 的路径可能是 `/opt/mssql-tools/bin/sqlcmd` 或 `/opt/mssql-tools18/bin/sqlcmd`，需根据实际安装版本调整。
5. **测试恢复**：每季度在 hp3 测试服务器上做一次完整的恢复演练，确保备份真实可用。
