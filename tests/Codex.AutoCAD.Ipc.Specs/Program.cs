using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Ipc;
using System.Security.Cryptography;
using System.Text;

var specs = new[]
{
    new SpecCase("协议v1固定canonical bytes与HMAC向量一致", KnownProtocolVectorMatches),
    new SpecCase("合法信封被接受", ValidEnvelopePasses),
    new SpecCase("篡改载荷被拒绝", TamperedPayloadFails),
    new SpecCase("跨会话信封被拒绝", CrossSessionFails),
    new SpecCase("重复序号被拒绝", ReplayedSequenceFails),
    new SpecCase("首包序号跳号被拒绝", InitialSequenceGapFails),
    new SpecCase("无效MAC不推进序号或nonce状态", InvalidMacDoesNotAdvanceGuardState),
    new SpecCase("序号必须为正且达到最大值后失败关闭", SequenceBoundsFailClosed),
    new SpecCase("重复nonce被拒绝", ReplayedNonceFails),
    new SpecCase("nonce历史满载时拒绝且在过期边界恢复", NonceCapacityFailsClosedAndExpiresAtBoundary),
    new SpecCase("nonce洪泛不能突破历史容量", NonceFloodCannotExceedHistoryCapacity),
    new SpecCase("超长nonce被拒绝", OversizedNonceFails),
    new SpecCase("非法nonce历史配置被拒绝", InvalidNonceHistoryOptionsFail),
    new SpecCase("密钥长度必须恰好为32字节", InvalidSecretLengthsFail),
    new SpecCase("认证器释放时清零私有密钥副本", AuthenticatorSecretIsZeroedOnDispose),
    new SpecCase("null签名字段被拒绝且不等价于空字符串", NullSignedFieldsFail),
    new SpecCase("畸形Unicode不能进入认证字节", MalformedUnicodeFailsClosed),
    new SpecCase("bootstrap v1固定frame与双向HMAC向量一致", BootstrapKnownVectorMatches),
    new SpecCase("bootstrap发送帧与同步异步读取逐字节一致", BootstrapRoundTripSyncAndAsync),
    new SpecCase("bootstrap发送与接收payload生命周期固定且失败关闭", BootstrapPayloadOriginAndLifecycleFailClosed),
    new SpecCase("bootstrap帧外认证键必须有效且错误键失败关闭", BootstrapExternalAuthenticationKeyRequired),
    new SpecCase("帧内secret自签攻击不能绕过帧外认证", BootstrapSelfSignedFrameAttackFails),
    new SpecCase("bootstrap magic版本flags与长度篡改失败关闭", BootstrapHeaderTamperingFailsClosed),
    new SpecCase("bootstrap认证后字段语义仍严格校验", BootstrapSemanticValidationAfterAuthentication),
    new SpecCase("bootstrap严格UTF8拒绝畸形标识符", BootstrapStrictUtf8FailsClosed),
    new SpecCase("bootstrap每一个stream截断点均被拒绝", BootstrapEveryStreamTruncationFails),
    new SpecCase("bootstrap尾随字节与第二帧均被拒绝", BootstrapTrailingDataAndSecondFrameFail),
    new SpecCase("bootstrap输入frame与一次性认证键均被清零", BootstrapInputBuffersAreConsumedAndZeroed),
    new SpecCase("bootstrap部分写与flush失败均使payload永久失效", BootstrapWriteFailuresPoisonPayload),
    new SpecCase("bootstrap payload与方向键释放时清零", BootstrapPayloadAndKeysZeroOnDispose),
    new SpecCase("bootstrap原始秘密只能派生一次", BootstrapDerivationIsSingleUse),
    new SpecCase("bootstrap Host与Agent方向映射且反射失败", BootstrapDirectionRolesAndReflection),
    new SpecCase("bootstrap pipe会话与ID变化均改变方向键", BootstrapContextChangesDirectionKeys),
    new SpecCase("bootstrap标识符采用严格规范格式", BootstrapIdentifierAndSizeRules),
    new SpecCase("bootstrap随机工厂产生可用非零材料", BootstrapCreateRandomProducesUsableMaterial)
};

var failed = 0;
foreach (var spec in specs)
{
    try
    {
        spec.Run();
        Console.WriteLine("PASS " + spec.Name);
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine("FAIL " + spec.Name + ": " + exception.Message);
    }
}

Console.WriteLine($"{specs.Length - failed}/{specs.Length} specs passed");
return failed == 0 ? 0 : 1;

static void KnownProtocolVectorMatches()
{
    const string secretHex = "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F";
    const string canonicalHex = "313A31383A6D73672DCEB12DF09F9880363A636F72722DE4B8AD31323A73657373696F6E2D32303136323A343231313A6361642E636F6E7465787432343A7B2274657874223A22E4B8ADE69687F09F9880222C226C696E65223A317D33323A3030313132323333343435353636373738383939414142424343444445454646";
    const string expectedMac = "46FFA5506FD595BA64CEAD67EDBAF8707E1A585988BC80298EBF569F69B38400";
    var secret = DecodeHex(secretHex);
    var envelope = new IpcEnvelope
    {
        ProtocolVersion = 1,
        MessageId = "msg-α-😀",
        CorrelationId = "corr-中",
        SessionId = "session-2016",
        Sequence = 42,
        MessageType = "cad.context",
        PayloadJson = "{\"text\":\"中文😀\",\"line\":1}",
        Nonce = "00112233445566778899AABBCCDDEEFF"
    };

    Equal(canonicalHex, EncodeHex(IpcCanonicalEnvelopeEncoding.GetBytes(envelope)));
    using var authenticator = new IpcEnvelopeAuthenticator(secret);
    Equal(expectedMac, authenticator.Sign(envelope));
    envelope.Mac = expectedMac;
    Equal(true, authenticator.Verify(envelope));
    Console.WriteLine("AUTH_VECTOR_V1 canonical=" + canonicalHex + " mac=" + expectedMac);
}

static void BootstrapKnownVectorMatches()
{
    // Public known-answer vector only. These bytes are deliberately non-secret and must
    // never be reused by a production bootstrap exchange.
    const string expectedFrame = "434458434144423101000000A4000000101112131415161718191A1B1C1D1E1F20002E0020003030313132323333343435353636373738383939616162626363646465656666636F6465782D6175746F6361642D6666656564646363626261613939383837373636353534343333323231313030202122232425262728292A2B2C2D2E2F303132333435363738393A3B3C3D3E3F386E5F60B2DA4B82E600AE2A4F29F7FCA8B8315CB7A5A7EA291CFA1A933A96B3";
    const string expectedTag = "386E5F60B2DA4B82E600AE2A4F29F7FCA8B8315CB7A5A7EA291CFA1A933A96B3";
    const string expectedHostToAgentContextSha256 = "2C5D369CC406BC57F6484837E17F6C4DFA46B7E18D949A713BC7509EDAA99F75";
    const string expectedHostToAgentKey = "BD7B3C03ACFCACB201B1967C30EDBE2369D79517BBA069C65B1431AFFF97C253";
    const string expectedAgentToHostKey = "89B7E2E5541EFE9FEBBAC62021F764AA91AC31E11342003AC07B43CA19AC03CD";
    const string expectedAgentToHostContextSha256 = "A52233871F1CE9AF67657B5E61F4E894ECA7EEE599FF86CB2CFFF4A60766319B";
    const string expectedHostToAgentMac = "AFDF6BDED2384AF724D4A0678C1AC6DE076C4E7E7254C6B975972C25D12C3D90";
    const string expectedAgentToHostMac = "548474E515652C3F182AE099DD2DC6EA99FCDE290F12006380E707ACDAD977D8";

    Equal((ushort)1, AgentBootstrapProtocol.CurrentVersion);
    Equal(expectedTag, expectedFrame.Substring(expectedFrame.Length - (AgentBootstrapProtocol.TagSize * 2)));
    Equal(
        expectedHostToAgentContextSha256,
        ComputeReferenceBootstrapDirectionContextSha256("host-to-agent"));
    Equal(
        expectedAgentToHostContextSha256,
        ComputeReferenceBootstrapDirectionContextSha256("agent-to-host"));

    var frame = DecodeHex(expectedFrame);
    var authenticationKey = FixedBootstrapAuthenticationKey();
    using var agentPayload = AgentBootstrapProtocol.DecodeSingleFrameAndClear(frame, authenticationKey);
    Equal(FixedBootstrapSessionId(), agentPayload.SessionId);
    Equal(FixedBootstrapPipeName(), agentPayload.PipeName);
    Equal("101112131415161718191A1B1C1D1E1F", EncodeHex(agentPayload.CopyBootstrapId()));

    using var hostPayload = CreateFixedOutboundBootstrapPayload();
    using var encoded = new MemoryStream();
    AgentBootstrapProtocol.WriteSingleFrameAndClearKey(
        encoded,
        hostPayload,
        FixedBootstrapAuthenticationKey());
    Equal(expectedFrame, EncodeHex(encoded.ToArray()));

    using var hostKeys = hostPayload.DeriveDirectionKeys();
    using var agentKeys = agentPayload.DeriveDirectionKeys();
    Equal(expectedHostToAgentKey, ReadPrivateDirectionKeyHex(hostKeys, "_hostToAgentKey"));
    Equal(expectedAgentToHostKey, ReadPrivateDirectionKeyHex(hostKeys, "_agentToHostKey"));
    var envelope = CreateBootstrapVectorEnvelope();
    using var hostOutbound = hostKeys.CreateOutboundAuthenticator();
    using var agentOutbound = agentKeys.CreateOutboundAuthenticator();
    Equal(expectedHostToAgentMac, hostOutbound.Sign(envelope));
    Equal(expectedAgentToHostMac, agentOutbound.Sign(envelope));
    Console.WriteLine(
        "BOOTSTRAP_VECTOR_V1 version=1"
        + " frame=" + expectedFrame
        + " tag=" + expectedTag
        + " h2a_ctx_sha256=" + expectedHostToAgentContextSha256
        + " h2a_key=" + expectedHostToAgentKey
        + " h2a_mac=" + expectedHostToAgentMac
        + " a2h_ctx_sha256=" + expectedAgentToHostContextSha256
        + " a2h_key=" + expectedAgentToHostKey
        + " a2h_mac=" + expectedAgentToHostMac);
}

