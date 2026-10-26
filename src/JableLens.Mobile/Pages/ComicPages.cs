using JableLens.Core.Http;
using JableLens.Core.Library;
using JableLens.Core.Sites;
using JableLens.Core.Sources.MyComic;
using JableLens.Mobile.Controls;
using JableLens.Mobile.Services;
using Microsoft.Maui.Controls.Shapes;

namespace JableLens.Mobile.Pages;

/// <summary>漫画书库：排序 + 筛选（地区 / 类型 / 受众 / 状态），有阅读记录时最上面「继续阅读」。</summary>
public sealed class ComicBrowsePage : PageBase
{
    private static readonly (string Sort, string Name)[] Sorts = [("-update", "最近更新"), ("-views", "最高人气"), ("", "最新上架")];

    private readonly CardGrid _grid = new() { Kind = CardKind.Comic, EmptyText = "没有符合条件的漫画" };
    private readonly Dictionary<string, string> _filters = [];
    private readonly VerticalStackLayout _continue = new();
    private string _sort = Sorts[0].Sort;
    private bool _loaded;

    public ComicBrowsePage()
    {
        Title = "漫画";
        var header = new VerticalStackLayout();
        header.Add(Ui.Tabs(Sorts.Select(s => s.Name).ToList(), 0, i => { _sort = Sorts[i].Sort; Reload(); }));
        var filters = new HorizontalStackLayout { Spacing = 8, Padding = new Thickness(Ui.Gutter, 0, Ui.Gutter, 6) };
        foreach (var f in MyComicUrls.Filters)
        {
            var filter = f;
            var label = new Label { Text = f.Name + " ▾", FontSize = 13.5 };
            var chip = new Border { StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 16 }, Padding = new Thickness(14, 7), BackgroundColor = Color.FromArgb("#2A2A2C"), Content = label };
            Cards.Tap(chip, async () =>
            {
                var options = new[] { "全部" }.Concat(filter.Options.Select(o => o.Name)).ToArray();
                var pick = await Shell.Current.DisplayActionSheetAsync(filter.Name, "取消", null, options);
                var i = Array.IndexOf(options, pick);
                if (i < 0) return;
                _filters[filter.Key] = i == 0 ? "" : filter.Options[i - 1].Value;
                label.Text = (i == 0 ? filter.Name : $"{filter.Name}：{options[i]}") + " ▾";
                Reload();
            });
            filters.Add(chip);
        }
        header.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = filters });
        header.Add(_continue);
        _grid.Header = header;
        Content = _grid;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await MobileServices.InitAsync();
        RenderContinue();
        if (!_loaded) { _loaded = true; Reload(); }
    }

    private void Reload()
    {
        var sort = _sort;
        var filters = new Dictionary<string, string>(_filters);
        _grid.Loader = async (p, ct) =>
        {
            var r = await MobileServices.Sites.ComicListAsync(MyComicUrls.List(sort, filters, p), p, ct);
            return new PageResult<CardItem>(r.Items.Select(CardItem.From).ToList(), r.HasMore);
        };
        _grid.Reload();
    }

    /// <summary>「继续阅读」：最近读过的几本，点了直接接着上次那一话读。</summary>
    private void RenderContinue()
    {
        _continue.Clear();
        var recent = MobileServices.Library.Read(d => d.Comics.Take(10).ToList());
        if (recent.Count == 0) return;
        _continue.Add(Ui.H2("继续阅读"));
        var row = new HorizontalStackLayout { Spacing = 10, Padding = new Thickness(Ui.Gutter, 0) };
        foreach (var c in recent)
        {
            var item = c;
            var cover = new Border { WidthRequest = 46, HeightRequest = 62, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 6 }, BackgroundColor = Cards.C("Card"), Content = new RemoteImage { Url = c.CoverUrl } };
            var text = new VerticalStackLayout
            {
                WidthRequest = 130, VerticalOptions = LayoutOptions.Center, Spacing = 3,
                Children =
                {
                    new Label { Text = c.Title, FontSize = 13.5, FontAttributes = FontAttributes.Bold, MaxLines = 1, LineBreakMode = LineBreakMode.TailTruncation },
                    new Label { Text = "读到 " + c.ChapterTitle, FontSize = 12, TextColor = Cards.C("Fg3"), MaxLines = 1, LineBreakMode = LineBreakMode.TailTruncation },
                },
            };
            var tile = new Border
            {
                StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 12 }, BackgroundColor = Color.FromArgb("#1E1E20"), Padding = new Thickness(8),
                Content = new HorizontalStackLayout { Spacing = 10, Children = { cover, text } },
            };
            Cards.Tap(tile, () => _ = Nav.Push(new ComicReaderPage(item.ComicId, item.Title, item.CoverUrl, item.ChapterId)));
            row.Add(tile);
        }
        _continue.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = row });
        _continue.Add(Ui.H2("全部漫画"));
    }
}

