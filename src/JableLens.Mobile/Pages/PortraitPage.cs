using JableLens.Core.Http;
using JableLens.Core.Library;
using JableLens.Core.Models;
using JableLens.Core.Sites;
using JableLens.Mobile.Controls;
using JableLens.Mobile.Services;
using Microsoft.Maui.Controls.Shapes;

namespace JableLens.Mobile.Pages;

/// <summary>
/// 我的画像（和 Windows 版同样的内容）：观看统计、口味偏好、喜欢的演员、演员外表画像、关注、收藏类型、片单与下载。
/// 全部用本机记录算；只有「演员外表」要去 Netflav 读演员资料（和打开演员页一样）。
/// </summary>
public sealed class PortraitPage : PageBase
{
    private static readonly Dictionary<string, PerformerProfile?> LooksCache = [];
    private readonly VerticalStackLayout _root = new() { Padding = new Thickness(0, 0, 0, 40) };
    private readonly VerticalStackLayout _looks = new();
    private Portrait? _p;
    private bool _loaded;

    public PortraitPage()
    {
        Title = "我的画像";
        Content = new ScrollView { Content = _root };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        Render(Build());
        var ct = Life.Token;
        try
        {
            // 看过但还没记下标签的作品，补读一部分后再画一次
            var seeds = MobileServices.Library.Read(d => Recommender.Seeds(d, DateTimeOffset.Now, 30));
            if (seeds.Any(s => !MobileServices.Library.HasProfile(s.PageUrl)))
            {
                await MobileServices.Recommend.FillProfilesAsync(seeds, 12, ct);
                Render(Build());
            }
            await LoadLooksAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    private static Portrait Build() => MobileServices.Library.Read(d => Portrait.Build(d, DateTimeOffset.Now));

    private void Render(Portrait p)
    {
        _p = p;
        _root.Clear();
        var head = new VerticalStackLayout { Spacing = 4, Padding = new Thickness(Ui.Gutter, 8, Ui.Gutter, 0) };
        head.Add(new Label { Text = Summary(p), FontSize = 15, TextColor = Cards.C("Fg2") });
        head.Add(Ui.Muted("只根据这台手机上的观看记录、片单、关注计算，不会上传。", 12));
        _root.Add(head);

        _root.Add(Ui.H2("观看统计"));
        _root.Add(Tiles(
            (Hours(p.WatchedSeconds), "累计观看"), ($"{p.Works}", "看过的作品"),
            (p.Works == 0 ? "—" : $"{p.FinishRate:P0}", "看完率"), (p.Works == 0 ? "—" : $"{p.WatchedSeconds / 60 / Math.Max(1, p.Works):0} 分钟", "平均每部")));
        _root.Add(WeekChart(p.Weeks));

        _root.Add(Ui.H2("口味偏好"));
        if (p.ProfiledWorks == 0)
            _root.Add(Pad(Ui.Muted("还没有可以分析的作品。看几部或往片单里加几部，这里就会出现你常看的类型和标签。")));
        else
        {
            _root.Add(Pad(Ui.Muted("常看类型")));
            _root.Add(Bars(p.Types.Take(8).ToList(), OpenTag));
            _root.Add(Pad(Ui.Muted("常看标签")).WithMargin(new Thickness(Ui.Gutter, 14, Ui.Gutter, 0)));
            _root.Add(Bars(p.Tags.Take(8).ToList(), OpenTag));
        }

        _root.Add(Ui.H2("喜欢的演员"));
        _root.Add(p.Performers.Count == 0 ? Pad(Ui.Muted("看过的作品里还没有演员信息。")) : Bars(p.Performers.Take(8).ToList(), w => Nav.OpenSitePerformer(w.Name, null, 1), alwaysClickable: true));
        _root.Add(Ui.H2("演员外表画像"));
        if (_looks.Count == 0) _looks.Add(Pad(Ui.Muted("正在读取喜欢的演员的资料…")));
        _root.Add(_looks);

        _root.Add(Ui.H2("关注"));
        var (follows, pinned) = MobileServices.Library.Read(d => (d.Follows.Select(f => f.Name).ToList(), d.PinnedTags.ToList()));
        _root.Add(Pad(Ui.Muted("关注的演员")));
        _root.Add(follows.Count == 0 ? Pad(Ui.Muted("还没有关注演员（在演员页点「关注」）", 12)) : ChipRow(follows.Select(n => (n, (Action)(() => Nav.OpenPerformer(n))))));
        _root.Add(Pad(Ui.Muted("关注的类型（固定到首页的标签）")).WithMargin(new Thickness(Ui.Gutter, 10, Ui.Gutter, 0)));
        _root.Add(pinned.Count == 0 ? Pad(Ui.Muted("还没有固定到首页的标签", 12)) : ChipRow(pinned.Select(t => (t.Name, (Action)(() => Nav.OpenTag(t))))));

        _root.Add(Ui.H2("收藏类型"));
        _root.Add(p.FavoriteTypes.Count == 0
            ? Pad(Ui.Muted(p.Favorites == 0 ? "片单是空的。" : "片单里的作品还没读到标签，打开一下它们的详情就有了。"))
            : Bars(p.FavoriteTypes.Take(8).ToList(), OpenTag));

        _root.Add(Ui.H2("片单与下载"));
        _root.Add(Tiles(($"{p.Favorites}", "片单作品"), ($"{p.Downloads}", "已下载"), (Size(p.DownloadBytes), "下载占用"), ($"{p.Comics}", "读过的漫画")));
    }

    public static string Summary(Portrait p)
    {
        if (p.Works == 0 && p.Favorites == 0) return "还没有观看记录。先看几部，画像会慢慢清晰起来。";
        var parts = new List<string>();
        var likes = p.Types.Take(2).Concat(p.Tags.Take(2)).Select(t => t.Name).ToList();
        if (likes.Count > 0) parts.Add("偏爱「" + string.Join("」「", likes) + "」");
        if (p.Performers.FirstOrDefault() is { } top) parts.Add($"最常看 {top.Name}");
        parts.Add($"一共看了 {Hours(p.WatchedSeconds)}");
        return string.Join("，", parts) + "。";
    }

    public static string Hours(double seconds) => seconds < 3600 ? $"{seconds / 60:0} 分钟" : $"{seconds / 3600:0.#} 小时";

    private static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.#} GB" : $"{bytes / (double)(1L << 20):0} MB";

    // ---------------- 演员外表 ----------------

    /// <summary>喜欢的演员（加权前 6 位）+ 关注的演员，读 Netflav 资料，按喜好程度加权平均。</summary>
    private async Task LoadLooksAsync(CancellationToken ct)
    {
        if (_p is null) return;
        var people = _p.Performers.Take(6).Select(w => (w.Name, w.Weight, Resolve: true)).ToList();
        var top = people.Count > 0 ? people.Max(x => x.Weight) : 1;
        foreach (var f in MobileServices.Library.Read(d => d.Follows.Select(f => f.Name).ToList())) people.Add((f, top * 0.5, false));

        var found = new List<(PerformerProfile P, double W)>();
        foreach (var (name, weight, resolve) in people)
        {
            ct.ThrowIfCancellationRequested();
            var p = await LooksAsync(name, resolve, ct);
            if (p is not null && found.All(x => x.P.Name != p.Name)) found.Add((p, weight));
        }

        // Jable「身材」分组里的标签：用户常看的那几个
        IReadOnlyList<WeightedName> body = [];
        try
        {
            var groups = await MobileServices.Sites.JableTagGroupsAsync(ct);
            var urls = groups.Where(g => g.Name.Contains("身材")).SelectMany(g => g.Tags).Select(t => t.PageUrl).OfType<string>().ToHashSet();
            body = _p.Tags.Where(t => t.Url is not null && urls.Contains(t.Url)).Take(6).ToList();
        }
        catch (SourceException) { }

        _looks.Clear();
        if (found.Count == 0 && body.Count == 0)
        {
            _looks.Add(Pad(Ui.Muted("还读不到喜欢的演员的身高、罩杯等资料（Netflav 上可能没有），多看几部再来。")));
            return;
        }
        var tiles = new List<(string, string)>();
        if (Avg(found, p => p.Height) is { } h) tiles.Add(($"{h:0} cm", "平均身高"));
        var cups = found.Where(x => !string.IsNullOrEmpty(x.P.Cup)).GroupBy(x => x.P.Cup!.Trim().ToUpperInvariant())
            .Select(g => (Cup: g.Key, W: g.Sum(x => x.W))).OrderByDescending(x => x.W).ToList();
        if (cups.Count > 0) tiles.Add(($"{cups[0].Cup} 罩杯", "最常见罩杯"));
        if (Avg(found, p => p.Bust) is { } b && Avg(found, p => p.Waist) is { } w && Avg(found, p => p.Hip) is { } hip) tiles.Add(($"B{b:0} W{w:0} H{hip:0}", "平均三围"));
        if (Avg(found, p => p.AgeOn(DateOnly.FromDateTime(DateTime.Today))) is { } age) tiles.Add(($"{age:0} 岁", "平均年龄（现在）"));
        if (tiles.Count > 0) _looks.Add(Tiles([.. tiles]));
        else if (found.Count > 0) _looks.Add(Pad(Ui.Muted("这几位演员在 Netflav 上没有身高、罩杯、三围资料，先按身材标签看。")));
        if (cups.Count > 1)
        {
            var total = cups.Sum(c => c.W);
            _looks.Add(Pad(Ui.Muted("罩杯分布")));
            _looks.Add(ChipRow(cups.Take(6).Select(c => ($"{c.Cup}  {c.W / total:P0}", (Action)(() => { })))));
        }
        if (body.Count > 0)
        {
            _looks.Add(Pad(Ui.Muted("身材偏好")));
            _looks.Add(ChipRow(body.Select(t => (t.Name, (Action)(() => OpenTag(t))))));
        }
        _looks.Add(Pad(Ui.Muted("参考演员")));
        _looks.Add(ChipRow(found.OrderByDescending(x => x.W).Select(x => (x.P.Name, (Action)(() => Nav.OpenPerformer(x.P.Name))))));
        _ = Motion.Enter(_looks);
    }

    private static async Task<PerformerProfile?> LooksAsync(string name, bool resolve, CancellationToken ct)
    {
        if (LooksCache.TryGetValue(name, out var hit)) return hit;
        PerformerProfile? p = null;
        try
        {
            var netflavName = resolve ? (await MobileServices.Names.ResolveAsync(name, ct: ct))?.NetflavName : name;
            if (netflavName is not null) p = (await MobileServices.Netflav.GetActressPageAsync(netflavName, 1, ct)).Profile;
        }
        catch (SourceException) { }
        catch (HttpRequestException) { }
        LooksCache[name] = p;
        return p;
    }

    private static double? Avg(List<(PerformerProfile P, double W)> list, Func<PerformerProfile, int?> value)
    {
        var xs = list.Where(x => value(x.P) is > 0).ToList();
        return xs.Count == 0 ? null : xs.Sum(x => value(x.P)!.Value * x.W) / xs.Sum(x => x.W);
    }

    private static void OpenTag(WeightedName t)
    {
        if (t.Url is { } url) Nav.OpenTag(new TagLink(t.Name, url));
    }

    // ---------------- 小部件 ----------------

    private static View Pad(View v) => v.WithMargin(new Thickness(Ui.Gutter, 0, Ui.Gutter, 6));

    private static View ChipRow(IEnumerable<(string Text, Action Click)> items)
    {
        var w = Ui.ChipWrap();
        foreach (var (text, click) in items) w.Add(Ui.Chip(text, click));
        return w;
    }

    /// <summary>两列统计小卡，依次弹出来。</summary>
    private static View Tiles(params (string Value, string Label)[] items)
    {
        var grid = new Grid { Padding = new Thickness(Ui.Gutter, 0, Ui.Gutter - 10, 0), ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } };
        for (var i = 0; i < items.Length; i++)
        {
            if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var tile = Ui.Tile(items[i].Value, items[i].Label);
            grid.Add(tile, i % 2, i / 2);
            var delay = i * 60;
            tile.Opacity = 0;
            tile.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(delay), () => _ = Motion.PopIn(tile, 0.9));
        }
        return grid;
    }

