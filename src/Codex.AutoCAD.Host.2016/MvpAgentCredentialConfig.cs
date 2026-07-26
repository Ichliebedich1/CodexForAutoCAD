using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Codex.AutoCAD.AgentLauncher;

namespace Codex.AutoCAD.Host2016
{
    /// <summary>
    /// M4.11：Host.2016 侧的凭据配置来源。
    /// </summary>
    /// <remarks>
    /// 在此之前 <see cref="AgentHostCredentialOptions"/> 只存在于 Launcher，Host 从不设置它，
    /// 因此生产路径上凭据模式永远是 <c>Disabled</c>——凭据读取代码写好了却无法被启用，
    /// M4.11 的真实 Windows 凭据管理器矩阵也就无法执行。本类补上这条配置通道。
    ///
    /// 刻意不做成 AutoCAD 命令或 Palette 选项：UI 当前冻结，而且一个能在运行时开关凭据的
    /// 命令，等于把安全边界交给任何能在命令行里打字的人。改为固定位置的配置文件，
    /// 与 M4.1 的策略存储同一形状：位置不可由调用方指定，缺失即禁用，任何异常一律拒绝启动。
    ///
    /// 语法故意极小，只有两个键，且不使用 JSON：Host.2016 是 net45，缺少可信的内置 JSON
    /// 读取器，为一个两键文件引入解析器只会扩大攻击面。
    /// <code>
    /// mode=windows-credential-manager-access-token
    /// target=OpenAI/CodexForAutoCAD/credential/&lt;name&gt;
    /// </code>
    /// </remarks>
    internal static class MvpAgentCredentialConfig
    {
        internal const string ConfigDirectoryName = "CodexForAutoCAD";
        internal const string ConfigFileName = "agenthost-credential.config";
        internal const string ModeKey = "mode";
        internal const string TargetKey = "target";
        internal const string WindowsCredentialManagerModeValue =
            "windows-credential-manager-access-token";

        /// <summary>配置文件大小上限。超过即拒绝，不做部分解析。</summary>
        internal const int MaximumConfigBytes = 4096;

        /// <summary>
        /// 读取固定位置的凭据配置。文件不存在时返回默认的禁用配置。
        /// </summary>
        internal static AgentHostCredentialOptions Load()
        {
            var localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData))
            {
                return new AgentHostCredentialOptions();
            }

            var path = Path.Combine(
                Path.Combine(localAppData, ConfigDirectoryName),
                ConfigFileName);
            return LoadFrom(path);
        }

        /// <summary>
        /// 从指定路径读取。仅供规格使用；生产路径固定，不接受调用方提供的位置。
        /// </summary>
        internal static AgentHostCredentialOptions LoadFrom(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return new AgentHostCredentialOptions();
            }
            if (!File.Exists(path))
            {
                // 未配置就是禁用。这是默认状态，不是错误。
                return new AgentHostCredentialOptions();
            }

            AssertNoReparsePoint(path);

            var info = new FileInfo(path);
            if (info.Length > MaximumConfigBytes)
            {
                throw new InvalidOperationException(
                    "AgentHost 凭据配置超出大小上限。");
            }

            var lines = File.ReadAllLines(path);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var rawLine in lines)
            {
                var line = rawLine == null ? string.Empty : rawLine.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator <= 0 || separator == line.Length - 1)
                {
                    throw new InvalidOperationException(
                        "AgentHost 凭据配置存在无法解析的行。");
                }

                var key = line.Substring(0, separator).Trim();
                var value = line.Substring(separator + 1).Trim();
                if (key != ModeKey && key != TargetKey)
                {
                    // 未知键一律拒绝：静默忽略会让拼错的键看起来"生效了"。
                    throw new InvalidOperationException(
                        "AgentHost 凭据配置包含未知配置键。");
                }
                if (values.ContainsKey(key))
                {
                    throw new InvalidOperationException(
                        "AgentHost 凭据配置存在重复配置键。");
                }
                values.Add(key, value);
            }

            if (!values.ContainsKey(ModeKey))
            {
                throw new InvalidOperationException(
                    "AgentHost 凭据配置缺少 mode。");
            }

            var mode = values[ModeKey];
            if (!string.Equals(mode, WindowsCredentialManagerModeValue, StringComparison.Ordinal))
            {
                // 大小写敏感：`Windows-...` 是笔误，不是另一种写法。
                throw new InvalidOperationException(
                    "AgentHost 凭据配置的 mode 不受支持。");
            }
            if (!values.ContainsKey(TargetKey))
            {
                throw new InvalidOperationException(
                    "AgentHost 凭据配置缺少 target。");
            }

            var options = new AgentHostCredentialOptions();
            options.Mode = AgentHostCredentialMode.WindowsCredentialManagerAccessToken;
            options.CredentialTargetName = values[TargetKey];

            // 产品前缀、长度和字符集由 Launcher 的既有校验负责；这里立刻调用一次，
            // 让非法目标在启动之前就失败，而不是等到 bootstrap 中途。
            options.Validate();
            return options;
        }

        private static void AssertNoReparsePoint(string path)
        {
            var full = Path.GetFullPath(path);
            var current = full;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(current) || File.Exists(current))
                {
                    var attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                    {
                        // 路径链上任何一段是重解析点，都可能把固定位置指向别处。
                        throw new InvalidOperationException(
                            "AgentHost 凭据配置路径包含重解析点。");
                    }
                }

                var parent = Path.GetDirectoryName(current);
                if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                current = parent;
            }
        }

        /// <summary>
        /// 供诊断显示的一行摘要。永远不输出目标名——它属于凭据身份的一部分。
        /// </summary>
        internal static string DescribeForDiagnostics(AgentHostCredentialOptions options)
        {
            if (options == null || options.Mode == AgentHostCredentialMode.Disabled)
            {
                return "AgentHost credential: disabled";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "AgentHost credential: {0}; target configured: {1}",
                WindowsCredentialManagerModeValue,
                string.IsNullOrEmpty(options.CredentialTargetName) ? "false" : "true");
        }
    }
}
