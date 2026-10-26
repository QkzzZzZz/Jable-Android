using Microsoft.Maui.Controls.Shapes;

namespace JableLens.Mobile.Controls;

/// <summary>按宽度定高的容器（封面 16:9、漫画 3:4……）：高 = 宽 × Ratio。</summary>
public class AspectView : ContentView
{
    public double Ratio { get; set; } = 0.5625;

    protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
    {
        var w = WidthRequest > 0 ? WidthRequest : double.IsInfinity(widthConstraint) ? 160 : widthConstraint;
        var size = new Size(w, Math.Round(w * Ratio));
        Content?.Measure(size.Width, size.Height);
        DesiredSize = size;
        return size;
    }
}

public enum CardKind { Video, Comic, Live, Performer, Category, Term }

/// <summary>
/// 卡片模板（代码写的 DataTemplate）。和 Windows 版同样的样子：小圆角封面 + 两行标题 + 灰色「番号 · 时长」。
/// 点卡片有一下弹簧回弹；卡片第一次出现时从下方浮上来。
/// <para>为了滑动流畅：</para>
/// <list type="bullet">
/// <item>卡片内容不用数据绑定（绑定走反射），换数据时直接一次性赋值；</item>
/// <item>每种卡片高度固定（文字行高按系统字体实测），骨架卡片和真卡片一样大，列表只量第一张，不用每张都量；</item>
/// <item>圆角用系统轮廓裁切（显卡完成），封面用 <see cref="CoverImage"/>。</item>
/// </list>
/// </summary>
public static class Cards
{
    public static Color C(string key) => (Color)Application.Current!.Resources[key];

    public static double RatioOf(CardKind kind) => kind switch
    {
        CardKind.Comic => 4 / 3.0,
        CardKind.Live => 0.75,
        _ => 0.5625,
    };

    // 卡片四周留白（和 Build 里的 Padding 一致）
    private const double SideInset = 5;
    private static double BottomInset(CardKind kind) => kind == CardKind.Performer ? 6 : 16;

    private static readonly Dictionary<(double, int, bool), double> Heights = [];

    /// <summary>几行文字实际占多高（用系统 TextView 按当前字体和字号缩放实测）。</summary>
    public static double TextHeight(double size, int lines, bool bold = false)
    {
        if (Heights.TryGetValue((size, lines, bold), out var h)) return h;
        var context = Android.App.Application.Context;
        using var probe = new Android.Widget.TextView(context);
        probe.SetTextSize(Android.Util.ComplexUnitType.Sp, (float)size);
        if (bold) probe.SetTypeface(Android.Graphics.Typeface.Default, Android.Graphics.TypefaceStyle.Bold);
        probe.SetLines(lines);
        probe.Text = "国Ag";
        var any = Android.Views.View.MeasureSpec.MakeMeasureSpec(0, Android.Views.MeasureSpecMode.Unspecified);
        probe.Measure(any, any);
        h = Math.Ceiling(probe.MeasuredHeight / context.Resources!.DisplayMetrics!.Density) + 1;
        Heights[(size, lines, bold)] = h;
        return h;
    }

    private static double TitleHeight => TextHeight(13, 2);
    private static double MetaHeight => TextHeight(11.5, 1);

    /// <summary>某种卡片在给定宽度下的总高度（横排的行高用）。</summary>
    public static double HeightOf(CardKind kind, double width)
    {
        var inner = width - SideInset * 2;
        return kind switch
        {
            CardKind.Performer => 84 + 8 + TextHeight(13, 1, true) + TextHeight(11, 1) + BottomInset(kind),
            CardKind.Category => Math.Round(inner * 0.5625) + BottomInset(kind),
            CardKind.Term => TermHeight + BottomInset(kind),
            _ => Math.Round(inner * RatioOf(kind)) + 8 + TitleHeight + 3 + MetaHeight + BottomInset(kind),
        };
    }

    private static double TermHeight => 24 + TextHeight(14, 1, true) + 3 + TextHeight(11.5, 1);

    public static DataTemplate Template(CardKind kind, Action<CardItem> open) =>
        new DataTemplateSelectorLite(kind, open);

    /// <summary>骨架卡片 / 真卡片二选一（加载中先显示骨架）。</summary>
    private sealed class DataTemplateSelectorLite(CardKind kind, Action<CardItem> open) : DataTemplateSelector
    {
        private readonly DataTemplate _real = new(() => Build(kind, open));
        private readonly DataTemplate _skeleton = new(() => Skeleton(kind));

        protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
            item is CardItem { IsSkeleton: true } ? _skeleton : _real;
    }

