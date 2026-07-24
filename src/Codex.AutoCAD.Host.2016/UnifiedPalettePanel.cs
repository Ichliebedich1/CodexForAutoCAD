using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Host2016
{
    /// <summary>
    /// Compact dark chat workbench for AutoCAD 2016, in the spirit of the VS Code Codex panel:
    /// session bar, CAD context strip, real message list and a fixed composer. The control only
    /// renders Host-provided state and sends commands through existing Host runtime entry points;
    /// connection and turn enablement derive exclusively from the real AgentClientSnapshot.
    /// CAD write and plugin-initiated save stay disabled.
    /// </summary>
    internal sealed class UnifiedPalettePanel : UserControl
    {
        private static readonly Brush PanelBackground = CreateBrush(0x1E, 0x1E, 0x1E);
        private static readonly Brush SurfaceBackground = CreateBrush(0x25, 0x25, 0x26);
        private static readonly Brush SurfaceBorder = CreateBrush(0x3C, 0x3C, 0x3C);
        private static readonly Brush InputBackground = CreateBrush(0x2D, 0x2D, 0x30);
        private static readonly Brush PrimaryText = CreateBrush(0xD4, 0xD4, 0xD4);
        private static readonly Brush SecondaryText = CreateBrush(0x9D, 0x9D, 0x9D);
        private static readonly Brush FaintText = CreateBrush(0x76, 0x76, 0x76);
        private static readonly Brush AccentBackground = CreateBrush(0x0E, 0x63, 0x9C);
        private static readonly Brush AccentHover = CreateBrush(0x11, 0x77, 0xBB);
        private static readonly Brush AccentPressed = CreateBrush(0x0B, 0x4E, 0x7A);
        private static readonly Brush ChromeBackground = CreateBrush(0x33, 0x33, 0x33);
        private static readonly Brush ChromeHover = CreateBrush(0x3E, 0x3E, 0x42);
        private static readonly Brush ChromePressed = CreateBrush(0x2D, 0x2D, 0x30);
        private static readonly Brush UserRowBackground = CreateBrush(0x23, 0x36, 0x48);
        private static readonly Brush UserTagText = CreateBrush(0x7D, 0xB8, 0xE8);
        private static readonly Brush AssistantTagText = CreateBrush(0x4E, 0xC9, 0xB0);
        private static readonly Brush ErrorText = CreateBrush(0xF4, 0x87, 0x71);
        private static readonly Brush ToneNeutral = CreateBrush(0x6E, 0x6E, 0x6E);
        private static readonly Brush ToneBusy = CreateBrush(0xD7, 0xA0, 0x00);
        private static readonly Brush ToneSuccess = CreateBrush(0x57, 0xA6, 0x4A);
        private static readonly Brush ToneWarning = CreateBrush(0xD1, 0x86, 0x16);
        private static readonly Brush ToneFailure = CreateBrush(0xF1, 0x4C, 0x4C);

        private const long MessageSyncWindowMilliseconds = 40L;

        private static readonly Style ChromeButtonStyle = CreateButtonStyle(
            ChromeBackground,
            ChromeHover,
            ChromePressed,
            PrimaryText,
            false);
        private static readonly Style AccentButtonStyle = CreateButtonStyle(
            AccentBackground,
            AccentHover,
            AccentPressed,
            CreateBrush(0xFF, 0xFF, 0xFF),
            false);
        private static readonly Style ScopeToggleStyle = CreateButtonStyle(
            ChromeBackground,
            ChromeHover,
            ChromePressed,
            PrimaryText,
            true);

        private readonly Ellipse agentStatusDot;
        private readonly TextBlock agentStatusText;
        private readonly Button startAgent;
        private readonly Button stopAgent;
        private readonly Button newConversation;

        private readonly TextBlock selectionSummary;
        private readonly Button clearContext;
        private readonly Button clearAll;
        private readonly TextBlock indexSummary;
        private readonly Button startIndex;
        private readonly Button cancelIndex;
        private readonly Button toggleDetails;
        private readonly Border detailsPanel;
        private readonly TextBlock contextDetail;
        private readonly TextBlock indexDetail;
        private readonly TextBlock indexRawStatus;
        private readonly RadioButton[] scopeOptions;

        private readonly ListBox messageList;
        private readonly Button backToLatest;
        private readonly List<RowVisual> rowVisuals = new List<RowVisual>();
        private ScrollViewer messageScroller;
        private bool scrollPinned = true;

        private readonly TextBox prompt;
        private readonly Button cancelTurn;
        private readonly Button send;

        private readonly TextBox metrics;
        private readonly TextBox json;
        private readonly TextBlock copyFeedback;

        private AgentClientSnapshot currentSnapshot = AgentClientSnapshot.Offline;
        private PaletteDrawingIndexView currentIndexView = PaletteDrawingIndexView.Empty;
        private IReadOnlyList<PaletteMessage> pendingMessages;
        private bool messageSyncPending;
        private DispatcherTimer messageTimer;
        private int lastMessageSyncTick;
        private long draftEpoch = -1L;
        private bool suppressDraftSave;

        internal UnifiedPalettePanel()
        {
            Background = PanelBackground;
            Focusable = true;

            var root = new Grid
            {
                Margin = new Thickness(PaletteLayoutPolicy.ContentPaddingDip),
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(Row(BuildSessionBar(out agentStatusDot, out agentStatusText,
                out startAgent, out stopAgent, out newConversation), 0));

            var boundary = new TextBlock
            {
                Text = "只读 · CAD 写入禁用 · 不会自动保存 DWG",
                FontSize = 10.5,
                Foreground = FaintText,
                Margin = new Thickness(0.0, 3.0, 0.0, 0.0),
            };
            root.Children.Add(Row(boundary, 1));

            root.Children.Add(Row(BuildContextBar(
                out selectionSummary,
                out clearContext,
                out clearAll,
                out indexSummary,
                out startIndex,
                out cancelIndex,
                out toggleDetails,
                out detailsPanel,
                out contextDetail,
                out indexDetail,
                out indexRawStatus,
                out scopeOptions), 2));

            var messageHost = new Grid
            {
                Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
            };
            messageList = new ListBox
            {
                Background = PanelBackground,
                BorderThickness = new Thickness(0.0),
                Focusable = false,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(messageList, ScrollBarVisibility.Disabled);
            messageList.Loaded += OnMessageListLoaded;
            messageHost.Children.Add(messageList);
            backToLatest = new Button
            {
                Content = "回到最新",
                Style = ChromeButtonStyle,
                MinHeight = PaletteLayoutPolicy.BackToLatestMinHeight,
                FontSize = 10.5,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0.0, 0.0, 4.0, 4.0),
                Visibility = Visibility.Collapsed,
                ToolTip = "回到底部并跟随最新回复。",
            };
            backToLatest.Click += OnBackToLatestClick;
            messageHost.Children.Add(backToLatest);
            root.Children.Add(Row(messageHost, 3));

            root.Children.Add(Row(BuildComposer(
                out prompt,
                out cancelTurn,
                out send), 4));

            root.Children.Add(Row(BuildDiagnostics(out metrics, out json, out copyFeedback), 5));

            Content = root;
            ApplySnapshot(AgentClientSnapshot.Offline);
        }

        private UIElement BuildSessionBar(
            out Ellipse statusDot,
            out TextBlock statusText,
            out Button startButton,
            out Button stopButton,
            out Button newButton)
        {
            statusDot = new Ellipse
            {
                Width = 8.0,
                Height = 8.0,
                Fill = ToneNeutral,
                VerticalAlignment = VerticalAlignment.Center,
            };
            statusText = new TextBlock
            {
                Text = "Agent 离线；只读模式。",
                FontSize = 12.0,
                Foreground = PrimaryText,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(6.0, 0.0, 0.0, 0.0),
            };
            var statusGrid = new Grid();
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            Grid.SetColumn(statusDot, 0);
            statusGrid.Children.Add(statusDot);
            Grid.SetColumn(statusText, 1);
            statusGrid.Children.Add(statusText);

            startButton = CreateActionButton("启动", "启动本机 AgentHost 并建立只读 Codex 会话；不会修改 CAD。", OnStartAgentClick, false);
            stopButton = CreateActionButton("停止", "停止 AgentHost 并回收连接；不会修改 CAD。", OnStopAgentClick, false);
            newButton = CreateActionButton("新建会话", "保留当前 CAD 上下文，建立新的 Codex 对话。", OnNewConversationClick, false);
            var switchButton = CreateActionButton("切换会话", "当前后端仅提供一个进程内会话；会话集合与历史切换属于 M7，尚未开放。", null, false);
            switchButton.IsEnabled = false;

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            buttons.Children.Add(startButton);
            buttons.Children.Add(stopButton);
            buttons.Children.Add(newButton);
            buttons.Children.Add(switchButton);

            var bar = new Grid
            {
                MinHeight = PaletteLayoutPolicy.SessionBarMinHeight,
            };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(statusGrid, 0);
            bar.Children.Add(statusGrid);
            Grid.SetColumn(buttons, 1);
            bar.Children.Add(buttons);
            return bar;
        }

        private UIElement BuildContextBar(
            out TextBlock selectionText,
            out Button clearSelectionButton,
            out Button clearEverythingButton,
            out TextBlock indexText,
            out Button startIndexButton,
            out Button cancelIndexButton,
            out Button detailsButton,
            out Border details,
            out TextBlock contextDetailText,
            out TextBlock indexDetailText,
            out TextBlock indexRawText,
            out RadioButton[] scopeRadios)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
            };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            selectionText = CreateStripText("选择：未捕获");
            clearSelectionButton = CreateActionButton(
                "清除选择上下文",
                "只清除内存中的选择上下文；不影响当前对话和整图索引，不修改图纸。",
                OnClearContextClick,
                false);
            clearEverythingButton = CreateActionButton(
                "清除全部",
                "清除 CAD 上下文、消息记录和当前 Codex 对话；不会修改 CAD。",
                OnClearAllClick,
                false);
            clearEverythingButton.Foreground = ErrorText;
            grid.Children.Add(Row(BuildStripLine(
                selectionText,
                clearSelectionButton,
                clearEverythingButton), 0));

            indexText = CreateStripText("索引：未建立");
            startIndexButton = CreateActionButton(
                "开始扫描",
                "按所选范围建立整图索引；只读扫描，不修改图纸。",
                OnStartIndexClick,
                false);
            cancelIndexButton = CreateActionButton(
                "取消扫描",
                "幂等取消当前扫描；失败后可按真实状态重试。",
                OnCancelIndexClick,
                false);
            detailsButton = CreateActionButton(
                "详情",
                "展开或收起选择摘要、索引统计和扫描范围。",
                OnToggleDetailsClick,
                false);
            grid.Children.Add(Row(BuildStripLine(
                indexText,
                startIndexButton,
                cancelIndexButton,
                detailsButton), 1));

            var meta = new TextBlock
            {
                Text = "会话：当前（仅此一个） · 长期记忆：未提供（M7）",
                FontSize = 10.5,
                Foreground = FaintText,
                Margin = new Thickness(2.0, 3.0, 0.0, 2.0),
                ToolTip = "会话集合、历史切换与长期记忆依赖 M7 SQLite，本阶段未提供；不伪造。",
            };
            grid.Children.Add(Row(meta, 2));

            contextDetailText = new TextBlock
            {
                FontSize = 11.0,
                Foreground = SecondaryText,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 120.0,
            };
            var contextScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 120.0,
                Content = contextDetailText,
            };

            indexDetailText = new TextBlock
            {
                FontSize = 11.0,
                Foreground = PrimaryText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
            };
            indexRawText = new TextBlock
            {
                FontSize = 10.5,
                Foreground = FaintText,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 40.0,
                Margin = new Thickness(0.0, 2.0, 0.0, 0.0),
            };

            var scopePanel = new WrapPanel
            {
                Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
            };
            scopeRadios = new[]
            {
                CreateScopeOption(scopePanel, "整张图纸", DrawingIndexScopes.Drawing, true),
                CreateScopeOption(scopePanel, "模型空间", DrawingIndexScopes.ModelSpace, false),
                CreateScopeOption(scopePanel, "当前空间", DrawingIndexScopes.CurrentSpace, false),
                CreateScopeOption(scopePanel, "所有布局", DrawingIndexScopes.Layouts, false),
                CreateScopeOption(scopePanel, "当前选择", DrawingIndexScopes.Selection, false),
            };

            var scanHint = new TextBlock
            {
                Text = "扫描在 AutoCAD 空闲时按只读分片执行；不修改、不保存图纸。",
                FontSize = 10.5,
                Foreground = FaintText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0.0, 3.0, 0.0, 0.0),
            };

            var detailGrid = new Grid();
            detailGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            detailGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            detailGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            detailGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            detailGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(contextScroll, 0);
            detailGrid.Children.Add(contextScroll);
            Grid.SetRow(indexDetailText, 1);
            detailGrid.Children.Add(indexDetailText);
            Grid.SetRow(indexRawText, 2);
            detailGrid.Children.Add(indexRawText);
            Grid.SetRow(scopePanel, 3);
            detailGrid.Children.Add(scopePanel);
            Grid.SetRow(scanHint, 4);
            detailGrid.Children.Add(scanHint);

            details = new Border
            {
                Background = SurfaceBackground,
                BorderBrush = SurfaceBorder,
                BorderThickness = new Thickness(1.0),
                CornerRadius = new CornerRadius(PaletteLayoutPolicy.CornerRadiusDip),
                Padding = new Thickness(6.0),
                Margin = new Thickness(0.0, 3.0, 0.0, 2.0),
                Visibility = Visibility.Collapsed,
                Child = detailGrid,
            };
            grid.Children.Add(Row(details, 3));

            var frame = new Border
            {
                Background = SurfaceBackground,
                BorderBrush = SurfaceBorder,
                BorderThickness = new Thickness(1.0),
                CornerRadius = new CornerRadius(PaletteLayoutPolicy.CornerRadiusDip),
                Child = grid,
            };
            return frame;
        }

        private UIElement BuildComposer(
            out TextBox input,
            out Button cancelButton,
            out Button sendButton)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
            };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            input = new TextBox
            {
                Height = PaletteLayoutPolicy.InputMinHeight + 8.0,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(6.0),
                FontSize = 12.5,
                Background = InputBackground,
                Foreground = PrimaryText,
                CaretBrush = PrimaryText,
                BorderBrush = SurfaceBorder,
                ToolTip = "输入只读问题。Enter 发送，Shift+Enter 换行；发送前请先建立整图索引或捕获选择上下文。",
            };
            input.KeyDown += OnPromptKeyDown;
            input.TextChanged += OnPromptTextChanged;
            grid.Children.Add(Row(input, 0));

            var modelButton = CreateActionButton(
                "模型：使用 Codex 默认值",
                "后端尚未开放模型选择能力（M8.8）；保持 Codex 默认值，不伪造选项。",
                null,
                false);
            modelButton.IsEnabled = false;
            var reasoningButton = CreateActionButton(
                "思考强度：使用 Codex 默认值",
                "后端尚未开放思考强度选择能力（M8.8）；保持 Codex 默认值，不伪造选项。",
                null,
                false);
            reasoningButton.IsEnabled = false;
            cancelButton = CreateActionButton(
                "取消回合",
                "幂等取消当前 Codex 回合；不会修改 CAD。",
                OnCancelTurnClick,
                false);
            sendButton = CreateActionButton(
                "发送",
                "发送只读问题；当前选择上下文或整图索引将自动附加。",
                OnSendClick,
                true);

            var row = new Grid
            {
                Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(modelButton, 0);
            row.Children.Add(modelButton);
            Grid.SetColumn(reasoningButton, 1);
            row.Children.Add(reasoningButton);
            Grid.SetColumn(cancelButton, 3);
            row.Children.Add(cancelButton);
            Grid.SetColumn(sendButton, 4);
            row.Children.Add(sendButton);
            grid.Children.Add(Row(row, 1));
            return grid;
        }

        private UIElement BuildDiagnostics(
            out TextBox metricsBox,
            out TextBox jsonBox,
            out TextBlock feedback)
        {
            metricsBox = CreateReadOnlyTextBox(true, 10.5, true);
            metricsBox.Height = 72.0;
            metricsBox.Text = "诊断信息将在面板打开后匿名更新。";

            jsonBox = CreateReadOnlyTextBox(false, 10.5, true);
            jsonBox.FontFamily = new FontFamily("Consolas");
            jsonBox.Height = 120.0;

            var copyButton = CreateActionButton(
                "复制 JSON",
                "将 canonical JSON 全文复制到剪贴板；内容不含图纸路径。",
                OnCopyJsonClick,
                false);
            feedback = new TextBlock
            {
                FontSize = 10.5,
                Foreground = SecondaryText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
            };
            var copyRow = new Grid
            {
                Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
            };
            copyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            copyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            Grid.SetColumn(copyButton, 0);
            copyRow.Children.Add(copyButton);
            Grid.SetColumn(feedback, 1);
            copyRow.Children.Add(feedback);

            var jsonGrid = new Grid
            {
                Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
            };
            jsonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            jsonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var jsonLabel = new TextBlock
            {
                Text = "Canonical JSON（开发诊断，已脱敏）",
                FontSize = 10.5,
                Foreground = FaintText,
            };
            Grid.SetRow(jsonLabel, 0);
            jsonGrid.Children.Add(jsonLabel);
            Grid.SetRow(jsonBox, 1);
            jsonGrid.Children.Add(jsonBox);
            Grid.SetRow(copyRow, 2);
            jsonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            jsonGrid.Children.Add(copyRow);

            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(metricsBox, 0);
            content.Children.Add(metricsBox);
            Grid.SetRow(jsonGrid, 1);
            content.Children.Add(jsonGrid);

            return new Expander
            {
                Header = "诊断",
                IsExpanded = false,
                Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
                FontSize = 11.0,
                Foreground = SecondaryText,
                Content = content,
            };
        }

        internal void UpdateMetrics(string value)
        {
            RunOnDispatcher(() => metrics.Text = value ?? string.Empty);
        }

        internal void UpdateContext(PaletteContextView context)
        {
            if (context == null)
            {
                return;
            }

            RunOnDispatcher(() =>
            {
                if (context.Published)
                {
                    selectionSummary.Text = "选择：已捕获 · 共 "
                        + context.SelectedCount.ToString(CultureInfo.InvariantCulture)
                        + " · 解析 "
                        + context.ParsedEntityCount.ToString(CultureInfo.InvariantCulture)
                        + " · 不支持 "
                        + context.UnsupportedEntityCount.ToString(CultureInfo.InvariantCulture)
                        + (context.Complete ? " · 完整" : " · 不完整");
                    contextDetail.Text = StripContextHashLines(context.ReadableSummary);
                    json.Text = context.CanonicalJson;
                }
                else
                {
                    selectionSummary.Text = "选择：未捕获";
                    contextDetail.Text = string.IsNullOrEmpty(context.ReadableSummary)
                        ? string.Empty
                        : StripContextHashLines(context.ReadableSummary);
                    json.Text = string.Empty;
                }

                copyFeedback.Text = string.Empty;
            });
        }

        internal void UpdateAgentStatus(string value)
        {
            RunOnDispatcher(() =>
            {
                var view = PaletteAgentStatusView.FromHostStatus(value);
                agentStatusText.Text = view.DisplayText;
                agentStatusText.ToolTip = view.DisplayText;
                SetTone(agentStatusDot, view.Tone);
            });
        }

        internal void UpdateAgentSnapshot(AgentClientSnapshot snapshot)
        {
            RunOnDispatcher(() => ApplySnapshot(snapshot ?? AgentClientSnapshot.Offline));
        }

        internal void UpdateDrawingIndex(string rawStatus, PaletteDrawingIndexView view)
        {
            RunOnDispatcher(() =>
            {
                currentIndexView = view ?? PaletteDrawingIndexView.Empty;
                indexSummary.Text = "索引：" + currentIndexView.StatusLabel;
                indexDetail.Text = currentIndexView.BuildStatsText();
                indexRawStatus.Text = rawStatus ?? string.Empty;
                startIndex.IsEnabled = currentIndexView.CanStart;
                cancelIndex.IsEnabled = currentIndexView.CanCancel;
            });
        }

        internal void SyncMessages(IReadOnlyList<PaletteMessage> messages)
        {
            RunOnDispatcher(() =>
            {
                pendingMessages = messages;
                var now = Environment.TickCount;
                if (unchecked(now - lastMessageSyncTick) >= MessageSyncWindowMilliseconds)
                {
                    ApplyMessages();
                    return;
                }

                if (!messageSyncPending)
                {
                    messageSyncPending = true;
                    EnsureMessageTimer().Start();
                }
            });
        }

        internal void SetDraft(long epoch, string text)
        {
            RunOnDispatcher(() =>
            {
                if (epoch == draftEpoch)
                {
                    return;
                }

                draftEpoch = epoch;
                suppressDraftSave = true;
                prompt.Text = text ?? string.Empty;
                prompt.CaretIndex = prompt.Text.Length;
                suppressDraftSave = false;
            });
        }

        private void ApplySnapshot(AgentClientSnapshot snapshot)
        {
            currentSnapshot = snapshot;
            var availability = PaletteCommandAvailability.FromSnapshot(snapshot);
            startAgent.IsEnabled = availability.CanStartAgent;
            stopAgent.IsEnabled = availability.CanStopAgent;
            newConversation.IsEnabled = availability.CanNewConversation;
            send.IsEnabled = availability.CanSend;
            send.ToolTip = availability.SendHint.Length == 0
                ? "发送只读问题；当前选择上下文或整图索引将自动附加。"
                : availability.SendHint;
            cancelTurn.IsEnabled = availability.CanCancelTurn;
        }

        private void ApplyMessages()
        {
            messageSyncPending = false;
            lastMessageSyncTick = Environment.TickCount;
            var messages = pendingMessages;
            pendingMessages = null;
            if (messages == null)
            {
                return;
            }

            while (rowVisuals.Count > messages.Count)
            {
                var last = rowVisuals.Count - 1;
                messageList.Items.RemoveAt(last);
                rowVisuals.RemoveAt(last);
            }

            for (var index = 0; index < messages.Count; index++)
            {
                var message = messages[index];
                if (index < rowVisuals.Count && rowVisuals[index].Sequence == message.Sequence)
                {
                    rowVisuals[index].Update(message);
                    continue;
                }

                var visual = new RowVisual(message);
                rowVisuals.Insert(index, visual);
                messageList.Items.Insert(index, visual.Root);
            }

            if (scrollPinned && messageList.Items.Count > 0)
            {
                messageList.ScrollIntoView(messageList.Items[messageList.Items.Count - 1]);
            }

            UpdateBackToLatestVisibility();
        }

        private DispatcherTimer EnsureMessageTimer()
        {
            if (messageTimer == null)
            {
                messageTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(MessageSyncWindowMilliseconds),
                };
                messageTimer.Tick += OnMessageTimerTick;
            }

            return messageTimer;
        }

        private void OnMessageTimerTick(object sender, EventArgs args)
        {
            var timer = sender as DispatcherTimer;
            if (timer != null)
            {
                timer.Stop();
            }

            ApplyMessages();
        }

        private async void OnStartAgentClick(object sender, RoutedEventArgs args)
        {
            await RunAgentAction(
                MvpAgentRuntime.StartAsync,
                "启动 AgentHost",
                MvpAgentFailureStages.StartingAgentHost);
        }

        private async void OnStopAgentClick(object sender, RoutedEventArgs args)
        {
            await RunAgentAction(
                MvpAgentRuntime.StopAsync,
                "停止 AgentHost",
                MvpAgentFailureStages.StoppingAgentHost);
        }

        private async void OnNewConversationClick(object sender, RoutedEventArgs args)
        {
            await RunAgentAction(
                MvpAgentRuntime.NewConversationAsync,
                "新建 Codex 对话",
                MvpAgentFailureStages.StartingConversation);
        }

        private async void OnCancelTurnClick(object sender, RoutedEventArgs args)
        {
            await RunAgentAction(
                MvpAgentRuntime.CancelAsync,
                "取消 Codex 回合",
                MvpAgentFailureStages.CancellingTurn);
        }

        private async void OnSendClick(object sender, RoutedEventArgs args)
        {
            await SendCurrentPrompt();
        }

        private void OnPromptKeyDown(object sender, KeyEventArgs args)
        {
            if (args == null)
            {
                return;
            }

            // An Enter consumed by an active IME composition arrives as Key.ImeProcessed and
            // must keep its default newline/commit behavior.
            var isEnter = args.Key == Key.Enter || args.Key == Key.Return;
            if (!isEnter)
            {
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                return;
            }

            args.Handled = true;
            var ignored = SendCurrentPrompt();
        }

        private void OnPromptTextChanged(object sender, TextChangedEventArgs args)
        {
            if (!suppressDraftSave)
            {
                UnifiedPaletteRuntime.SaveDraft(prompt.Text);
            }
        }

        private async System.Threading.Tasks.Task SendCurrentPrompt()
        {
            if (!PaletteCommandAvailability.FromSnapshot(currentSnapshot).CanSend)
            {
                return;
            }

            var value = prompt.Text;
            if (string.IsNullOrWhiteSpace(value))
            {
                agentStatusText.Text = "请输入只读问题。";
                return;
            }

            var submitted = value;
            UnifiedPaletteRuntime.RecordUserPrompt(submitted.Trim());
            try
            {
                await MvpAgentRuntime.AskAsync(submitted);
                if (PaletteDraftGuard.ShouldClearAfterSend(submitted, prompt.Text))
                {
                    suppressDraftSave = true;
                    prompt.Clear();
                    suppressDraftSave = false;
                    UnifiedPaletteRuntime.SaveDraft(string.Empty);
                }
            }
            catch (Exception exception)
            {
                UnifiedPaletteRuntime.RecordPromptError(
                    MvpAgentFailureFormatter
                        .FromException(exception, MvpAgentFailureStages.SendingTurn)
                        .FormatForUser("发送只读问题"));
            }
        }

        private async System.Threading.Tasks.Task RunAgentAction(
            Func<System.Threading.Tasks.Task> action,
            string operationName,
            string errorStage)
        {
            if (action == null)
            {
                return;
            }

            try
            {
                await action();
            }
            catch (Exception exception)
            {
                UnifiedPaletteRuntime.RecordPromptError(
                    MvpAgentFailureFormatter
                        .FromException(exception, errorStage)
                        .FormatForUser(operationName));
            }
        }

        private void OnCopyJsonClick(object sender, RoutedEventArgs args)
        {
            var value = json.Text;
            if (string.IsNullOrEmpty(value))
            {
                copyFeedback.Text = PaletteClipboardFeedback.Empty;
                return;
            }

            try
            {
                Clipboard.SetText(value);
                copyFeedback.Text = PaletteClipboardFeedback.Copied;
            }
            catch (Exception exception)
            {
                // Bounded, sanitized, retryable: the fixed hint never carries exception content.
                copyFeedback.Text = PaletteClipboardFeedback.FromException(exception);
            }
        }

        private void OnClearContextClick(object sender, RoutedEventArgs args)
        {
            try
            {
                UnifiedReadOnlyContextRuntime.Clear("palette-user-clear");
            }
            catch (Exception exception)
            {
                UnifiedPaletteRuntime.RecordPromptError(
                    "清除 CAD 上下文失败（"
                    + exception.GetType().Name
                    + "）；图纸未修改、未保存。");
            }
        }

        private void OnClearAllClick(object sender, RoutedEventArgs args)
        {
            try
            {
                MvpAgentRuntime.ClearAll();
            }
            catch (Exception exception)
            {
                UnifiedPaletteRuntime.RecordPromptError(
                    MvpAgentFailureFormatter
                        .FromException(exception, MvpAgentFailureStages.ClearingConversation)
                        .FormatForUser("清除全部"));
            }
        }

        private void OnStartIndexClick(object sender, RoutedEventArgs args)
        {
            var scope = DrawingIndexScopes.Drawing;
            foreach (var option in scopeOptions)
            {
                if (option.IsChecked == true)
                {
                    scope = option.Tag as string ?? DrawingIndexScopes.Drawing;
                    break;
                }
            }

            try
            {
                DrawingIndexRuntime.Start(scope);
            }
            catch (Exception exception)
            {
                UnifiedPaletteRuntime.RecordPromptError(
                    "整图索引启动失败（"
                    + exception.GetType().Name
                    + "）；图纸未修改、未保存。");
            }
            finally
            {
                // Start/cancel buttons always re-derive from the real descriptor afterwards,
                // so a failure never strands the controls in a disabled state.
                UnifiedPaletteRuntime.RefreshDrawingIndexView();
            }
        }

        private void OnCancelIndexClick(object sender, RoutedEventArgs args)
        {
            try
            {
                DrawingIndexRuntime.Cancel();
            }
            catch (Exception exception)
            {
                UnifiedPaletteRuntime.RecordPromptError(
                    "整图索引取消失败（"
                    + exception.GetType().Name
                    + "）；可按真实状态重试，图纸未修改、未保存。");
            }
            finally
            {
                UnifiedPaletteRuntime.RefreshDrawingIndexView();
            }
        }

        private void OnToggleDetailsClick(object sender, RoutedEventArgs args)
        {
            var show = detailsPanel.Visibility != Visibility.Visible;
            detailsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            toggleDetails.Content = show ? "收起" : "详情";
        }

        private void OnMessageListLoaded(object sender, RoutedEventArgs args)
        {
            var scroller = FindScrollViewer(messageList);
            if (scroller != null)
            {
                messageScroller = scroller;
                messageScroller.ScrollChanged += OnMessageScrollChanged;
            }
        }

        private void OnMessageScrollChanged(object sender, ScrollChangedEventArgs args)
        {
            if (messageScroller == null)
            {
                return;
            }

            if (args.ExtentHeightChange > 0.0 && scrollPinned)
            {
                messageScroller.ScrollToEnd();
                return;
            }

            scrollPinned = messageScroller.ScrollableHeight - messageScroller.VerticalOffset < 8.0;
            UpdateBackToLatestVisibility();
        }

        private void OnBackToLatestClick(object sender, RoutedEventArgs args)
        {
            scrollPinned = true;
            if (messageScroller != null)
            {
                messageScroller.ScrollToEnd();
            }

            UpdateBackToLatestVisibility();
        }

        private void UpdateBackToLatestVisibility()
        {
            backToLatest.Visibility = scrollPinned
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private TextBlock CreateStripText(string initial)
        {
            return new TextBlock
            {
                Text = initial,
                FontSize = 11.5,
                Foreground = PrimaryText,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(6.0, 0.0, 0.0, 0.0),
            };
        }

        private UIElement BuildStripLine(TextBlock text, params Button[] buttons)
        {
            var line = new Grid
            {
                MinHeight = PaletteLayoutPolicy.ContextBarMinHeight,
            };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            Grid.SetColumn(text, 0);
            line.Children.Add(text);
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            foreach (var button in buttons)
            {
                actions.Children.Add(button);
            }

            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(actions, 1);
            line.Children.Add(actions);
            return line;
        }

        private RadioButton CreateScopeOption(
            Panel host,
            string label,
            string scope,
            bool selected)
        {
            var option = new RadioButton
            {
                Content = label,
                Tag = scope,
                IsChecked = selected,
                Style = ScopeToggleStyle,
                GroupName = "drawingIndexScope",
                MinHeight = 26.0,
                FontSize = 11.0,
                Margin = new Thickness(0.0, 0.0, 4.0, 2.0),
                ToolTip = "选择只读索引范围：" + label,
            };
            host.Children.Add(option);
            return option;
        }

        private Button CreateActionButton(
            string content,
            string toolTip,
            RoutedEventHandler onClick,
            bool accent)
        {
            var button = new Button
            {
                Content = content,
                Style = accent ? AccentButtonStyle : ChromeButtonStyle,
                MinHeight = PaletteLayoutPolicy.ActionButtonMinHeight,
                Margin = new Thickness(2.0, 1.0, 2.0, 1.0),
                Padding = new Thickness(10.0, 2.0, 10.0, 2.0),
                FontSize = 11.5,
                ToolTip = toolTip,
            };
            if (onClick != null)
            {
                button.Click += onClick;
            }

            return button;
        }

        private static UIElement Row(UIElement child, int row)
        {
            Grid.SetRow(child, row);
            return child;
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root == null)
            {
                return null;
            }

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                var scroller = child as ScrollViewer;
                if (scroller != null)
                {
                    return scroller;
                }

                var nested = FindScrollViewer(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static string StripContextHashLines(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var lines = value.Replace("\r\n", "\n").Split('\n');
            var builder = new StringBuilder(value.Length);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.StartsWith("上下文 SHA-256：", StringComparison.Ordinal))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(line);
            }

            return builder.ToString().TrimEnd();
        }

        private static void SetTone(Ellipse dot, PaletteStatusTone tone)
        {
            switch (tone)
            {
                case PaletteStatusTone.Busy:
                    dot.Fill = ToneBusy;
                    break;
                case PaletteStatusTone.Success:
                    dot.Fill = ToneSuccess;
                    break;
                case PaletteStatusTone.Warning:
                    dot.Fill = ToneWarning;
                    break;
                case PaletteStatusTone.Failure:
                    dot.Fill = ToneFailure;
                    break;
                default:
                    dot.Fill = ToneNeutral;
                    break;
            }
        }

        private void RunOnDispatcher(Action action)
        {
            if (Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.BeginInvoke(action);
            }
        }

        private static TextBox CreateReadOnlyTextBox(bool wrap, double fontSize, bool dark)
        {
            return new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                IsUndoEnabled = false,
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = wrap
                    ? ScrollBarVisibility.Disabled
                    : ScrollBarVisibility.Auto,
                VerticalContentAlignment = VerticalAlignment.Top,
                Padding = new Thickness(6.0),
                FontSize = fontSize,
                Background = dark ? InputBackground : SurfaceBackground,
                Foreground = dark ? SecondaryText : PrimaryText,
                BorderBrush = SurfaceBorder,
                CaretBrush = PrimaryText,
            };
        }

        private static Brush CreateBrush(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        private static Style CreateButtonStyle(
            Brush background,
            Brush hover,
            Brush pressed,
            Brush foreground,
            bool toggle)
        {
            var border = new FrameworkElementFactory(typeof(Border), "chrome");
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(PaletteLayoutPolicy.CornerRadiusDip));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);

            var template = new ControlTemplate(toggle ? typeof(RadioButton) : typeof(Button))
            {
                VisualTree = border,
            };

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hover, "chrome"));
            template.Triggers.Add(hoverTrigger);

            if (!toggle)
            {
                var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
                pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, pressed, "chrome"));
                template.Triggers.Add(pressedTrigger);
            }
            else
            {
                var checkedTrigger = new Trigger { Property = RadioButton.IsCheckedProperty, Value = true };
                checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, AccentPressed, "chrome"));
                template.Triggers.Add(checkedTrigger);
            }

            var style = new Style(toggle ? typeof(RadioButton) : typeof(Button));
            style.Setters.Add(new Setter(Button.BackgroundProperty, background));
            style.Setters.Add(new Setter(Button.ForegroundProperty, foreground));
            style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0.0)));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            style.Triggers.Add(disabledTrigger);
            return style;
        }

        /// <summary>One materialized message row; updated in place to avoid full-list repaints.</summary>
        private sealed class RowVisual
        {
            private readonly Border root;
            private readonly TextBlock tag;
            private readonly TextBlock body;
            private PaletteMessageKind kind;

            internal RowVisual(PaletteMessage message)
            {
                tag = new TextBlock
                {
                    FontSize = 10.5,
                    Width = 46.0,
                    Margin = new Thickness(0.0, 1.0, 0.0, 0.0),
                };
                body = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12.5,
                    Foreground = PrimaryText,
                };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
                Grid.SetColumn(tag, 0);
                grid.Children.Add(tag);
                Grid.SetColumn(body, 1);
                grid.Children.Add(body);
                root = new Border
                {
                    Padding = new Thickness(6.0, 3.0, 6.0, 3.0),
                    Margin = new Thickness(0.0, 1.0, 0.0, 1.0),
                    CornerRadius = new CornerRadius(PaletteLayoutPolicy.CornerRadiusDip),
                    Child = grid,
                };
                kind = (PaletteMessageKind)(-1);
                Update(message);
            }

            internal Border Root
            {
                get { return root; }
            }

            internal int Sequence { get; private set; }

            internal void Update(PaletteMessage message)
            {
                Sequence = message.Sequence;
                if (kind != message.Kind)
                {
                    kind = message.Kind;
                    ApplyKindStyle(kind);
                }

                tag.Text = message.IsStreaming ? "Codex…" : TagLabel(kind);
                body.Text = message.Text;
            }

            private void ApplyKindStyle(PaletteMessageKind current)
            {
                switch (current)
                {
                    case PaletteMessageKind.User:
                        root.Background = UserRowBackground;
                        tag.Foreground = UserTagText;
                        body.Foreground = PrimaryText;
                        body.FontSize = 12.5;
                        break;
                    case PaletteMessageKind.Assistant:
                        root.Background = Brushes.Transparent;
                        tag.Foreground = AssistantTagText;
                        body.Foreground = PrimaryText;
                        body.FontSize = 12.5;
                        break;
                    case PaletteMessageKind.Error:
                        root.Background = Brushes.Transparent;
                        tag.Foreground = ErrorText;
                        body.Foreground = ErrorText;
                        body.FontSize = 11.5;
                        break;
                    default:
                        root.Background = Brushes.Transparent;
                        tag.Foreground = FaintText;
                        body.Foreground = SecondaryText;
                        body.FontSize = 11.0;
                        break;
                }
            }

            private static string TagLabel(PaletteMessageKind current)
            {
                switch (current)
                {
                    case PaletteMessageKind.User:
                        return "你";
                    case PaletteMessageKind.Assistant:
                        return "Codex";
                    case PaletteMessageKind.Error:
                        return "错误";
                    default:
                        return "状态";
                }
            }
        }
    }
}