static void BootstrapRoundTripSyncAndAsync()
{
    var expectedFrame = BuildReferenceBootstrapFrame();
    try
    {
        using (var outboundPayload = CreateFixedOutboundBootstrapPayload())
        using (var output = new MemoryStream())
        {
            var writeKey = FixedBootstrapAuthenticationKey();
            AgentBootstrapProtocol.WriteSingleFrameAndClearKey(output, outboundPayload, writeKey);
            Equal(true, writeKey.All(value => value == 0));
            Equal(EncodeHex(expectedFrame), EncodeHex(output.ToArray()));
            using var hostKeys = outboundPayload.DeriveDirectionKeys();
            using var hostOutbound = hostKeys.CreateOutboundAuthenticator();
            Equal(false, string.IsNullOrWhiteSpace(hostOutbound.Sign(CreateBootstrapVectorEnvelope())));
        }

        using (var input = new OneByteReadStream((byte[])expectedFrame.Clone()))
        {
            var readKey = FixedBootstrapAuthenticationKey();
            using var payload = AgentBootstrapProtocol.ReadSingleFrameAndClearKey(input, readKey);
            Equal(true, readKey.All(value => value == 0));
            using var agentKeys = payload.DeriveDirectionKeys();
            using var agentInbound = agentKeys.CreateInboundGuard();
        }

        using (var input = new OneByteReadStream((byte[])expectedFrame.Clone()))
        {
            var readKey = FixedBootstrapAuthenticationKey();
            using var payload = AgentBootstrapProtocol.ReadSingleFrameAndClearKeyAsync(
                input,
                readKey,
                CancellationToken.None).GetAwaiter().GetResult();
            Equal(FixedBootstrapSessionId(), payload.SessionId);
            Equal(FixedBootstrapPipeName(), payload.PipeName);
            Equal(true, readKey.All(value => value == 0));
            using var agentKeys = payload.DeriveDirectionKeys();
            using var agentOutbound = agentKeys.CreateOutboundAuthenticator();
        }
    }
    finally
    {
        Array.Clear(expectedFrame, 0, expectedFrame.Length);
    }
}

static void BootstrapPayloadOriginAndLifecycleFailClosed()
{
    using (var inboundPayload = AgentBootstrapProtocol.DecodeSingleFrameAndClear(
        BuildReferenceBootstrapFrame(),
        FixedBootstrapAuthenticationKey()))
    using (var output = new MemoryStream())
    {
        var forwardingKey = FixedBootstrapAuthenticationKey();
        BootstrapFails(
            AgentBootstrapValidationCode.InvalidPayloadState,
            () => AgentBootstrapProtocol.WriteSingleFrameAndClearKey(
                output,
                inboundPayload,
                forwardingKey));
        Equal(0L, output.Length);
        Equal(true, forwardingKey.All(value => value == 0));
        BootstrapFails(
            AgentBootstrapValidationCode.AlreadyConsumed,
            () => inboundPayload.DeriveDirectionKeys());
        BootstrapFails(
            AgentBootstrapValidationCode.AlreadyConsumed,
            () => inboundPayload.CopyBootstrapId());
    }

    using var outboundPayload = CreateFixedOutboundBootstrapPayload();
    BootstrapFails(
        AgentBootstrapValidationCode.InvalidPayloadState,
        () => outboundPayload.DeriveDirectionKeys());
    using var outboundStream = new MemoryStream();
    AgentBootstrapProtocol.WriteSingleFrameAndClearKey(
        outboundStream,
        outboundPayload,
        FixedBootstrapAuthenticationKey());
    using var hostKeys = outboundPayload.DeriveDirectionKeys();
    using var hostOutbound = hostKeys.CreateOutboundAuthenticator();
}

static void BootstrapExternalAuthenticationKeyRequired()
{
    var frame = BuildReferenceBootstrapFrame();
    var wrongKey = Enumerable.Range(1, AgentBootstrapProtocol.AuthenticationKeySize)
        .Select(value => (byte)value)
        .Reverse()
        .ToArray();
    BootstrapFails(
        AgentBootstrapValidationCode.InvalidTag,
        () => AgentBootstrapProtocol.DecodeSingleFrameAndClear((byte[])frame.Clone(), wrongKey));
    Equal(true, wrongKey.All(value => value == 0));

    using (var syncInput = new MemoryStream((byte[])frame.Clone(), writable: false))
    {
        var syncWrongKey = Enumerable.Range(1, AgentBootstrapProtocol.AuthenticationKeySize)
            .Select(value => (byte)value)
            .Reverse()
            .ToArray();
        BootstrapFails(
            AgentBootstrapValidationCode.InvalidTag,
            () => AgentBootstrapProtocol.ReadSingleFrameAndClearKey(syncInput, syncWrongKey));
        Equal(true, syncWrongKey.All(value => value == 0));
    }

    using (var asyncInput = new MemoryStream((byte[])frame.Clone(), writable: false))
    {
        var asyncWrongKey = Enumerable.Range(1, AgentBootstrapProtocol.AuthenticationKeySize)
            .Select(value => (byte)value)
            .Reverse()
            .ToArray();
        BootstrapFails(
            AgentBootstrapValidationCode.InvalidTag,
            () => AgentBootstrapProtocol.ReadSingleFrameAndClearKeyAsync(
                asyncInput,
                asyncWrongKey,
                CancellationToken.None).GetAwaiter().GetResult());
        Equal(true, asyncWrongKey.All(value => value == 0));
    }

    using (var noEofInput = new ThrowOnEofProbeStream((byte[])frame.Clone()))
    {
        var noEofWrongKey = Enumerable.Range(1, AgentBootstrapProtocol.AuthenticationKeySize)
            .Select(value => (byte)value)
            .Reverse()
            .ToArray();
        BootstrapFails(
            AgentBootstrapValidationCode.InvalidTag,
            () => AgentBootstrapProtocol.ReadSingleFrameAndClearKey(noEofInput, noEofWrongKey));
        Equal(false, noEofInput.EofProbeAttempted);
        Equal(true, noEofWrongKey.All(value => value == 0));
    }

    var shortKey = new byte[AgentBootstrapProtocol.AuthenticationKeySize - 1];
    shortKey[0] = 1;
    Throws<ArgumentException>(() =>
        AgentBootstrapProtocol.DecodeSingleFrameAndClear((byte[])frame.Clone(), shortKey));
    Equal(true, shortKey.All(value => value == 0));

    var zeroKey = new byte[AgentBootstrapProtocol.AuthenticationKeySize];
    Throws<ArgumentException>(() =>
        AgentBootstrapProtocol.DecodeSingleFrameAndClear((byte[])frame.Clone(), zeroKey));
    Equal(true, zeroKey.All(value => value == 0));
    Array.Clear(frame, 0, frame.Length);
}

static void BootstrapSelfSignedFrameAttackFails()
{
    var sessionSecret = FixedBootstrapSessionSecret();
    var selfSigned = BuildReferenceBootstrapFrame(authenticationKey: sessionSecret);
    try
    {
        BootstrapFails(
            AgentBootstrapValidationCode.InvalidTag,
            () => AgentBootstrapProtocol.DecodeSingleFrameAndClear(
                selfSigned,
                FixedBootstrapAuthenticationKey()));
        Equal(true, selfSigned.All(value => value == 0));

        var degenerateFrame = BuildReferenceBootstrapFrame(authenticationKey: sessionSecret);
        var degenerateKey = (byte[])sessionSecret.Clone();
        BootstrapFails(
            AgentBootstrapValidationCode.AuthenticationKeyReuse,
            () => AgentBootstrapProtocol.DecodeSingleFrameAndClear(
                degenerateFrame,
                degenerateKey));
        Equal(true, degenerateFrame.All(value => value == 0));
        Equal(true, degenerateKey.All(value => value == 0));

        using var payload = CreateFixedOutboundBootstrapPayload();
        using var output = new MemoryStream();
        var reusedSessionKey = FixedBootstrapSessionSecret();
        BootstrapFails(
            AgentBootstrapValidationCode.AuthenticationKeyReuse,
            () => AgentBootstrapProtocol.WriteSingleFrameAndClearKey(
                output,
                payload,
                reusedSessionKey));
        Equal(0L, output.Length);
        Equal(true, reusedSessionKey.All(value => value == 0));
        BootstrapFails(
            AgentBootstrapValidationCode.AlreadyConsumed,
            () => payload.DeriveDirectionKeys());
    }
    finally
    {
        Array.Clear(sessionSecret, 0, sessionSecret.Length);
        Array.Clear(selfSigned, 0, selfSigned.Length);
    }
}

