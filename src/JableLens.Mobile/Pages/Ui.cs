using JableLens.Mobile.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace JableLens.Mobile.Pages;

/// <summary>页面基类：深色背景；离开导航栈时取消正在进行的读取。</summary>
public abstract class PageBase : ContentPage
{
    protected CancellationTokenSource Life { get; } = new();

    protected PageBase()
    {
        BackgroundColor = Cards.C("Bg");
        Shell.SetNavBarIsVisible(this, true);
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        // 被弹出导航栈（不是被新页面盖住）才算关闭
        if (!Navigation.NavigationStack.Contains(this) && !Shell.Current.Navigation.NavigationStack.Contains(this)) OnClosed();
    }

    protected virtual void OnClosed() => Life.Cancel();
}

/// <summary>常用小部件（和 Windows 版 Ui 对应）。</summary>
public static class Ui
{
    public const double Gutter = 16;

    public static Label H1(string text) => new() { Text = text, FontSize = 26, FontAttributes = FontAttributes.Bold, Margin = new Thickness(Gutter, 12, Gutter, 4) };

    public static Label H2(string text) => new() { Text = text, FontSize = 18, FontAttributes = FontAttributes.Bold, Margin = new Thickness(Gutter, 22, Gutter, 10) };

    public static Label Muted(string text, double size = 13.5) => new() { Text = text, FontSize = size, TextColor = Cards.C("Fg3"), LineBreakMode = LineBreakMode.WordWrap };

    /// <summary>胶囊按钮（标签、演员、筛选项）。selected 时白底黑字。</summary>
    public static Border Chip(string text, Action click, bool selected = false)
    {
        var label = new Label { Text = text, FontSize = 13.5, TextColor = selected ? Colors.Black : Colors.White, VerticalOptions = LayoutOptions.Center };
        var chip = new Border
        {
            StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 16 }, Padding = new Thickness(14, 7),
            BackgroundColor = selected ? Colors.White : Color.FromArgb("#2A2A2C"), Content = label, Margin = new Thickness(0, 0, 8, 8),
        };
        Cards.Tap(chip, click);
        return chip;
    }

    public static FlexLayout ChipWrap() => new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Margin = new Thickness(Gutter, 0, Gutter, 0) };

    /// <summary>主按钮：白底黑字（播放），或半透明玻璃（次要）。</summary>
    public static Border Action(string text, Action click, bool primary = true)
    {
        var label = new Label
        {
            Text = text, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = primary ? Colors.Black : Colors.White,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
        };
        var b = new Border
        {
            StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 22 }, HeightRequest = 44, Padding = new Thickness(22, 0),
            BackgroundColor = primary ? Colors.White : Color.FromArgb("#40FFFFFF"), Content = label,
        };
        Cards.Tap(b, click);
        return b;
    }

    /// <summary>横向滑动的一排胶囊（标签页切换）。</summary>
    public static ScrollView Tabs(IReadOnlyList<string> names, int selected, Action<int> onSelect)
    {
        var row = new HorizontalStackLayout { Padding = new Thickness(Gutter, 4, Gutter, 6) };
        void Render()
        {
            row.Clear();
            for (var i = 0; i < names.Count; i++)
            {
                var idx = i;
                row.Add(Chip(names[i], () => { selected = idx; Render(); onSelect(idx); }, i == selected));
            }
        }
        Render();
        return new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = row };
    }

    /// <summary>一行菜单项（「我的」页）：左边文字，右边灰色说明和箭头。</summary>
    public static View MenuRow(string text, string? detail, Action click)
    {
        var g = new Grid
        {
            Padding = new Thickness(Gutter, 15), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            BackgroundColor = Colors.Transparent,
        };
        g.Add(new Label { Text = text, FontSize = 16, VerticalOptions = LayoutOptions.Center });
        g.Add(new Label { Text = (detail is null ? "" : detail + "  ") + "›", FontSize = 14, TextColor = Cards.C("Fg3"), VerticalOptions = LayoutOptions.Center }, 1);
        Cards.Tap(g, click);
        return g;
    }

    public static BoxView Divider() => new() { HeightRequest = 1, Color = Color.FromArgb("#262626"), Margin = new Thickness(Gutter, 0) };

    /// <summary>统计小卡：大数字 + 说明。</summary>
    public static Border Tile(string value, string label) => new()
    {
        StrokeThickness = 1, Stroke = Color.FromArgb("#1AFFFFFF"), StrokeShape = new RoundRectangle { CornerRadius = 14 }, BackgroundColor = Color.FromArgb("#14FFFFFF"),
        Padding = new Thickness(16, 12), Margin = new Thickness(0, 0, 10, 10), MinimumWidthRequest = 140,
        Content = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = value, FontSize = 22, FontAttributes = FontAttributes.Bold },
                new Label { Text = label, FontSize = 12, TextColor = Cards.C("Fg3") },
            },
        },
    };
}
