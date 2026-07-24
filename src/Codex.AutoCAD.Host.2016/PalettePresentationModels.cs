using System;
using System.Collections.Generic;
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
                false,
                false,
                true,
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
            bool established,
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
            Established = established;
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

        /// <summary>
        /// True only for a usable terminal index snapshot (ready/partial/limited), including a
        /// legitimate 0/0 empty-drawing scan. Distinct from HasIndex, which only records that a
        /// real build session was started.
        /// </summary>
        internal bool Established { get; private set; }

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
                IsEstablishedStatus(status),
                !scanning,
                scanning);
        }

        private static bool IsEstablishedStatus(string status)
        {
            // An established index is a usable terminal snapshot, including a legitimate
            // zero-entity scan of an empty drawing. Null descriptors and non-terminal or
            // invalidated states never count as established.
            return string.Equals(status, DrawingIndexStatuses.Ready, StringComparison.Ordinal)
                || string.Equals(status, DrawingIndexStatuses.Partial, StringComparison.Ordinal)
                || string.Equals(status, DrawingIndexStatuses.Limited, StringComparison.Ordinal);
        }

        internal string BuildStatsText()
        {
            var builder = new StringBuilder();
            builder.Append("范围：").Append(ScopeLabel);
            if (HasIndex)
            {
                if (Established && EntityCount == 0 && IndexedEntityCount == 0)
                {
                    // A completed empty-drawing scan is a real established index, never
                    // confused with not_built: show it explicitly as established, 0 / 0.
                    builder.Append("  已建立：0 / 0");
                }
                else
                {
                    builder.Append("  已索引：")
                        .Append(IndexedEntityCount.ToString(CultureInfo.InvariantCulture));
                    builder.Append(" / ")
                        .Append(EntityCount.ToString(CultureInfo.InvariantCulture));
                }
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

    /// <summary>
    /// Control enablement derived exclusively from the real Host Agent snapshot. This is a pure
    /// projection: the client state machine remains the single owner of connection/turn state and
    /// no control-local boolean ever replaces it.
    /// </summary>
    internal sealed class PaletteCommandAvailability
    {
        private PaletteCommandAvailability(
            bool canStartAgent,
            bool canStopAgent,
            bool canSend,
            bool canCancelTurn,
            bool canNewConversation,
            string sendHint)
        {
            CanStartAgent = canStartAgent;
            CanStopAgent = canStopAgent;
            CanSend = canSend;
            CanCancelTurn = canCancelTurn;
            CanNewConversation = canNewConversation;
            SendHint = sendHint ?? string.Empty;
        }

        internal bool CanStartAgent { get; private set; }

        internal bool CanStopAgent { get; private set; }

        internal bool CanSend { get; private set; }

        internal bool CanCancelTurn { get; private set; }

        internal bool CanNewConversation { get; private set; }

        /// <summary>User-facing reason shown when Send is unavailable; empty when Send is available.</summary>
        internal string SendHint { get; private set; }

        internal static PaletteCommandAvailability FromSnapshot(AgentClientSnapshot snapshot)
        {
            var current = snapshot ?? AgentClientSnapshot.Offline;
            var online = current.IsOnline;
            var starting = string.Equals(
                current.ConnectionState,
                AgentClientSnapshot.ConnectionStarting,
                StringComparison.Ordinal);
            var stopping = string.Equals(
                current.ConnectionState,
                AgentClientSnapshot.ConnectionStopping,
                StringComparison.Ordinal);
            var activeTurn = current.HasActiveTurn;

            bool canSend;
            string sendHint;
            if (online && activeTurn)
            {
                canSend = false;
                sendHint = "当前回合尚未结束，完成后才能继续发送。";
            }
            else if (starting)
            {
                canSend = false;
                sendHint = "AgentHost 正在启动，在线后才能发送。";
            }
            else if (stopping)
            {
                canSend = false;
                sendHint = "AgentHost 正在停止，完成后才能发送。";
            }
            else if (!online)
            {
                // Offline is a stable state: AskAsync auto-starts AgentHost on the real chain.
                canSend = true;
                sendHint = "Agent 离线；发送时将先启动 AgentHost。";
            }
            else
            {
                canSend = true;
                sendHint = string.Empty;
            }

            return new PaletteCommandAvailability(
                !online && !starting && !stopping,
                online || starting,
                canSend,
                online && activeTurn,
                online && !activeTurn,
                sendHint);
        }
    }

    /// <summary>
    /// Draft protection for the prompt box. The submitted text is captured when the user sends;
    /// the input is cleared only if it still equals that exact submitted text, so anything typed
    /// while the request is in flight (IME, paste, continued drafting) survives the terminal state.
    /// </summary>
    internal static class PaletteDraftGuard
    {
        internal static bool ShouldClearAfterSend(string submittedText, string currentText)
        {
            return !string.IsNullOrEmpty(submittedText)
                && string.Equals(submittedText, currentText, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Bounded, sanitized clipboard copy feedback. Expected WPF clipboard failures (busy clipboard,
    /// thread affinity, access denied) and any unexpected failure map to one fixed retry hint; no
    /// path, stack trace or raw exception text ever reaches the UI.
    /// </summary>
    internal static class PaletteClipboardFeedback
    {
        internal const string Copied = "已复制到剪贴板。";
        internal const string Empty = "暂无可复制的 JSON。";
        internal const string Unavailable = "剪贴板暂不可用，请稍后重试。";

        internal static string FromException(Exception exception)
        {
            // Deliberately total: clipboard failures are always transient from the user's view and
            // the fixed hint stays identical for every failure class.
            return Unavailable;
        }
    }

    /// <summary>
    /// Coalesces high-frequency streaming deltas so the Dispatcher repaints at most once per
    /// window (30-60 ms). Pure logic with an injected clock so it is spec-testable without WPF.
    /// </summary>
    internal sealed class PaletteDeltaCoalescer
    {
        internal const long DefaultWindowMilliseconds = 40L;

        private readonly Func<long> clock;
        private readonly long windowMilliseconds;
        private readonly StringBuilder pending = new StringBuilder();
        private readonly long createdAtMilliseconds;
        private long lastFlushAtMilliseconds;

        internal PaletteDeltaCoalescer(Func<long> clock, long windowMilliseconds)
        {
            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (windowMilliseconds <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(windowMilliseconds));
            }

            this.clock = clock;
            this.windowMilliseconds = windowMilliseconds;
            createdAtMilliseconds = clock();
            lastFlushAtMilliseconds = createdAtMilliseconds;
        }

        internal bool HasPending
        {
            get { return pending.Length > 0; }
        }

        internal void Append(string delta)
        {
            if (!string.IsNullOrEmpty(delta))
            {
                pending.Append(delta);
            }
        }

        /// <summary>Replace the pending buffer with a full-text snapshot (e.g. after reset).</summary>
        internal void ReplaceAll(string fullText)
        {
            pending.Length = 0;
            if (!string.IsNullOrEmpty(fullText))
            {
                pending.Append(fullText);
            }
        }

        internal void Clear()
        {
            pending.Length = 0;
        }

        internal bool IsDue()
        {
            return HasPending
                && clock() - lastFlushAtMilliseconds >= windowMilliseconds;
        }

        /// <summary>Returns the coalesced text when due; otherwise null and keeps buffering.</summary>
        internal string TryFlush()
        {
            if (!IsDue())
            {
                return null;
            }

            return Flush();
        }

        /// <summary>Emits whatever is pending regardless of the window (final repaint).</summary>
        internal string Flush()
        {
            if (!HasPending)
            {
                return null;
            }

            var value = pending.ToString();
            pending.Length = 0;
            lastFlushAtMilliseconds = clock();
            return value;
        }
    }

    /// <summary>Role of a single presentation message in the chat workbench.</summary>
    internal enum PaletteMessageKind
    {
        User = 0,
        Assistant = 1,
        Status = 2,
        Error = 3,
    }

    /// <summary>
    /// One immutable-ish presentation message. Assistant text mutates only while IsStreaming.
    /// </summary>
    internal sealed class PaletteMessage
    {
        internal PaletteMessage(int sequence, PaletteMessageKind kind, string text, bool isStreaming)
        {
            Sequence = sequence;
            Kind = kind;
            Text = text ?? string.Empty;
            IsStreaming = isStreaming;
        }

        internal int Sequence { get; private set; }

        internal PaletteMessageKind Kind { get; private set; }

        internal string Text { get; private set; }

        internal bool IsStreaming { get; private set; }

        internal void AppendDelta(string delta)
        {
            if (!IsStreaming || string.IsNullOrEmpty(delta))
            {
                return;
            }

            Text += delta;
        }

        internal void FinalizeStream()
        {
            IsStreaming = false;
        }
    }

    /// <summary>
    /// Process-local presentation message list for the single backend conversation, organized as
    /// Conversation -> Messages -> Items (one text item per message at this stage). It never owns
    /// Provider threads or process state; the Host snapshot epoch is the conversation boundary.
    /// Late deltas after a finalized stream are rejected and counted, so a stale event cannot
    /// pollute the conversation the user switched to.
    /// </summary>
    internal sealed class PaletteConversationStore
    {
        internal const int MaxMessages = 500;

        private readonly List<PaletteMessage> messages = new List<PaletteMessage>();
        private PaletteMessage streamingAssistant;
        private bool streamResetPending;
        private int nextSequence;
        private long epoch;

        internal PaletteConversationStore()
        {
            epoch = 0L;
        }

        internal long Epoch
        {
            get { return epoch; }
        }

        internal int Count
        {
            get { return messages.Count; }
        }

        internal int LateDeltasIgnored { get; private set; }

        /// <summary>Aligns the store with the real conversation epoch; a change clears all messages.</summary>
        internal bool EnsureEpoch(long newEpoch)
        {
            if (newEpoch == epoch)
            {
                return false;
            }

            Reset(newEpoch);
            return true;
        }

        internal void Reset(long newEpoch)
        {
            epoch = newEpoch;
            messages.Clear();
            streamingAssistant = null;
            streamResetPending = false;
        }

        internal PaletteMessage AppendUserMessage(string text)
        {
            FinalizeAssistantStream();
            return AddMessage(PaletteMessageKind.User, text, false);
        }

        /// <summary>Starts a fresh streaming assistant message (Host published an empty text reset).</summary>
        internal PaletteMessage BeginAssistantStream()
        {
            FinalizeAssistantStream();
            streamResetPending = false;
            streamingAssistant = AddMessage(PaletteMessageKind.Assistant, string.Empty, true);
            return streamingAssistant;
        }

        /// <summary>
        /// Marks the Host text-reset boundary. The next real delta lazily opens the streaming
        /// message, so repeated resets (e.g. Clear All) never leave a phantom empty bubble.
        /// </summary>
        internal void NoteStreamReset()
        {
            FinalizeAssistantStream();
            streamResetPending = true;
        }

        internal void AppendAssistantDelta(string delta)
        {
            if (string.IsNullOrEmpty(delta))
            {
                return;
            }

            if (streamResetPending)
            {
                BeginAssistantStream();
            }

            if (streamingAssistant == null || !streamingAssistant.IsStreaming)
            {
                // A late delta after the real terminal state must not reopen the stream.
                LateDeltasIgnored++;
                return;
            }

            streamingAssistant.AppendDelta(delta);
        }

        internal void FinalizeAssistantStream()
        {
            if (streamingAssistant != null)
            {
                streamingAssistant.FinalizeStream();
                streamingAssistant = null;
            }
        }

        internal PaletteMessage AddStatus(string text)
        {
            return AddMessage(PaletteMessageKind.Status, text, false);
        }

        internal PaletteMessage AddError(string text)
        {
            return AddMessage(PaletteMessageKind.Error, text, false);
        }

        internal IReadOnlyList<PaletteMessage> Snapshot()
        {
            return messages.ToArray();
        }

        private PaletteMessage AddMessage(PaletteMessageKind kind, string text, bool isStreaming)
        {
            var message = new PaletteMessage(nextSequence++, kind, text, isStreaming);
            messages.Add(message);
            TrimOverflow();
            return message;
        }

        private void TrimOverflow()
        {
            // Bounded rendering for long sessions: drop the oldest finalized messages first.
            var overflow = messages.Count - MaxMessages;
            for (var index = 0; overflow > 0 && index < messages.Count;)
            {
                var candidate = messages[index];
                if (candidate.IsStreaming)
                {
                    index++;
                    continue;
                }

                messages.RemoveAt(index);
                overflow--;
            }
        }
    }

    /// <summary>
    /// Model / reasoning-effort capability view. The backend does not expose model capabilities
    /// yet, so the gate stays closed: selectors are disabled, show the honest "使用 Codex 默认值"
    /// label, and any proposed value is rejected instead of passing through to AgentHost.
    /// </summary>
    internal sealed class PaletteModelCapabilityView
    {
        private static readonly PaletteModelCapabilityView UnavailableView =
            new PaletteModelCapabilityView(false, "使用 Codex 默认值", "使用 Codex 默认值");

        private PaletteModelCapabilityView(bool enabled, string modelLabel, string reasoningLabel)
        {
            Enabled = enabled;
            ModelLabel = modelLabel ?? string.Empty;
            ReasoningLabel = reasoningLabel ?? string.Empty;
        }

        internal bool Enabled { get; private set; }

        internal string ModelLabel { get; private set; }

        internal string ReasoningLabel { get; private set; }

        internal static PaletteModelCapabilityView Unavailable
        {
            get { return UnavailableView; }
        }
    }

    /// <summary>
    /// Closed-until-capability selection gate for model and reasoning effort. Without a backend
    /// allowlist every proposal is rejected with a fixed sanitized reason; with one, only exact
    /// allowlisted values pass. Arbitrary strings can never travel toward AgentHost.
    /// </summary>
    internal sealed class PaletteModelSelectionGate
    {
        private readonly HashSet<string> allowedModels;
        private readonly HashSet<string> allowedReasoningLevels;

        private PaletteModelSelectionGate(
            HashSet<string> allowedModels,
            HashSet<string> allowedReasoningLevels)
        {
            this.allowedModels = allowedModels;
            this.allowedReasoningLevels = allowedReasoningLevels;
        }

        internal bool CapabilityAvailable
        {
            get { return allowedModels != null; }
        }

        internal static PaletteModelSelectionGate Closed()
        {
            return new PaletteModelSelectionGate(null, null);
        }

        internal static PaletteModelSelectionGate FromAllowlists(
            IEnumerable<string> models,
            IEnumerable<string> reasoningLevels)
        {
            if (models == null || reasoningLevels == null)
            {
                return Closed();
            }

            var modelSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var model in models)
            {
                if (!string.IsNullOrWhiteSpace(model))
                {
                    modelSet.Add(model.Trim());
                }
            }

            var reasoningSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var level in reasoningLevels)
            {
                if (!string.IsNullOrWhiteSpace(level))
                {
                    reasoningSet.Add(level.Trim());
                }
            }

            if (modelSet.Count == 0 || reasoningSet.Count == 0)
            {
                return Closed();
            }

            return new PaletteModelSelectionGate(modelSet, reasoningSet);
        }

        internal bool TryAcceptModel(string proposed, out string error)
        {
            return TryAccept(allowedModels, proposed, "模型", out error);
        }

        internal bool TryAcceptReasoningLevel(string proposed, out string error)
        {
            return TryAccept(allowedReasoningLevels, proposed, "思考强度", out error);
        }

        private static bool TryAccept(
            HashSet<string> allowed,
            string proposed,
            string label,
            out string error)
        {
            if (allowed == null)
            {
                error = "后端尚未开放" + label + "选择能力；保持 Codex 默认值。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(proposed) || !allowed.Contains(proposed.Trim()))
            {
                error = label + "不在后端允许列表内；已拒绝并保持当前设置。";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }

    /// <summary>
    /// Fixed layout contract for the compact dark workbench. Values are DIP and reviewed by specs
    /// so the 300/360/520 widths keep stable chrome and never rely on font scaling.
    /// </summary>
    internal static class PaletteLayoutPolicy
    {
        internal const double MinWorkableWidthDip = 300.0;
        internal const double DefaultWidthDip = 520.0;
        internal const double SessionBarMinHeight = 30.0;
        internal const double ActionButtonMinHeight = 30.0;
        internal const double InputMinHeight = 56.0;
        internal const double ContextBarMinHeight = 24.0;
        internal const double CornerRadiusDip = 4.0;
        internal const double ContentPaddingDip = 8.0;
        internal const double BackToLatestMinHeight = 24.0;
        internal const int MaxStatusLineCharacters = 160;
    }
}
