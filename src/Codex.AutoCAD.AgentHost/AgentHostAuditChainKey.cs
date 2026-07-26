using System.Security.Cryptography;

namespace Codex.AutoCAD.AgentHost;

/// <summary>
/// M4.13 审计链 MAC 密钥。
///
/// 威胁模型（必须如实理解，不要过度宣称）：
/// AgentHost 以当前用户身份运行，因此它能读取的任何本地密钥，同一个用户也能读取。
/// 本密钥能提升的是——
///   * 其他用户或其他会话无法伪造本用户的审计链锚点；
///   * 离线拷贝出去的 segment/anchor 无法被重新配平；
///   * 密钥缺失或损坏时立即 fail-closed，而不是静默降级为无 MAC 保护。
/// 本密钥不能防御——
///   * 同一用户蓄意篡改：该用户可读出密钥并重算整条链与锚点。
/// 要真正抵抗同用户篡改，必须由更高权限的组件代签（密钥对该用户不可达）、把锚点外发到
/// 用户不可写的仲裁端，或使用 TPM 封装密钥。这些属于架构变更，不在本类型范围内，
/// 因此对外报告的 <see cref="SameUserTamperResistant"/> 恒为 false。
/// </summary>
internal sealed class AgentHostAuditChainKey : IDisposable
{
    /// <summary>HMAC-SHA256 的密钥长度。</summary>
    internal const int KeyLengthBytes = 32;

    internal const string KeyFileName = "chain-key.bin";

    /// <summary>本地同权限方案无法抵抗同用户篡改，恒为 false，不得被调用方覆盖。</summary>
    internal const bool SameUserTamperResistant = false;

    private byte[]? _key;

    private AgentHostAuditChainKey(byte[] key, bool created)
    {
        _key = key;
        Created = created;
    }

    /// <summary>true 表示本次是首次生成密钥，可用于区分新审计存储与既有存储。</summary>
    internal bool Created { get; }

    /// <summary>
    /// 从已受保护的审计根加载密钥；不存在时生成。调用方必须保证 <paramref name="protectedAuditRoot"/>
    /// 已通过受保护目录校验。
    ///
    /// fail-closed 关键点：密钥文件已存在但无效（长度不符、不可读、是 reparse point）时
    /// **抛出异常而不是重新生成**。否则攻击者只要删除或破坏密钥文件，系统就会换一把新密钥，
    /// 旧链随之变得不可验证却不会报警——那正是一次静默降级攻击。
    /// </summary>
    internal static AgentHostAuditChainKey LoadOrCreate(string protectedAuditRoot)
    {
        if (string.IsNullOrWhiteSpace(protectedAuditRoot))
        {
            throw new AgentHostAuditIntegrityException("Audit chain key root is unavailable.");
        }

        var keyPath = Path.Combine(protectedAuditRoot, KeyFileName);

        FileInfo info;
        try
        {
            info = new FileInfo(keyPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or PathTooLongException or NotSupportedException)
        {
            throw new AgentHostAuditIntegrityException(
                "Audit chain key path is invalid.", exception);
        }

        if (info.Exists)
        {
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new AgentHostAuditIntegrityException(
                    "Audit chain key must not be a reparse point.");
            }
            if (info.Length != KeyLengthBytes)
            {
                throw new AgentHostAuditIntegrityException(
                    "Audit chain key length is invalid; refusing to regenerate.");
            }

            byte[] existing;
            try
            {
                existing = File.ReadAllBytes(keyPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                throw new AgentHostAuditIntegrityException(
                    "Audit chain key cannot be read; refusing to regenerate.", exception);
            }

            if (existing.Length != KeyLengthBytes)
            {
                CryptographicOperations.ZeroMemory(existing);
                throw new AgentHostAuditIntegrityException(
                    "Audit chain key length is invalid; refusing to regenerate.");
            }
            if (IsAllZero(existing))
            {
                CryptographicOperations.ZeroMemory(existing);
                throw new AgentHostAuditIntegrityException(
                    "Audit chain key is degenerate; refusing to regenerate.");
            }

            return new AgentHostAuditChainKey(existing, created: false);
        }

        var generated = RandomNumberGenerator.GetBytes(KeyLengthBytes);
        try
        {
            // CreateNew：并发首次启动时只有一个会成功，另一个回到加载路径，
            // 避免两个进程各生成一把密钥而互相判定对方的链无效。
            using var stream = new FileStream(
                keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(generated, 0, generated.Length);
            stream.Flush(flushToDisk: true);
        }
        catch (IOException)
        {
            CryptographicOperations.ZeroMemory(generated);
            // 竞态：另一进程刚创建。按既有密钥加载，仍然走完整校验。
            return LoadExistingAfterRace(keyPath);
        }
        catch (UnauthorizedAccessException exception)
        {
            CryptographicOperations.ZeroMemory(generated);
            throw new AgentHostAuditIntegrityException(
                "Audit chain key cannot be created.", exception);
        }

        return new AgentHostAuditChainKey(generated, created: true);
    }

    private static AgentHostAuditChainKey LoadExistingAfterRace(string keyPath)
    {
        byte[] existing;
        try
        {
            existing = File.ReadAllBytes(keyPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new AgentHostAuditIntegrityException(
                "Audit chain key cannot be read after a creation race.", exception);
        }

        if (existing.Length != KeyLengthBytes || IsAllZero(existing))
        {
            CryptographicOperations.ZeroMemory(existing);
            throw new AgentHostAuditIntegrityException(
                "Audit chain key is invalid after a creation race.");
        }
        return new AgentHostAuditChainKey(existing, created: false);
    }

    /// <summary>计算 HMAC-SHA256。</summary>
    internal byte[] ComputeMac(ReadOnlySpan<byte> data)
    {
        var key = _key ?? throw new ObjectDisposedException(nameof(AgentHostAuditChainKey));
        return HMACSHA256.HashData(key, data);
    }

    /// <summary>固定时间比较，避免通过比较耗时泄露 MAC 前缀。</summary>
    internal bool VerifyMac(ReadOnlySpan<byte> data, ReadOnlySpan<byte> expectedMac)
    {
        if (expectedMac.Length != SHA256.HashSizeInBytes)
        {
            return false;
        }
        var actual = ComputeMac(data);
        try
        {
            return CryptographicOperations.FixedTimeEquals(actual, expectedMac);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
        }
    }

    private static bool IsAllZero(ReadOnlySpan<byte> value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != 0)
            {
                return false;
            }
        }
        return true;
    }

    public void Dispose()
    {
        if (_key != null)
        {
            CryptographicOperations.ZeroMemory(_key);
            _key = null;
        }
    }
}
