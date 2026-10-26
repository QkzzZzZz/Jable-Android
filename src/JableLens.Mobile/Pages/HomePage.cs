using JableLens.Core.Http;
using JableLens.Core.Models;
using JableLens.Core.Sources.Jable;
using JableLens.Core.Sources.MissAv;
using JableLens.Mobile.Controls;
using JableLens.Mobile.Services;
using Microsoft.Maui.Controls.Shapes;

namespace JableLens.Mobile.Pages;

/// <summary>
/// 首页（和 Windows 版一样的 Netflix 式）：顶部大图（本周热门，几秒换一张），下面一行行横向滑动的分类：
/// 继续观看、猜我喜欢、最近更新、本周热门 TOP 10、关注演员的新作、新片上市、固定的标签、MissAV……
/// </summary>
public sealed class HomePage : PageBase
{
    private readonly VerticalStackLayout _rows = new() { Padding = new Thickness(0, 0, 0, 30) };
    private readonly Grid _hero = new() { HeightRequest = 440 };
    private readonly RemoteImage _heroImage = new();
    private readonly Label _heroCode = new() { FontSize = 40, FontAttributes = FontAttributes.Bold, FontFamily = "sans-serif-condensed" };
    private readonly Label _heroTitle = new() { FontSize = 14, TextColor = Color.FromArgb("#E0E0E0"), MaxLines = 2, LineBreakMode = LineBreakMode.TailTruncation };
    private readonly Label _heroRank = new() { FontSize = 14, FontAttributes = FontAttributes.Bold };
    private readonly VerticalStackLayout _heroText = new() { Spacing = 6, VerticalOptions = LayoutOptions.End, Padding = new Thickness(Ui.Gutter, 0, Ui.Gutter, 22) };
    private readonly List<MediaRow> _rowList = [];
    private IReadOnlyList<VideoCard> _heroItems = [];
    private int _heroIndex;
    private bool _built;
    private IDispatcherTimer? _heroTimer;

    public HomePage()
    {
        Title = "首页";
        Shell.SetNavBarIsVisible(this, false);
        BuildHero();
        var root = new VerticalStackLayout { _hero, _rows };
        var scroll = new ScrollView { Content = root };
        var refresh = new RefreshView { Content = scroll };
        refresh.Refreshing += (_, _) => { Rebuild(); refresh.IsRefreshing = false; };

        // 顶上浮着一条：JABLE 标志 + 搜索
        var bar = new Grid
        {
            Padding = new Thickness(Ui.Gutter, 10, Ui.Gutter, 10), VerticalOptions = LayoutOptions.Start,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Background = new LinearGradientBrush([new GradientStop(Color.FromArgb("#B3000000"), 0), new GradientStop(Colors.Transparent, 1)], new Point(0, 0), new Point(0, 1)),
        };
        bar.Add(new Label { Text = "JABLE", FontSize = 26, FontAttributes = FontAttributes.Bold, TextColor = Cards.C("Accent"), FontFamily = "sans-serif-condensed" });
        var search = new Image { Source = "ic_search.png", WidthRequest = 26, HeightRequest = 26, VerticalOptions = LayoutOptions.Center };
        Cards.Tap(search, () => _ = Nav.Push(new SearchPage()));
        bar.Add(search, 1);

        Content = new Grid { refresh, bar };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await MobileServices.InitAsync();
        if (!_built) { _built = true; Rebuild(); }
        else RefreshLocalRows();
        _heroTimer?.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _heroTimer?.Stop();
    }

    private void BuildHero()
    {
        _hero.Add(_heroImage);
        _hero.Add(new BoxView
        {
            Background = new LinearGradientBrush([
                new GradientStop(Color.FromArgb("#66000000"), 0), new GradientStop(Colors.Transparent, 0.3f),
                new GradientStop(Color.FromArgb("#99141414"), 0.7f), new GradientStop(Cards.C("Bg"), 1)], new Point(0, 0), new Point(0, 1)),
        });
        var rank = new HorizontalStackLayout { Spacing = 8 };
        rank.Add(new Border
        {
            BackgroundColor = Cards.C("Accent"), StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 4 }, Padding = new Thickness(5, 1),
            Content = new Label { Text = "TOP\n10", FontSize = 9, FontAttributes = FontAttributes.Bold, LineHeight = 0.9, HorizontalTextAlignment = TextAlignment.Center },
        });
        rank.Add(_heroRank);
        _heroText.Add(rank);
        _heroText.Add(_heroCode);
        _heroText.Add(_heroTitle);
        var buttons = new HorizontalStackLayout { Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Add(Ui.Action("▶  播放", () => { if (HeroCard is { } c) Nav.Play(c); }));
        buttons.Add(Ui.Action("ⓘ  详情", () => { if (HeroCard is { } c) _ = Nav.Push(new VideoDetailPage(c)); }, primary: false));
        _heroText.Add(buttons);
        _heroText.Opacity = 0;
        _hero.Add(_heroText);

        // 左右滑动换一张
        var swipeLeft = new SwipeGestureRecognizer { Direction = SwipeDirection.Left };
        swipeLeft.Swiped += (_, _) => ShowHero(_heroIndex + 1);
        var swipeRight = new SwipeGestureRecognizer { Direction = SwipeDirection.Right };
        swipeRight.Swiped += (_, _) => ShowHero(_heroIndex - 1);
        _hero.GestureRecognizers.Add(swipeLeft);
        _hero.GestureRecognizers.Add(swipeRight);
    }

    private VideoCard? HeroCard => _heroItems.Count == 0 ? null : _heroItems[_heroIndex];

