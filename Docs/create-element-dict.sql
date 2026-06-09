-- 通用项字典表：报价单三级(控制柜/单元属性/元件)的标准补充项主数据。
-- 新建独立表，不影响历史 PB 表结构。
IF OBJECT_ID(N'dbo.STD_ELEMENT_DICT', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.STD_ELEMENT_DICT
    (
        Id                INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Level]           TINYINT       NOT NULL,                 -- 1/2/3：对应 x_bm 的 4/8/12 位
        Name              NVARCHAR(50)  NOT NULL,                 -- 写入 BJB.x_mc
        Xlx               INT           NOT NULL,                 -- 写入 BJB.x_lx；对齐汇总槽位(稳定身份)
        Amount            DECIMAL(18,2) NOT NULL CONSTRAINT DF_STD_ELEMENT_DICT_Amount DEFAULT(1), -- 写入 BJB.x_sl
        Ggxh              NVARCHAR(50)  NULL,                     -- 写入 BJB.x_ggxh(如散热风机 200mm)，空则不写
        DefaultUnit       NVARCHAR(10)  NULL,                     -- 写入 BJB.x_dw
        TargetParentScope NVARCHAR(8)   NULL,                     -- L3 专用：挂到哪个 8 位分类，默认 '0001'(器件)
        SortOrder         INT           NOT NULL CONSTRAINT DF_STD_ELEMENT_DICT_SortOrder DEFAULT(0),
        IsDefaultOnImport BIT           NOT NULL CONSTRAINT DF_STD_ELEMENT_DICT_IsDefaultOnImport DEFAULT(0),
        IsEnabled         BIT           NOT NULL CONSTRAINT DF_STD_ELEMENT_DICT_IsEnabled DEFAULT(1),
        IsLocked          BIT           NOT NULL CONSTRAINT DF_STD_ELEMENT_DICT_IsLocked DEFAULT(0),
        Remark            NVARCHAR(300) NULL,
        UpdatedAt         DATETIME2     NOT NULL CONSTRAINT DF_STD_ELEMENT_DICT_UpdatedAt DEFAULT(SYSDATETIME()),
        UpdatedBy         NVARCHAR(50)  NULL
    );

    CREATE INDEX IX_STD_ELEMENT_DICT_Level_Sort ON dbo.STD_ELEMENT_DICT([Level], SortOrder);
END;
GO

-- 已有表补充 Amount 列（幂等）。
IF COL_LENGTH(N'dbo.STD_ELEMENT_DICT', N'Amount') IS NULL
BEGIN
    ALTER TABLE dbo.STD_ELEMENT_DICT
        ADD Amount DECIMAL(18,2) NOT NULL CONSTRAINT DF_STD_ELEMENT_DICT_Amount DEFAULT(1);
END;
GO

-- 第 2 级 8 类种子(与现状等价：前 5 类导入默认写入，器件锁定首位)。
-- 仅在表内尚无第 2 级数据时插入，避免重复。
IF NOT EXISTS (SELECT 1 FROM dbo.STD_ELEMENT_DICT WHERE [Level] = 2)
BEGIN
    INSERT INTO dbo.STD_ELEMENT_DICT ([Level], Name, Xlx, Amount, SortOrder, IsDefaultOnImport, IsEnabled, IsLocked, Remark)
    VALUES
        (2, N'器件',   1,  1, 1, 1, 1, 1, N'固定首位，其下挂第3级元件明细'),
        (2, N'辅料',   12, 1, 2, 1, 1, 0, NULL),
        (2, N'壳体',   13, 1, 3, 1, 1, 0, NULL),
        (2, N'安装',   14, 1, 4, 1, 1, 0, NULL),
        (2, N'包装',   15, 1, 5, 1, 1, 0, NULL),
        (2, N'唛头',   16, 1, 6, 0, 1, 0, N'国外发货时使用'),
        (2, N'抽真空', 17, 1, 7, 0, 1, 0, N'国外发货时使用'),
        (2, N'干燥剂', 18, 1, 8, 0, 1, 0, N'国外发货时使用');
END;
GO

-- 第 1 级扩展项种子(运费/保费/侧板等；导入时不自动写入，由结构维护页挂入)。
IF NOT EXISTS (SELECT 1 FROM dbo.STD_ELEMENT_DICT WHERE [Level] = 1)
BEGIN
    INSERT INTO dbo.STD_ELEMENT_DICT ([Level], Name, Xlx, Amount, SortOrder, IsDefaultOnImport, IsEnabled, IsLocked, Remark)
    VALUES
        (1, N'运费', 1, 1, 1, 0, 1, 0, N'顶层费用项'),
        (1, N'保费', 1, 1, 2, 0, 1, 0, N'顶层费用项'),
        (1, N'侧板', 1, 1, 3, 0, 1, 0, NULL),
        (1, N'备件', 1, 1, 4, 0, 1, 0, NULL),
        (1, N'附件', 1, 1, 5, 0, 1, 0, NULL);
END;
GO
