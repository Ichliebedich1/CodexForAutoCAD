using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Codex.AutoCAD.Host.Selection;

namespace Codex.AutoCAD.Host.UI;

internal sealed class CodexPanelControl : UserControl
{
    private static readonly SolidColorBrush PanelBackground = Brush("#1E1E1E");
    private static readonly SolidColorBrush CardBackground = Brush("#252526");
    private static readonly SolidColorBrush CardBorder = Brush("#3A3A3A");
    private static readonly SolidColorBrush PrimaryText = Brush("#F2F2F2");
    private static readonly SolidColorBrush SecondaryText = Brush("#B8B8B8");
    private static readonly SolidColorBrush Accent = Brush("#3B82F6");

    private readonly TextBlock selectionStatus;
    private readonly TextBlock selectionDetails;

    public CodexPanelControl()
    {
        Background = PanelBackground;
        Foreground = PrimaryText;

        Grid root = new() { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        StackPanel header = new();
        header.Children.Add(new TextBlock
        {
            Text = "Codex for AutoCAD",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = PrimaryText,
        });
        header.Children.Add(new TextBlock
        {
            Text = "AutoCAD 2025 宿主已就绪",
            Margin = new Thickness(0, 5, 0, 14),
            FontSize = 12,
            Foreground = SecondaryText,
        });
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        Border selectionCard = CreateCard();
        StackPanel selectionContent = new();
        selectionContent.Children.Add(new TextBlock
        {
            Text = "当前选择",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = PrimaryText,
        });

        selectionStatus = new TextBlock
        {
            Margin = new Thickness(0, 9, 0, 4),
            TextWrapping = TextWrapping.Wrap,
            Foreground = PrimaryText,
        };
        selectionContent.Children.Add(selectionStatus);

        selectionDetails = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = SecondaryText,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
        };
        selectionContent.Children.Add(selectionDetails);

        Button refreshButton = CreateButton("刷新选择摘要");
        refreshButton.Margin = new Thickness(0, 12, 0, 0);
        refreshButton.Click += (_, _) => RefreshSelectionSummary();
        selectionContent.Children.Add(refreshButton);

        selectionCard.Child = selectionContent;
        Grid.SetRow(selectionCard, 1);
        root.Children.Add(selectionCard);

        Border conversationPlaceholder = CreateCard();
        conversationPlaceholder.Margin = new Thickness(0, 12, 0, 12);
        conversationPlaceholder.Child = new TextBlock
        {
            Text = "对话、计划、工具时间线与审批卡将在后续 AgentHost 接入阶段显示在这里。",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Foreground = SecondaryText,
            Margin = new Thickness(18),
        };
        Grid.SetRow(conversationPlaceholder, 2);
        root.Children.Add(conversationPlaceholder);

        Border safetyNote = new()
        {
            Background = Brush("#17233A"),
            BorderBrush = Accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Child = new TextBlock
            {
                Text = "安全演示：输入 CODEXCADLINE。只有在命令行确认后才会写入一条直线；不会自动保存。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = PrimaryText,
                FontSize = 12,
            },
        };
        Grid.SetRow(safetyNote, 3);
        root.Children.Add(safetyNote);

        Content = root;
        RefreshSelectionSummary();
    }

    public void RefreshSelectionSummary()
    {
        try
        {
            SelectionSummary summary = SelectionSummaryService.ReadCurrentSelection();
            selectionStatus.Text = summary.Message;
            selectionDetails.Text = summary.EntityTypes.Count == 0
                ? "数量: 0"
                : string.Join(
                    Environment.NewLine,
                    summary.EntityTypes
                        .OrderBy(static item => item.Key, StringComparer.Ordinal)
                        .Select(static item => $"{item.Key}: {item.Value}"));
        }
        catch (System.Exception exception)
        {
            selectionStatus.Text = "读取当前选择失败。";
            selectionDetails.Text = exception.Message;
        }
    }

    private static Border CreateCard()
    {
        return new Border
        {
            Background = CardBackground,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14),
        };
    }

    private static Button CreateButton(string text)
    {
        return new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 6, 12, 6),
            Background = Accent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
    }

    private static SolidColorBrush Brush(string hex)
    {
        SolidColorBrush brush = new((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
