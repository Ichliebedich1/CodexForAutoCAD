using System.Security.Cryptography;
using Codex.AutoCAD.AgentHost;

/// <summary>
/// M4.13 审计链 MAC 密钥规格。重点覆盖降级攻击：密钥被删除或破坏时必须 fail-closed，
/// 绝不静默换一把新密钥让旧链无声失效。
/// </summary>
internal static class AgentHostAuditChainKeySpecs
{
    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "codex-chainkey-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string root)
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static AgentHostAuditIntegrityException ExpectRejection(Func<object> action, string what)
    {
        try
        {
            action();
        }
        catch (AgentHostAuditIntegrityException exception)
        {
            return exception;
        }
        throw new InvalidOperationException("Expected rejection: " + what);
    }

    public static Task CreatesStableKeyAndReloadsIt()
    {
        var root = CreateTemporaryRoot();
        try
        {
            byte[] firstMac;
            using (var key = AgentHostAuditChainKey.LoadOrCreate(root))
            {
                if (!key.Created)
                {
                    throw new InvalidOperationException("First load must report creation.");
                }
                firstMac = key.ComputeMac("audit-record"u8);
                if (firstMac.Length != SHA256.HashSizeInBytes)
                {
                    throw new InvalidOperationException("MAC length must be SHA-256 sized.");
                }
            }

            var keyPath = Path.Combine(root, AgentHostAuditChainKey.KeyFileName);
            if (new FileInfo(keyPath).Length != AgentHostAuditChainKey.KeyLengthBytes)
            {
                throw new InvalidOperationException("Persisted key length is wrong.");
            }

            // 重新加载必须得到同一把密钥，否则既有链会无声失效。
            using (var reloaded = AgentHostAuditChainKey.LoadOrCreate(root))
            {
                if (reloaded.Created)
                {
                    throw new InvalidOperationException("Reload must not report creation.");
                }
                if (!reloaded.VerifyMac("audit-record"u8, firstMac))
                {
                    throw new InvalidOperationException("Reloaded key produced a different MAC.");
                }
                if (reloaded.VerifyMac("tampered-record"u8, firstMac))
                {
                    throw new InvalidOperationException("MAC must not verify for different data.");
                }
            }
        }
        finally
        {
            Cleanup(root);
        }
        return Task.CompletedTask;
    }

    public static Task CorruptedOrTruncatedKeyRefusesToRegenerate()
    {
        var root = CreateTemporaryRoot();
        try
        {
            using (var key = AgentHostAuditChainKey.LoadOrCreate(root)) { }
            var keyPath = Path.Combine(root, AgentHostAuditChainKey.KeyFileName);

            // 截断：攻击者破坏密钥后，系统绝不能换新密钥继续跑。
            File.WriteAllBytes(keyPath, new byte[8]);
            ExpectRejection(
                () => AgentHostAuditChainKey.LoadOrCreate(root),
                "truncated key");

            // 超长
            File.WriteAllBytes(keyPath, new byte[AgentHostAuditChainKey.KeyLengthBytes + 1]);
            ExpectRejection(
                () => AgentHostAuditChainKey.LoadOrCreate(root),
                "oversized key");

            // 全零退化密钥
            File.WriteAllBytes(keyPath, new byte[AgentHostAuditChainKey.KeyLengthBytes]);
            ExpectRejection(
                () => AgentHostAuditChainKey.LoadOrCreate(root),
                "degenerate all-zero key");

            // 密钥文件仍在原处，没有被悄悄重建。
            if (!File.Exists(keyPath))
            {
                throw new InvalidOperationException("Rejected key must be left in place for review.");
            }
        }
        finally
        {
            Cleanup(root);
        }
        return Task.CompletedTask;
    }

    public static Task DistinctRootsProduceDistinctKeys()
    {
        var first = CreateTemporaryRoot();
        var second = CreateTemporaryRoot();
        try
        {
            using var a = AgentHostAuditChainKey.LoadOrCreate(first);
            using var b = AgentHostAuditChainKey.LoadOrCreate(second);
            var macA = a.ComputeMac("same-input"u8);
            // 另一个存储的密钥不得验证本存储的 MAC，否则跨会话伪造成立。
            if (b.VerifyMac("same-input"u8, macA))
            {
                throw new InvalidOperationException("Independent stores must not share a key.");
            }
        }
        finally
        {
            Cleanup(first);
            Cleanup(second);
        }
        return Task.CompletedTask;
    }

    public static Task DisposedKeyCannotSign()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var key = AgentHostAuditChainKey.LoadOrCreate(root);
            key.Dispose();
            var threw = false;
            try
            {
                key.ComputeMac("x"u8);
            }
            catch (ObjectDisposedException)
            {
                threw = true;
            }
            if (!threw)
            {
                throw new InvalidOperationException("Disposed key must not sign.");
            }
            key.Dispose(); // 幂等
        }
        finally
        {
            Cleanup(root);
        }
        return Task.CompletedTask;
    }

    public static Task ThreatModelBoundaryIsDeclaredHonestly()
    {
        // 本地同权限方案无法抵抗同用户篡改；该边界必须是编译期常量，
        // 以免后续实现无意中把它宣称为已解决。
        if (AgentHostAuditChainKey.SameUserTamperResistant)
        {
            throw new InvalidOperationException(
                "Local same-privilege MAC cannot resist same-user tampering; do not claim it does.");
        }
        return Task.CompletedTask;
    }
}
