using Microsoft.AspNetCore.Mvc;
using PanelFlow.Web.Extensions;
using PanelFlow.Web.Filters;

namespace PanelFlow.Web.Controllers;

[RoleAuthorize]
public class HomeController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        var user = HttpContext.Session.GetLoginUser();
        return View(user);
    }
}
