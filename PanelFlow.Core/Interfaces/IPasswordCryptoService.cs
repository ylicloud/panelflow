namespace PanelFlow.Core.Interfaces;

/// <summary>
/// 密码加密/验证接口。
/// 实现必须兼容历史 PowerBuilder 系统的基于时间种子的字符替换加密算法，
/// 禁止替换为 BCrypt、SHA256 等其他算法，否则历史用户将无法登录。
/// </summary>
public interface IPasswordCryptoService
{
    /// <summary>
    /// 验证用户输入的明文密码是否与数据库中存储的密文一致
    /// </summary>
    /// <param name="plainPassword">用户输入的明文密码</param>
    /// <param name="storedCipher">数据库中存储的密文 (YHQXGL.kl)</param>
    /// <param name="seed">加密种子时间 (YHQXGL.kzhdrq，开账号日期)</param>
    bool Verify(string plainPassword, string storedCipher, DateTime seed);

    /// <summary>
    /// 将明文密码加密为与历史系统兼容的密文格式
    /// </summary>
    string Encrypt(string plainPassword, DateTime seed);

    /// <summary>
    /// 将密文解密为明文
    /// </summary>
    string Decrypt(string cipherText, DateTime seed);
}
