USE [DKZX_MIS_MSSQL]
GO
/****** Object:  UserDefinedFunction [dbo].[F_CleanString]    Script Date: 2026/5/1 21:15:48 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
ALTER FUNCTION [dbo].[F_CleanString] (@inputString NVARCHAR(400))
RETURNS NVARCHAR(400)
AS  
BEGIN  
	-- author: zhao 2026-03-23  modified: 2026-05-01
    -- 如果输入为 NULL 或空，直接返回 NULL
    IF @inputString IS NULL OR LEN(@inputString) = 0
        RETURN NULL;
    
    DECLARE @result NVARCHAR(400) = LOWER(@inputString);
    -- 删除常见不可见字符，避免"看起来相同但比较不等"
    SET @result = REPLACE(@result, CHAR(13), '');
    SET @result = REPLACE(@result, CHAR(10), '');
    SET @result = REPLACE(@result, CHAR(9), '');
    SET @result = REPLACE(@result, NCHAR(160), '');   -- 不间断空格
    SET @result = REPLACE(@result, NCHAR(8203), '');  -- 零宽空格

    DECLARE @i INT = 1;
    DECLARE @currentChar NCHAR(1);
    DECLARE @cleanResult NVARCHAR(400) = '';
    DECLARE @parenDepth INT = 0;  -- 标记括号嵌套层级
    DECLARE @charCode INT;
    DECLARE @nextCloseHalf INT;
    DECLARE @nextCloseFull INT;
    
    -- 定义所有需要过滤的符号（不包括括号，因为括号单独处理）
    -- 修复：补充 ASCII 双引号 "（原版遗漏）
    DECLARE @symbols NVARCHAR(200) = 
        N'`~!@#$%^&*-_=+{}[]\|;:''",<>?/ ' 
        + N'▲▼◆■●◎▄▅▇▓█▓★〓【】《》''""·—…';
    
    -- 逐个字符处理
    WHILE @i <= LEN(@result)
    BEGIN
        SET @currentChar = SUBSTRING(@result, @i, 1);
        SET @charCode = UNICODE(@currentChar);

        -- 全角转半角（全角空格 U+3000、全角 ASCII U+FF01~U+FF5E）
        -- 注：全角括号（U+FF08/U+FF09）在此处已被转为半角 ( )，
        --     后续括号判断只需检测半角字符即可。
        IF @charCode = 12288
            SET @currentChar = N' ';
        ELSE IF @charCode BETWEEN 65281 AND 65374
            SET @currentChar = NCHAR(@charCode - 65248);

        -- 转换后再次统一小写，避免全角大写转半角后残留大写
        SET @currentChar = LOWER(@currentChar);
        
        -- 处理括号：进入或退出括号区域（支持嵌套）
        -- 修复：移除原版中永远不会命中的全角括号条件（全角已在上方转为半角）
        IF @currentChar = '('
        BEGIN
            -- 只有存在后续右括号时才进入"忽略括号内容"模式，
            -- 避免孤立左括号导致后续内容全部被误删。
            -- 注意：此处在 @result（原始串）中向前查找，
            --       因为全角右括号尚未在 @result 中被转换。
            SET @nextCloseHalf = CHARINDEX(')', @result, @i + 1);
            SET @nextCloseFull = CHARINDEX(N'）', @result, @i + 1);

            IF @nextCloseHalf > 0 OR @nextCloseFull > 0
                SET @parenDepth = @parenDepth + 1;
            -- 若无匹配右括号，@parenDepth 不变，本字符直接丢弃（不加入结果）
        END
        ELSE IF @currentChar = ')'
        BEGIN
            IF @parenDepth > 0
                SET @parenDepth = @parenDepth - 1;  -- 退出当前层括号
            -- 孤立右括号（depth=0）直接丢弃，不影响后续字符
        END
        -- 不在括号内才保留字符
        ELSE IF @parenDepth = 0
        BEGIN
            -- 保留字母、数字
            -- 修复：中文字符上限从 U+9FA5（龥）扩展到 U+9FFF，覆盖现代 Unicode CJK 基本区
            IF (@currentChar BETWEEN 'a' AND 'z') 
               OR (@currentChar BETWEEN '0' AND '9')
               OR (@charCode BETWEEN 19968 AND 40959)  -- U+4E00~U+9FFF 保留中文
            BEGIN
                SET @cleanResult = @cleanResult + @currentChar;
            END
            -- 如果不是符号列表中的字符，也保留（如 μ、Ω、°等单位符号）
            ELSE IF CHARINDEX(@currentChar, @symbols) = 0
            BEGIN
                SET @cleanResult = @cleanResult + @currentChar;
            END
        END
        -- 括号内的字符全部忽略，不做任何处理
        
        SET @i = @i + 1;
    END
    
    -- 如果结果为空，返回 NULL
    IF LEN(@cleanResult) = 0
        RETURN NULL;
    
    RETURN @cleanResult;  
END;
