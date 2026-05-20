using System.Runtime.CompilerServices;

// 允许 PanelFlow.Web.Tests 程序集访问本程序集中的 internal 类型/成员，
// 以便属性测试（PBT）能够直接验证 BuildRowsFromTable / ParseSourceUnits /
// SourceUnitBlock / BjbWriteRow 等内部实现细节。
//
// 注意：仅扩展可见性，不改变现有任何类型的 internal/public 修饰。
[assembly: InternalsVisibleTo("PanelFlow.Web.Tests")]
