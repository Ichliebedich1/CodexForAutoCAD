using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Codex.AutoCAD.Host2016
{
    internal sealed class UnifiedPalettePanel : UserControl
    {
        private readonly TextBlock status;
        private readonly TextBox summary;
        private readonly TextBox json;
        private readonly TextBlock agentStatus;
        private readonly TextBox agentText;
        private readonly TextBlock metrics;

        internal UnifiedPalettePanel()
        {
            Background = Brushes.WhiteSmoke;

            var root = new Grid
            {
                Margin = new Thickness(14.0),
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(140.0) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "Codex for AutoCAD 2016",
                FontSize = 20.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black,
                Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
            };
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            var boundaries = new TextBlock
            {
                Text = "统一只读 AI MVP 候选 · Agent 手动连接 · CAD 写入禁用 · 插件不会保存 DWG",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DarkRed,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
            };
            Grid.SetRow(boundaries, 1);
            root.Children.Add(boundaries);

            status = new TextBlock
            {
                Text = "先在图形区预选对象，再执行 CODEX16CTX。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DarkSlateBlue,
                Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
            };
            Grid.SetRow(status, 2);
            root.Children.Add(status);

            var tabs = new TabControl();
            summary = CreateReadOnlyTextBox(true);
            json = CreateReadOnlyTextBox(false);
            json.FontFamily = new FontFamily("Consolas");
            json.FontSize = 12.0;

            tabs.Items.Add(new TabItem
            {
                Header = "可读摘要",
                Content = summary,
            });
            tabs.Items.Add(new TabItem
            {
                Header = "Canonical JSON",
                Content = json,
            });
            Grid.SetRow(tabs, 3);
            root.Children.Add(tabs);

            agentStatus = new TextBlock
            {
                Text = "Agent 离线；先设置 AgentHost 配置或执行 CODEX16AGENTSTART。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DarkSlateBlue,
                Margin = new Thickness(0.0, 8.0, 0.0, 4.0),
            };
            Grid.SetRow(agentStatus, 4);
            root.Children.Add(agentStatus);

            agentText = CreateReadOnlyTextBox(true);
            Grid.SetRow(agentText, 5);
            root.Children.Add(agentText);

            metrics = new TextBlock
            {
                Text = "Palette 指标将在面板打开后匿名更新。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0.0, 8.0, 0.0, 0.0),
            };
            Grid.SetRow(metrics, 6);
            root.Children.Add(metrics);

            var prompt = new TextBox
            {
                MinHeight = 28.0,
                AcceptsReturn = false,
                Margin = new Thickness(0.0, 8.0, 0.0, 0.0),
                ToolTip = "输入只读问题；当前上下文将自动附加。",
            };
            var send = new Button
            {
                Content = "发送给 Codex",
                Margin = new Thickness(8.0, 8.0, 0.0, 0.0),
                Padding = new Thickness(12.0, 4.0, 12.0, 4.0),
            };
            send.Click += async (sender, args) =>
            {
                var value = prompt.Text;
                if (string.IsNullOrWhiteSpace(value))
                {
                    UpdateAgentStatus("请输入只读问题。");
                    return;
                }

                send.IsEnabled = false;
                try
                {
                    await MvpAgentRuntime.AskAsync(value);
                    prompt.Clear();
                }
                catch (Exception exception)
                {
                    UpdateAgentStatus("发送失败：" + exception.GetType().Name + "。" + exception.Message);
                }
                finally
                {
                    send.IsEnabled = true;
                }
            };
            var input = new DockPanel();
            DockPanel.SetDock(send, Dock.Right);
            input.Children.Add(send);
            input.Children.Add(prompt);
            Grid.SetRow(input, 7);
            root.Children.Add(input);

            Content = root;
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
                status.Text = context.Published
                    ? "已发布只读 CadContextJson v1：" + context.SelectedCount
                        + " 个图元，" + context.CanonicalBytes + " 字节。"
                    : "上下文状态：" + context.Status + "。先预选对象，再执行 CODEX16CTX。";
                summary.Text = context.ReadableSummary;
                json.Text = context.CanonicalJson;
            });
        }

        internal void UpdateAgentStatus(string value)
        {
            RunOnDispatcher(() => agentStatus.Text = value ?? string.Empty);
        }

        internal void UpdateAgentText(string value)
        {
            RunOnDispatcher(() =>
            {
                agentText.Text = value ?? string.Empty;
                agentText.ScrollToEnd();
            });
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
                Background = Brushes.White,
                Foreground = Brushes.Black,
            };
        }
    }
}

