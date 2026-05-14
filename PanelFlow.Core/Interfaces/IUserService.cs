using PanelFlow.Core.Models;

namespace PanelFlow.Core.Interfaces;

public interface IUserService
{
    Task<List<UserDto>> GetAllAsync();
    Task<UserDto?> GetByUsernameAsync(string username);
    Task<(bool Success, string Message)> CreateAsync(
        string username, string displayName, int role, string password, string remark);
    Task<(bool Success, string Message)> UpdateAsync(
        string username, string displayName, int role, bool isEnabled, string remark);
    Task<(bool Success, string Message)> ResetPasswordAsync(string username, string newPassword);
}
