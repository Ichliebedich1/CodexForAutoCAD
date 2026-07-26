using Codex.AutoCAD.AgentHost;

/// <summary>
/// M4.13 锚点 MAC 规格。重点是降级防护：只要密钥存在，删除或破坏 .mac 都不能把存储
/// 退回无保护状态。
/// </summary>
internal static class AgentHostAuditAnchorMacSpecs
{
    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "codex-anchormac-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string root)
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static AgentHostAuditAnchor SampleAnchor()
        => new()
        {
            SystemSessionId = "0123456789abcdef0123456789abcdef",
            SegmentId = "segment-000001",
            Sequence = 5,
            RecordHash = "3f1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7f8",
        };

    private static void ExpectIntegrityFailure(Action action, string what)
    {
        try
        {
            action();
        }
        catch (AgentHostAuditIntegrityException)
        {
            return;
        }
        throw new InvalidOperationException("Expected integrity failure: " + what);
    }

    public static Task AnchorMacRoundTripsAndDetectsTampering()
    {
        var root = CreateTemporaryRoot();
        try
        {
            using var key = AgentHostAuditChainKey.LoadOrCreate(root);
            var anchorPath = Path.Combine(root, "anchor.json");
            using (var sink = new AgentHostAuditFileAnchorSink(anchorPath, key))
            {
                sink.Write(SampleAnchor());
            }

            var macPath = anchorPath + ".mac";
            if (!File.Exists(macPath))
            {
                throw new InvalidOperationException("Anchor MAC was not written.");
            }
            // 正常情况必须通过。
            AgentHostAuditAnchorMac.Verify(anchorPath, key);

            // 篡改锚点内容后必须被检出。
            var original = File.ReadAllBytes(anchorPath);
            var tampered = File.ReadAllText(anchorPath).Replace("\"sequence\":5", "\"sequence\":6");
            File.WriteAllText(anchorPath, tampered);
            ExpectIntegrityFailure(
                () => AgentHostAuditAnchorMac.Verify(anchorPath, key),
                "tampered anchor content");

            // 还原后重新通过，证明失败来自内容而非环境。
            File.WriteAllBytes(anchorPath, original);
            AgentHostAuditAnchorMac.Verify(anchorPath, key);
        }
        finally
        {
            Cleanup(root);
        }
        return Task.CompletedTask;
    }

    /// <summary>本组最重要的一条：删除或破坏 MAC 不得降级为无保护。</summary>
    public static Task MissingOrCorruptMacCannotDowngradeVerification()
    {
        var root = CreateTemporaryRoot();
        try
        {
            using var key = AgentHostAuditChainKey.LoadOrCreate(root);
            var anchorPath = Path.Combine(root, "anchor.json");
            using (var sink = new AgentHostAuditFileAnchorSink(anchorPath, key))
            {
                sink.Write(SampleAnchor());
            }
            var macPath = anchorPath + ".mac";

            // 删除 .mac：攻击者最直接的降级手段。
            File.Delete(macPath);
            ExpectIntegrityFailure(
                () => AgentHostAuditAnchorMac.Verify(anchorPath, key),
                "missing MAC while a key exists");

            // 长度不对的 MAC。
            File.WriteAllBytes(macPath, new byte[8]);
            ExpectIntegrityFailure(
                () => AgentHostAuditAnchorMac.Verify(anchorPath, key),
                "truncated MAC");

            // 长度正确但内容错误的 MAC。
            File.WriteAllBytes(macPath, new byte[32]);
            ExpectIntegrityFailure(
                () => AgentHostAuditAnchorMac.Verify(anchorPath, key),
                "wrong MAC value");
        }
        finally
        {
            Cleanup(root);
        }
        return Task.CompletedTask;
    }

    /// <summary>未配置密钥的既有存储必须继续可用，且不会写出 MAC 伴随文件。</summary>
    public static Task StoresWithoutKeyRemainBackwardCompatible()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var anchorPath = Path.Combine(root, "anchor.json");
            using (var sink = new AgentHostAuditFileAnchorSink(anchorPath))
            {
                sink.Write(SampleAnchor());
            }

