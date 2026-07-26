using System.Security.Cryptography;

namespace Codex.AutoCAD.AgentHost;

/// <summary>
/// M4.13 锚点 MAC 伴随文件。锚点本身是审计链的对外可信端点，本类型为它加一层
/// HMAC-SHA256 保护，使不持有密钥的主体无法在重算哈希链后配平锚点。
///
/// 威胁模型边界与 <see cref="AgentHostAuditChainKey"/> 一致：本地同权限下无法抵抗同用户
/// 蓄意篡改。参见该类型的说明。
///
/// 降级防护是本类型的关键：只要密钥存在，缺失或不可读的 MAC 一律判为失败，
/// 不允许"删掉 .mac 就退回无保护模式"。
/// </summary>
internal static class AgentHostAuditAnchorMac
{
    internal const string MacFileSuffix = ".mac";

    internal static string GetMacPath(string anchorPath) => anchorPath + MacFileSuffix;

    /// <summary>
    /// 以临时文件加同卷原子 rename 写出 MAC，避免留下被截断的伴随文件。
    /// </summary>
    internal static void Write(
        string anchorPath, ReadOnlySpan<byte> anchorContent, AgentHostAuditChainKey chainKey)
    {
        ArgumentNullException.ThrowIfNull(chainKey);
        var macPath = GetMacPath(anchorPath);
        var mac = chainKey.ComputeMac(anchorContent);
        var temporaryPath = macPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(mac, 0, mac.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, macPath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(mac);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>
    /// 校验锚点的 MAC。<paramref name="chainKey"/> 为 null 表示该存储未启用 MAC，直接放行，
    /// 以兼容启用密钥之前写出的既有存储。
    ///
    /// 一旦提供密钥，则 MAC 必须存在、长度正确且比对通过；任一条件不满足都抛出，
    /// 因此删除 .mac 无法把存储降级回无保护状态。
    /// </summary>
    internal static void Verify(string anchorPath, AgentHostAuditChainKey? chainKey)
    {
        if (chainKey == null)
        {
            return;
        }

        var macPath = GetMacPath(anchorPath);
        byte[] expected;
        try
        {
            var info = new FileInfo(macPath);
            if (!info.Exists)
            {
                throw new AgentHostAuditIntegrityException(
                    "Audit anchor MAC is missing while a chain key is configured.");
            }
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new AgentHostAuditIntegrityException(
                    "Audit anchor MAC must not be a reparse point.");
            }
            if (info.Length != SHA256.HashSizeInBytes)
            {
                throw new AgentHostAuditIntegrityException(
                    "Audit anchor MAC length is invalid.");
            }
            expected = File.ReadAllBytes(macPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new AgentHostAuditIntegrityException(
                "Audit anchor MAC cannot be read.", exception);
        }

        byte[] anchorContent;
        try
        {
            anchorContent = File.ReadAllBytes(anchorPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new AgentHostAuditIntegrityException(
                "Audit anchor cannot be read for MAC verification.", exception);
        }

        if (!chainKey.VerifyMac(anchorContent, expected))
        {
            throw new AgentHostAuditIntegrityException(
                "Audit anchor MAC does not match the anchor content.");
        }
    }
}
