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
                Text = "统一只读 MVP 候选 · Agent 未连接 · CAD 写入禁用 · 插件不会保存 DWG",
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

            metrics = new TextBlock
            {
                Text = "Palette 指标将在面板打开后匿名更新。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0.0, 8.0, 0.0, 0.0),
            };
            Grid.SetRow(metrics, 4);
            root.Children.Add(metrics);

            Content = root;
        }

        internal void UpdateMetrics(string value)
        {
            metrics.Text = value ?? string.Empty;
        }

        internal void UpdateContext(PaletteContextView context)
        {
            if (context == null)
            {
                return;
            }

            status.Text = context.Published
                ? "已发布只读 CadContextJson v1：" + context.SelectedCount
                    + " 个图元，" + context.CanonicalBytes + " 字节。"
                : "上下文状态：" + context.Status + "。先预选对象，再执行 CODEX16CTX。";
            summary.Text = context.ReadableSummary;
            json.Text = context.CanonicalJson;
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