static void BootstrapHeaderTamperingFailsClosed()
{
    AssertBootstrapMutationFails(
        frame => frame[0] ^= 0x01,
        AgentBootstrapValidationCode.InvalidMagic,
        retag: false);
    AssertBootstrapMutationFails(
        frame => WriteUInt16(frame, 8, AgentBootstrapProtocol.CurrentVersion + 1),
        AgentBootstrapValidationCode.UnsupportedVersion,
        retag: false);
    AssertBootstrapMutationFails(
        frame => WriteUInt16(frame, 10, 1),
        AgentBootstrapValidationCode.UnknownFlags,
        retag: false);
    AssertBootstrapMutationFails(
        frame => WriteUInt32(frame, 12, AgentBootstrapProtocol.MaximumBodyBytes - 1),
        AgentBootstrapValidationCode.InvalidLength,
        retag: false);
    AssertBootstrapMutationFails(
        frame => frame[38] ^= 0x01,
        AgentBootstrapValidationCode.InvalidTag,
        retag: false);
}

static void BootstrapSemanticValidationAfterAuthentication()
{
    AssertBootstrapMutationFails(
        frame => Array.Clear(frame, 16, AgentBootstrapProtocol.BootstrapIdSize),
        AgentBootstrapValidationCode.InvalidBootstrapId,
        retag: true);
    AssertBootstrapMutationFails(
        frame => WriteUInt16(frame, 32, AgentBootstrapProtocol.SessionIdBytes - 1),
        AgentBootstrapValidationCode.InvalidLength,
        retag: true);
    AssertBootstrapMutationFails(
        frame => Array.Clear(frame, 116, AgentBootstrapProtocol.SessionSecretSize),
        AgentBootstrapValidationCode.InvalidSecret,
        retag: true);
}

static void BootstrapStrictUtf8FailsClosed()
{
    AssertBootstrapMutationFails(
        frame =>
        {
            frame[38] = 0xC0;
            frame[39] = 0xAF;
        },
        AgentBootstrapValidationCode.InvalidUtf8,
        retag: true);
}

static void BootstrapEveryStreamTruncationFails()
{
    var frame = BuildReferenceBootstrapFrame();
    try
    {
        for (var length = 0; length < frame.Length; length++)
        {
            var prefix = new byte[length];
            Buffer.BlockCopy(frame, 0, prefix, 0, length);
            using var input = new MemoryStream(prefix, writable: false);
            var key = FixedBootstrapAuthenticationKey();
            BootstrapFails(
                AgentBootstrapValidationCode.TruncatedFrame,
                () => AgentBootstrapProtocol.ReadSingleFrameAndClearKey(input, key));
            Equal(true, key.All(value => value == 0));
            Array.Clear(prefix, 0, prefix.Length);
        }

        for (var length = 0; length < frame.Length; length++)
        {
            var prefix = new byte[length];
            Buffer.BlockCopy(frame, 0, prefix, 0, length);
            using var input = new MemoryStream(prefix, writable: false);
            var key = FixedBootstrapAuthenticationKey();
            BootstrapFails(
                AgentBootstrapValidationCode.TruncatedFrame,
                () => AgentBootstrapProtocol.ReadSingleFrameAndClearKeyAsync(
                    input,
                    key,
                    CancellationToken.None).GetAwaiter().GetResult());
            Equal(true, key.All(value => value == 0));
            Array.Clear(prefix, 0, prefix.Length);
        }

        AssertBootstrapCancellationAtOffset(frame, 0, cancelBeforeStart: true);
        AssertBootstrapCancellationAtOffset(
            frame,
            AgentBootstrapProtocol.HeaderSize + 5,
            cancelBeforeStart: false);
        AssertBootstrapCancellationAtOffset(frame, frame.Length, cancelBeforeStart: false);
    }
    finally
    {
        Array.Clear(frame, 0, frame.Length);
    }
}

static void BootstrapTrailingDataAndSecondFrameFail()
{
    var frame = BuildReferenceBootstrapFrame();
    try
    {
        var trailing = new byte[frame.Length + 1];
        Buffer.BlockCopy(frame, 0, trailing, 0, frame.Length);
        trailing[trailing.Length - 1] = 0x7F;
        using (var input = new MemoryStream(trailing, writable: false))
        {
            BootstrapFails(
                AgentBootstrapValidationCode.TrailingData,
                () => AgentBootstrapProtocol.ReadSingleFrameAndClearKey(
                    input,
                    FixedBootstrapAuthenticationKey()));
        }

        using (var input = new MemoryStream(trailing, writable: false))
        {
            BootstrapFails(
                AgentBootstrapValidationCode.TrailingData,
                () => AgentBootstrapProtocol.ReadSingleFrameAndClearKeyAsync(
                    input,
                    FixedBootstrapAuthenticationKey(),
                    CancellationToken.None).GetAwaiter().GetResult());
        }

        var secondFrame = new byte[frame.Length * 2];
        Buffer.BlockCopy(frame, 0, secondFrame, 0, frame.Length);
        Buffer.BlockCopy(frame, 0, secondFrame, frame.Length, frame.Length);
        using (var input = new MemoryStream(secondFrame, writable: false))
        {
            BootstrapFails(
                AgentBootstrapValidationCode.TrailingData,
                () => AgentBootstrapProtocol.ReadSingleFrameAndClearKey(
                    input,
                    FixedBootstrapAuthenticationKey()));
        }

        using (var input = new MemoryStream(secondFrame, writable: false))
        {
            BootstrapFails(
                AgentBootstrapValidationCode.TrailingData,
                () => AgentBootstrapProtocol.ReadSingleFrameAndClearKeyAsync(
                    input,
                    FixedBootstrapAuthenticationKey(),
                    CancellationToken.None).GetAwaiter().GetResult());
        }

        var inMemoryTrailing = new byte[frame.Length + 1];
        Buffer.BlockCopy(frame, 0, inMemoryTrailing, 0, frame.Length);
        BootstrapFails(
            AgentBootstrapValidationCode.TrailingData,
            () => AgentBootstrapProtocol.DecodeSingleFrameAndClear(
                inMemoryTrailing,
                FixedBootstrapAuthenticationKey()));
        Equal(true, inMemoryTrailing.All(value => value == 0));

        Array.Clear(trailing, 0, trailing.Length);
        Array.Clear(secondFrame, 0, secondFrame.Length);
    }
    finally
    {
        Array.Clear(frame, 0, frame.Length);
    }
}

static void BootstrapInputBuffersAreConsumedAndZeroed()
{
    var frame = BuildReferenceBootstrapFrame();
    var authenticationKey = FixedBootstrapAuthenticationKey();
    using var inboundPayload = AgentBootstrapProtocol.DecodeSingleFrameAndClear(frame, authenticationKey);
    Equal(true, frame.All(value => value == 0));
    Equal(true, authenticationKey.All(value => value == 0));
    using var inboundKeys = inboundPayload.DeriveDirectionKeys();

    using var payload = CreateFixedOutboundBootstrapPayload();
    using var output = new MemoryStream();
    var writeKey = FixedBootstrapAuthenticationKey();
    AgentBootstrapProtocol.WriteSingleFrameAndClearKey(output, payload, writeKey);
    Equal(true, writeKey.All(value => value == 0));

    using var capturingInput = new CapturingReadStream(BuildReferenceBootstrapFrame());
    using var readPayload = AgentBootstrapProtocol.ReadSingleFrameAndClearKey(
        capturingInput,
        FixedBootstrapAuthenticationKey());
    Equal(true, capturingInput.CapturedBuffers.Count > 0);
    Equal(true, capturingInput.CapturedBuffers.All(buffer => buffer.All(value => value == 0)));
    using var readKeys = readPayload.DeriveDirectionKeys();

    using var capturingOutput = new CapturingWriteStream();
    var capturingWriteKey = FixedBootstrapAuthenticationKey();
    using var capturingPayload = CreateFixedOutboundBootstrapPayload();
    AgentBootstrapProtocol.WriteSingleFrameAndClearKey(
        capturingOutput,
        capturingPayload,
        capturingWriteKey);
    Equal(true, capturingWriteKey.All(value => value == 0));
    Equal(true, capturingOutput.CapturedBuffer is not null);
    Equal(true, capturingOutput.CapturedBuffer!.All(value => value == 0));

    var invalidTagFrame = BuildReferenceBootstrapFrame();
    invalidTagFrame[invalidTagFrame.Length - 1] ^= 0x01;
    using (var invalidTagInput = new CapturingReadStream(invalidTagFrame))
    {
        var invalidTagKey = FixedBootstrapAuthenticationKey();
        BootstrapFails(
            AgentBootstrapValidationCode.InvalidTag,
            () => AgentBootstrapProtocol.ReadSingleFrameAndClearKey(
                invalidTagInput,
                invalidTagKey));
        Equal(true, invalidTagKey.All(value => value == 0));
        Equal(true, invalidTagInput.CapturedBuffers.Count > 0);
        Equal(true, invalidTagInput.CapturedBuffers.All(buffer => buffer.All(value => value == 0)));
    }

    var truncatedFrame = new byte[AgentBootstrapProtocol.HeaderSize + 1];
    var completeFrame = BuildReferenceBootstrapFrame();
    Buffer.BlockCopy(completeFrame, 0, truncatedFrame, 0, truncatedFrame.Length);
    Array.Clear(completeFrame, 0, completeFrame.Length);
    using (var truncatedInput = new CapturingReadStream(truncatedFrame))
    {
        var truncatedKey = FixedBootstrapAuthenticationKey();
        BootstrapFails(
            AgentBootstrapValidationCode.TruncatedFrame,
            () => AgentBootstrapProtocol.ReadSingleFrameAndClearKey(
                truncatedInput,
                truncatedKey));
        Equal(true, truncatedKey.All(value => value == 0));
        Equal(true, truncatedInput.CapturedBuffers.Count > 0);
        Equal(true, truncatedInput.CapturedBuffers.All(buffer => buffer.All(value => value == 0)));
    }

    using var failingPayload = CreateFixedOutboundBootstrapPayload();
    using var throwingOutput = new ThrowingCapturingWriteStream();
    var throwingWriteKey = FixedBootstrapAuthenticationKey();
    Throws<IOException>(() => AgentBootstrapProtocol.WriteSingleFrameAndClearKey(
        throwingOutput,
        failingPayload,
        throwingWriteKey));
    Equal(true, throwingWriteKey.All(value => value == 0));
    Equal(true, throwingOutput.CapturedBuffer is not null);
    Equal(true, throwingOutput.CapturedBuffer!.All(value => value == 0));
    BootstrapFails(
        AgentBootstrapValidationCode.AlreadyConsumed,
        () => failingPayload.DeriveDirectionKeys());
}