/// <summary>漫画详情：封面 + 书名 / 作者 / 类型 / 简介，「开始阅读 / 继续阅读」，下面按单话 / 单行本 / 番外篇分组的章节。</summary>
public sealed class ComicDetailPage : PageBase
{
    private readonly int _id;
    private readonly string _title;
    private readonly string? _cover;
    private readonly VerticalStackLayout _info = new() { Spacing = 8 };
    private readonly HorizontalStackLayout _buttons = new() { Spacing = 10 };
    private readonly VerticalStackLayout _groupTabs = new();
    private readonly Grid _chapters = new() { Padding = new Thickness(Ui.Gutter - 4, 0), ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) } };
    private readonly Label _order = new() { FontSize = 13, TextColor = Cards.C("Fg2"), Padding = new Thickness(Ui.Gutter, 6) };
    private ComicDetail? _detail;
    private int _group;
    private bool _ascending;
    private bool _loaded;

    public ComicDetailPage(int id, string title, string? cover)
    {
        _id = id;
        _title = title;
        _cover = cover;
        Title = title;

        var coverBox = new Border
        {
            WidthRequest = 130, HeightRequest = 173, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 12 }, BackgroundColor = Cards.C("Card"),
            Content = new RemoteImage { Url = cover }, VerticalOptions = LayoutOptions.Start,
        };
        _info.Add(new Label { Text = title, FontSize = 21, FontAttributes = FontAttributes.Bold });
        _info.Add(Ui.Muted("正在读取…"));
        var top = new Grid { Padding = new Thickness(Ui.Gutter, 14), ColumnSpacing = 16, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        top.Add(coverBox);
        top.Add(_info, 1);
        Cards.Tap(_order, () => { _ascending = !_ascending; RenderChapters(); });

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(0, 0, 0, 40),
                Children = { top, new ContentView { Padding = new Thickness(Ui.Gutter, 0, Ui.Gutter, 6), Content = _buttons }, _groupTabs, _order, _chapters },
            },
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) { if (_detail is not null) { RenderButtons(); RenderChapters(); } return; }
        _loaded = true;
        await MobileServices.InitAsync();
        try
        {
            _detail = await MobileServices.Sites.ComicDetailAsync(_id, Life.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (SourceException ex)
        {
            ((Label)_info[1]).Text = "读取失败：" + ex.Message;
            return;
        }
        RenderInfo();
        RenderButtons();
        RenderGroups();
        RenderChapters();
    }

    private void RenderInfo()
    {
        var d = _detail!;
        _info.Clear();
        _info.Add(new Label { Text = d.Title, FontSize = 21, FontAttributes = FontAttributes.Bold });
        var meta = string.Join("  ·  ", new[] { d.Status, d.Summary?.Replace(" / ", " · "), d.Region, d.Updated is { } u ? u + " 更新" : null }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (meta.Length > 0) _info.Add(new Label { Text = meta, FontSize = 13, TextColor = d.Status == "连载中" ? Color.FromArgb("#46D369") : Cards.C("Fg2") });
        if (d.Author is not null) _info.Add(new Label { Text = "作者  " + d.Author, FontSize = 13, TextColor = Cards.C("Fg2") });
        if (d.Tags.Count > 0)
        {
            var tags = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            foreach (var t in d.Tags) tags.Add(Cards.Pill(t, Color.FromArgb("#2A2A2C"), 12));
            foreach (var c in tags.Children.OfType<View>()) c.Margin = new Thickness(0, 0, 6, 6);
            _info.Add(tags);
        }
        if (d.Intro is { } intro)
        {
            var text = new Label { Text = intro, FontSize = 13.5, TextColor = Color.FromArgb("#D8D8D8"), MaxLines = 4, LineBreakMode = LineBreakMode.TailTruncation };
            Cards.Tap(text, () => text.MaxLines = text.MaxLines == -1 ? 4 : -1);
            _info.Add(text);
        }
    }

    private ComicProgress? Progress => MobileServices.Library.Read(d => d.Comics.FirstOrDefault(c => c.ComicId == _id));

    private void RenderButtons()
    {
        _buttons.Clear();
        var chapters = _detail!.Groups.FirstOrDefault()?.Chapters;
        if (chapters is null || chapters.Count == 0) return;
        var progress = Progress;
        var first = chapters[^1];
        _buttons.Add(Ui.Action(progress is null ? "▶  开始阅读" : "▶  继续 " + progress.ChapterTitle, () => Open(progress?.ChapterId ?? first.Id)));
        _buttons.Add(Ui.Action("最新 " + chapters[0].Title, () => Open(chapters[0].Id), primary: false));
    }

    private void RenderGroups()
    {
        _groupTabs.Clear();
        if (_detail!.Groups.Count == 0) return;
        _groupTabs.Add(Ui.Tabs(_detail.Groups.Select(g => $"{g.Name}  {g.Chapters.Count}").ToList(), _group, i => { _group = i; RenderChapters(); }));
    }

    private void RenderChapters()
    {
        _chapters.Clear();
        _chapters.RowDefinitions.Clear();
        if (_detail is null || _detail.Groups.Count == 0)
        {
            _chapters.Add(Ui.Muted("没有章节"));
            return;
        }
        _order.Text = _ascending ? "正序 ⇅" : "倒序 ⇅";
        var current = Progress?.ChapterId;
        IEnumerable<ComicChapter> list = _detail.Groups[Math.Min(_group, _detail.Groups.Count - 1)].Chapters;
        if (_ascending) list = list.Reverse();
        var i = 0;
        foreach (var c in list)
        {
            if (i % 3 == 0) _chapters.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var ch = c;
            var cell = new Border
            {
                StrokeThickness = c.Id == current ? 1.5 : 0, Stroke = Cards.C("Accent"), StrokeShape = new RoundRectangle { CornerRadius = 10 },
                BackgroundColor = Color.FromArgb("#222224"), Margin = new Thickness(4), Padding = new Thickness(8, 10),
                Content = new Label { Text = c.Title, FontSize = 13, MaxLines = 1, LineBreakMode = LineBreakMode.TailTruncation, HorizontalTextAlignment = TextAlignment.Center, TextColor = c.Id == current ? Color.FromArgb("#FF4D55") : Colors.White },
            };
            Cards.Tap(cell, () => Open(ch.Id));
            _chapters.Add(cell, i % 3, i / 3);
            i++;
        }
    }

    private void Open(int chapterId) => _ = Nav.Push(new ComicReaderPage(_id, _detail?.Title ?? _title, _cover ?? _detail?.CoverUrl, chapterId));
}

/// <summary>漫画阅读：黑底竖向连续滚动（列表只解码屏幕附近的图），读到最后可以直接点下一话；读到哪一话自动记下来。</summary>
public sealed class ComicReaderPage : PageBase
{
    private readonly int _comicId;
    private readonly string _comicTitle;
    private readonly string? _cover;
    private int _chapterId;
    private readonly CollectionView _pages;
    private readonly Label _footer = new() { HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 30, 0, 80), TextColor = Cards.C("Fg3") };
    private IReadOnlyList<ComicChapter> _order = []; // 正序（第 1 话在前）
    private CancellationTokenSource? _chapterCts;
    private bool _loaded;

    public ComicReaderPage(int comicId, string comicTitle, string? cover, int chapterId)
    {
        _comicId = comicId;
        _comicTitle = comicTitle;
        _cover = cover;
        _chapterId = chapterId;
        Title = comicTitle;
        BackgroundColor = Color.FromArgb("#0B0B0B");

        var next = Ui.Action("下一话 ▶", () => Step(1));
        _pages = new CollectionView
        {
            ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems,
            ItemTemplate = new DataTemplate(() =>
            {
                var img = new RemoteImage { Aspect = Aspect.AspectFit };
                img.SetBinding(RemoteImage.UrlProperty, nameof(ComicPage.Url));
                var box = new AspectView { BackgroundColor = Color.FromArgb("#161616"), Content = img };
                box.BindingContextChanged += (_, _) => { if (box.BindingContext is ComicPage p) { box.Ratio = p.Ratio > 0 ? p.Ratio : 1.4; box.InvalidateMeasure(); } };
                return box;
            }),
            Footer = new VerticalStackLayout { Spacing = 12, Children = { _footer, next } },
        };
        var prev = new ToolbarItem { Text = "上一话" };
        prev.Clicked += (_, _) => Step(-1);
        var nextItem = new ToolbarItem { Text = "下一话" };
        nextItem.Clicked += (_, _) => Step(1);
        ToolbarItems.Add(prev);
        ToolbarItems.Add(nextItem);
        Content = _pages;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        await MobileServices.InitAsync();
        _ = LoadChapterAsync(_chapterId);
        try
        {
            var d = await MobileServices.Sites.ComicDetailAsync(_comicId, Life.Token);
            // 当前这一话所在的分组，倒过来就是从第 1 话开始的顺序
            _order = (d.Groups.FirstOrDefault(g => g.Chapters.Any(c => c.Id == _chapterId)) ?? d.Groups.FirstOrDefault())?.Chapters.Reverse().ToList() ?? [];
            UpdateTitle();
        }
        catch (Exception ex) when (ex is OperationCanceledException or SourceException) { }
    }

    private ComicChapter? Current => _order.FirstOrDefault(c => c.Id == _chapterId);

    private void Step(int delta)
    {
        var i = _order.ToList().FindIndex(c => c.Id == _chapterId);
        if (i < 0 || i + delta < 0 || i + delta >= _order.Count) { Nav.Toast(delta > 0 ? "已经是最后一话" : "已经是第一话"); return; }
        _ = LoadChapterAsync(_order[i + delta].Id);
    }

    private void UpdateTitle()
    {
        var i = _order.ToList().FindIndex(c => c.Id == _chapterId);
        Title = Current is { } c ? $"{c.Title}" : _comicTitle;
        _footer.Text = i >= 0 && i < _order.Count - 1 ? "这一话读完了" : "已经是最后一话";
        if (i >= 0 && i < _order.Count - 1) MobileServices.Sites.Prefetch(_order[i + 1].Url); // 先把下一话的网页读好
        if (Current is { } cur)
            MobileServices.Library.Mutate(d =>
            {
                d.Comics.RemoveAll(x => x.ComicId == _comicId);
                d.Comics.Insert(0, new ComicProgress(_comicId, _comicTitle, _cover, cur.Id, cur.Title, DateTimeOffset.Now));
                if (d.Comics.Count > 50) d.Comics.RemoveRange(50, d.Comics.Count - 50);
            });
    }

    private async Task LoadChapterAsync(int chapterId)
    {
        _chapterCts?.Cancel();
        var cts = _chapterCts = CancellationTokenSource.CreateLinkedTokenSource(Life.Token);
        _chapterId = chapterId;
        UpdateTitle();
        _pages.ItemsSource = null;
        _footer.Text = "正在读取…";
        try
        {
            var pages = await MobileServices.Sites.ComicPagesAsync(chapterId, cts.Token);
            if (cts.IsCancellationRequested) return;
            _pages.ItemsSource = pages;
            _pages.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
            UpdateTitle();
        }
        catch (OperationCanceledException) { }
        catch (SourceException ex)
        {
            _footer.Text = "读取失败：" + ex.Message;
        }
    }

    protected override void OnClosed()
    {
        base.OnClosed();
        _chapterCts?.Cancel();
    }
}
