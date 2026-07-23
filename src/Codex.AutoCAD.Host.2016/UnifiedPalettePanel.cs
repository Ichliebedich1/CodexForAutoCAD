using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Host2016
{
    /// <summary>
    /// Read-only work palette for AutoCAD 2016. Three compact sections (conversation, current
    /// selection, drawing index) render only Host-provided state; every action calls existing
    /// Host runtime entry points. CAD write and plugin-initiated save stay disabled.
    /// </summary>
    internal sealed class UnifiedPalettePanel : UserControl
    {
        private static readonly Brush PanelBackground = CreateBrush(0xF5, 0xF5, 0xF5);
        private static readonly Brush SurfaceBackground = CreateBrush(0xFF, 0xFF, 0xFF);
        private static readonly Brush SurfaceBorder = CreateBrush(0xE0, 0xE0, 0xE0);
        private static readonly Brush PrimaryText = CreateBrush(0x21, 0x21, 0x21);
        private static readonly Brush SecondaryText = CreateBrush(0x61, 0x61, 0x61);
        private static readonly Brush BoundaryText = CreateBrush(0xB7, 0x1C, 0x1C);
        private static readonly Brush BoundaryBackground = CreateBrush(0xFD, 0xEC, 0xEA);
        private static readonly Brush PrimaryActionBackground = CreateBrush(0x19, 0x76, 0xD2);
        private static readonly Brush PrimaryActionForeground = CreateBrush(0xFF, 0xFF, 0xFF);
        private static readonly Brush DangerText = CreateBrush(0xB7, 0x1C, 0x1C);
        private static readonly Brush ToneNeutral = CreateBrush(0x9E, 0x9E, 0x9E);
        private static readonly Brush ToneBusy = CreateBrush(0xF9, 0xA8, 0x25);
        private static readonly Brush ToneSuccess = CreateBrush(0x43, 0xA0, 0x47);
        private static readonly Brush ToneWarning = CreateBrush(0xFB, 0x8C, 0x00);
        private static readonly Brush ToneFailure = CreateBrush(0xE5, 0x39, 0x35);

        // Assigned once from the Build*Tab helpers during construction; C# does not allow
        // readonly fields to be assigned through out parameters outside the constructor body.
        private TextBlock agentStatusText;
        private Ellipse agentStatusDot;
        private TextBox agentText;
        private TextBox prompt;
        private Button startAgent;
        private Button stopAgent;
        private Button newConversation;
        private Button cancelTurn;
        private Button send;

        private TextBlock contextStatusText;
        private Ellipse contextStatusDot;
        private TextBlock contextStats;
        private TextBox summary;
        private TextBox json;
        private TextBlock copyFeedback;

        private TextBlock indexStatusText;
        private Ellipse indexStatusDot;
        private TextBlock indexStats;
        private TextBlock indexRawStatus;
        private ComboBox indexScope;
        private Button startIndex;
        private Button cancelIndex;

        private TextBox metrics;

        private bool sendInFlight;

        internal UnifiedPalettePanel()
        {
            Background = PanelBackground;

            var root = new Grid
            {
                Margin = new Thickness(10.0),
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "Codex for AutoCAD 2016",
                FontSize = 13.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = PrimaryText,
            };
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            var boundary = new Border
            {
                Background = BoundaryBackground,
                CornerRadius = new CornerRadius(4.0),
                Padding = new Thickness(8.0, 4.0, 8.0, 4.0),
                Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
                Child = new TextBlock
                {
                    Text = "只读 · CAD 写入禁用 · 不会自动保存 DWG",
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = BoundaryText,
                    TextWrapping = TextWrapping.Wrap,
                },
            };
            Grid.SetRow(boundary, 1);
            root.Children.Add(boundary);

            var tabs = new TabControl
            {
                Margin = new Thickness(0.0, 8.0, 0.0, 0.0),
                Background = PanelBackground,
            };
            tabs.Items.Add(new TabItem
            {
                Header = "对话",
                Content = BuildConversationTab(),
            });
            tabs.Items.Add(new TabItem
            {
                Header = "当前选择",
                Content = BuildSelectionTab(),
            });
            tabs.Items.Add(new TabItem
            {
                Header = "整图索引",
                Content = BuildDrawingIndexTab(),
            });
            Grid.SetRow(tabs, 2);
            root.Children.Add(tabs);

            metrics = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                IsUndoEnabled = false,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10.5,
                Height = 96.0,
                Padding = new Thickness(6.0),
                Background = SurfaceBackground,
                Foreground = SecondaryText,
                Text = "诊断信息将在面板打开后匿名更新。",
            };
            var diagnostics = new Expander
            {
                Header = "诊断",
                IsExpanded = false,
                Margin = new Thickness(0.0, 8.0, 0.0, 0.0),
                FontSize = 11.5,
                Foreground = SecondaryText,
                Content = metrics,
            };
            Grid.SetRow(diagnostics, 3);
            root.Children.Add(diagnostics);

            Content = root;
        }

        private FrameworkElement BuildConversationTab()
        {
            var grid = new Grid
            {
                Margin = new Thickness(0.0, 8.0, 0.0, 0.0),
            };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var statusChip = CreateStatusChip(
                out agentStatusDot,
                out agentStatusText,
                "Agent 离线；只读模式。");
            Grid.SetRow(statusChip, 0);
            grid.Children.Add(statusChip);

            agentText = CreateReadOnlyTextBox(true);
            agentText.Margin = new Thickness(0.0, 6.0, 0.0, 0.0);
            agentText.FontSize = 12.5;
            Grid.SetRow(agentText, 1);
            grid.Children.Add(agentText);

            var input = new Grid
            {
                Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
            };
            input.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            input.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            prompt = new TextBox
            {
                Height = 56.0,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(6.0),
                FontSize = 12.0,
                Background = SurfaceBackground,
                Foreground = PrimaryText,
                ToolTip = "输入只读问题。Enter 发送，Shift+Enter 换行；发送前请先建立整图索引或捕获选择上下文。",
            };
            prompt.KeyDown += OnPromptKeyDown;
            Grid.SetRow(prompt, 0);
            input.Children.Add(prompt);

            startAgent = CreateActionButton(
                "启动 Agent",
                "启动本机 AgentHost 并建立只读 Codex 会话；不会修改 CAD。",
                OnStartAgentClick);
            stopAgent = CreateActionButton(
                "停止 Agent",
                "停止 AgentHost 并回收连接；不会修改 CAD。",
                OnStopAgentClick);
            newConversation = CreateActionButton(
                "新建对话",
                "保留当前 CAD 上下文，建立新的 Codex 对话。",
                OnNewConversationClick);
            cancelTurn = CreateActionButton(
                "取消回合",
                "幂等取消当前 Codex 回合；不会修改 CAD。",
                OnCancelTurnClick);
            send = CreateActionButton(
                "发送",
                "发送只读问题；当前选择上下文或整图索引将自动附加。",
                OnSendClick);
            send.Background = PrimaryActionBackground;
            send.Foreground = PrimaryActionForeground;
            send.BorderBrush = PrimaryActionBackground;

            var lifecycle = new UniformGrid
            {
                Columns = 3,
                Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
            };
            lifecycle.Children.Add(startAgent);
            lifecycle.Children.Add(stopAgent);
            lifecycle.Children.Add(newConversation);
            Grid.SetRow(lifecycle, 1);

            var turnActions = new UniformGrid
            {
                Columns = 2,
                Margin = new Thickness(0.0, 2.0, 0.0, 0.0),
            };
            turnActions.Children.Add(cancelTurn);
            turnActions.Children.Add(send);
            Grid.SetRow(turnActions, 2);

            input.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            input.Children.Add(lifecycle);
            input.Children.Add(turnActions);
            Grid.SetRow(input, 2);
            grid.Children.Add(input);

            return grid;
        }

        private FrameworkElement BuildSelectionTab()
        {
            var grid = new Grid
            {
                Margin = new Thickness(0.0, 8.0, 0.0, 0.0),
            };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var statusChip = CreateStatusChip(
                out contextStatusDot,
                out contextStatusText,
                "尚未捕获选择上下文。先预选对象，再执行 CODEX16CTX。");
            Grid.SetRow(statusChip, 0);
            grid.Children.Add(statusChip);

            contextStats = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.0,
                Foreground = PrimaryText,
                Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
            };
            Grid.SetRow(contextStats, 1);
            grid.Children.Add(contextStats);

            var contextTabs = new TabControl
            {
                Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
            };

            summary = CreateReadOnlyTextBox(true);
            contextTabs.Items.Add(new TabItem
            {
                Header = "可读摘要",
                Content = summary,
            });

            var jsonGrid = new Grid();
            jsonGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            jsonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            json = CreateReadOnlyTextBox(false);
            json.FontFamily = new FontFamily("Consolas");
            json.FontSize = 11.0;
            Grid.SetRow(json, 0);
            jsonGrid.Children.Add(json);

            var copyRow = new Grid
            {
                Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
            };
            copyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            copyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            var copyJson = CreateActionButton(
                "复制 JSON",
                "将 canonical JSON 全文复制到剪贴板；内容不含图纸路径。",
                OnCopyJsonClick);
            Grid.SetColumn(copyJson, 0);
            copyRow.Children.Add(copyJson);
            copyFeedback = new TextBlock
            {
                FontSize = 11.5,
                Foreground = SecondaryText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
            };
            Grid.SetColumn(copyFeedback, 1);
            copyRow.Children.Add(copyFeedback);
            Grid.SetRow(copyRow, 1);
            jsonGrid.Children.Add(copyRow);

            contextTabs.Items.Add(new TabItem
            {
                Header = "Canonical JSON",
                Content = jsonGrid,
            });
            Grid.SetRow(contextTabs, 2);
            grid.Children.Add(contextTabs);

            var clearContext = CreateActionButton(
                "清除 CAD 上下文",
                "只清除内存中的选择上下文；不影响当前 Codex 对话和整图索引，不修改图纸。",
                OnClearContextClick);
            var clearAll = CreateActionButton(
                "清除全部",
                "清除 CAD 上下文、回答文本和当前 Codex 对话；不会修改 CAD。",
                OnClearAllClick);
            clearAll.Foreground = DangerText;
            clearAll.BorderBrush = DangerText;

            var actions = new UniformGrid
            {
                Columns = 2,
                Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
            };
            actions.Children.Add(clearContext);
            actions.Children.Add(clearAll);
            Grid.SetRow(actions, 3);
            grid.Children.Add(actions);

            return grid;
        }

        private FrameworkElement BuildDrawingIndexTab()
        {
            var grid = new Grid
            {
                Margin = new Thickness(0.0, 8.0, 0.0, 0.0),
            };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var statusChip = CreateStatusChip(
                out indexStatusDot,
                out indexStatusText,
                "未建立");
            Grid.SetRow(statusChip, 0);
            grid.Children.Add(statusChip);

            indexStats = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.0,
                Foreground = PrimaryText,
                Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
            };
            Grid.SetRow(indexStats, 1);
            grid.Children.Add(indexStats);

            indexRawStatus = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 48.0,
                FontSize = 11.0,
                Foreground = SecondaryText,
                Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
            };
            Grid.SetRow(indexRawStatus, 2);
            grid.Children.Add(indexRawStatus);

            var hint = new TextBlock
            {
                Text = "扫描在 AutoCAD 空闲时按只读分片执行；不修改、不保存图纸。",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11.0,
                Foreground = SecondaryText,
                Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
            };
            Grid.SetRow(hint, 3);
            grid.Children.Add(hint);

            indexScope = new ComboBox
            {
                MinHeight = 32.0,
                FontSize = 12.0,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0.0, 0.0, 4.0, 0.0),
                ToolTip = "选择只读索引范围。",
            };
            AddScopeItem(indexScope, "整张图纸", DrawingIndexScopes.Drawing, true);
            AddScopeItem(indexScope, "模型空间", DrawingIndexScopes.ModelSpace, false);
            AddScopeItem(indexScope, "当前空间", DrawingIndexScopes.CurrentSpace, false);
            AddScopeItem(indexScope, "所有布局", DrawingIndexScopes.Layouts, false);
            AddScopeItem(indexScope, "当前选择（先预选）", DrawingIndexScopes.Selection, false);

            startIndex = CreateActionButton(
                "开始扫描",
                "按所选范围建立整图索引；只读扫描，不修改图纸。",
                OnStartIndexClick);
            cancelIndex = CreateActionButton(
                "取消扫描",
                "幂等取消当前扫描；已建立的索引状态不变。",
                OnCancelIndexClick);
            cancelIndex.IsEnabled = false;

            var actions = new Grid
            {
                Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
            };
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(indexScope, 0);
            actions.Children.Add(indexScope);
            Grid.SetColumn(startIndex, 1);
            actions.Children.Add(startIndex);
            Grid.SetColumn(cancelIndex, 2);
            actions.Children.Add(cancelIndex);
            Grid.SetRow(actions, 4);
            grid.Children.Add(actions);

            return grid;
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
                    contextStatusText.Text = "已捕获只读选择上下文（CadContextJson v"
                        + context.SchemaVersion.ToString(CultureInfo.InvariantCulture)
                        + "）。";
                    SetTone(contextStatusDot, PaletteStatusTone.Success);
                    var stats = new StringBuilder();
                    stats.Append("已选择：")
                        .Append(context.SelectedCount.ToString(CultureInfo.InvariantCulture));
                    stats.Append("  已解析：")
                        .Append(context.ParsedEntityCount.ToString(CultureInfo.InvariantCulture));
                    stats.Append("  不支持/受限：")
                        .Append(context.UnsupportedEntityCount.ToString(CultureInfo.InvariantCulture));
                    stats.Append("  完整性：").Append(context.Complete ? "完整" : "不完整");
                    stats.Append("\nJSON：")
                        .Append(context.CanonicalBytes.ToString(CultureInfo.InvariantCulture))
                        .Append(" 字节");
                    if (!string.IsNullOrEmpty(context.ReadIssueSummary))
                    {
                        stats.Append("\n").Append(context.ReadIssueSummary);
                    }

                    contextStats.Text = stats.ToString();
                    summary.Text = StripContextHashLines(context.ReadableSummary);
                    json.Text = context.CanonicalJson;
                }
                else
                {
                    var idle = string.IsNullOrEmpty(context.Status)
                        || context.Status == "not-captured"
                        || context.Status.StartsWith("cleared", StringComparison.Ordinal);
                    contextStatusText.Text = idle
                        ? "尚未捕获选择上下文。先预选对象，再执行 CODEX16CTX。"
                        : "捕获未完成：" + context.Status;
                    SetTone(
                        contextStatusDot,
                        idle ? PaletteStatusTone.Neutral : PaletteStatusTone.Warning);
                    contextStats.Text = string.Empty;
                    summary.Text = StripContextHashLines(context.ReadableSummary);
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
                SetTone(agentStatusDot, view.Tone);
            });
        }

        internal void UpdateAgentText(string value)
        {
            RunOnDispatcher(() =>
            {
                agentText.Text = value ?? string.Empty;
                agentText.ScrollToEnd();
            });
        }

        internal void UpdateDrawingIndex(string rawStatus, PaletteDrawingIndexView view)
        {
            RunOnDispatcher(() =>
            {
                var current = view ?? PaletteDrawingIndexView.Empty;
                indexStatusText.Text = current.StatusLabel;
                SetTone(indexStatusDot, current.Tone);
                indexStats.Text = current.BuildStatsText();
                indexRawStatus.Text = rawStatus ?? string.Empty;
                startIndex.IsEnabled = current.CanStart;
                cancelIndex.IsEnabled = current.CanCancel;
            });
        }

        private async void OnStartAgentClick(object sender, RoutedEventArgs args)
        {
            await RunAgentAction(
                startAgent,
                MvpAgentRuntime.StartAsync,
                "启动 AgentHost",
                MvpAgentFailureStages.StartingAgentHost);
        }

        private async void OnStopAgentClick(object sender, RoutedEventArgs args)
        {
            await RunAgentAction(
                stopAgent,
                MvpAgentRuntime.StopAsync,
                "停止 AgentHost",
                MvpAgentFailureStages.StoppingAgentHost);
        }

        private async void OnNewConversationClick(object sender, RoutedEventArgs args)
        {
            await RunAgentAction(
                newConversation,
                MvpAgentRuntime.NewConversationAsync,
                "新建 Codex 对话",
                MvpAgentFailureStages.StartingConversation);
        }

        private async void OnCancelTurnClick(object sender, RoutedEventArgs args)
        {
            await RunAgentAction(
                cancelTurn,
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

        private async System.Threading.Tasks.Task SendCurrentPrompt()
        {
            if (sendInFlight)
            {
                return;
            }

            var value = prompt.Text;
            if (string.IsNullOrWhiteSpace(value))
            {
                UpdateAgentStatus("请输入只读问题。");
                return;
            }

            sendInFlight = true;
            send.IsEnabled = false;
            try
            {
                await MvpAgentRuntime.AskAsync(value);
                prompt.Clear();
            }
            catch (Exception exception)
            {
                UpdateAgentStatus(
                    MvpAgentFailureFormatter
                        .FromException(exception, MvpAgentFailureStages.SendingTurn)
                        .FormatForUser("发送只读问题"));
            }
            finally
            {
                sendInFlight = false;
                send.IsEnabled = true;
            }
        }

        private async System.Threading.Tasks.Task RunAgentAction(
            Button button,
            Func<System.Threading.Tasks.Task> action,
            string operationName,
            string errorStage)
        {
            if (button == null || action == null)
            {
                return;
            }

            button.IsEnabled = false;
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                UpdateAgentStatus(
                    MvpAgentFailureFormatter
                        .FromException(exception, errorStage)
                        .FormatForUser(operationName));
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        private void OnCopyJsonClick(object sender, RoutedEventArgs args)
        {
            var value = json.Text;
            if (string.IsNullOrEmpty(value))
            {
                copyFeedback.Text = "暂无可复制的 JSON。";
                return;
            }

            try
            {
                Clipboard.SetText(value);
                copyFeedback.Text = "已复制到剪贴板。";
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                copyFeedback.Text = "剪贴板暂不可用，请重试。";
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
                contextStatusText.Text = "清除 CAD 上下文失败（"
                    + exception.GetType().Name
                    + "）；图纸未修改、未保存。";
                SetTone(contextStatusDot, PaletteStatusTone.Failure);
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
                UpdateAgentStatus(
                    MvpAgentFailureFormatter
                        .FromException(exception, MvpAgentFailureStages.ClearingConversation)
                        .FormatForUser("清除全部"));
            }
        }

        private void OnStartIndexClick(object sender, RoutedEventArgs args)
        {
            var item = indexScope.SelectedItem as ComboBoxItem;
            var scope = item == null ? DrawingIndexScopes.Drawing : item.Tag as string;
            if (string.IsNullOrEmpty(scope))
            {
                scope = DrawingIndexScopes.Drawing;
            }

            startIndex.IsEnabled = false;
            try
            {
                DrawingIndexRuntime.Start(scope);
            }
            catch (Exception exception)
            {
                indexStatusText.Text = "整图索引启动失败（"
                    + exception.GetType().Name
                    + "）；图纸未修改、未保存。";
                SetTone(indexStatusDot, PaletteStatusTone.Failure);
                startIndex.IsEnabled = true;
            }
        }

        private void OnCancelIndexClick(object sender, RoutedEventArgs args)
        {
            cancelIndex.IsEnabled = false;
            try
            {
                DrawingIndexRuntime.Cancel();
            }
            catch (Exception exception)
            {
                indexStatusText.Text = "整图索引取消失败（"
                    + exception.GetType().Name
                    + "）；图纸未修改、未保存。";
                SetTone(indexStatusDot, PaletteStatusTone.Failure);
            }
        }

        private static Border CreateStatusChip(
            out Ellipse dot,
            out TextBlock text,
            string initialText)
        {
            dot = new Ellipse
            {
                Width = 8.0,
                Height = 8.0,
                Fill = ToneNeutral,
                VerticalAlignment = VerticalAlignment.Center,
            };
            text = new TextBlock
            {
                Text = initialText,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 36.0,
                FontSize = 12.0,
                Foreground = PrimaryText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6.0, 0.0, 0.0, 0.0),
            };

            var inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            Grid.SetColumn(dot, 0);
            inner.Children.Add(dot);
            Grid.SetColumn(text, 1);
            inner.Children.Add(text);

            return new Border
            {
                Background = SurfaceBackground,
                BorderBrush = SurfaceBorder,
                BorderThickness = new Thickness(1.0),
                CornerRadius = new CornerRadius(4.0),
                Padding = new Thickness(8.0, 6.0, 8.0, 6.0),
                Child = inner,
            };
        }

        private Button CreateActionButton(
            string content,
            string toolTip,
            RoutedEventHandler onClick)
        {
            var button = new Button
            {
                Content = content,
                MinHeight = 32.0,
                Margin = new Thickness(2.0),
                Padding = new Thickness(10.0, 3.0, 10.0, 3.0),
                FontSize = 12.0,
                ToolTip = toolTip,
            };
            button.Click += onClick;
            return button;
        }

        private static void AddScopeItem(
            ComboBox comboBox,
            string label,
            string scope,
            bool selected)
        {
            var item = new ComboBoxItem
            {
                Content = label,
                Tag = scope,
                IsSelected = selected,
            };
            comboBox.Items.Add(item);
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

        private static TextBox CreateReadOnlyTextBox(bool wrap)
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
                Padding = new Thickness(8.0),
                Background = SurfaceBackground,
                Foreground = PrimaryText,
            };
        }

        private static Brush CreateBrush(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }
    }
}