static void BootstrapWriteFailuresPoisonPayload()
{
    using (var partialPayload = CreateFixedOutboundBootstrapPayload())
    using (var partialOutput = new PartialWriteThenThrowStream())
    {
        var partialKey = FixedBootstrapAuthenticationKey();
        Throws<IOException>(() => AgentBootstrapProtocol.WriteSingleFrameAndClearKey(
            partialOutput,
            partialPayload,
            partialKey));
        Equal(true, partialKey.All(value => value == 0));
        Equal(true, partialOutput.DeliveredByteCount > 0);
        Equal(true, partialOutput.DeliveredByteCount < AgentBootstrapProtocol.HeaderSize + AgentBootstrapProtocol.MaximumBodyBytes);

        using var retryOutput = new MemoryStream();
        var retryKey = FixedBootstrapAuthenticationKey();
        BootstrapFails(
            AgentBootstrapValidationCode.AlreadyConsumed,
            () => AgentBootstrapProtocol.WriteSingleFrameAndClearKey(
                retryOutput,
                partialPayload,
                retryKey));
        Equal(0L, retryOutput.Length);
        Equal(true, retryKey.All(value => value == 0));
        BootstrapFails(
            AgentBootstrapValidationCode.AlreadyConsumed,
            () => partialPayload.DeriveDirectionKeys());
    }

    using (var flushPayload = CreateFixedOutboundBootstrapPayload())
    using (var flushOutput = new FlushThenThrowStream())
    {
        var flushKey = FixedBootstrapAuthenticationKey();
        Throws<IOException>(() => AgentBootstrapProtocol.WriteSingleFrameAndClearKey(
            flushOutput,
            flushPayload,
            flushKey));
        Equal(true, flushKey.All(value => value == 0));
        Equal(
            AgentBootstrapProtocol.HeaderSize + AgentBootstrapProtocol.MaximumBodyBytes,
            flushOutput.WrittenByteCount);

        using var retryOutput = new MemoryStream();
        BootstrapFails(
            AgentBootstrapValidationCode.AlreadyConsumed,
            () => AgentBootstrapProtocol.WriteSingleFrameAndClearKey(
                retryOutput,
                flushPayload,
                FixedBootstrapAuthenticationKey()));
        Equal(0L, retryOutput.Length);
        BootstrapFails(
            AgentBootstrapValidationCode.AlreadyConsumed,
            () => flushPayload.DeriveDirectionKeys());
    }
}