            if (File.Exists(anchorPath + ".mac"))
            {
                throw new InvalidOperationException("No MAC expected when no key is configured.");
            }
            // 无密钥时校验直接放行。
            AgentHostAuditAnchorMac.Verify(anchorPath, chainKey: null);
        }
        finally
        {
            Cleanup(root);
        }
        return Task.CompletedTask;
    }

    /// <summary>另一把密钥不得验证本存储的锚点，否则跨存储伪造成立。</summary>
    public static Task ForeignKeyCannotVerifyAnchor()
    {
        var root = CreateTemporaryRoot();
        var otherRoot = CreateTemporaryRoot();
        try
        {
            using var key = AgentHostAuditChainKey.LoadOrCreate(root);
            using var foreign = AgentHostAuditChainKey.LoadOrCreate(otherRoot);
            var anchorPath = Path.Combine(root, "anchor.json");
            using (var sink = new AgentHostAuditFileAnchorSink(anchorPath, key))
            {
                sink.Write(SampleAnchor());
            }

            AgentHostAuditAnchorMac.Verify(anchorPath, key);
            ExpectIntegrityFailure(
                () => AgentHostAuditAnchorMac.Verify(anchorPath, foreign),
                "foreign key verifying another store's anchor");
        }
        finally
        {
            Cleanup(root);
            Cleanup(otherRoot);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 只读路径必须使用只加载不创建的入口。若只读分类顺手生成密钥，会把"该存储从未启用
    /// MAC"悄悄变成"已启用"，反而掩盖既有锚点缺少 MAC 的事实。
    /// </summary>
    public static Task ReadOnlyKeyLookupNeverCreatesAKey()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var keyPath = Path.Combine(root, AgentHostAuditChainKey.KeyFileName);

            // 不存在时返回 null，且绝不落盘。
            var absent = AgentHostAuditChainKey.TryLoad(root);
            if (absent != null)
            {
                absent.Dispose();
                throw new InvalidOperationException("TryLoad must return null when absent.");
            }
            if (File.Exists(keyPath))
            {
                throw new InvalidOperationException("Read-only lookup must not create a key.");
            }

            // 存在时正常加载。
            using (var created = AgentHostAuditChainKey.LoadOrCreate(root)) { }
            using (var loaded = AgentHostAuditChainKey.TryLoad(root))
            {
                if (loaded == null)
                {
                    throw new InvalidOperationException("TryLoad must load an existing key.");
                }
            }

            // 存在但损坏时仍然 fail-closed，而不是当作"未启用"放行。
            File.WriteAllBytes(keyPath, new byte[8]);
            ExpectIntegrityFailure(
                () => AgentHostAuditChainKey.TryLoad(root)?.Dispose(),
                "corrupt key surfaced through the read-only entry point");
        }
        finally
        {
            Cleanup(root);
        }
        return Task.CompletedTask;
    }

    /// <summary>锚点重写时 MAC 必须同步更新，不能留下上一版的 MAC。</summary>
    public static Task RewritingAnchorRefreshesMac()
    {
        var root = CreateTemporaryRoot();
        try
        {
            using var key = AgentHostAuditChainKey.LoadOrCreate(root);
            var anchorPath = Path.Combine(root, "anchor.json");
            using var sink = new AgentHostAuditFileAnchorSink(anchorPath, key);

            sink.Write(SampleAnchor());
            var firstMac = File.ReadAllBytes(anchorPath + ".mac");

            sink.Write(new AgentHostAuditAnchor
            {
                SystemSessionId = "0123456789abcdef0123456789abcdef",
                SegmentId = "segment-000001",
                Sequence = 6,
                RecordHash = "a01a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7f8",
            });
            var secondMac = File.ReadAllBytes(anchorPath + ".mac");

            if (firstMac.AsSpan().SequenceEqual(secondMac))
            {
                throw new InvalidOperationException("MAC must change when the anchor changes.");
            }
            AgentHostAuditAnchorMac.Verify(anchorPath, key);
        }
        finally
        {
            Cleanup(root);
        }
        return Task.CompletedTask;
    }
}
