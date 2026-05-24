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
            entity.Property(e => e.x_mc).HasColumnName("x_mc").HasColumnType("char(100)");
            entity.Property(e => e.x_ggxh).HasColumnName("x_ggxh").HasColumnType("char(100)");
            entity.Property(e => e.x_dw).HasColumnName("x_dw").HasColumnType("char(10)");
            entity.Property(e => e.x_dj).HasColumnName("x_dj").HasColumnType("numeric(18,4)");
            entity.Property(e => e.x_bj_dj).HasColumnName("x_bj_dj").HasColumnType("numeric(18,4)");
            entity.Property(e => e.x_sl).HasColumnName("x_sl").HasColumnType("numeric(18,4)");
            entity.Property(e => e.x_fdds).HasColumnName("x_fdds").HasColumnType("numeric(18,4)");
            entity.Property(e => e.x_sccj).HasColumnName("x_sccj").HasColumnType("char(20)");
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
    }
}