static void BootstrapPayloadAndKeysZeroOnDispose()
{
    var frame = BuildReferenceBootstrapFrame();
    var payload = AgentBootstrapProtocol.DecodeSingleFrameAndClear(
        frame,
        FixedBootstrapAuthenticationKey());
    var payloadIdField = typeof(AgentBootstrapPayload).GetField(
        "_bootstrapId",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Bootstrap id field not found.");
    var payloadSecretField = typeof(AgentBootstrapPayload).GetField(
        "_sessionSecret",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Bootstrap secret field not found.");
    var idReference = (byte[]?)payloadIdField.GetValue(payload)
        ?? throw new InvalidOperationException("Bootstrap id reference missing.");
    var secretReference = (byte[]?)payloadSecretField.GetValue(payload)
        ?? throw new InvalidOperationException("Bootstrap secret reference missing.");

    var keys = payload.DeriveDirectionKeys();
    Equal(true, idReference.All(value => value == 0));
    Equal(true, secretReference.All(value => value == 0));

    var hostKeyField = typeof(AgentBootstrapDirectionKeys).GetField(
        "_hostToAgentKey",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Host direction key field not found.");
    var agentKeyField = typeof(AgentBootstrapDirectionKeys).GetField(
        "_agentToHostKey",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Agent direction key field not found.");
    var hostKeyReference = (byte[]?)hostKeyField.GetValue(keys)
        ?? throw new InvalidOperationException("Host direction key missing.");
    var agentKeyReference = (byte[]?)agentKeyField.GetValue(keys)
        ?? throw new InvalidOperationException("Agent direction key missing.");

    keys.Dispose();
    Equal(true, hostKeyReference.All(value => value == 0));
    Equal(true, agentKeyReference.All(value => value == 0));
    payload.Dispose();
}

static void BootstrapDerivationIsSingleUse()
{
    using var payload = CreateFixedOutboundBootstrapPayload();

    BootstrapFails(
        AgentBootstrapValidationCode.InvalidPayloadState,
        () => payload.DeriveDirectionKeys());

    using (var firstOutput = new MemoryStream())
    {
        AgentBootstrapProtocol.WriteSingleFrameAndClearKey(
            firstOutput,
            payload,
            FixedBootstrapAuthenticationKey());
        Equal(
            AgentBootstrapProtocol.HeaderSize + AgentBootstrapProtocol.MaximumBodyBytes,
            checked((int)firstOutput.Length));
    }
    using (var replayOutput = new MemoryStream())
    {
        var replayKey = FixedBootstrapAuthenticationKey();
        BootstrapFails(
            AgentBootstrapValidationCode.AlreadyConsumed,
            () => AgentBootstrapProtocol.WriteSingleFrameAndClearKey(
                replayOutput,
                payload,
                replayKey));
        Equal(0L, replayOutput.Length);
        Equal(true, replayKey.All(value => value == 0));
    }

    using var keys = payload.DeriveDirectionKeys();
    BootstrapFails(
        AgentBootstrapValidationCode.AlreadyConsumed,
        () => payload.DeriveDirectionKeys());
    BootstrapFails(
        AgentBootstrapValidationCode.AlreadyConsumed,
        () => payload.CopyBootstrapId());
}

static void BootstrapDirectionRolesAndReflection()
{
    using var hostKeys = DeriveFixedHostBootstrapDirectionKeys();
    using var agentKeys = DeriveFixedInboundBootstrapDirectionKeys();

    using var hostConfirmationInbound = hostKeys.CreateConfirmationInboundGuard();
    using var agentConfirmationOutbound = agentKeys.CreateConfirmationOutboundAuthenticator();
    using var agentConfirmationInbound = agentKeys.CreateConfirmationInboundGuard();
    var confirmationEnvelope = CreateBootstrapEnvelope(1, "agent-confirmation-nonce");
    confirmationEnvelope.Mac = agentConfirmationOutbound.Sign(confirmationEnvelope);
    Equal(
        IpcValidationCode.Accepted,
        hostConfirmationInbound.ValidateAndAccept(confirmationEnvelope));
    Equal(
        IpcValidationCode.InvalidMac,
        agentConfirmationInbound.ValidateAndAccept(confirmationEnvelope));
    BootstrapFails(
        AgentBootstrapValidationCode.AlreadyConsumed,
        () => hostKeys.CreateConfirmationInboundGuard());
    BootstrapFails(
        AgentBootstrapValidationCode.AlreadyConsumed,
        () => agentKeys.CreateConfirmationOutboundAuthenticator());

    using var hostOutbound = hostKeys.CreateOutboundAuthenticator();
    using var hostInbound = hostKeys.CreateInboundGuard();
    using var agentOutbound = agentKeys.CreateOutboundAuthenticator();
    using var agentInbound = agentKeys.CreateInboundGuard();

    var hostEnvelope = CreateBootstrapEnvelope(1, "host-to-agent-nonce");
    hostEnvelope.Mac = hostOutbound.Sign(hostEnvelope);
    var agentEnvelope = CreateBootstrapEnvelope(1, "agent-to-host-nonce");
    agentEnvelope.Mac = agentOutbound.Sign(agentEnvelope);

    Equal(IpcValidationCode.InvalidMac, hostInbound.ValidateAndAccept(hostEnvelope));
    Equal(IpcValidationCode.Accepted, hostInbound.ValidateAndAccept(agentEnvelope));
    Equal(IpcValidationCode.InvalidMac, agentInbound.ValidateAndAccept(agentEnvelope));
    Equal(IpcValidationCode.Accepted, agentInbound.ValidateAndAccept(hostEnvelope));

    BootstrapFails(
        AgentBootstrapValidationCode.AlreadyConsumed,
        () => hostKeys.CreateInboundGuard());
    BootstrapFails(
        AgentBootstrapValidationCode.AlreadyConsumed,
        () => hostKeys.CreateOutboundAuthenticator());
    BootstrapFails(
        AgentBootstrapValidationCode.AlreadyConsumed,
        () => agentKeys.CreateInboundGuard());
    BootstrapFails(
        AgentBootstrapValidationCode.AlreadyConsumed,
        () => agentKeys.CreateOutboundAuthenticator());
}

static void BootstrapContextChangesDirectionKeys()
{
    var baseline = SignWithBootstrapContext(
        FixedBootstrapSessionId(),
        FixedBootstrapPipeName(),
        FixedBootstrapId());
    var otherPipe = SignWithBootstrapContext(
        FixedBootstrapSessionId(),
        "codex-autocad-00112233445566778899aabbccddeeff",
        FixedBootstrapId());
    var otherSession = SignWithBootstrapContext(
        "ffeeddccbbaa99887766554433221100",
        FixedBootstrapPipeName(),
        FixedBootstrapId());
    var changedId = FixedBootstrapId();
    changedId[0] ^= 0x5A;
    var otherId = SignWithBootstrapContext(
        FixedBootstrapSessionId(),
        FixedBootstrapPipeName(),
        changedId);

    Equal(false, string.Equals(baseline, otherPipe, StringComparison.Ordinal));
    Equal(false, string.Equals(baseline, otherSession, StringComparison.Ordinal));
    Equal(false, string.Equals(baseline, otherId, StringComparison.Ordinal));
    Array.Clear(changedId, 0, changedId.Length);
}

static void BootstrapIdentifierAndSizeRules()
{
    BootstrapFails(
        AgentBootstrapValidationCode.InvalidSessionId,
        () => AgentBootstrapPayload.CreateRandom(
            "00112233445566778899AABBCCDDEEFF",
            FixedBootstrapPipeName()));
    BootstrapFails(
        AgentBootstrapValidationCode.InvalidSessionId,
        () => AgentBootstrapPayload.CreateRandom(
            "00112233445566778899aabbccddeef",
            FixedBootstrapPipeName()));
    BootstrapFails(
        AgentBootstrapValidationCode.InvalidPipeName,
        () => AgentBootstrapPayload.CreateRandom(
            FixedBootstrapSessionId(),
            "other-autocad-ffeeddccbbaa99887766554433221100"));
    BootstrapFails(
        AgentBootstrapValidationCode.InvalidPipeName,
        () => AgentBootstrapPayload.CreateRandom(
            FixedBootstrapSessionId(),
            "codex-autocad-ffeeddccbbaa9988776655443322110G"));
    BootstrapFails(
        AgentBootstrapValidationCode.InvalidUtf8,
        () => AgentBootstrapPayload.CreateRandom(
            "\uD800" + new string('0', AgentBootstrapProtocol.SessionIdBytes - 1),
            FixedBootstrapPipeName()));
}

static void BootstrapCreateRandomProducesUsableMaterial()
{
    var authenticationKey = AgentBootstrapProtocol.CreateAuthenticationKey();
    Equal(AgentBootstrapProtocol.AuthenticationKeySize, authenticationKey.Length);
    Equal(false, authenticationKey.All(value => value == 0));
    Array.Clear(authenticationKey, 0, authenticationKey.Length);

    using var payload = AgentBootstrapPayload.CreateRandom(
        FixedBootstrapSessionId(),
        FixedBootstrapPipeName());
    var bootstrapId = payload.CopyBootstrapId();
    Equal(AgentBootstrapProtocol.BootstrapIdSize, bootstrapId.Length);
    Equal(false, bootstrapId.All(value => value == 0));
    Array.Clear(bootstrapId, 0, bootstrapId.Length);

    using var output = new MemoryStream();
    AgentBootstrapProtocol.WriteSingleFrameAndClearKey(
        output,
        payload,
        AgentBootstrapProtocol.CreateAuthenticationKey());
    using var keys = payload.DeriveDirectionKeys();
    Equal(
        false,
        string.Equals(
            ReadPrivateDirectionKeyHex(keys, "_hostToAgentKey"),
            ReadPrivateDirectionKeyHex(keys, "_agentToHostKey"),
            StringComparison.Ordinal));
}

static void ValidEnvelopePasses()
{
    var secret = IpcSessionSecret.Generate();
    var envelope = CreateEnvelope("session-a", 1, "nonce-1");
    envelope.Mac = new IpcEnvelopeAuthenticator(secret).Sign(envelope);
    Equal(IpcValidationCode.Accepted, new IpcSessionGuard("session-a", secret).ValidateAndAccept(envelope));
}

static void TamperedPayloadFails()
{
    var secret = IpcSessionSecret.Generate();
    var envelope = CreateEnvelope("session-a", 1, "nonce-1");
    envelope.Mac = new IpcEnvelopeAuthenticator(secret).Sign(envelope);
    envelope.PayloadJson = "{\"unsafe\":true}";
    Equal(IpcValidationCode.InvalidMac, new IpcSessionGuard("session-a", secret).ValidateAndAccept(envelope));
}

static void CrossSessionFails()
{
    var secret = IpcSessionSecret.Generate();
    var envelope = CreateEnvelope("session-b", 1, "nonce-1");
    envelope.Mac = new IpcEnvelopeAuthenticator(secret).Sign(envelope);
    Equal(IpcValidationCode.InvalidSession, new IpcSessionGuard("session-a", secret).ValidateAndAccept(envelope));
}

static void ReplayedSequenceFails()
{
    var secret = IpcSessionSecret.Generate();
    var authenticator = new IpcEnvelopeAuthenticator(secret);
    var guard = new IpcSessionGuard("session-a", secret);
    var first = CreateEnvelope("session-a", 1, "nonce-1");
    first.Mac = authenticator.Sign(first);
    Equal(IpcValidationCode.Accepted, guard.ValidateAndAccept(first));

    var replay = CreateEnvelope("session-a", 1, "nonce-2");
    replay.Mac = authenticator.Sign(replay);
    Equal(IpcValidationCode.InvalidSequence, guard.ValidateAndAccept(replay));
}

static void SequenceBoundsFailClosed()
{
    var secret = IpcSessionSecret.Generate();
    using var authenticator = new IpcEnvelopeAuthenticator(secret);
    using var guard = new IpcSessionGuard("session-a", secret);

    foreach (var invalidSequence in new[] { 0L, -1L, long.MinValue })
    {
        var invalid = CreateEnvelope("session-a", invalidSequence, "nonce-" + invalidSequence);
        invalid.Mac = authenticator.Sign(invalid);
        Equal(IpcValidationCode.InvalidSequence, guard.ValidateAndAccept(invalid));
    }

    var sequenceField = typeof(IpcSessionGuard).GetField(
        "_lastAcceptedSequence",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("IpcSessionGuard sequence field not found.");
    sequenceField.SetValue(guard, long.MaxValue);

    var wrapped = CreateEnvelope("session-a", long.MinValue, "nonce-wrapped");
    wrapped.Mac = authenticator.Sign(wrapped);
    Equal(IpcValidationCode.InvalidSequence, guard.ValidateAndAccept(wrapped));
}
static void ReplayedNonceFails()
{
    var secret = IpcSessionSecret.Generate();
    var authenticator = new IpcEnvelopeAuthenticator(secret);
    var guard = new IpcSessionGuard("session-a", secret);
    var first = CreateEnvelope("session-a", 1, "nonce-1");
    first.Mac = authenticator.Sign(first);
    Equal(IpcValidationCode.Accepted, guard.ValidateAndAccept(first));

    var replay = CreateEnvelope("session-a", 2, "nonce-1");
    replay.Mac = authenticator.Sign(replay);
    Equal(IpcValidationCode.ReplayedNonce, guard.ValidateAndAccept(replay));
}

static void InvalidSecretLengthsFail()
{
    Throws<ArgumentException>(() => _ = new IpcEnvelopeAuthenticator(new byte[16]));
    Throws<ArgumentException>(() => _ = new IpcEnvelopeAuthenticator(new byte[33]));
}

static void InitialSequenceGapFails()
{
    var secret = IpcSessionSecret.Generate();
    using var authenticator = new IpcEnvelopeAuthenticator(secret);
    using var guard = new IpcSessionGuard("session-a", secret);
    var skipped = CreateEnvelope("session-a", 2, "nonce-2");
    skipped.Mac = authenticator.Sign(skipped);
    Equal(IpcValidationCode.InvalidSequence, guard.ValidateAndAccept(skipped));
}

static void InvalidMacDoesNotAdvanceGuardState()
{
    var secret = IpcSessionSecret.Generate();
    using var authenticator = new IpcEnvelopeAuthenticator(secret);
    using var guard = new IpcSessionGuard("session-a", secret);
    var invalid = CreateEnvelope("session-a", 1, "nonce-1");
    invalid.Mac = new string('0', 64);
    Equal(IpcValidationCode.InvalidMac, guard.ValidateAndAccept(invalid));

    var valid = CreateEnvelope("session-a", 1, "nonce-1");
    valid.Mac = authenticator.Sign(valid);
    Equal(IpcValidationCode.Accepted, guard.ValidateAndAccept(valid));
}

static void AuthenticatorSecretIsZeroedOnDispose()
{
    var original = Enumerable.Range(1, IpcSessionSecret.SizeInBytes).Select(value => (byte)value).ToArray();
    var authenticator = new IpcEnvelopeAuthenticator(original);
    var field = typeof(IpcEnvelopeAuthenticator).GetField(
        "_sessionSecret",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("IpcEnvelopeAuthenticator secret field not found.");
    var privateCopy = (byte[]?)field.GetValue(authenticator)
        ?? throw new InvalidOperationException("IpcEnvelopeAuthenticator secret copy missing.");
    Equal(false, ReferenceEquals(original, privateCopy));
    authenticator.Dispose();
    Equal(true, privateCopy.All(value => value == 0));
    Equal(false, original.All(value => value == 0));
}

static void NullSignedFieldsFail()
{
    var secret = IpcSessionSecret.Generate();
    using var authenticator = new IpcEnvelopeAuthenticator(secret);
    var emptyCorrelation = CreateEnvelope("session-a", 1, "nonce-1");
    emptyCorrelation.CorrelationId = string.Empty;
    var emptyMac = authenticator.Sign(emptyCorrelation);

    var nullCorrelation = CreateEnvelope("session-a", 1, "nonce-1");
    nullCorrelation.CorrelationId = null!;
    Throws<ArgumentException>(() => IpcCanonicalEnvelopeEncoding.GetBytes(nullCorrelation));
    Throws<ArgumentException>(() => authenticator.Sign(nullCorrelation));
    nullCorrelation.Mac = emptyMac;
    Equal(false, authenticator.Verify(nullCorrelation));
    using var correlationGuard = new IpcSessionGuard("session-a", secret);
    Equal(IpcValidationCode.InvalidMetadata, correlationGuard.ValidateAndAccept(nullCorrelation));

    var nullPayload = CreateEnvelope("session-a", 1, "nonce-2");
    nullPayload.PayloadJson = null!;
    nullPayload.Mac = new string('0', 64);
    using var payloadGuard = new IpcSessionGuard("session-a", secret);
    Equal(IpcValidationCode.InvalidMetadata, payloadGuard.ValidateAndAccept(nullPayload));
}

static void MalformedUnicodeFailsClosed()
{
    var secret = IpcSessionSecret.Generate();
    var malformed = CreateEnvelope("session-a", 1, "nonce-1");
    malformed.PayloadJson = "\uD800";
    using var authenticator = new IpcEnvelopeAuthenticator(secret);
    Throws<System.Text.EncoderFallbackException>(() => authenticator.Sign(malformed));
    malformed.Mac = new string('0', 64);
    Equal(false, authenticator.Verify(malformed));
    using var guard = new IpcSessionGuard("session-a", secret);
    Equal(IpcValidationCode.InvalidMetadata, guard.ValidateAndAccept(malformed));
}

static void NonceCapacityFailsClosedAndExpiresAtBoundary()
{
    var secret = IpcSessionSecret.Generate();
    var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero));
    var retention = TimeSpan.FromMinutes(1);
    var options = new IpcSessionGuardOptions
    {
        MaximumNonceHistoryEntries = 2,
        NonceRetention = retention,
        Clock = clock
    };
    using var authenticator = new IpcEnvelopeAuthenticator(secret);
    using var guard = new IpcSessionGuard("session-a", secret, options);

    var first = CreateEnvelope("session-a", 1, "nonce-1");
    first.Mac = authenticator.Sign(first);
    Equal(IpcValidationCode.Accepted, guard.ValidateAndAccept(first));

    var second = CreateEnvelope("session-a", 2, "nonce-2");
    second.Mac = authenticator.Sign(second);
    Equal(IpcValidationCode.Accepted, guard.ValidateAndAccept(second));

    var third = CreateEnvelope("session-a", 3, "nonce-3");
    third.Mac = authenticator.Sign(third);
    Equal(IpcValidationCode.NonceHistoryCapacityExceeded, guard.ValidateAndAccept(third));

    clock.Advance(retention - TimeSpan.FromTicks(1));
    Equal(IpcValidationCode.NonceHistoryCapacityExceeded, guard.ValidateAndAccept(third));

    clock.Advance(TimeSpan.FromTicks(1));
    Equal(IpcValidationCode.Accepted, guard.ValidateAndAccept(third));
    Equal(IpcValidationCode.InvalidSequence, guard.ValidateAndAccept(first));
}

static void NonceFloodCannotExceedHistoryCapacity()
{
    const int capacity = 32;
    var secret = IpcSessionSecret.Generate();
    var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero));
    var options = new IpcSessionGuardOptions
    {
        MaximumNonceHistoryEntries = capacity,
        NonceRetention = TimeSpan.FromMinutes(1),
        Clock = clock
    };
    using var authenticator = new IpcEnvelopeAuthenticator(secret);
    using var guard = new IpcSessionGuard("session-a", secret, options);

    for (var sequence = 1; sequence <= capacity; sequence++)
    {
        var envelope = CreateEnvelope("session-a", sequence, "nonce-" + sequence);
        envelope.Mac = authenticator.Sign(envelope);
        Equal(IpcValidationCode.Accepted, guard.ValidateAndAccept(envelope));
    }

    for (var attempt = 0; attempt < 256; attempt++)
    {
        var flooded = CreateEnvelope("session-a", capacity + 1L, "flood-" + attempt);
        flooded.Mac = authenticator.Sign(flooded);
        Equal(IpcValidationCode.NonceHistoryCapacityExceeded, guard.ValidateAndAccept(flooded));
    }

    clock.Advance(TimeSpan.FromMinutes(1));
    var recovered = CreateEnvelope("session-a", capacity + 1L, "recovered");
    recovered.Mac = authenticator.Sign(recovered);
    Equal(IpcValidationCode.Accepted, guard.ValidateAndAccept(recovered));
}

static void OversizedNonceFails()
{
    var secret = IpcSessionSecret.Generate();
    using var authenticator = new IpcEnvelopeAuthenticator(secret);
    using var guard = new IpcSessionGuard("session-a", secret);
    var envelope = CreateEnvelope(
        "session-a",
        1,
        new string('n', IpcSessionGuard.MaximumNonceCharacters + 1));
    envelope.Mac = authenticator.Sign(envelope);
    Equal(IpcValidationCode.InvalidNonce, guard.ValidateAndAccept(envelope));
}

static void InvalidNonceHistoryOptionsFail()
{
    var options = new IpcSessionGuardOptions { MaximumNonceHistoryEntries = 0 };
    Throws<ArgumentOutOfRangeException>(() =>
        _ = new IpcSessionGuard("session-a", IpcSessionSecret.Generate(), options));
}

static byte[] BuildReferenceBootstrapFrame(
    string? sessionId = null,
    string? pipeName = null,
    byte[]? bootstrapId = null,
    byte[]? sessionSecret = null,
    byte[]? authenticationKey = null)
{
    sessionId ??= FixedBootstrapSessionId();
    pipeName ??= FixedBootstrapPipeName();
    var id = bootstrapId is null ? FixedBootstrapId() : (byte[])bootstrapId.Clone();
    var secret = sessionSecret is null
        ? FixedBootstrapSessionSecret()
        : (byte[])sessionSecret.Clone();
    var key = authenticationKey is null
        ? FixedBootstrapAuthenticationKey()
        : (byte[])authenticationKey.Clone();
    var sessionBytes = Encoding.ASCII.GetBytes(sessionId);
    var pipeBytes = Encoding.ASCII.GetBytes(pipeName);
    byte[]? tag = null;
    try
    {
        var bodyLength = AgentBootstrapProtocol.BootstrapIdSize
            + 6
            + sessionBytes.Length
            + pipeBytes.Length
            + AgentBootstrapProtocol.SessionSecretSize
            + AgentBootstrapProtocol.TagSize;
        var frame = new byte[AgentBootstrapProtocol.HeaderSize + bodyLength];
        Buffer.BlockCopy(Encoding.ASCII.GetBytes("CDXCADB1"), 0, frame, 0, 8);
        WriteUInt16(frame, 8, AgentBootstrapProtocol.CurrentVersion);
        WriteUInt16(frame, 10, AgentBootstrapProtocol.SupportedFlags);
        WriteUInt32(frame, 12, bodyLength);

        var offset = AgentBootstrapProtocol.HeaderSize;
        Buffer.BlockCopy(id, 0, frame, offset, id.Length);
        offset += id.Length;
        WriteUInt16(frame, offset, sessionBytes.Length);
        offset += 2;
        WriteUInt16(frame, offset, pipeBytes.Length);
        offset += 2;
        WriteUInt16(frame, offset, secret.Length);
        offset += 2;
        Buffer.BlockCopy(sessionBytes, 0, frame, offset, sessionBytes.Length);
        offset += sessionBytes.Length;
        Buffer.BlockCopy(pipeBytes, 0, frame, offset, pipeBytes.Length);
        offset += pipeBytes.Length;
        Buffer.BlockCopy(secret, 0, frame, offset, secret.Length);
        offset += secret.Length;

        tag = ComputeReferenceBootstrapTag(key, frame, offset);
        Buffer.BlockCopy(tag, 0, frame, offset, tag.Length);
        return frame;
    }
    finally
    {
        Array.Clear(id, 0, id.Length);
        Array.Clear(secret, 0, secret.Length);
        Array.Clear(key, 0, key.Length);
        Array.Clear(sessionBytes, 0, sessionBytes.Length);
        Array.Clear(pipeBytes, 0, pipeBytes.Length);
        if (tag is not null)
        {
            Array.Clear(tag, 0, tag.Length);
        }
    }
}

static byte[] ComputeReferenceBootstrapTag(
    byte[] authenticationKey,
    byte[] frame,
    int authenticatedFrameBytes)
{
    var domain = Encoding.ASCII.GetBytes("Codex.AutoCAD.AgentBootstrap.Frame.v1\0");
    var input = new byte[domain.Length + authenticatedFrameBytes];
    try
    {
        Buffer.BlockCopy(domain, 0, input, 0, domain.Length);
        Buffer.BlockCopy(frame, 0, input, domain.Length, authenticatedFrameBytes);
        using var hmac = new HMACSHA256(authenticationKey);
        return hmac.ComputeHash(input);
    }
    finally
    {
        Array.Clear(domain, 0, domain.Length);
        Array.Clear(input, 0, input.Length);
    }
}

static string ComputeReferenceBootstrapDirectionContextSha256(string roleLabel)
{
    var domain = Encoding.ASCII.GetBytes("Codex.AutoCAD.AgentBootstrap.Direction.v1\0");
    var labelBytes = Encoding.ASCII.GetBytes(roleLabel);
    var bootstrapId = FixedBootstrapId();
    var sessionBytes = Encoding.ASCII.GetBytes(FixedBootstrapSessionId());
    var pipeBytes = Encoding.ASCII.GetBytes(FixedBootstrapPipeName());
    var context = new byte[
        domain.Length
        + 2
        + 2 + labelBytes.Length
        + AgentBootstrapProtocol.BootstrapIdSize
        + 2 + sessionBytes.Length
        + 2 + pipeBytes.Length];
    byte[]? digest = null;
    try
    {
        var offset = 0;
        Buffer.BlockCopy(domain, 0, context, offset, domain.Length);
        offset += domain.Length;
        WriteUInt16(context, offset, AgentBootstrapProtocol.CurrentVersion);
        offset += 2;
        WriteUInt16(context, offset, labelBytes.Length);
        offset += 2;
        Buffer.BlockCopy(labelBytes, 0, context, offset, labelBytes.Length);
        offset += labelBytes.Length;
        Buffer.BlockCopy(bootstrapId, 0, context, offset, bootstrapId.Length);
        offset += bootstrapId.Length;
        WriteUInt16(context, offset, sessionBytes.Length);
        offset += 2;
        Buffer.BlockCopy(sessionBytes, 0, context, offset, sessionBytes.Length);
        offset += sessionBytes.Length;
        WriteUInt16(context, offset, pipeBytes.Length);
        offset += 2;
        Buffer.BlockCopy(pipeBytes, 0, context, offset, pipeBytes.Length);

        using var sha256 = SHA256.Create();
        digest = sha256.ComputeHash(context);
        return EncodeHex(digest);
    }
    finally
    {
        Array.Clear(domain, 0, domain.Length);
        Array.Clear(labelBytes, 0, labelBytes.Length);
        Array.Clear(bootstrapId, 0, bootstrapId.Length);
        Array.Clear(sessionBytes, 0, sessionBytes.Length);
        Array.Clear(pipeBytes, 0, pipeBytes.Length);
        Array.Clear(context, 0, context.Length);
        if (digest is not null)
        {
            Array.Clear(digest, 0, digest.Length);
        }
    }
}

static void AssertBootstrapCancellationAtOffset(
    byte[] frame,
    int blockAtOffset,
    bool cancelBeforeStart)
{
    using var cancellation = new CancellationTokenSource();
    using var input = new BlockingAtOffsetReadStream(
        (byte[])frame.Clone(),
        blockAtOffset);
    var key = FixedBootstrapAuthenticationKey();
    if (cancelBeforeStart)
    {
        cancellation.Cancel();
    }

    var readTask = AgentBootstrapProtocol.ReadSingleFrameAndClearKeyAsync(
        input,
        key,
        cancellation.Token);
    if (!cancelBeforeStart)
    {
        Equal(true, input.BlockedReadStarted.Wait(TimeSpan.FromSeconds(5)));
        cancellation.Cancel();
    }

    Throws<OperationCanceledException>(() => readTask.GetAwaiter().GetResult());
    Equal(true, key.All(value => value == 0));
    Equal(true, input.CapturedBuffers.Count > 0);
    Equal(true, input.CapturedBuffers.All(buffer => buffer.All(value => value == 0)));
}

static void RetagReferenceBootstrapFrame(byte[] frame, byte[] authenticationKey)
{
    var tagOffset = frame.Length - AgentBootstrapProtocol.TagSize;
    var tag = ComputeReferenceBootstrapTag(authenticationKey, frame, tagOffset);
    try
    {
        Buffer.BlockCopy(tag, 0, frame, tagOffset, tag.Length);
    }
    finally
    {
        Array.Clear(tag, 0, tag.Length);
    }
}

static void AssertBootstrapMutationFails(
    Action<byte[]> mutate,
    AgentBootstrapValidationCode expectedCode,
    bool retag)
{
    var frame = BuildReferenceBootstrapFrame();
    mutate(frame);
    if (retag)
    {
        var retagKey = FixedBootstrapAuthenticationKey();
        try
        {
            RetagReferenceBootstrapFrame(frame, retagKey);
        }
        finally
        {
            Array.Clear(retagKey, 0, retagKey.Length);
        }
    }

    BootstrapFails(
        expectedCode,
        () => AgentBootstrapProtocol.DecodeSingleFrameAndClear(
            frame,
            FixedBootstrapAuthenticationKey()));
    Equal(true, frame.All(value => value == 0));
}

static string SignWithBootstrapContext(
    string sessionId,
    string pipeName,
    byte[] bootstrapId)
{
    var frame = BuildReferenceBootstrapFrame(
        sessionId: sessionId,
        pipeName: pipeName,
        bootstrapId: bootstrapId);
    using var payload = AgentBootstrapProtocol.DecodeSingleFrameAndClear(
        frame,
        FixedBootstrapAuthenticationKey());
    using var keys = payload.DeriveDirectionKeys();
    using var authenticator = keys.CreateOutboundAuthenticator();
    return authenticator.Sign(CreateBootstrapVectorEnvelope());
}

static AgentBootstrapDirectionKeys DeriveFixedInboundBootstrapDirectionKeys()
{
    using var payload = AgentBootstrapProtocol.DecodeSingleFrameAndClear(
        BuildReferenceBootstrapFrame(),
        FixedBootstrapAuthenticationKey());
    return payload.DeriveDirectionKeys();
}

static AgentBootstrapDirectionKeys DeriveFixedHostBootstrapDirectionKeys()
{
    using var payload = CreateFixedOutboundBootstrapPayload();
    using var output = new MemoryStream();
    AgentBootstrapProtocol.WriteSingleFrameAndClearKey(
        output,
        payload,
        FixedBootstrapAuthenticationKey());
    return payload.DeriveDirectionKeys();
}

static AgentBootstrapPayload CreateFixedOutboundBootstrapPayload()
{
    return CreateOutboundBootstrapPayload(
        FixedBootstrapSessionId(),
        FixedBootstrapPipeName(),
        FixedBootstrapId());
}

static AgentBootstrapPayload CreateOutboundBootstrapPayload(
    string sessionId,
    string pipeName,
    byte[] bootstrapId)
{
    var idCopy = (byte[])bootstrapId.Clone();
    var sessionSecret = FixedBootstrapSessionSecret();
    try
    {
        var constructor = typeof(AgentBootstrapPayload)
            .GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 5);
        var parameters = constructor.GetParameters();
        var hostOutbound = Enum.ToObject(parameters[4].ParameterType, 1);
        return (AgentBootstrapPayload)(constructor.Invoke(new object[]
        {
            sessionId,
            pipeName,
            idCopy,
            sessionSecret,
            hostOutbound
        }) ?? throw new InvalidOperationException("Bootstrap payload constructor returned null."));
    }
    finally
    {
        Array.Clear(idCopy, 0, idCopy.Length);
        Array.Clear(sessionSecret, 0, sessionSecret.Length);
    }
}

static IpcEnvelope CreateBootstrapVectorEnvelope()
{
    return new IpcEnvelope
    {
        ProtocolVersion = 1,
        MessageId = "bootstrap-vector",
        CorrelationId = string.Empty,
        SessionId = FixedBootstrapSessionId(),
        Sequence = 1,
        MessageType = "agent.hello",
        PayloadJson = "{\"bootstrap\":\"v1\"}",
        Nonce = "0123456789abcdef0123456789abcdef"
    };
}

static IpcEnvelope CreateBootstrapEnvelope(long sequence, string nonce)
{
    return new IpcEnvelope
    {
        ProtocolVersion = 1,
        MessageId = "bootstrap-direction-" + sequence,
        CorrelationId = string.Empty,
        SessionId = FixedBootstrapSessionId(),
        Sequence = sequence,
        MessageType = "agent.hello",
        PayloadJson = "{}",
        Nonce = nonce
    };
}

static string FixedBootstrapSessionId()
{
    return "00112233445566778899aabbccddeeff";
}

static string FixedBootstrapPipeName()
{
    return "codex-autocad-ffeeddccbbaa99887766554433221100";
}

static byte[] FixedBootstrapAuthenticationKey()
{
    return Enumerable.Range(0x00, AgentBootstrapProtocol.AuthenticationKeySize)
        .Select(value => (byte)value)
        .ToArray();
}

static byte[] FixedBootstrapId()
{
    return Enumerable.Range(0x10, AgentBootstrapProtocol.BootstrapIdSize)
        .Select(value => (byte)value)
        .ToArray();
}

static byte[] FixedBootstrapSessionSecret()
{
    return Enumerable.Range(0x20, AgentBootstrapProtocol.SessionSecretSize)
        .Select(value => (byte)value)
        .ToArray();
}

static void BootstrapFails(AgentBootstrapValidationCode expectedCode, Action action)
{
    var exception = Throws<AgentBootstrapException>(action);
    Equal(expectedCode, exception.ValidationCode);
}

static string ReadPrivateDirectionKeyHex(AgentBootstrapDirectionKeys keys, string fieldName)
{
    var field = typeof(AgentBootstrapDirectionKeys).GetField(
        fieldName,
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Direction key field not found: " + fieldName);
    var value = (byte[]?)field.GetValue(keys)
        ?? throw new InvalidOperationException("Direction key value missing: " + fieldName);
    return EncodeHex(value);
}

static void WriteUInt16(byte[] bytes, int offset, int value)
{
    bytes[offset] = (byte)value;
    bytes[offset + 1] = (byte)(value >> 8);
}

static void WriteUInt32(byte[] bytes, int offset, int value)
{
    bytes[offset] = (byte)value;
    bytes[offset + 1] = (byte)(value >> 8);
    bytes[offset + 2] = (byte)(value >> 16);
    bytes[offset + 3] = (byte)(value >> 24);
}

static IpcEnvelope CreateEnvelope(string sessionId, long sequence, string nonce)
{
    return new IpcEnvelope
    {
        MessageId = Guid.NewGuid().ToString("N"),
        SessionId = sessionId,
        Sequence = sequence,
        MessageType = "cad.context",
        PayloadJson = "{}",
        Nonce = nonce
    };
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, actual {actual}.");
    }
}

static TException Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static byte[] DecodeHex(string value)
{
    if ((value.Length & 1) != 0)
    {
        throw new ArgumentException("Hex length must be even.", nameof(value));
    }

    var bytes = new byte[value.Length / 2];
    for (var index = 0; index < bytes.Length; index++)
    {
        bytes[index] = Convert.ToByte(value.Substring(index * 2, 2), 16);
    }

    return bytes;
}

static string EncodeHex(byte[] bytes)
{
    var builder = new System.Text.StringBuilder(bytes.Length * 2);
    foreach (var value in bytes)
    {
        builder.Append(value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
    }

    return builder.ToString();
}

sealed class ThrowOnEofProbeStream : Stream
{
    private byte[] _data;
    private int _position;

    public ThrowOnEofProbeStream(byte[] data)
    {
        _data = data;
    }

    public bool EofProbeAttempted { get; private set; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _data.Length;
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _data.Length)
        {
            EofProbeAttempted = true;
            throw new IOException("EOF probe must not run before bootstrap authentication fails.");
        }

        var available = Math.Min(count, _data.Length - _position);
        Buffer.BlockCopy(_data, _position, buffer, offset, available);
        _position += available;
        return available;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Array.Clear(_data, 0, _data.Length);
            _data = new byte[0];
        }

        base.Dispose(disposing);
    }
}

