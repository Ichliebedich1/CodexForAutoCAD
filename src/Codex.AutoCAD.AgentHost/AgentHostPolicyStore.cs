using System.Text.Json;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.AgentHost;

/// <summary>
/// M4.1 分层策略加载结果。失败时不返回部分策略；成功时报告各层是否存在，
/// 但不输出任何路径、用户名或原始配置内容。
/// </summary>
public sealed class AgentPolicyLoadResult
{
    public bool Accepted { get; set; }

    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>产生错误的配置层标识，不含路径。</summary>
    public string ErrorLayer { get; set; } = string.Empty;

    public ResolvedAgentPolicy? Policy { get; set; }

    public bool MachinePolicyPresent { get; set; }

    public bool AdministratorPolicyPresent { get; set; }

    public bool UserPolicyPresent { get; set; }

    internal static AgentPolicyLoadResult Reject(string errorCode, string layer)
        => new AgentPolicyLoadResult { Accepted = false, ErrorCode = errorCode, ErrorLayer = layer };
}

/// <summary>
/// M4.1 策略配置不可用。只公开稳定错误码与配置层标识，不携带路径、用户名或文件内容，
/// 使管理员能据此排查，同时不让配置细节经诊断通道外泄。
/// </summary>
public sealed class AgentHostPolicyConfigurationException : Exception
{
    public AgentHostPolicyConfigurationException(string errorCode, string layer)
        : base("Agent policy configuration is unusable; refusing to start.")
    {
        ErrorCode = errorCode;
        Layer = layer;
    }

    public string ErrorCode { get; }

    public string Layer { get; }

    public override string ToString()
        => nameof(AgentHostPolicyConfigurationException)
            + " { ErrorCode = " + ErrorCode + ", Layer = " + Layer + " }";
}

/// <summary>
/// 从固定位置读取机器策略、管理员配置和用户配置，并交由 Contracts 的
/// <see cref="AgentPolicyResolver"/> 合并。产品入口不接受任意源路径，与 audit-export
/// 的设计一致：调用方无法把加载目标指向任意文件。
///
/// 缺失文件表示"该层未配置"，属于正常情况；文件存在但路径越界、超限、损坏或含未知字段
/// 一律 fail-closed，不静默跳过。
/// </summary>
public static class AgentHostPolicyStore
{
    /// <summary>单个策略文件的有界读取上限。策略是小型声明式文档，不需要更大预算。</summary>
    internal const long MaximumPolicyFileBytes = 64 * 1024;

    private const string PolicyDirectoryName = "CodexForAutoCAD";

    private const string PolicySubdirectoryName = "policy";

