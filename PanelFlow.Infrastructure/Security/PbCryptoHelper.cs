using System.Text;
using PanelFlow.Core.Interfaces;

namespace PanelFlow.Infrastructure.Security;

/// <summary>
/// 模拟 MSVCRT 线性同余随机数生成器 (LCG)，
/// 与 PowerBuilder 的 Randomize / Rand 行为一致。
/// </summary>
internal class PbRandom
{
    private int _seed;

    public PbRandom(int seed) => _seed = seed;

    /// <summary>
    /// 生成 1 到 max 之间的随机整数 (包含 max)
    /// </summary>
    public int Next(int max)
    {
        if (max <= 0) return 0;

        unchecked
        {
            _seed = _seed * 214013 + 2531011;
        }

        int val = (_seed >> 16) & 0x7FFF;
        return (val % max) + 1;
    }
}

/// <summary>
/// PowerBuilder 历史系统密码加解密实现。
/// 算法：基于开账号日期 (kzhdrq) 作为种子生成 94 字符的替换映射表，
/// 将 ASCII 33~126 的明文字符逐个映射为密文字符。
/// </summary>
public class PbCryptoHelper : IPasswordCryptoService
{
    /// <summary>
    /// 生成基于时间种子的字符替换映射表 (对应 PB 的 resetmb)
    /// </summary>
    private static string ResetMb(DateTime seed)
    {
        long i = seed.Year + seed.Month + seed.Day +
                 seed.Hour + seed.Minute + seed.Second;

        int pbSeed = (int)((i % 32767) + 1);
        var random = new PbRandom(pbSeed);

        var cb = new char[94];
        var fg = new bool[94];

        for (int j = 1; j <= 94; j++)
        {
            int range = 94 - j + 1;
            int randVal = random.Next(range);

            int k = 0;
            int m = -1;

            for (int tempM = 1; tempM <= 94; tempM++)
            {
                if (!fg[tempM - 1])
                {
                    k++;
                    if (k == randVal)
                    {
                        m = tempM - 1;
                        break;
                    }
                }
            }

            if (m != -1)
            {
                fg[m] = true;
                cb[m] = (char)(32 + j);
            }
        }

        return new string(cb);
    }

    public string Encrypt(string plainPassword, DateTime seed)
    {
        if (string.IsNullOrEmpty(plainPassword)) return string.Empty;

        var mb = ResetMb(seed);
        var sb = new StringBuilder(plainPassword.Length);

        foreach (char c in plainPassword)
        {
            int ascii = c;
            if (ascii < 33 || ascii > 126)
                throw new ArgumentException($"不支持的字符: '{c}' (ASCII: {ascii})。仅支持 33-126。");

            sb.Append(mb[ascii - 33]);
        }

        return sb.ToString();
    }

    public string Decrypt(string cipherText, DateTime seed)
    {
        if (string.IsNullOrEmpty(cipherText)) return string.Empty;

        var mb = ResetMb(seed);
        var sb = new StringBuilder(cipherText.Length);

        foreach (char c in cipherText)
        {
            int pos = mb.IndexOf(c);
            if (pos == -1)
                throw new ArgumentException($"无效的密文字符: '{c}'。可能是种子时间不匹配。");

            sb.Append((char)(pos + 1 + 32));
        }

        return sb.ToString();
    }

    public bool Verify(string plainPassword, string storedCipher, DateTime seed)
    {
        if (string.IsNullOrEmpty(plainPassword) || string.IsNullOrEmpty(storedCipher))
            return false;

        var encrypted = Encrypt(plainPassword.Trim(), seed);
        return encrypted == storedCipher.Trim();
    }
}
