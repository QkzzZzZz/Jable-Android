using JableLens.Core.Http;
using JableLens.Core.Models;
using JableLens.Core.Sites;
using JableLens.Core.Sources.Av01;
using JableLens.Mobile.Controls;
using JableLens.Mobile.Services;
using Microsoft.Maui.Controls.Shapes;

namespace JableLens.Mobile.Pages;

/// <summary>
/// 作品详情（和 Windows 版同样的内容）：大图 + 番号 + 播放 / 片单 / 下载；日期时长、标题、简介；
/// 资料卡（演员 / 类型 / 标签 / 厂牌……）；剧照；更多类似内容。先显示卡片上已有的信息，再并行补全。
/// </summary>
public sealed class VideoDetailPage : PageBase
{
    private readonly VideoCard? _card;
    private readonly string _code;
    private readonly CodedWork? _work;
    private VideoDetail? _detail;
    private Av01Video? _av01;
    private readonly Dictionary<string, string> _facts = [];

    private readonly RemoteImage _cover = new();
    private readonly Label _logo = new() { FontSize = 36, FontAttributes = FontAttributes.Bold, FontFamily = "sans-serif-condensed" };
    private readonly FlexLayout _meta = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
    private readonly Label _title = new() { FontSize = 17, FontAttributes = FontAttributes.Bold, LineHeight = 1.25 };
    private readonly HorizontalStackLayout _buttons = new() { Spacing = 10 };
    private readonly VerticalStackLayout _progress = new() { Spacing = 6, IsVisible = false };
    private readonly Label _description = new() { FontSize = 14, TextColor = Color.FromArgb("#D6D6D6"), LineHeight = 1.35, MaxLines = 4, LineBreakMode = LineBreakMode.TailTruncation, IsVisible = false };
    private readonly Label _expand = new() { Text = "展开简介", FontSize = 13, TextColor = Cards.C("Fg2"), IsVisible = false };
    private readonly Label _status = new() { FontSize = 14, TextColor = Cards.C("Fg2"), IsVisible = false };
    private readonly VerticalStackLayout _factsBox = new() { Spacing = 10 };
    private readonly Border _factsCard;
    private readonly VerticalStackLayout _gallery = new();
    private readonly VerticalStackLayout _more = new();
    private readonly VerticalStackLayout _body;

    public VideoDetailPage(VideoCard card) : this(card.Code, card, null) { }

    private VideoDetailPage(string code, VideoCard? card, CodedWork? work)
    {
        _code = code;
        _card = card;
        _work = work;
        Title = code;

        var hero = new Grid { _cover };
        hero.Add(new BoxView { Background = new LinearGradientBrush([new GradientStop(Colors.Transparent, 0.45f), new GradientStop(Cards.C("Bg"), 1)], new Point(0, 0), new Point(0, 1)) });
        _logo.VerticalOptions = LayoutOptions.End;
        _logo.Margin = new Thickness(Ui.Gutter, 0, Ui.Gutter, 6);
        hero.Add(_logo);

        _factsCard = new Border
        {
            StrokeThickness = 1, Stroke = Color.FromArgb("#14FFFFFF"), BackgroundColor = Color.FromArgb("#0FFFFFFF"), StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(16, 14), Content = _factsBox, IsVisible = false,
        };
        Cards.Tap(_expand, () =>
        {
            var open = _description.MaxLines == -1;
            _description.MaxLines = open ? 4 : -1;
            _expand.Text = open ? "展开简介" : "收起";
        });

        var info = new VerticalStackLayout { Spacing = 12, Padding = new Thickness(Ui.Gutter, 4, Ui.Gutter, 0) };
        info.Add(_buttons);
        info.Add(_progress);
        info.Add(_meta);
        info.Add(_title);
        info.Add(_description);
        info.Add(_expand);
        info.Add(_status);
        info.Add(_factsCard);

        _body = new VerticalStackLayout { new AspectView { Ratio = 0.62, Content = hero }, info, _gallery, _more, new BoxView { HeightRequest = 40, Color = Colors.Transparent } };
        Content = new ScrollView { Content = _body };
        ShowBasic();
    }

    public static VideoDetailPage ForCode(string code, CodedWork? work) => new(code, null, work);

