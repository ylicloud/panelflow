using Microsoft.EntityFrameworkCore;
using PanelFlow.Infrastructure.Entities;

namespace PanelFlow.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<SysUser> SysUsers { get; set; }
    public DbSet<SysAuditLog> SysAuditLogs { get; set; }
    public DbSet<XmylbContract> XmylbContracts { get; set; }
    public DbSet<KhylbCustomer> KhylbCustomers { get; set; }
    public DbSet<KhylbCustomerContact> KhylbCustomerContacts { get; set; }
    public DbSet<BjfatQuotation> BjfatQuotations { get; set; }
    public DbSet<BjbItem> BjbItems { get; set; }
    public DbSet<StdPriceHistory> StdPriceHistories { get; set; }
    public DbSet<StdPriceExclusion> StdPriceExclusions { get; set; }
    public DbSet<StdElementDict> StdElementDicts { get; set; }
    public DbSet<PurchasePlan> PurchasePlans { get; set; }
    public DbSet<PurchasePlanItem> PurchasePlanItems { get; set; }
    public DbSet<BjbXmyjhzItem> BjbXmyjhzItems { get; set; }
    public DbSet<BjbHzbItem> BjbHzbItems { get; set; }
    public DbSet<BjbXmyjbItem> BjbXmyjbItems { get; set; }
    public DbSet<BjbXmhzItem> BjbXmhzItems { get; set; }
    public DbSet<BjhzbCategoryItem> BjhzbCategoryItems { get; set; }
    public DbSet<BjdBzbItem> BjdBzbItems { get; set; }
    public DbSet<BjdWybItem> BjdWybItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SysUser>(entity =>
        {
            entity.ToTable("YHQXGL");
            entity.HasKey(e => e.yhmcc);

            entity.Property(e => e.yhmcc).HasColumnName("yhmcc").HasColumnType("char(10)").IsRequired();
            entity.Property(e => e.yhname).HasColumnName("yhname").HasColumnType("char(20)").IsRequired();
            entity.Property(e => e.kl).HasColumnName("kl").HasColumnType("char(20)").IsRequired();
            entity.Property(e => e.yhlx).HasColumnName("yhlx");
            entity.Property(e => e.syxz).HasColumnName("syxz");
            entity.Property(e => e.beizhu).HasColumnName("beizhu").HasColumnType("varchar(100)");
            entity.Property(e => e.bmbm).HasColumnName("bmbm").HasColumnType("char(10)");
            entity.Property(e => e.kzhdrq).HasColumnName("kzhdrq").HasColumnType("smalldatetime");
            entity.Property(e => e.zgyhbh).HasColumnName("zgyhbh").HasColumnType("char(10)");
            entity.Property(e => e.yhbh).HasColumnName("yhbh").HasColumnType("char(10)");
            entity.Property(e => e.js).HasColumnName("js");
            entity.Property(e => e.IsEnabled).HasColumnName("IsEnabled");
            entity.Property(e => e.LastLogin).HasColumnName("LastLogin").HasColumnType("datetime");
        });

        modelBuilder.Entity<SysAuditLog>(entity =>
        {
            entity.ToTable("SYS_AUDIT_LOG");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("Id").ValueGeneratedOnAdd();
            entity.Property(e => e.ActionType).HasColumnName("ActionType").HasColumnType("nvarchar(50)").IsRequired();
            entity.Property(e => e.Module).HasColumnName("Module").HasColumnType("nvarchar(100)").IsRequired();
            entity.Property(e => e.EntityName).HasColumnName("EntityName").HasColumnType("nvarchar(100)");
            entity.Property(e => e.EntityId).HasColumnName("EntityId").HasColumnType("nvarchar(100)");
            entity.Property(e => e.UserName).HasColumnName("UserName").HasColumnType("nvarchar(50)").IsRequired();
            entity.Property(e => e.DisplayName).HasColumnName("DisplayName").HasColumnType("nvarchar(50)");
            entity.Property(e => e.RoleName).HasColumnName("RoleName").HasColumnType("nvarchar(50)");
            entity.Property(e => e.ClientIp).HasColumnName("ClientIp").HasColumnType("varchar(45)");
            entity.Property(e => e.UserAgent).HasColumnName("UserAgent").HasColumnType("nvarchar(500)");
            entity.Property(e => e.IsSuccess).HasColumnName("IsSuccess");
            entity.Property(e => e.ErrorMessage).HasColumnName("ErrorMessage").HasColumnType("nvarchar(1000)");
            entity.Property(e => e.BeforeData).HasColumnName("BeforeData").HasColumnType("nvarchar(max)");
            entity.Property(e => e.AfterData).HasColumnName("AfterData").HasColumnType("nvarchar(max)");
            entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt").HasColumnType("datetime");
        });

        modelBuilder.Entity<XmylbContract>(entity =>
        {
            entity.ToTable("XMYLB");
            entity.HasKey(e => e.xmbh);

            entity.Property(e => e.xmbh).HasColumnName("xmbh").HasColumnType("varchar(20)").IsRequired();
            entity.Property(e => e.xmmc).HasColumnName("xmmc").HasColumnType("varchar(50)").IsRequired();
            entity.Property(e => e.xmdh).HasColumnName("xmdh").HasColumnType("char(20)").IsRequired();
            entity.Property(e => e.hth_1).HasColumnName("hth_1").HasColumnType("varchar(10)");
            entity.Property(e => e.qdsj).HasColumnName("qdsj").HasColumnType("smalldatetime");
            entity.Property(e => e.fzr).HasColumnName("fzr").HasColumnType("varchar(10)").IsRequired();
            entity.Property(e => e.htlx).HasColumnName("htlx");
            entity.Property(e => e.htnr).HasColumnName("htnr").HasColumnType("varchar(100)").IsRequired();
            entity.Property(e => e.jhsj).HasColumnName("jhsj").HasColumnType("smalldatetime");
            entity.Property(e => e.zhtje).HasColumnName("zhtje").HasColumnType("decimal(18,4)");
            entity.Property(e => e.dkjl).HasColumnName("dkjl").HasColumnType("char(200)").IsRequired();
            entity.Property(e => e.ht_zdk).HasColumnName("ht_zdk").HasColumnType("decimal(18,4)");
            entity.Property(e => e.ht_bfcb).HasColumnName("ht_bfcb").HasColumnType("decimal(18,4)");
            entity.Property(e => e.wgcz).HasColumnName("wgcz").HasColumnType("decimal(18,4)");
            entity.Property(e => e.sffh).HasColumnName("sffh").HasColumnType("char(10)").IsRequired();
            entity.Property(e => e.sfkfp).HasColumnName("sfkfp").HasColumnType("char(10)").IsRequired();
            entity.Property(e => e.khbh).HasColumnName("khbh").HasColumnType("varchar(10)").IsRequired();
            entity.Property(e => e.qydw).HasColumnName("qydw").HasColumnType("varchar(10)").IsRequired();
            entity.Property(e => e.bjd_fabh).HasColumnName("bjd_fabh").HasColumnType("varchar(20)");
            entity.Property(e => e.dqzt).HasColumnName("dqzt");
            entity.Property(e => e.xgcs).HasColumnName("xgcs").HasColumnType("char(10)").IsRequired();
            entity.Property(e => e.beizhu).HasColumnName("beizhu").HasColumnType("varchar(200)");
            entity.Property(e => e.htqj_1).HasColumnName("htqj_1");
            entity.Property(e => e.htqj_2).HasColumnName("htqj_2");
            entity.Property(e => e.htqj_3).HasColumnName("htqj_3");
            entity.Property(e => e.htqj_4).HasColumnName("htqj_4");
            entity.Property(e => e.htqj_5).HasColumnName("htqj_5");
            entity.Property(e => e.htqj_6).HasColumnName("htqj_6");
            entity.Property(e => e.xmlx).HasColumnName("xmlx");
        });

        modelBuilder.Entity<KhylbCustomer>(entity =>
        {
            entity.ToTable("KHYLB");
            entity.HasKey(e => e.gsbh);

            entity.Property(e => e.gsmc).HasColumnName("gsmc").HasColumnType("varchar(50)").IsRequired();
            entity.Property(e => e.gsbh).HasColumnName("gsbh").HasColumnType("varchar(10)").IsRequired();
            entity.Property(e => e.gsld).HasColumnName("gsld").HasColumnType("varchar(10)").IsRequired();
            entity.Property(e => e.lxr).HasColumnName("lxr").HasColumnType("varchar(100)").IsRequired();
            entity.Property(e => e.lxdh).HasColumnName("lxdh").HasColumnType("varchar(40)").IsRequired();
            entity.Property(e => e.created_at).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.updated_at).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.beizhu).HasColumnName("beizhu").HasColumnType("varchar(100)").IsRequired();
        });

        modelBuilder.Entity<KhylbCustomerContact>(entity =>
        {
            entity.ToTable("KHYLB_CONTACT");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("Id").ValueGeneratedOnAdd();
            entity.Property(e => e.gsbh).HasColumnName("gsbh").HasColumnType("varchar(10)").IsRequired();
            entity.Property(e => e.lxr).HasColumnName("lxr").HasColumnType("varchar(100)").IsRequired();
            entity.Property(e => e.lxdh).HasColumnName("lxdh").HasColumnType("varchar(40)");
            entity.Property(e => e.email).HasColumnName("email").HasColumnType("varchar(100)");
            entity.Property(e => e.zw).HasColumnName("zw").HasColumnType("varchar(50)");
            entity.Property(e => e.is_default).HasColumnName("is_default");
            entity.Property(e => e.sort_no).HasColumnName("sort_no");
            entity.Property(e => e.is_enabled).HasColumnName("is_enabled");
            entity.Property(e => e.created_at).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.updated_at).HasColumnName("updated_at").HasColumnType("datetime");
        });

        modelBuilder.Entity<BjfatQuotation>(entity =>
        {
            entity.ToTable("BJFAT");
            entity.HasKey(e => e.fabh);

            entity.Property(e => e.fabh).HasColumnName("fabh").HasColumnType("char(20)").IsRequired();
            entity.Property(e => e.fasj).HasColumnName("fasj").HasColumnType("smalldatetime");
            entity.Property(e => e.famc).HasColumnName("famc").HasColumnType("char(50)");
            entity.Property(e => e.famxbh).HasColumnName("famxbh").HasColumnType("numeric(12,0)");
            entity.Property(e => e.bjr).HasColumnName("bjr").HasColumnType("char(10)");
            entity.Property(e => e.bz).HasColumnName("bz").HasColumnType("varchar(50)");
            entity.Property(e => e.khbh).HasColumnName("khbh").HasColumnType("char(10)");
            entity.Property(e => e.falx).HasColumnName("falx").HasColumnType("numeric(18,2)");
            entity.Property(e => e.dqzt).HasColumnName("dqzt");
        });

        modelBuilder.Entity<BjbItem>(entity =>
        {
            entity.ToTable("BJB");
            entity.HasNoKey();
            entity.Property(e => e.fabh).HasColumnName("fabh").HasColumnType("char(20)");
            entity.Property(e => e.x_bm).HasColumnName("x_bm").HasColumnType("char(100)");
            entity.Property(e => e.x_mc).HasColumnName("x_mc").HasColumnType("char(50)");
            entity.Property(e => e.x_ggxh).HasColumnName("x_ggxh").HasColumnType("char(50)");
            entity.Property(e => e.x_dw).HasColumnName("x_dw").HasColumnType("char(10)");
            entity.Property(e => e.x_dj).HasColumnName("x_dj").HasColumnType("numeric(18,4)");
            entity.Property(e => e.x_bj_dj).HasColumnName("x_bj_dj").HasColumnType("numeric(18,4)");
            entity.Property(e => e.x_sl).HasColumnName("x_sl").HasColumnType("numeric(18,4)");
            entity.Property(e => e.x_fdds).HasColumnName("x_fdds").HasColumnType("numeric(18,4)");
            entity.Property(e => e.x_sccj).HasColumnName("x_sccj").HasColumnType("char(50)");
            entity.Property(e => e.x_wzdh).HasColumnName("x_wzdh").HasColumnType("char(100)");
            entity.Property(e => e.x_bjb_dj).HasColumnName("x_bjb_dj").HasColumnType("numeric(18,4)");
            entity.Property(e => e.x_bjb_bj).HasColumnName("x_bjb_bj").HasColumnType("numeric(18,4)");
            entity.Property(e => e.x_lx).HasColumnName("x_lx");
            entity.Property(e => e.x_bjb_datetime).HasColumnName("x_bjb_datetime").HasColumnType("datetime");
        });

        modelBuilder.Entity<StdPriceHistory>(entity =>
        {
            entity.ToTable("STD_PRICE_HISTORY");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.x_wzdh).IsUnique();
            entity.Property(e => e.x_wzdh).HasColumnType("nvarchar(400)").IsRequired();
            entity.Property(e => e.last_price).HasColumnType("decimal(18,4)");
            entity.Property(e => e.avg_price).HasColumnType("decimal(18,4)");
            entity.Property(e => e.min_price).HasColumnType("decimal(18,4)");
            entity.Property(e => e.max_price).HasColumnType("decimal(18,4)");
        });

        modelBuilder.Entity<StdPriceExclusion>(entity =>
        {
            entity.ToTable("STD_PRICE_EXCLUSION");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.fabh).HasColumnName("fabh").HasColumnType("varchar(50)").IsRequired();
            entity.Property(e => e.x_wzdh).HasColumnName("x_wzdh").HasColumnType("nvarchar(400)");
            entity.Property(e => e.reason).HasColumnName("reason").HasColumnType("nvarchar(500)");
            entity.Property(e => e.created_by).HasColumnName("created_by").HasColumnType("nvarchar(50)");
            entity.Property(e => e.created_at).HasColumnName("created_at").HasColumnType("datetime");
        });

        modelBuilder.Entity<StdElementDict>(entity =>
        {
            entity.ToTable("STD_ELEMENT_DICT");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("Id").ValueGeneratedOnAdd();
            entity.Property(e => e.Level).HasColumnName("Level");
            entity.Property(e => e.Name).HasColumnName("Name").HasColumnType("nvarchar(50)").IsRequired();
            entity.Property(e => e.Xlx).HasColumnName("Xlx");
            entity.Property(e => e.Amount).HasColumnName("Amount").HasColumnType("decimal(18,2)");
            entity.Property(e => e.Ggxh).HasColumnName("Ggxh").HasColumnType("nvarchar(50)");
            entity.Property(e => e.DefaultUnit).HasColumnName("DefaultUnit").HasColumnType("nvarchar(10)");
            entity.Property(e => e.TargetParentScope).HasColumnName("TargetParentScope").HasColumnType("nvarchar(8)");
            entity.Property(e => e.SortOrder).HasColumnName("SortOrder");
            entity.Property(e => e.IsDefaultOnImport).HasColumnName("IsDefaultOnImport");
            entity.Property(e => e.IsEnabled).HasColumnName("IsEnabled");
            entity.Property(e => e.IsLocked).HasColumnName("IsLocked");
            entity.Property(e => e.Remark).HasColumnName("Remark").HasColumnType("nvarchar(300)");
            entity.Property(e => e.UpdatedAt).HasColumnName("UpdatedAt").HasColumnType("datetime2");
            entity.Property(e => e.UpdatedBy).HasColumnName("UpdatedBy").HasColumnType("nvarchar(50)");
        });

        modelBuilder.Entity<PurchasePlan>(entity =>
        {
            entity.ToTable("PF_PURCHASE_PLAN");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.PlanNo).HasColumnName("plan_no").HasColumnType("varchar(20)").IsRequired();
            entity.Property(e => e.Fabh).HasColumnName("fabh").HasColumnType("char(20)").IsRequired();
            entity.Property(e => e.ContractNo).HasColumnName("contract_no").HasColumnType("varchar(20)");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.Creator).HasColumnName("creator").HasColumnType("varchar(10)").IsRequired();
            entity.Property(e => e.Reviewer).HasColumnName("reviewer").HasColumnType("varchar(10)");
            entity.Property(e => e.UnitChief).HasColumnName("unit_chief").HasColumnType("varchar(10)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.IssuedAt).HasColumnName("issued_at").HasColumnType("datetime");
            entity.Property(e => e.IssuedBy).HasColumnName("issued_by").HasColumnType("varchar(10)");
            entity.HasMany(e => e.Items).WithOne(i => i.Plan).HasForeignKey(i => i.PlanId);
        });

        modelBuilder.Entity<PurchasePlanItem>(entity =>
        {
            entity.ToTable("PF_PURCHASE_PLAN_ITEM");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.PlanId).HasColumnName("plan_id");
            entity.Property(e => e.SortNo).HasColumnName("sort_no");
            entity.Property(e => e.ItemName).HasColumnName("item_name").HasColumnType("nvarchar(100)").IsRequired();
            entity.Property(e => e.ItemSpec).HasColumnName("item_spec").HasColumnType("nvarchar(200)").IsRequired();
            entity.Property(e => e.ItemUnit).HasColumnName("item_unit").HasColumnType("nvarchar(20)");
            entity.Property(e => e.ItemQty).HasColumnName("item_qty").HasColumnType("decimal(18,4)");
            entity.Property(e => e.ItemNoBuyQty).HasColumnName("item_no_buy_qty").HasColumnType("decimal(18,4)");
            entity.Property(e => e.ItemManufacturer).HasColumnName("item_manufacturer").HasColumnType("nvarchar(100)");
            entity.Property(e => e.ChangeType).HasColumnName("change_type");
            entity.Property(e => e.ChangeRemark).HasColumnName("change_remark").HasColumnType("nvarchar(200)");
            entity.Property(e => e.NeedDate).HasColumnName("need_date").HasColumnType("date");
            entity.Property(e => e.Remark).HasColumnName("remark").HasColumnType("nvarchar(200)");
            entity.Property(e => e.HasCert).HasColumnName("has_cert");
            entity.Property(e => e.HasInspection).HasColumnName("has_inspection");
            entity.Property(e => e.AppearanceOk).HasColumnName("appearance_ok");
            entity.Property(e => e.HasAccessories).HasColumnName("has_accessories");
            entity.Property(e => e.HasDocuments).HasColumnName("has_documents");
            entity.Property(e => e.VerifyDate).HasColumnName("verify_date").HasColumnType("date");
            entity.Property(e => e.Conclusion).HasColumnName("conclusion").HasColumnType("nvarchar(50)");
            entity.Property(e => e.Verifier).HasColumnName("verifier").HasColumnType("varchar(10)");
            entity.Property(e => e.VerifiedAt).HasColumnName("verified_at").HasColumnType("datetime");
        });

        modelBuilder.Entity<BjbXmyjhzItem>(entity =>
        {
            entity.ToTable("BJB_XMYJHZ");
            entity.HasKey(e => new { e.fabh, e.x_flbh, e.x_ggxh, e.x_sccj, e.x_key_ry });
            entity.Property(e => e.fabh).HasColumnName("fabh").HasColumnType("char(20)");
            entity.Property(e => e.x_flbh).HasColumnName("x_flbh").HasColumnType("char(50)");
            entity.Property(e => e.x_ggxh).HasColumnName("x_ggxh").HasColumnType("char(50)");
            entity.Property(e => e.x_sccj).HasColumnName("x_sccj").HasColumnType("char(50)");
            entity.Property(e => e.x_key_ry).HasColumnName("x_key_ry").HasColumnType("char(50)");
            entity.Property(e => e.x_mc).HasColumnName("x_mc").HasColumnType("char(100)");
            entity.Property(e => e.x_dw).HasColumnName("x_dw").HasColumnType("char(10)");
            entity.Property(e => e.x_sl).HasColumnName("x_sl").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_je).HasColumnName("x_je").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_bcg_sl).HasColumnName("x_bcg_sl").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_hzjb).HasColumnName("x_hzjb").HasColumnType("char(100)");
        });

        modelBuilder.Entity<BjbHzbItem>(entity =>
        {
            entity.ToTable("BJB_HZB");
            entity.HasKey(e => new { e.FABH, e.x_bm });
            entity.Property(e => e.FABH).HasColumnName("FABH").HasColumnType("char(20)");
            entity.Property(e => e.x_bm).HasColumnName("x_bm").HasColumnType("char(100)");
            entity.Property(e => e.x_mc).HasColumnName("x_mc").HasColumnType("char(100)");
            entity.Property(e => e.x_sm).HasColumnName("x_sm").HasColumnType("char(100)");
            entity.Property(e => e.x_lx).HasColumnName("x_lx");
            entity.Property(e => e.x_sl).HasColumnName("x_sl").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj1).HasColumnName("x_zj1").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj1_bj).HasColumnName("x_zj1_bj").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj1_jj).HasColumnName("x_zj1_jj").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj1_scj).HasColumnName("x_zj1_scj").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj1_zdj).HasColumnName("x_zj1_zdj").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj2).HasColumnName("x_zj2").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj2_zdj).HasColumnName("x_zj2_zdj").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj3).HasColumnName("x_zj3").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj3_zdj).HasColumnName("x_zj3_zdj").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj4).HasColumnName("x_zj4").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj4_zdj).HasColumnName("x_zj4_zdj").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj5).HasColumnName("x_zj5").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj5_zdj).HasColumnName("x_zj5_zdj").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj6).HasColumnName("x_zj6").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj6_zdj).HasColumnName("x_zj6_zdj").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj7).HasColumnName("x_zj7").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj7_zdj).HasColumnName("x_zj7_zdj").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj8).HasColumnName("x_zj8").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj8_zdj).HasColumnName("x_zj8_zdj").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj9).HasColumnName("x_zj9").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj9_zdj).HasColumnName("x_zj9_zdj").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj10).HasColumnName("x_zj10").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zj10_zdj).HasColumnName("x_zj10_zdj").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_wzfy).HasColumnName("x_wzfy").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_cgf).HasColumnName("x_cgf");
            entity.Property(e => e.x_flbh).HasColumnName("x_flbh").HasColumnType("char(50)");
            entity.Property(e => e.x_ggxh).HasColumnName("x_ggxh").HasColumnType("char(50)");
            entity.Property(e => e.x_sccj).HasColumnName("x_sccj").HasColumnType("char(50)");
            entity.Property(e => e.x_key_ry).HasColumnName("x_key_ry").HasColumnType("char(50)");
        });

        modelBuilder.Entity<BjbXmyjbItem>(entity =>
        {
            entity.ToTable("BJB_XMYJB");
            entity.HasKey(e => new { e.fabh, e.x_dyh, e.x_ggxh, e.x_sccj, e.x_key_ry, e.x_lylx });
            entity.Property(e => e.fabh).HasColumnName("fabh").HasColumnType("char(20)");
            entity.Property(e => e.x_dyh).HasColumnName("x_dyh").HasColumnType("char(20)");
            entity.Property(e => e.x_ggxh).HasColumnName("x_ggxh").HasColumnType("char(50)");
            entity.Property(e => e.x_sccj).HasColumnName("x_sccj").HasColumnType("char(50)");
            entity.Property(e => e.x_key_ry).HasColumnName("x_key_ry").HasColumnType("char(50)");
            entity.Property(e => e.x_lylx).HasColumnName("x_lylx");
            entity.Property(e => e.x_flbh).HasColumnName("x_flbh").HasColumnType("char(50)");
            entity.Property(e => e.x_qjmc).HasColumnName("x_qjmc").HasColumnType("char(100)");
            entity.Property(e => e.x_dymc).HasColumnName("x_dymc").HasColumnType("char(100)");
            entity.Property(e => e.x_lx).HasColumnName("x_lx");
            entity.Property(e => e.x_zsl).HasColumnName("x_zsl").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zje).HasColumnName("x_zje").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_bcg_sl).HasColumnName("x_bcg_sl").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_sxh).HasColumnName("x_sxh").HasColumnType("decimal(18,0)");
            entity.Property(e => e.x_zxm_sl).HasColumnName("x_zxm_sl").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_zxm_je).HasColumnName("x_zxm_je").HasColumnType("decimal(18,0)");
            entity.Property(e => e.x_zxm_bcg_sl).HasColumnName("x_zxm_bcg_sl").HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<BjbXmhzItem>(entity =>
        {
            entity.ToTable("BJB_XMHZ");
            entity.HasNoKey();
            entity.Property(e => e.fabh).HasColumnName("fabh").HasColumnType("char(20)");
            entity.Property(e => e.x_flbh).HasColumnName("x_flbh").HasColumnType("char(50)");
            entity.Property(e => e.x_ggxh).HasColumnName("x_ggxh").HasColumnType("char(50)");
            entity.Property(e => e.x_sccj).HasColumnName("x_sccj").HasColumnType("char(50)");
            entity.Property(e => e.x_key_ry).HasColumnName("x_key_ry").HasColumnType("char(50)");
            entity.Property(e => e.x_mc).HasColumnName("x_mc").HasColumnType("char(100)");
            entity.Property(e => e.x_sl).HasColumnName("x_sl").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_je).HasColumnName("x_je").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_bcg_sl).HasColumnName("x_bcg_sl").HasColumnType("decimal(18,2)");
            entity.Property(e => e.x_hzjb).HasColumnName("x_hzjb").HasColumnType("char(100)");
        });

        modelBuilder.Entity<BjhzbCategoryItem>(entity =>
        {
            entity.ToTable("BJHZB");
            entity.HasKey(e => new { e.fabh, e.x_bh });
            entity.Property(e => e.fabh).HasColumnName("fabh").HasColumnType("numeric(12,0)");
            entity.Property(e => e.x_bh).HasColumnName("x_bh").HasColumnType("char(100)");
            entity.Property(e => e.famc).HasColumnName("famc").HasColumnType("nchar(100)");
            entity.Property(e => e.x_mc).HasColumnName("x_mc").HasColumnType("char(100)");
            entity.Property(e => e.bz).HasColumnName("bz").HasColumnType("varchar(50)");
            entity.Property(e => e.x_flbh).HasColumnName("x_flbh").HasColumnType("char(100)");
            entity.Property(e => e.x_fl_bhf).HasColumnName("x_fl_bhf");
        });

        modelBuilder.Entity<BjdBzbItem>(entity =>
        {
            entity.ToTable("BJD_BZB");
            entity.HasKey(e => e.XH);
            entity.Property(e => e.XH).HasColumnName("XH");
            entity.Property(e => e.ZD).HasColumnName("ZD").HasColumnType("decimal(18,4)");
            entity.Property(e => e.ZG).HasColumnName("ZG").HasColumnType("decimal(18,4)");
            entity.Property(e => e.JG).HasColumnName("JG").HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<BjdWybItem>(entity =>
        {
            entity.ToTable("BJD_WYB");
            entity.HasKey(e => e.XH);
            entity.Property(e => e.XH).HasColumnName("XH");
            entity.Property(e => e.MC).HasColumnName("MC").HasColumnType("char(50)");
            entity.Property(e => e.DJ).HasColumnName("DJ").HasColumnType("decimal(18,2)");
        });
    }
}