sealed class OneByteReadStream : Stream
{
    private byte[] _data;
    private int _position;

    public OneByteReadStream(byte[] data)
    {
        _data = data;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _data.Length;
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _data.Length || count == 0)
        {
            return 0;
        }

        buffer[offset] = _data[_position++];
        return 1;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            var canceled = new TaskCompletionSource<int>();
            canceled.SetCanceled();
            return canceled.Task;
        }

        return Task.FromResult(Read(buffer, offset, count));
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        Array.Clear(_data, 0, _data.Length);
        _data = new byte[0];
        base.Dispose(disposing);
    }
}

sealed class BlockingAtOffsetReadStream : Stream
{
    private byte[] _data;
    private readonly int _blockAtOffset;
    private int _position;
    private CancellationTokenRegistration _cancellationRegistration;
    private TaskCompletionSource<int>? _blockedRead;

    public BlockingAtOffsetReadStream(byte[] data, int blockAtOffset)
    {
        if (blockAtOffset < 0 || blockAtOffset > data.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(blockAtOffset));
        }

        _data = data;
        _blockAtOffset = blockAtOffset;
    }

    public ManualResetEventSlim BlockedReadStarted { get; } = new ManualResetEventSlim(false);

    public List<byte[]> CapturedBuffers { get; } = new List<byte[]>();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _data.Length;
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        CapturedBuffers.Add(buffer);
        if (cancellationToken.IsCancellationRequested)
        {
            var canceled = new TaskCompletionSource<int>();
            canceled.SetCanceled();
            return canceled.Task;
        }

        if (_position >= _blockAtOffset)
        {
            if (_blockedRead is not null)
            {
                throw new InvalidOperationException("Only one blocked read is supported.");
            }

            var blockedRead = new TaskCompletionSource<int>();
            _blockedRead = blockedRead;
            _cancellationRegistration = cancellationToken.Register(
                () => blockedRead.TrySetCanceled());
            BlockedReadStarted.Set();
            return blockedRead.Task;
        }

        var available = Math.Min(count, Math.Min(_data.Length - _position, _blockAtOffset - _position));
        Buffer.BlockCopy(_data, _position, buffer, offset, available);
        _position += available;
        return Task.FromResult(available);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        _cancellationRegistration.Dispose();
        BlockedReadStarted.Dispose();
        Array.Clear(_data, 0, _data.Length);
        _data = new byte[0];
        base.Dispose(disposing);
    }
}