    private bool _loaded;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) { RenderButtons(); return; }
        _loaded = true;
        _ = Motion.Enter(_body);
        await MobileServices.InitAsync();
        _ = LoadAsync();
    }

    // ---------------- 读取 ----------------

    private void ShowBasic()
    {
        _title.Text = _card is not null ? Text.TitleOnly(_card) : _work?.Title ?? "";
        _cover.Url = _card is not null ? Text.HiRes(_card) ?? _card.CoverUrl : _work?.CoverUrl;
        RenderMeta();
        RenderButtons();
        RenderFacts();
    }

    private async Task LoadAsync()
    {
        var ct = Life.Token;
        await Task.WhenAll(LoadSiteAsync(ct), LoadAv01Async(ct));
        if (!ct.IsCancellationRequested) await LoadR18Async(ct);
    }

    private async Task LoadSiteAsync(CancellationToken ct)
    {
        try
        {
            _detail = _card is not null
                ? await MobileServices.Sites.DetailAsync(_card.Source, _card.PageUrl, ct)
                : await MobileServices.Sites.FindByCodeAsync(_code, ct);
            if (ct.IsCancellationRequested) return;
            if (_detail is null)
            {
                _status.Text = "暂无片源";
                _status.IsVisible = true;
                RenderButtons();
                return;
            }
            if (_card is null)
            {
                _title.Text = _detail.Title.StartsWith(_detail.Code, StringComparison.OrdinalIgnoreCase) ? _detail.Title[_detail.Code.Length..].Trim() : _detail.Title;
                _cover.Url = Text.HiRes(PlayableCard()!) ?? _detail.CoverUrl;
            }
            MobileServices.Library.RememberProfile(_detail);
            if (_detail.ReleaseDate is { } rd) _facts.TryAdd("上市", rd.ToString("yyyy-MM-dd"));
            RenderMeta();
            RenderButtons();
            RenderFacts();
            RenderMore();
        }
        catch (OperationCanceledException) { }
        catch (SourceException e)
        {
            _status.Text = "读取失败：" + e.Message;
            _status.IsVisible = true;
        }
    }

    private async Task LoadAv01Async(CancellationToken ct)
    {
        try
        {
            _av01 = await MobileServices.Av01.FindByCodeAsync(_code, ct);
            if (_av01 is null || ct.IsCancellationRequested) return;
            if (_av01.Maker is { } maker) _facts["厂牌"] = maker;
            if (_av01.DurationSeconds is { } secs) _facts["片长"] = $"{secs / 60} 分钟";
            if (_av01.PublishedDate is { } d) _facts["上市"] = d.ToString("yyyy-MM-dd");
            if (!string.IsNullOrWhiteSpace(_av01.DescriptionCn))
            {
                _description.Text = _av01.DescriptionCn;
                _description.IsVisible = true;
                _expand.IsVisible = _av01.DescriptionCn.Length > 90;
            }
            RenderMeta();
            RenderFacts();
        }
        catch (OperationCanceledException) { }
        catch (SourceException) { } // 简介是锦上添花，读不到就不显示
    }

    private async Task LoadR18Async(CancellationToken ct)
    {
        var date = _av01?.PublishedDate ?? _detail?.ReleaseDate;
        if (date is null) return;
        try
        {
            // R18 只有发行日完全一致才采用；Jable 显示的日期有时差一天，两个都试
            var d = await MobileServices.R18.LookupAsync(_code, date.Value, ct);
            if (d is null && _detail?.ReleaseDate is { } jd && jd != date) d = await MobileServices.R18.LookupAsync(_code, jd, ct);
            if (d is null || ct.IsCancellationRequested) return;
            if (d.Directors.FirstOrDefault()?.NameKanji is { } dir) _facts.TryAdd("导演", dir);
            if (d.Label is { } label && label != d.Maker) _facts.TryAdd("系列", label);
            if (d.Maker is { } m) _facts.TryAdd("厂牌", m);
            if (d.RuntimeMinutes is { } rt) _facts.TryAdd("片长", $"{rt} 分钟");
            if (d.Categories.Count > 0) _facts.TryAdd("分类", string.Join("、", d.Categories.Take(8)));
            RenderFacts();
            if (d.Gallery.Count > 0) RenderGallery(d.Gallery);
        }
        catch (OperationCanceledException) { }
        catch (SourceException) { }
    }

    // ---------------- 渲染 ----------------

    private void RenderMeta()
    {
        _logo.Text = _detail?.Code ?? _card?.Code ?? _code;
        _meta.Clear();
        var date = _av01?.PublishedDate ?? _detail?.ReleaseDate ?? _work?.Date;
        if (date is not null) _meta.Add(MetaPill($"{date:yyyy-MM-dd}"));
        if (_card?.Duration is { } dur) _meta.Add(MetaPill(dur));
        else if (_av01?.DurationSeconds is { } s) _meta.Add(MetaPill($"{s / 60} 分钟"));
        _meta.Add(MetaPill("HD"));
        if (_detail?.Source is { } src) _meta.Add(MetaPill(src == VideoSource.MissAv ? "MissAV" : "Jable"));
    }

    private static Border MetaPill(string s) => new()
    {
        BackgroundColor = Color.FromArgb("#1AFFFFFF"), StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 12 },
        Padding = new Thickness(11, 3), Margin = new Thickness(0, 0, 8, 6),
        Content = new Label { Text = s, FontSize = 12.5, TextColor = Color.FromArgb("#D0D0D0") },
    };

    private VideoCard? PlayableCard()
    {
        if (_detail is not null)
            return new VideoCard(_detail.Source, _detail.PageUrl, _detail.Code, _detail.Title, _card?.CoverUrl ?? _detail.CoverUrl, _card?.PreviewUrl, _card?.Duration);
        return _card;
    }

    private void RenderButtons()
    {
        _buttons.Clear();
        var card = PlayableCard();
        var history = card is null ? null : MobileServices.Library.GetHistory(card.PageUrl);
        var resume = history?.IsUnfinished == true;
        var play = Ui.Action(resume ? "▶  继续播放" : "▶  播放", () => Play(resume: true));
        play.Opacity = card is null ? 0.4 : 1;
        _buttons.Add(play);
        if (resume) _buttons.Add(Round("↺", () => Play(resume: false)));
        if (card is not null)
        {
            var fav = MobileServices.Library.IsFavorite(card.PageUrl);
            _buttons.Add(Round(fav ? "✓" : "＋", () =>
            {
                var now = MobileServices.Library.ToggleFavorite(card);
                if (_detail is not null) MobileServices.Library.RememberProfile(_detail);
                Nav.Toast(now ? "已加入我的片单" : "已从片单移除");
                RenderButtons();
            }));
            var queued = MobileServices.Downloads.Find(card) is not null;
            _buttons.Add(Round(queued ? "✓↓" : "↓", () =>
            {
                if (MobileServices.Downloads.Find(card) is null)
                {
                    MobileServices.Downloads.Enqueue(card);
                    Nav.Toast("已加入下载（在「我的 › 下载」查看进度）");
                    RenderButtons();
                }
                else _ = Nav.Push(new DownloadsPage());
            }));
        }

        _progress.Clear();
        _progress.IsVisible = resume;
        if (resume && history is not null)
        {
            var bar = new Grid { HeightRequest = 3, BackgroundColor = Color.FromArgb("#4D4D4D") };
            var fill = new BoxView { Color = Cards.C("Accent"), HorizontalOptions = LayoutOptions.Start };
            bar.Add(fill);
            bar.SizeChanged += (_, _) => fill.WidthRequest = bar.Width * history.ProgressRatio;
            _progress.Add(bar);
            _progress.Add(Ui.Muted($"已看 {Text.Clock(history.Position)} / 共 {Text.Clock(history.Duration)}", 12.5));
        }
    }

    private static Border Round(string glyph, Action click)
    {
        var b = new Border
        {
            WidthRequest = 44, HeightRequest = 44, StrokeThickness = 1.5, Stroke = Color.FromArgb("#B3FFFFFF"), StrokeShape = new Ellipse(),
            BackgroundColor = Color.FromArgb("#33000000"),
            Content = new Label { Text = glyph, FontSize = 17, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
        };
        Cards.Tap(b, click);
        return b;
    }

    private void Play(bool resume)
    {
        var card = PlayableCard();
        if (card is null) return;
        if (!resume) MobileServices.Library.RecordProgress(card, 0, MobileServices.Library.GetHistory(card.PageUrl)?.Duration ?? 0);
        else if (MobileServices.Library.GetHistory(card.PageUrl) is null) MobileServices.Library.RecordProgress(card, 0, 0);
        if (_detail is not null) MobileServices.Library.RememberProfile(_detail);
        Nav.Play(card);
    }

    private IReadOnlyList<(string Name, string? Alt)> Performers()
    {
        var list = _detail?.Performers.Select(p => p.Name).ToList()
                   ?? _av01?.Actresses.Select(a => a.Name).ToList()
                   ?? _work?.Actors.ToList() ?? [];
        return list.Select(name =>
        {
            var tr = _av01?.Actresses.FirstOrDefault(a => a.AllNames.Contains(name))?.Translations.GetValueOrDefault("cn");
            return (name, tr is not null && tr != name ? tr : null);
        }).ToList();
    }

    /// <summary>资料卡：左边灰色字段名，右边内容；演员 / 类型 / 标签是可点的胶囊。</summary>
    private void RenderFacts()
    {
        _factsBox.Clear();
        void Add(string label, View content)
        {
            _factsBox.Add(new Label { Text = label, FontSize = 12.5, TextColor = Cards.C("Fg3") });
            _factsBox.Add(content);
        }
        View Chips(IEnumerable<(string Text, Action Click)> items)
        {
            var w = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            foreach (var (text, click) in items) w.Add(Ui.Chip(text, click));
            return w;
        }

        var performers = Performers();
        if (performers.Count > 0)
            Add("演员", Chips(performers.Select(p => (p.Alt is null ? p.Name : $"{p.Name}（{p.Alt}）", (Action)(() => Nav.OpenSitePerformer(p.Name, _code, performers.Count))))));
        if (_detail is not null)
        {
            var (types, tags) = WorkInfo.Split(_detail);
            if (types.Count > 0) Add("类型", Chips(types.Select(t => (t.Name, (Action)(() => Nav.OpenTag(t))))));
            if (tags.Count > 0) Add("标签", Chips(tags.Select(t => (t.Name, (Action)(() => Nav.OpenTag(t))))));
        }
        foreach (var key in new[] { "上市", "片长", "厂牌", "系列", "导演", "分类" })
            if (_facts.GetValueOrDefault(key) is { } value) Add(key, new Label { Text = value, FontSize = 14, TextColor = Color.FromArgb("#E0E0E0") });
        _factsCard.IsVisible = _factsBox.Count > 0;
    }

    private void RenderGallery(IReadOnlyList<string> urls)
    {
        _gallery.Clear();
        _gallery.Add(Ui.H2("剧照"));
        var row = new HorizontalStackLayout { Spacing = 8, Padding = new Thickness(Ui.Gutter, 0) };
        for (var i = 0; i < urls.Count; i++)
        {
            var idx = i;
            var img = new RemoteImage { Url = urls[i], HeightRequest = 110, WidthRequest = 160 };
            var frame = new Border { StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 10 }, BackgroundColor = Cards.C("Card"), Content = img };
            Cards.Tap(frame, () => _ = Nav.Push(new GalleryPage(urls, idx)));
            row.Add(frame);
        }
        _gallery.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = row });
    }

    /// <summary>更多类似内容：两列卡片。</summary>
    private void RenderMore()
    {
        _more.Clear();
        if (_detail is null || _detail.Related.Count == 0) return;
        _more.Add(Ui.H2("更多类似内容"));
        var grid = new Grid { Padding = new Thickness(11, 0), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        var items = _detail.Related.Take(16).ToList();
        for (var i = 0; i < items.Count; i++)
        {
            if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var card = Cards.Build(CardKind.Video, Nav.Open);
            card.BindingContext = CardItem.From(items[i]);
            grid.Add(card, i % 2, i / 2);
        }
        _more.Add(grid);
    }
}

/// <summary>剧照大图：左右滑动翻看。</summary>
public sealed class GalleryPage : ContentPage
{
    public GalleryPage(IReadOnlyList<string> urls, int index)
    {
        BackgroundColor = Colors.Black;
        Title = $"剧照 {index + 1} / {urls.Count}";
        var carousel = new CarouselView
        {
            ItemsSource = urls, Loop = false,
            ItemTemplate = new DataTemplate(() =>
            {
                var img = new RemoteImage { Aspect = Aspect.AspectFit };
                img.SetBinding(RemoteImage.UrlProperty, ".");
                return img;
            }),
        };
        carousel.Position = index;
        carousel.PositionChanged += (_, e) => Title = $"剧照 {e.CurrentPosition + 1} / {urls.Count}";
        Content = carousel;
    }
}