    public static View Build(CardKind kind, Action<CardItem> open)
    {
        var (root, apply) = kind switch
        {
            CardKind.Performer => PerformerCard(),
            CardKind.Term => TermCard(),
            CardKind.Category => CategoryCard(),
            _ => CoverCard(kind),
        };
        root.Padding = new Thickness(SideInset, 0, SideInset, BottomInset(kind));
        Tap(root, () => { if (root.BindingContext is CardItem item) open(item); });
        root.BindingContextChanged += (_, _) =>
        {
            if (root.BindingContext is not CardItem item) return;
            apply(item);
            if (item.TakeAppear()) Motion.Appear(root);
        };
        return root;
    }

    /// <summary>点一下：轻压缩小再弹回，然后执行。</summary>
    public static void Tap(View view, Action action)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            await Motion.Press(view);
            action();
        };
        view.GestureRecognizers.Add(tap);
    }

    private static (ContentView, Action<CardItem>) CoverCard(CardKind kind)
    {
        var img = new CoverImage();
        var layers = Round.Corners(new Grid { BackgroundColor = C("Card"), Children = { img } }, 8);

        // 排行（TOP 10）：左上角红色小标
        var rankText = Label(11.5, bold: true);
        var rank = new Border
        {
            BackgroundColor = C("Accent"), StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8, 0, 0, 8) },
            Padding = new Thickness(7, 2), HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start,
            Content = rankText, IsVisible = false,
        };
        layers.Add(rank);

        if (kind == CardKind.Live)
        {
            var live = Pill("直播", C("Accent"));
            live.Margin = new Thickness(6);
            live.HorizontalOptions = LayoutOptions.Start;
            live.VerticalOptions = LayoutOptions.Start;
            layers.Add(live);
        }

        // 「新作 / 新人」：底部居中的红色小条
        var badgeText = Label(11, bold: true);
        var badge = new Border
        {
            BackgroundColor = C("Accent"), StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(3, 3, 0, 0) },
            Padding = new Thickness(8, 1), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.End,
            Content = badgeText, IsVisible = false,
        };
        layers.Add(badge);

        // 观看进度：灰底红条
        var track = new Grid { HeightRequest = 3, VerticalOptions = LayoutOptions.End, Margin = new Thickness(10, 0, 10, 8), BackgroundColor = Color.FromArgb("#80808080"), IsVisible = false };
        var fill = new BoxView { Color = C("Accent"), HorizontalOptions = LayoutOptions.Start, AnchorX = 0 };
        track.Add(fill);
        var progress = 0.0;
        track.SizeChanged += (_, _) => fill.WidthRequest = track.Width * progress;
        layers.Add(track);

        var cover = new AspectView { Ratio = RatioOf(kind), Content = layers };
        var title = Label(13, maxLines: 2);
        title.TextColor = Color.FromArgb("#E5E5E5");
        title.Margin = new Thickness(1, 8, 1, 0);
        title.HeightRequest = TitleHeight;
        var meta = Label(11.5, maxLines: 1);
        meta.TextColor = C("Fg3");
        meta.Margin = new Thickness(1, 3, 1, 0);
        meta.HeightRequest = MetaHeight;

        var root = new ContentView { Content = new VerticalStackLayout { cover, title, meta } };
        return (root, item =>
        {
            img.Url = item.CoverUrl;
            rank.IsVisible = item.HasRank;
            if (item.HasRank) rankText.Text = item.RankText;
            badge.IsVisible = item.HasBadge;
            if (item.HasBadge) badgeText.Text = item.Badge;
            progress = item.Progress;
            track.IsVisible = item.HasProgress;
            if (item.HasProgress && track.Width > 0) fill.WidthRequest = track.Width * progress;
            title.Text = item.Title;
            meta.Text = item.Meta;
        });
    }

    private static (ContentView, Action<CardItem>) PerformerCard()
    {
        var img = new CoverImage();
        var initial = Label(28);
        initial.TextColor = C("Fg3");
        initial.HorizontalOptions = initial.VerticalOptions = LayoutOptions.Center;
        var avatar = Round.Oval(new Grid
        {
            WidthRequest = 84, HeightRequest = 84, BackgroundColor = C("Card"), HorizontalOptions = LayoutOptions.Center, Children = { initial, img },
        });
        var name = Label(13, maxLines: 1, bold: true);
        name.HorizontalTextAlignment = TextAlignment.Center;
        name.Margin = new Thickness(0, 8, 0, 0);
        name.HeightRequest = TextHeight(13, 1, true);
        var sub = Label(11, maxLines: 1);
        sub.TextColor = C("Fg3");
        sub.HorizontalTextAlignment = TextAlignment.Center;
        sub.HeightRequest = TextHeight(11, 1);
        var root = new ContentView { Content = new VerticalStackLayout { avatar, name, sub } };
        return (root, item =>
        {
            img.Url = item.IconUrl;
            initial.Text = item.Initial;
            name.Text = item.Title;
            sub.Text = item.Subtitle;
        });
    }

    private static (ContentView, Action<CardItem>) CategoryCard()
    {
        var img = new CoverImage();
        var shade = new BoxView { Background = new LinearGradientBrush([new GradientStop(Colors.Transparent, 0.3f), new GradientStop(Color.FromArgb("#CC000000"), 1)], new Point(0, 0), new Point(0, 1)) };
        var name = Label(15, maxLines: 1, bold: true);
        var sub = Label(11.5, maxLines: 1);
        sub.TextColor = C("Fg2");
        var text = new VerticalStackLayout { name, sub };
        text.VerticalOptions = LayoutOptions.End;
        text.Margin = new Thickness(10, 0, 10, 8);
        var layers = Round.Corners(new Grid { BackgroundColor = C("Card"), Children = { img, shade, text } }, 10);
        var root = new ContentView { Content = new AspectView { Ratio = 0.5625, Content = layers } };
        return (root, item =>
        {
            img.Url = item.CoverUrl;
            name.Text = item.Title;
            sub.Text = item.Subtitle;
        });
    }

    private static (ContentView, Action<CardItem>) TermCard()
    {
        var name = Label(14, maxLines: 1, bold: true);
        name.HeightRequest = TextHeight(14, 1, true);
        var sub = Label(11.5, maxLines: 1);
        sub.TextColor = C("Fg3");
        sub.HeightRequest = TextHeight(11.5, 1);
        var root = new ContentView
        {
            Content = Round.Corners(new Grid
            {
                BackgroundColor = C("Soft"), Padding = new Thickness(14, 12),
                Children = { new VerticalStackLayout { Spacing = 3, Children = { name, sub } } },
            }, 10),
        };
        return (root, item =>
        {
            name.Text = item.Title;
            sub.Text = item.Subtitle;
        });
    }

    private static Label Label(double size, int maxLines = 0, bool bold = false)
    {
        var l = new Label { FontSize = size, TextColor = Colors.White, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None };
        if (maxLines > 0)
        {
            l.MaxLines = maxLines;
            l.LineBreakMode = LineBreakMode.TailTruncation;
        }
        return l;
    }

    public static Border Pill(string text, Color background, double size = 11) => new()
    {
        BackgroundColor = background, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 10 }, Padding = new Thickness(8, 2),
        Content = new Label { Text = text, FontSize = size, TextColor = Colors.White, FontAttributes = FontAttributes.Bold },
    };

    /// <summary>骨架卡片：灰色占位 + 柔和的明暗呼吸（加载中）。大小和真卡片完全一样。</summary>
    public static View Skeleton(CardKind kind)
    {
        View body = kind switch
        {
            CardKind.Performer => new VerticalStackLayout
            {
                Children =
                {
                    new BoxView { WidthRequest = 84, HeightRequest = 84, CornerRadius = 42, Color = Color.FromArgb("#222224"), HorizontalOptions = LayoutOptions.Center },
                    Slot(TextHeight(13, 1, true), new Thickness(0, 8, 0, 0), Bar(70, 10, center: true)),
                    Slot(TextHeight(11, 1), default, null),
                },
            },
            CardKind.Category => new AspectView { Ratio = 0.5625, Content = Block(10) },
            CardKind.Term => new BoxView { HeightRequest = TermHeight, CornerRadius = 10, Color = Color.FromArgb("#1F1F21") },
            _ => new VerticalStackLayout
            {
                Children =
                {
                    new AspectView { Ratio = RatioOf(kind), Content = Block(8) },
                    Slot(TitleHeight, new Thickness(1, 8, 1, 0), Bar(-1, 11)),
                    Slot(MetaHeight, new Thickness(1, 3, 1, 0), Bar(60, 9)),
                },
            },
        };
        var root = new ContentView { Padding = new Thickness(SideInset, 0, SideInset, BottomInset(kind)), Content = body };
        Motion.Breathe(root);
        return root;
    }

    private static BoxView Block(double radius) => new() { CornerRadius = radius, Color = Color.FromArgb("#1F1F21") };

    /// <summary>和真卡片里一行文字同样高的格子，里面放一根灰条。</summary>
    private static View Slot(double height, Thickness margin, View? bar) => new ContentView
    {
        HeightRequest = height, Margin = margin, Content = bar,
    };

    private static View Bar(double width, double height, bool center = false) => new BoxView
    {
        HeightRequest = height, WidthRequest = width, CornerRadius = height / 2, Color = Color.FromArgb("#242426"),
        HorizontalOptions = width > 0 ? center ? LayoutOptions.Center : LayoutOptions.Start : LayoutOptions.Fill,
        VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 2, 0, 0),
    };
}