    private async void ShowHero(int index)
    {
        if (_heroItems.Count == 0) return;
        _heroIndex = (index % _heroItems.Count + _heroItems.Count) % _heroItems.Count;
        var c = _heroItems[_heroIndex];
        await Task.WhenAll(Motion.Fade(_heroText, 0, 150), Motion.Fade(_heroImage, 0.3, 150));
        _heroImage.Url = Text.HiRes(c) ?? c.CoverUrl;
        _heroCode.Text = c.Code;
        _heroTitle.Text = Text.TitleOnly(c);
        _heroRank.Text = $"本周热门第 {_heroIndex + 1} 名";
        _heroText.Opacity = 1;
        _ = Motion.Fade(_heroImage, 1, 500);
        await Motion.Enter(_heroText);
    }

    private void Rebuild()
    {
        _life.Cancel();
        var ct = (_life = new CancellationTokenSource()).Token;
        _signature = Signature();
        _rows.Clear();
        _rowList.Clear();
        var lib = MobileServices.Library;

        if (lib.RecentHistory(true).Count > 0)
            AddRow(new MediaRow("继续观看",
                _ => Task.FromResult<IReadOnlyList<CardItem>>(lib.RecentHistory(true).Take(20).Select(h => CardItem.From(h.Card)).ToList()),
                () => _ = Nav.Push(new HistoryPage())));
        if (MobileServices.Recommend.HasSeeds)
            AddRow(new MediaRow("猜我喜欢",
                async c => (await MobileServices.Recommend.GuessAsync(30, c)).Select(v => CardItem.From(v)).ToList(),
                () => _ = Nav.Push(new GuessPage())));

        AddRow(JableRow("最近更新", JableUrls.Latest));
        AddRow(new MediaRow("本周热门 TOP 10",
            async c => (await MobileServices.Sites.JableListAsync(JableUrls.Hot(), 1, c)).Items.Take(10)
                .Select((v, i) => new CardItem
                {
                    Title = Text.TitleOnly(v), Meta = v.Duration is { Length: > 0 } d ? $"{v.Code}  ·  {d}" : v.Code,
                    CoverUrl = Text.HiRes(v) ?? v.CoverUrl, Payload = v, Rank = i + 1,
                }).ToList(),
            null));

        var newWorks = lib.Read(d => d.Follows.SelectMany(f => f.NewWorks.Select(w => CardItem.From(w, "新作"))).Take(20).ToList());
        if (newWorks.Count > 0)
            AddRow(new MediaRow("关注演员的新作", _ => Task.FromResult<IReadOnlyList<CardItem>>(newWorks), () => _ = Nav.Push(new FollowsPage())));

        AddRow(JableRow("新片上市", JableUrls.NewRelease));
        foreach (var tag in lib.Read(d => d.PinnedTags.ToList()))
            if (tag.PageUrl is { } url)
                AddRow(new MediaRow(tag.Name,
                    async c => (await MobileServices.Sites.JableListAsync(url, 1, c)).Items.Select(v => CardItem.From(v)).ToList(),
                    () => Nav.OpenTag(tag)));
        AddRow(JableRow("本周热门", JableUrls.Hot));
        AddRow(MissAvRow("MissAV · 最近更新", "new"));
        AddRow(MissAvRow("MissAV · 中文字幕", "chinese-subtitle"));
        AddRow(MissAvRow("麻豆传媒", "madou"));

        _ = LoadHeroAsync(ct);
        foreach (var row in _rowList) _ = row.LoadAsync(ct);
    }

    private CancellationTokenSource _life = new();

    protected override void OnClosed()
    {
        base.OnClosed();
        _life.Cancel();
    }

    private string _signature = "";

    /// <summary>观看记录、片单、固定标签、新作有变化时首页要重排。</summary>
    private static string Signature() => MobileServices.Library.Read(d =>
        $"{d.History.Count}|{(d.History.Count == 0 ? 0 : d.History.Values.Max(h => h.UpdatedAt.Ticks))}|{d.Favorites.Count}|{d.PinnedTags.Count}|{d.Follows.Sum(f => f.NewWorks.Count)}");

    /// <summary>回到首页时：继续观看 / 片单变了，重排一次（其它行有缓存，很快）。</summary>
    private void RefreshLocalRows()
    {
        if (Signature() != _signature) Rebuild();
    }

    private void AddRow(MediaRow row)
    {
        _rowList.Add(row);
        _rows.Add(row);
    }

    private static MediaRow JableRow(string title, Func<int, string> url) =>
        new(title, async ct => (await MobileServices.Sites.JableListAsync(url(1), 1, ct)).Items.Select(v => CardItem.From(v)).ToList(),
            () => _ = Nav.Push(new GridPage(title, [GridPage.JableTab(title, url)])));

    private static MediaRow MissAvRow(string title, string path) =>
        new(title, async ct => (await MobileServices.Sites.MissAvListAsync(MissAvUrls.List(path, 1), 1, ct)).Items.Select(v => CardItem.From(v)).ToList(),
            () => _ = Nav.Push(new GridPage(title, [GridPage.MissAvTab(title, p => MissAvUrls.List(path, p))])));

    private async Task LoadHeroAsync(CancellationToken ct)
    {
        try
        {
            var hot = await MobileServices.Sites.JableListAsync(JableUrls.Hot(), 1, ct);
            _heroItems = hot.Items.Take(6).ToList();
            ShowHero(0);
            _heroTimer ??= Dispatcher.CreateTimer();
            _heroTimer.Interval = TimeSpan.FromSeconds(8);
            _heroTimer.Tick -= OnHeroTick;
            _heroTimer.Tick += OnHeroTick;
            _heroTimer.Start();
        }
        catch (OperationCanceledException) { }
        catch (SourceException)
        {
            _hero.HeightRequest = 80;
        }
    }

    private void OnHeroTick(object? sender, EventArgs e) => ShowHero(_heroIndex + 1);
}
