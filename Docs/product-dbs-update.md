## 创建审计表(产品库已创建)

## KHYLB 增加字段,初始化值,更新科室数据;
USE [DKZX_MIS_MSSQL]
GO

-- 检查字段是否已存在（避免重复执行报错）
IF NOT EXISTS (
    SELECT 1 FROM sys.columns 
    WHERE object_id = OBJECT_ID('dbo.KHYLB') 
    AND name = 'created_at'
)
BEGIN
    ALTER TABLE [dbo].[KHYLB] ADD [created_at] [datetime] NULL;
    PRINT '已添加 created_at 字段';
END
ELSE
BEGIN
    PRINT 'created_at 字段已存在，跳过';
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns 
    WHERE object_id = OBJECT_ID('dbo.KHYLB') 
    AND name = 'updated_at'
)
BEGIN
    ALTER TABLE [dbo].[KHYLB] ADD [updated_at] [datetime] NULL;
    PRINT '已添加 updated_at 字段';
END
ELSE
BEGIN
    PRINT 'updated_at 字段已存在，跳过';
END
GO

 为历史数据设置默认值（如需要）
UPDATE [dbo].[KHYLB] SET [created_at] = GETDATE() WHERE [created_at] IS NULL;
UPDATE [dbo].[KHYLB] SET [updated_at] = GETDATE() WHERE [updated_at] IS NULL;
-- GO

ALTER TABLE [dbo].[KHYLB] ADD [updated_by] varchar(50) NULL;
ALTER TABLE [dbo].[KHYLB] ADD [created_by] varchar(50) NULL;

PRINT 'KHYLB 表结构更新完成';
GO

### 创建客户联系人表
- 先要创建客户表的主键
ALTER TABLE [dbo].[KHYLB] 
ADD CONSTRAINT [PK_KHYLB] PRIMARY KEY NONCLUSTERED 
(
    [gsbh] ASC,
    [bmmc] ASC
) 
WITH (
    PAD_INDEX = OFF, 
    STATISTICS_NORECOMPUTE = OFF, 
    IGNORE_DUP_KEY = OFF, 
    ALLOW_ROW_LOCKS = ON, 
    ALLOW_PAGE_LOCKS = ON, 
    OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF
) 
ON [PRIMARY];

- 再创建联系人表
USE [DKZX_MIS_MSSQL]
GO

/****** Object:  Table [dbo].[KHYLB_CONTACT]    Script Date: 2026/5/21 16:30:34 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[KHYLB_CONTACT](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[gsbh] [varchar](10) NOT NULL,
	[lxr] [varchar](100) NOT NULL,
	[lxdh] [varchar](40) NULL,
	[email] [varchar](100) NULL,
	[zw] [varchar](50) NULL,
	[is_default] [bit] NOT NULL,
	[sort_no] [int] NOT NULL,
	[is_enabled] [bit] NOT NULL,
	[created_at] [datetime] NOT NULL,
	[updated_at] [datetime] NOT NULL,
 CONSTRAINT [PK_KHYLB_CONTACT] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO

ALTER TABLE [dbo].[KHYLB_CONTACT] ADD  CONSTRAINT [DF_KHYLB_CONTACT_is_default]  DEFAULT ((0)) FOR [is_default]
GO

ALTER TABLE [dbo].[KHYLB_CONTACT] ADD  CONSTRAINT [DF_KHYLB_CONTACT_sort_no]  DEFAULT ((100)) FOR [sort_no]
GO

ALTER TABLE [dbo].[KHYLB_CONTACT] ADD  CONSTRAINT [DF_KHYLB_CONTACT_is_enabled]  DEFAULT ((1)) FOR [is_enabled]
GO

ALTER TABLE [dbo].[KHYLB_CONTACT] ADD  CONSTRAINT [DF_KHYLB_CONTACT_created_at]  DEFAULT (getdate()) FOR [created_at]
GO

ALTER TABLE [dbo].[KHYLB_CONTACT] ADD  CONSTRAINT [DF_KHYLB_CONTACT_updated_at]  DEFAULT (getdate()) FOR [updated_at]
GO

ALTER TABLE [dbo].[KHYLB_CONTACT]  WITH CHECK ADD  CONSTRAINT [FK_KHYLB_CONTACT_KHYLB] FOREIGN KEY([gsbh])
REFERENCES [dbo].[KHYLB] ([gsbh])
GO

ALTER TABLE [dbo].[KHYLB_CONTACT] CHECK CONSTRAINT [FK_KHYLB_CONTACT_KHYLB]
GO


### 创建历史价格表 STD_PRICE_HISTORY
### 创建存储过程，生成历史价格表
