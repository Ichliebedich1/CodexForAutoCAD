using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Codex.AutoCAD.Host2016.Palette
{
    internal sealed class CodexPalettePanel : UserControl
    {
        private readonly TextBlock metrics;

        internal CodexPalettePanel()
        {
            Background = Brushes.WhiteSmoke;

            Grid root = new Grid
            {
                Margin = new Thickness(16.0)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock title = new TextBlock
            {
                Text = "Codex for AutoCAD 2016",
                FontSize = 20.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black,
                Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
            };
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            TextBlock boundaries = new TextBlock
            {
                Text = "正式 Palette 阶段\nAgent：禁用\n选择读取：禁用\nCAD 写入：禁用\n自动保存：禁用",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DarkRed,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
            };
            Grid.SetRow(boundaries, 1);
            root.Children.Add(boundaries);

            TextBlock prompt = new TextBlock
            {
                Text = "中文多行输入（仅保留在当前控件内，用于 IME 与布局验证）：",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Black,
                Margin = new Thickness(0.0, 0.0, 0.0, 6.0)
            };
            Grid.SetRow(prompt, 2);
            root.Children.Add(prompt);

            TextBox input = new TextBox
            {
                Text = "中文输入测试：焊缝 A-01，直径 Φ25\n第二行：AutoCAD 2016\n可继续使用中文输入法编辑；内容不会发送、写入图纸或保存。",
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalContentAlignment = VerticalAlignment.Top,
                MinHeight = 180.0,
                Padding = new Thickness(8.0),
                FontSize = 14.0,
                IsUndoEnabled = true
            };
            InputMethod.SetIsInputMethodEnabled(input, true);
            Grid.SetRow(input, 3);
            root.Children.Add(input);

            metrics = new TextBlock
            {
                Text = "事件计数将在面板打开后匿名更新。DBMOD 仅由 CODEX16PALINFO 只读查询。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
            };
            Grid.SetRow(metrics, 4);
            root.Children.Add(metrics);

            Content = root;
        }

        internal void UpdateMetrics(string value)
        {
            metrics.Text = value ?? string.Empty;
        }
    }
}