sealed class CapturingReadStream : Stream
{
    private byte[] _data;
    private int _position;

    public CapturingReadStream(byte[] data)
    {
        _data = data;
    }

    public List<byte[]> CapturedBuffers { get; } = new List<byte[]>();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _data.Length;
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        CapturedBuffers.Add(buffer);
        var available = Math.Min(count, _data.Length - _position);
        if (available <= 0)
        {
            return 0;
        }

        Buffer.BlockCopy(_data, _position, buffer, offset, available);
        _position += available;
        return available;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        Array.Clear(_data, 0, _data.Length);
        _data = new byte[0];
        base.Dispose(disposing);
    }
}

sealed class CapturingWriteStream : MemoryStream
{
    public byte[]? CapturedBuffer { get; private set; }

    public override void Write(byte[] buffer, int offset, int count)
    {
        CapturedBuffer = buffer;
        base.Write(buffer, offset, count);
    }
}

sealed class ThrowingCapturingWriteStream : Stream
{
    public byte[]? CapturedBuffer { get; private set; }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        throw new InvalidOperationException("Flush must not run after the injected write failure.");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        CapturedBuffer = buffer;
        throw new IOException("Injected bootstrap write failure.");
    }
}

sealed class PartialWriteThenThrowStream : Stream
{
    private byte[] _delivered = new byte[0];