    /// <summary>机器策略：由管理员预置于 ProgramData，普通用户不可写。</summary>
    public static string MachinePolicyPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            PolicyDirectoryName, PolicySubdirectoryName, "machine-policy.json");

    public static string AdministratorPolicyPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            PolicyDirectoryName, PolicySubdirectoryName, "administrator.json");

    /// <summary>用户配置：当前用户本地目录，只能进一步收窄。</summary>
    public static string UserPolicyPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            PolicyDirectoryName, PolicySubdirectoryName, "user.json");

    /// <summary>从固定产品位置加载并合并三层策略。</summary>
    public static AgentPolicyLoadResult Load()
        => LoadFrom(MachinePolicyPath(), AdministratorPolicyPath(), UserPolicyPath());

    /// <summary>
    /// 指定路径加载，仅供自动化使用。路径安全边界与产品入口完全一致，
    /// 因此测试覆盖的就是真实校验逻辑。
    /// </summary>
    internal static AgentPolicyLoadResult LoadFrom(
        string machinePolicyPath, string administratorPath, string userPath)
    {
        var machine = ReadLayer(machinePolicyPath, AgentPolicyLayers.MachinePolicy, out var machineError);
        if (machineError.Length != 0)
        {
            return AgentPolicyLoadResult.Reject(machineError, AgentPolicyLayers.MachinePolicy);
        }

        var administrator = ReadLayer(administratorPath, AgentPolicyLayers.Administrator, out var adminError);
        if (adminError.Length != 0)
        {
            return AgentPolicyLoadResult.Reject(adminError, AgentPolicyLayers.Administrator);
        }

        var user = ReadLayer(userPath, AgentPolicyLayers.User, out var userError);
        if (userError.Length != 0)
        {
            return AgentPolicyLoadResult.Reject(userError, AgentPolicyLayers.User);
        }

        var resolution = AgentPolicyResolver.Resolve(machine, administrator, user);
        if (!resolution.Accepted)
        {
            return AgentPolicyLoadResult.Reject(resolution.ErrorCode, resolution.ErrorLayer);
        }

        return new AgentPolicyLoadResult
        {
            Accepted = true,
            Policy = resolution.Policy,
            MachinePolicyPresent = machine != null,
            AdministratorPolicyPresent = administrator != null,
            UserPolicyPresent = user != null,
        };
    }

    private static AgentPolicyLayerDocument? ReadLayer(string path, string layer, out string errorCode)
    {
        errorCode = string.Empty;

        if (!IsAcceptablePolicyPath(path))
        {
            errorCode = AgentPolicyErrorCodes.PathRejected;
            return null;
        }

        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists)
            {
                // 缺失即"该层未配置"，由更低优先级层或默认值决定，不是错误。
                return null;
            }
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                errorCode = AgentPolicyErrorCodes.PathRejected;
                return null;
            }
            if (info.Length > MaximumPolicyFileBytes)
            {
                errorCode = AgentPolicyErrorCodes.FileTooLarge;
                return null;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            errorCode = AgentPolicyErrorCodes.FileUnreadable;
            return null;
        }

        // 路径链上任一段是 reparse point 都拒绝，避免通过 junction 把加载目标改写到别处。
        try
        {
            if (ContainsReparsePoint(info.FullName))
            {
                errorCode = AgentPolicyErrorCodes.PathRejected;
                return null;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            errorCode = AgentPolicyErrorCodes.PathRejected;
            return null;
        }

        string text;
        try
        {
            text = File.ReadAllText(info.FullName);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            errorCode = AgentPolicyErrorCodes.FileUnreadable;
            return null;
        }

        return ParseLayer(text, layer, out errorCode);
    }

    private static AgentPolicyLayerDocument? ParseLayer(string text, string layer, out string errorCode)
    {
        errorCode = string.Empty;
        var document = new AgentPolicyLayerDocument { Layer = layer };

        try
        {
            using var json = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });

            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errorCode = AgentPolicyErrorCodes.FileMalformed;
                return null;
            }

            foreach (var property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "schema":
                        document.Schema = ReadString(property.Value);
                        break;
                    case "schemaVersion":
                        if (property.Value.ValueKind != JsonValueKind.Number ||
                            !property.Value.TryGetInt32(out var version))
                        {
                            errorCode = AgentPolicyErrorCodes.FileMalformed;
                            return null;
                        }
                        document.SchemaVersion = version;
                        break;
                    case "allowedModels":
                        document.AllowedModels = ReadStringArray(property.Value);
                        break;
                    case "defaultModel":
                        document.DefaultModel = ReadString(property.Value);
                        break;
                    case "allowedReasoningEfforts":
                        document.AllowedReasoningEfforts = ReadStringArray(property.Value);
                        break;
                    case "defaultReasoningEffort":
                        document.DefaultReasoningEffort = ReadString(property.Value);
                        break;
                    case "lockModel":
                        document.LockModel = ReadBoolean(property.Value);
                        break;
                    case "lockReasoningEffort":
                        document.LockReasoningEffort = ReadBoolean(property.Value);
                        break;
                    default:
                        // 未知字段 fail-closed：不静默忽略可能改变语义的配置。
                        errorCode = AgentPolicyErrorCodes.FileMalformed;
                        return null;
                }
            }
        }
        catch (JsonException)
        {
            errorCode = AgentPolicyErrorCodes.FileMalformed;
            return null;
        }
        catch (InvalidOperationException)
        {
            errorCode = AgentPolicyErrorCodes.FileMalformed;
            return null;
        }

        // Layer 由加载器固定，配置文件不得自行声明所属层。
        document.Layer = layer;
        return document;
    }

    private static string ReadString(JsonElement element)
        => element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : throw new InvalidOperationException("expected string");

    private static bool ReadBoolean(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidOperationException("expected boolean"),
        };

    private static string[] ReadStringArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("expected array");
        }
        var values = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("expected string element");
            }
            if (values.Count >= AgentPolicyConstants.MaximumAllowedEntryCount + 1)
            {
                // 交由 Resolver 以 AllowListTooLarge 拒绝，这里只做有界读取。
                break;
            }
            values.Add(item.GetString() ?? string.Empty);
        }
        return values.ToArray();
    }

    /// <summary>
    /// 只接受本地固定盘上的绝对路径。拒绝相对路径、UNC 和设备命名空间，
    /// 使配置源无法被指向网络位置或原始设备。
    /// </summary>
    internal static bool IsAcceptablePolicyPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // 同时覆盖 UNC (\\server\share) 与设备命名空间 (\\?\, \\.\)。
            return false;
        }
        if (!Path.IsPathRooted(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root) || root!.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var drive = new DriveInfo(root);
            return drive.DriveType == DriveType.Fixed;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 逐段检查路径链上的 reparse point。只检查已存在的段，缺失段由调用方按
    /// "该层未配置"处理。
    /// </summary>
    private static bool ContainsReparsePoint(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return true;
        }

        var current = root!;
        foreach (var segment in fullPath.Substring(root!.Length).Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                return false;
            }
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }
}
