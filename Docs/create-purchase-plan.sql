-- PanelFlow 采购计划表（生产下达 + 采购执行）
USE [DKZX_MIS_MSSQL]
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'PF_PURCHASE_PLAN')
BEGIN
    CREATE TABLE dbo.PF_PURCHASE_PLAN (
        id           INT IDENTITY(1,1) NOT NULL,
        plan_no      VARCHAR(20)  NOT NULL,
        fabh         CHAR(20)     NOT NULL,
        contract_no  VARCHAR(20)  NULL,
        status       SMALLINT     NOT NULL CONSTRAINT DF_PF_PURCHASE_PLAN_status DEFAULT (0),
        creator      VARCHAR(10)  NOT NULL,
        reviewer     VARCHAR(10)  NULL,
        unit_chief   VARCHAR(10)  NULL,
        created_at   DATETIME     NOT NULL CONSTRAINT DF_PF_PURCHASE_PLAN_created_at DEFAULT (GETDATE()),
        issued_at    DATETIME     NULL,
        issued_by    VARCHAR(10)  NULL,
        CONSTRAINT PK_PF_PURCHASE_PLAN PRIMARY KEY CLUSTERED (id),
        CONSTRAINT UQ_PF_PURCHASE_PLAN_plan_no UNIQUE (plan_no)
    )
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'PF_PURCHASE_PLAN_ITEM')
BEGIN
    CREATE TABLE dbo.PF_PURCHASE_PLAN_ITEM (
        id                INT IDENTITY(1,1) NOT NULL,
        plan_id           INT            NOT NULL,
        sort_no           INT            NOT NULL CONSTRAINT DF_PF_PURCHASE_PLAN_ITEM_sort_no DEFAULT (0),
        item_name         NVARCHAR(100)  NOT NULL,
        item_spec         NVARCHAR(200)  NOT NULL,
        item_unit         NVARCHAR(20)   NULL,
        item_qty          DECIMAL(18,4)  NOT NULL,
        item_no_buy_qty   DECIMAL(18,4)  NOT NULL CONSTRAINT DF_PF_PURCHASE_PLAN_ITEM_no_buy DEFAULT (0),
        item_manufacturer NVARCHAR(100)  NULL,
        change_type       SMALLINT       NOT NULL CONSTRAINT DF_PF_PURCHASE_PLAN_ITEM_change_type DEFAULT (0),
        change_remark     NVARCHAR(200)  NULL,
        need_date         DATE           NULL,
        remark            NVARCHAR(200)  NULL,
        has_cert          BIT            NULL,
        has_inspection    BIT            NULL,
        appearance_ok     BIT            NULL,
        has_accessories   BIT            NULL,
        has_documents     BIT            NULL,
        verify_date       DATE           NULL,
        conclusion        NVARCHAR(50)   NULL,
        verifier          VARCHAR(10)    NULL,
        verified_at       DATETIME       NULL,
        CONSTRAINT PK_PF_PURCHASE_PLAN_ITEM PRIMARY KEY CLUSTERED (id),
        CONSTRAINT FK_PF_PURCHASE_PLAN_ITEM_plan FOREIGN KEY (plan_id)
            REFERENCES dbo.PF_PURCHASE_PLAN(id) ON DELETE CASCADE
    )

    CREATE NONCLUSTERED INDEX IX_PF_PURCHASE_PLAN_ITEM_plan
    ON dbo.PF_PURCHASE_PLAN_ITEM (plan_id)
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_PF_PURCHASE_PLAN_fabh' AND object_id = OBJECT_ID(N'dbo.PF_PURCHASE_PLAN'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PF_PURCHASE_PLAN_fabh
    ON dbo.PF_PURCHASE_PLAN (fabh)
END
GO