    public int DeliveredByteCount => _delivered.Length;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        throw new InvalidOperationException("Flush must not run after the injected partial write failure.");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        var deliveredCount = Math.Max(1, count / 2);
        _delivered = new byte[deliveredCount];
        Buffer.BlockCopy(buffer, offset, _delivered, 0, deliveredCount);
        throw new IOException("Injected bootstrap partial write failure.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Array.Clear(_delivered, 0, _delivered.Length);
            _delivered = new byte[0];
        }

        base.Dispose(disposing);
    }
}

sealed class FlushThenThrowStream : Stream
{
    private byte[] _written = new byte[0];

    public int WrittenByteCount => _written.Length;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        throw new IOException("Injected bootstrap flush failure after write delivery.");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _written = new byte[count];
        Buffer.BlockCopy(buffer, offset, _written, 0, count);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Array.Clear(_written, 0, _written.Length);
            _written = new byte[0];
        }

        base.Dispose(disposing);
    }
}

sealed class ManualTimeProvider : IIpcClock
{
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public DateTimeOffset GetUtcNow()
    {
        return _utcNow;
    }

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        _utcNow = _utcNow.Add(elapsed);
    }
}

sealed class SpecCase
{
    public SpecCase(string name, Action run)
    {
        Name = name;
        Run = run;
    }

    public string Name { get; }

    public Action Run { get; }
}
