using System.Text.Json;
using PanelFlow.Core.Models;

namespace PanelFlow.Web.Extensions;

public static class SessionExtensions
{
    private const string LoginUserKey = "LoginUser";

    public static void SetLoginUser(this ISession session, LoginUser user)
    {
        session.SetString(LoginUserKey, JsonSerializer.Serialize(user));
    }

    public static LoginUser? GetLoginUser(this ISession session)
    {
        var json = session.GetString(LoginUserKey);
        if (string.IsNullOrEmpty(json)) return null;
        return JsonSerializer.Deserialize<LoginUser>(json);
    }

    public static void ClearLoginUser(this ISession session)
    {
        session.Remove(LoginUserKey);
    }
}