    /// <summary>横向权重条：名称 ━━━━ N 部。条长按第一名归一，弹簧伸长出来。</summary>
    private static View Bars(IReadOnlyList<WeightedName> items, Action<WeightedName> open, bool alwaysClickable = false)
    {
        var list = new VerticalStackLayout { Padding = new Thickness(Ui.Gutter, 0) };
        var max = items.Count == 0 ? 1 : items.Max(x => x.Weight);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var g = new Grid { HeightRequest = 32, ColumnSpacing = 10, ColumnDefinitions = { new(new GridLength(96)), new(GridLength.Star), new(new GridLength(44)) } };
            g.Add(new Label { Text = item.Name, FontSize = 13.5, VerticalOptions = LayoutOptions.Center, MaxLines = 1, LineBreakMode = LineBreakMode.TailTruncation });
            var track = new Grid { HeightRequest = 8, VerticalOptions = LayoutOptions.Center };
            track.Add(new Border { StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 4 }, BackgroundColor = Color.FromArgb("#14FFFFFF") });
            var fill = new Border { StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 4 }, BackgroundColor = Cards.C("Accent"), HorizontalOptions = LayoutOptions.Start, WidthRequest = 0 };
            track.Add(fill);
            var ratio = Math.Max(0.03, item.Weight / max);
            var delay = 80 + i * 45;
            track.SizeChanged += (_, _) =>
            {
                if (track.Width <= 0 || fill.WidthRequest > 0) return;
                var target = track.Width * ratio;
                fill.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(delay), () =>
                    new Animation(x => fill.WidthRequest = x, 0.01, target, Easing.SpringOut).Commit(fill, "grow", length: 520));
            };
            g.Add(track, 1);
            g.Add(new Label { Text = $"{item.Count} 部", FontSize = 12, TextColor = Cards.C("Fg3"), VerticalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.End }, 2);
            if (item.Url is not null || alwaysClickable) Cards.Tap(g, () => open(item));
            list.Add(g);
        }
        return list;
    }

    /// <summary>近 12 周每周观看时长：单色竖条（红），顶部圆角，从底部长出来。</summary>
    private static View WeekChart(IReadOnlyList<WeekWatch> weeks)
    {
        const double H = 120;
        var max = Math.Max(1, weeks.Max(w => w.Seconds));
        var box = new VerticalStackLayout { Padding = new Thickness(Ui.Gutter, 10, Ui.Gutter, 0) };
        box.Add(Ui.Muted($"近 {weeks.Count} 周观看时长（最多一周 {Hours(max)}）", 12.5).WithMargin(new Thickness(0, 0, 0, 10)));
        var bars = new Grid { HeightRequest = H };
        var labels = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        for (var i = 0; i < weeks.Count; i++)
        {
            bars.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            labels.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var w = weeks[i];
            var height = w.Seconds <= 0 ? 2 : Math.Max(4, H * w.Seconds / max);
            var bar = new Border
            {
                StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(4, 4, 0, 0) }, Margin = new Thickness(3, 0),
                BackgroundColor = w.Seconds <= 0 ? Color.FromArgb("#1FFFFFFF") : Cards.C("Accent"), VerticalOptions = LayoutOptions.End, HeightRequest = 0,
            };
            var delay = i * 30;
            bar.Loaded += (_, _) => bar.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(delay), () =>
                new Animation(x => bar.HeightRequest = x, 0, height, Easing.SpringOut).Commit(bar, "grow", length: 520));
            var label = $"{w.WeekStart:M月d日} 那周：{Hours(w.Seconds)}，{w.Works} 部";
            Cards.Tap(bar, () => Nav.Toast(label));
            bars.Add(bar, i);
            labels.Add(new Label
            {
                Text = (weeks.Count - 1 - i) % 3 != 0 ? "" : i == weeks.Count - 1 ? "本周" : $"{w.WeekStart:M/d}",
                FontSize = 10.5, TextColor = Cards.C("Fg3"), HorizontalTextAlignment = TextAlignment.Center,
            }, i);
        }
        box.Add(bars);
        box.Add(new BoxView { HeightRequest = 1, Color = Cards.C("Line") });
        box.Add(labels);
        return box;
    }
}
