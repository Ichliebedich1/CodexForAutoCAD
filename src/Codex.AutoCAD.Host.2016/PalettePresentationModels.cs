using System;
using System.Globalization;
using System.Text;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Host2016
{
    /// <summary>
    /// Display emphasis for a single real status line. The tone only colors the accompanying
    /// indicator; the verbatim Host text remains the authoritative status expression.
    /// </summary>
    internal enum PaletteStatusTone
    {
        Neutral = 0,
        Busy = 1,
        Success = 2,
        Warning = 3,
        Failure = 4,
    }

    /// <summary>
    /// Read-only presentation view of the latest Host-published Agent status line. The view never
    /// invents state: unknown or empty text stays neutral and the Host string is shown verbatim.
    /// </summary>
    internal sealed class PaletteAgentStatusView
    {
        private static readonly PaletteAgentStatusView EmptyView =
            new PaletteAgentStatusView(string.Empty, PaletteStatusTone.Neutral);

        private PaletteAgentStatusView(string displayText, PaletteStatusTone tone)
        {
            DisplayText = displayText ?? string.Empty;
            Tone = tone;
        }

        internal string DisplayText { get; private set; }

        internal PaletteStatusTone Tone { get; private set; }

        internal static PaletteAgentStatusView Empty
        {
            get { return EmptyView; }
        }

        internal static PaletteAgentStatusView FromHostStatus(string hostStatus)
        {
            if (string.IsNullOrWhiteSpace(hostStatus))
            {
                return EmptyView;
            }

            var text = hostStatus.Trim();
            return new PaletteAgentStatusView(text, ClassifyTone(text));
        }

        private static PaletteStatusTone ClassifyTone(string text)
        {
            // Sanitized failure lines always carry the structured formatter marker.
            if (text.IndexOf("失败（error_code=", StringComparison.Ordinal) >= 0)
            {
                return PaletteStatusTone.Failure;
            }

            if (HasPrefix(text, "AgentHost 在线")
                || HasPrefix(text, "Codex 回答完成"))
            {
                return PaletteStatusTone.Success;
            }

            if (HasPrefix(text, "正在启动")
                || HasPrefix(text, "正在停止")
                || HasPrefix(text, "正在向本机 Codex")
                || HasPrefix(text, "Codex 正在分析")
                || HasPrefix(text, "Codex 回答文本已接收")
                || HasPrefix(text, "启动期间已收到停止请求"))
            {
                return PaletteStatusTone.Busy;
            }

            if (HasPrefix(text, "Agent Bridge 状态："))
            {
                return ClassifyBridgeState(text.Substring("Agent Bridge 状态：".Length));
            }

            if (HasPrefix(text, "取消请求")
                || HasPrefix(text, "正在取消")
                || HasPrefix(text, "Codex 回合已取消")
                || text.IndexOf("已取消", StringComparison.Ordinal) >= 0)
            {
                return PaletteStatusTone.Warning;
            }

            return PaletteStatusTone.Neutral;
        }

        private static PaletteStatusTone ClassifyBridgeState(string connectionState)
        {
            var state = (connectionState ?? string.Empty).Trim();
            if (string.Equals(state, AgentBridgeConnectionStates.Online, StringComparison.Ordinal))
            {
                return PaletteStatusTone.Success;
            }

            if (string.Equals(state, AgentBridgeConnectionStates.Connecting, StringComparison.Ordinal))
            {
                return PaletteStatusTone.Busy;
            }

            if (string.Equals(state, AgentBridgeConnectionStates.Degraded, StringComparison.Ordinal))
            {
                return PaletteStatusTone.Warning;
            }

            return PaletteStatusTone.Neutral;
        }

        private static bool HasPrefix(string text, string prefix)
        {
            return text.StartsWith(prefix, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Read-only presentation view of the real DrawingIndexRuntime descriptor snapshot. All counts
    /// and the progress value come from the descriptor; nothing is estimated or fabricated here.
    /// </summary>
    internal sealed class PaletteDrawingIndexView
    {
        private static readonly PaletteDrawingIndexView EmptyView =
            new PaletteDrawingIndexView(
                "未建立",
                PaletteStatusTone.Neutral,
                "整张图纸",
                0,
                0,
                0,
                0,
                0,
                false,
                false,
                string.Empty,
                true,
                false,
                false);

        private PaletteDrawingIndexView(
            string statusLabel,
            PaletteStatusTone tone,
            string scopeLabel,
            int entityCount,
            int indexedEntityCount,
            int unsupportedEntityCount,
            int failedEntityCount,
            int progressPercent,
            bool complete,
            bool limited,
            string limitReason,
            bool hasIndex,
            bool canStart,
            bool canCancel)
        {
            StatusLabel = statusLabel ?? string.Empty;
            Tone = tone;
            ScopeLabel = scopeLabel ?? string.Empty;
            EntityCount = entityCount;
            IndexedEntityCount = indexedEntityCount;
            UnsupportedEntityCount = unsupportedEntityCount;
            FailedEntityCount = failedEntityCount;
            ProgressPercent = progressPercent;
            Complete = complete;
            Limited = limited;
            LimitReason = limitReason ?? string.Empty;
            HasIndex = hasIndex;
            CanStart = canStart;
            CanCancel = canCancel;
        }

        internal string StatusLabel { get; private set; }

        internal PaletteStatusTone Tone { get; private set; }

        internal string ScopeLabel { get; private set; }

        internal int EntityCount { get; private set; }

        internal int IndexedEntityCount { get; private set; }

        internal int UnsupportedEntityCount { get; private set; }

        internal int FailedEntityCount { get; private set; }

        internal int ProgressPercent { get; private set; }

        internal bool Complete { get; private set; }

        internal bool Limited { get; private set; }

        internal string LimitReason { get; private set; }

        internal bool HasIndex { get; private set; }

        internal bool CanStart { get; private set; }

        internal bool CanCancel { get; private set; }

        internal static PaletteDrawingIndexView Empty
        {
            get { return EmptyView; }
        }

        internal static PaletteDrawingIndexView FromDescriptor(DrawingIndexDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return EmptyView;
            }

            var status = descriptor.Status ?? string.Empty;
            var scanning = string.Equals(status, DrawingIndexStatuses.Preparing, StringComparison.Ordinal)
                || string.Equals(status, DrawingIndexStatuses.Scanning, StringComparison.Ordinal);
            return new PaletteDrawingIndexView(
                MapStatusLabel(status),
                MapStatusTone(status),
                MapScopeLabel(descriptor.Scope),
                descriptor.EntityCount,
                descriptor.IndexedEntityCount,
                descriptor.UnsupportedEntityCount,
                descriptor.FailedEntityCount,
                descriptor.ProgressPercent,
                descriptor.Complete,
                descriptor.Limited,
                descriptor.LimitReason,
                !string.IsNullOrEmpty(descriptor.IndexId),
                !scanning,
                scanning);
        }

        internal string BuildStatsText()
        {
            var builder = new StringBuilder();
            builder.Append("范围：").Append(ScopeLabel);
            if (HasIndex)
            {
                builder.Append("  已索引：")
                    .Append(IndexedEntityCount.ToString(CultureInfo.InvariantCulture));
                builder.Append(" / ")
                    .Append(EntityCount.ToString(CultureInfo.InvariantCulture));
                builder.Append("  真实进度：")
                    .Append(ProgressPercent.ToString(CultureInfo.InvariantCulture))
                    .Append("%");
                if (UnsupportedEntityCount > 0 || FailedEntityCount > 0)
                {
                    builder.Append("\n不支持：")
                        .Append(UnsupportedEntityCount.ToString(CultureInfo.InvariantCulture));
                    builder.Append("  读取失败：")
                        .Append(FailedEntityCount.ToString(CultureInfo.InvariantCulture));
                }

                builder.Append("\n完整性：").Append(Complete ? "完整" : "不完整");
                if (Limited)
                {
                    builder.Append("（受限）");
                }

                if (!string.IsNullOrEmpty(LimitReason))
                {
                    builder.Append("\n限制原因：").Append(LimitReason);
                }
            }

            return builder.ToString();
        }

        private static string MapStatusLabel(string status)
        {
            if (string.Equals(status, DrawingIndexStatuses.NotBuilt, StringComparison.Ordinal))
            {
                return "未建立";
            }

            if (string.Equals(status, DrawingIndexStatuses.Preparing, StringComparison.Ordinal))
            {
                return "准备中";
            }

            if (string.Equals(status, DrawingIndexStatuses.Scanning, StringComparison.Ordinal))
            {
                return "扫描中";
            }

            if (string.Equals(status, DrawingIndexStatuses.Ready, StringComparison.Ordinal))
            {
                return "已完成";
            }

            if (string.Equals(status, DrawingIndexStatuses.Partial, StringComparison.Ordinal))
            {
                return "部分完成";
            }

            if (string.Equals(status, DrawingIndexStatuses.Limited, StringComparison.Ordinal))
            {
                return "受限完成";
            }

            if (string.Equals(status, DrawingIndexStatuses.Cancelled, StringComparison.Ordinal))
            {
                return "已取消";
            }

            if (string.Equals(status, DrawingIndexStatuses.Stale, StringComparison.Ordinal))
            {
                return "已失效";
            }

            if (string.Equals(status, DrawingIndexStatuses.Failed, StringComparison.Ordinal))
            {
                return "失败";
            }

            return string.IsNullOrEmpty(status) ? "未建立" : status;
        }

        private static PaletteStatusTone MapStatusTone(string status)
        {
            if (string.Equals(status, DrawingIndexStatuses.Preparing, StringComparison.Ordinal)
                || string.Equals(status, DrawingIndexStatuses.Scanning, StringComparison.Ordinal))
            {
                return PaletteStatusTone.Busy;
            }

            if (string.Equals(status, DrawingIndexStatuses.Ready, StringComparison.Ordinal))
            {
                return PaletteStatusTone.Success;
            }

            if (string.Equals(status, DrawingIndexStatuses.Partial, StringComparison.Ordinal)
                || string.Equals(status, DrawingIndexStatuses.Limited, StringComparison.Ordinal)
                || string.Equals(status, DrawingIndexStatuses.Stale, StringComparison.Ordinal))
            {
                return PaletteStatusTone.Warning;
            }

            if (string.Equals(status, DrawingIndexStatuses.Failed, StringComparison.Ordinal))
            {
                return PaletteStatusTone.Failure;
            }

            return PaletteStatusTone.Neutral;
        }

        private static string MapScopeLabel(string scope)
        {
            if (string.Equals(scope, DrawingIndexScopes.Selection, StringComparison.Ordinal))
            {
                return "当前选择";
            }

            if (string.Equals(scope, DrawingIndexScopes.CurrentSpace, StringComparison.Ordinal))
            {
                return "当前空间";
            }

            if (string.Equals(scope, DrawingIndexScopes.ModelSpace, StringComparison.Ordinal))
            {
                return "模型空间";
            }

            if (string.Equals(scope, DrawingIndexScopes.Layouts, StringComparison.Ordinal))
            {
                return "所有布局";
            }

            if (string.Equals(scope, DrawingIndexScopes.Drawing, StringComparison.Ordinal))
            {
                return "整张图纸";
            }

            return string.IsNullOrEmpty(scope) ? "整张图纸" : scope;
        }
    }
}
