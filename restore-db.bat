@echo off
chcp 65001 >nul

set DB_NAME=DKZX_MIS_MSSQL
set BAK_FILE=%~dp0DKZX_MIS_MSSQL_FULL_20260522_212133.bak
set SERVER=WINCC02

echo.
echo === SQL Server 数据库还原脚本 (WINCC02) ===
echo 数据库: %DB_NAME%
echo 服务器: %SERVER%
echo 备份文件: %BAK_FILE%
echo.

REM 获取 SQL Server 默认数据目录
echo [1/4] 获取数据文件目录...
for /f "tokens=*" %%i in ('sqlcmd -S %SERVER% -E -h -1 -W -Q "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(260))"') do (
    set "DATA_DIR=%%i"
    goto :got_dir
)
:got_dir
echo 数据目录: %DATA_DIR%
echo.

echo [2/4] 断开现有连接...
sqlcmd -S %SERVER% -E -Q "IF DB_ID('%DB_NAME%') IS NOT NULL ALTER DATABASE [%DB_NAME%] SET SINGLE_USER WITH ROLLBACK IMMEDIATE"
echo.

echo [3/4] 还原数据库（含 MOVE）...
sqlcmd -S %SERVER% -E -Q "RESTORE DATABASE [%DB_NAME%] FROM DISK = '%BAK_FILE%' WITH REPLACE, RECOVERY, MOVE 'DKZX_MIS_MSSQL_Data' TO '%DATA_DIR%%DB_NAME%.mdf', MOVE 'DKZX_MIS_MSSQL_Log' TO '%DATA_DIR%%DB_NAME%_log.ldf'"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo 还原失败，请检查错误信息。
    pause
    exit /b 1
)

echo.
echo [4/4] 设置多用户模式...
sqlcmd -S %SERVER% -E -Q "ALTER DATABASE [%DB_NAME%] SET MULTI_USER"

echo.
echo ============================================================
echo 还原完成! 数据库 [%DB_NAME%] 已可用。
echo ============================================================
pause
