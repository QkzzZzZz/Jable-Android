using JableLens.Core.Http;
using JableLens.Core.Models;
using JableLens.Core.Sites;
using JableLens.Core.Sources.Jable;
using JableLens.Core.Sources.MissAv;
using JableLens.Mobile.Controls;
using JableLens.Mobile.Services;
using Microsoft.Maui.Controls.Shapes;

namespace JableLens.Mobile.Pages;

/// <summary>「发现」：Windows 版顶栏里的栏目都在这里（标签、分类、演员、MissAV、封面墙……），最上面是搜索框。</summary>
public sealed class DiscoverPage : PageBase
{
    public DiscoverPage()
    {
        Title = "发现";
        var root = new VerticalStackLayout { Padding = new Thickness(0, 0, 0, 30) };
        var search = new Border
        {
            StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 22 }, BackgroundColor = Color.FromArgb("#262628"),
            Margin = new Thickness(Ui.Gutter, 10, Ui.Gutter, 6), Padding = new Thickness(16, 12),
            Content = new HorizontalStackLayout
            {
                Spacing = 10,
                Children = { new Image { Source = "ic_search.png", WidthRequest = 20, HeightRequest = 20, Opacity = 0.7 }, new Label { Text = "搜索番号、标题、演员", TextColor = Cards.C("Fg3"), FontSize = 15 } },
            },
        };
        Cards.Tap(search, () => _ = Nav.Push(new SearchPage()));
        root.Add(search);

        root.Add(Ui.H2("Jable"));
        root.Add(Tiles([
            ("标签", "衣着、身材、剧情…", () => Nav.Push(new TagsPage())),
            ("分类", "主题分类", () => Nav.Push(new GridPage("分类", [new GridTab("主题", async (_, ct) =>
                new PageResult<CardItem>((await MobileServices.Sites.JableCategoriesAsync(ct)).Select(CardItem.From).ToList(), false), CardKind.Category)]))),
            ("最近更新", "每天的新上传", () => Nav.Push(new GridPage("最近更新", [GridPage.JableTab("最近更新", JableUrls.Latest)]))),
            ("本周热门", "这周大家在看", () => Nav.Push(new GridPage("本周热门", [GridPage.JableTab("本周热门", JableUrls.Hot)]))),
            ("新片上市", "刚发售的作品", () => Nav.Push(new GridPage("新片上市", [GridPage.JableTab("新片上市", JableUrls.NewRelease)]))),
            ("封面墙", "大图浏览", () => Nav.Push(new GridPage("封面墙", [
                GridPage.JableTab("最近更新", JableUrls.Latest, CardKind.Category), GridPage.JableTab("本周热门", JableUrls.Hot, CardKind.Category),
                GridPage.JableTab("新片上市", JableUrls.NewRelease, CardKind.Category), GridPage.MissAvTab("MissAV 最近更新", p => MissAvUrls.List("new", p), CardKind.Category)]))),
        ]));
        root.Add(Ui.H2("演员"));
        root.Add(Tiles([
            ("演员目录", "按年龄、身高、罩杯筛选", () => Nav.Push(new PerformerDirectoryPage())),
            ("关注", "关注演员的新作", () => Nav.Push(new FollowsPage())),
        ]));
        root.Add(Ui.H2("MissAV"));
        var sections = MissAvUrls.Sections.Select((s, i) => (s.Name, (string?)null, (Func<Task>)(() => Nav.Push(new MissAvPage(i))))).ToList();
        var offset = MissAvUrls.Sections.Count;
        sections.AddRange(MissAvPage.Kinds.Select((k, i) => (k.Name, (string?)"目录", (Func<Task>)(() => Nav.Push(new MissAvPage(offset + i))))));
        root.Add(Tiles(sections));
        root.Add(Ui.H2("为你推荐"));
        root.Add(Tiles([("猜我喜欢", "按你的观看记录和片单", () => Nav.Push(new GuessPage()))]));

        Content = new ScrollView { Content = root };
    }

    /// <summary>两列的入口方块。</summary>
    private static View Tiles(IReadOnlyList<(string Name, string? Sub, Func<Task> Open)> items)
    {
        var grid = new Grid { Padding = new Thickness(Ui.Gutter - 5, 0), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        for (var i = 0; i < items.Count; i++)
        {
            if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var (name, sub, open) = items[i];
            var body = new VerticalStackLayout { Spacing = 3, Children = { new Label { Text = name, FontSize = 15.5, FontAttributes = FontAttributes.Bold } } };
            if (sub is not null) body.Add(new Label { Text = sub, FontSize = 12, TextColor = Cards.C("Fg3"), MaxLines = 1, LineBreakMode = LineBreakMode.TailTruncation });
            var tile = new Border
            {
                StrokeThickness = 1, Stroke = Color.FromArgb("#14FFFFFF"), StrokeShape = new RoundRectangle { CornerRadius = 14 },
                BackgroundColor = Color.FromArgb("#1E1E20"), Padding = new Thickness(14, 14), Margin = new Thickness(5), Content = body,
            };
            Cards.Tap(tile, () => _ = open());
            grid.Add(tile, i % 2, i / 2);
        }
        return grid;
    }
}

/// <summary>搜索：输入框 + 最近搜索；回车后打开结果页（Jable / MissAV / 演员三个标签）。</summary>
public sealed class SearchPage : PageBase
{
    private static readonly List<string> Recent = [];
    private readonly FlexLayout _recent = Ui.ChipWrap();

    public SearchPage()
    {
        Title = "搜索";
        var box = new SearchBar { Placeholder = "番号、标题、演员", Margin = new Thickness(8, 6, 8, 0) };
        box.SearchButtonPressed += (_, _) => Go(box.Text);
        var root = new VerticalStackLayout { box, Ui.H2("最近搜索"), _recent };
        Content = new ScrollView { Content = root };
        Loaded += (_, _) => { box.Focus(); Render(); };
    }

    private void Go(string? text)
    {
        var q = text?.Trim();
        if (string.IsNullOrEmpty(q)) return;
        Recent.Remove(q);
        Recent.Insert(0, q);
        if (Recent.Count > 12) Recent.RemoveAt(12);
        Render();
        _ = Nav.Push(new SearchResultPage(q));
    }

    private void Render()
    {
        _recent.Clear();
        if (Recent.Count == 0) _recent.Add(Ui.Muted("还没有搜索过"));
        foreach (var r in Recent)
        {
            var q = r;
            _recent.Add(Ui.Chip(q, () => Go(q)));
        }
    }
}

/// <summary>标签：按组（衣着、身材……）列出全部标签，点一个看它的作品（可固定到首页）。</summary>
public sealed class TagsPage : PageBase
{
    private readonly VerticalStackLayout _root = new() { Padding = new Thickness(0, 0, 0, 30) };

    public TagsPage()
    {
        Title = "标签";
        _root.Add(new ActivityIndicator { IsRunning = true, Margin = new Thickness(40) });
        Content = new ScrollView { Content = _root };
    }

    private bool _loaded;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        try
        {
            var groups = await MobileServices.Sites.JableTagGroupsAsync(Life.Token);
            _root.Clear();
            var pinned = MobileServices.Library.Read(d => d.PinnedTags.ToList());
            if (pinned.Count > 0)
            {
                _root.Add(Ui.H2("固定在首页的"));
                var wrap = Ui.ChipWrap();
                foreach (var t in pinned) wrap.Add(Ui.Chip(t.Name, () => Nav.OpenTag(t), selected: true));
                _root.Add(wrap);
            }
            foreach (var g in groups)
            {
                _root.Add(Ui.H2(g.Name));
                var wrap = Ui.ChipWrap();
                foreach (var t in g.Tags) wrap.Add(Ui.Chip(t.Name, () => Nav.OpenTag(t)));
                _root.Add(wrap);
            }
            _ = Motion.Enter(_root);
        }
        catch (OperationCanceledException) { }
        catch (SourceException e)
        {
            _root.Clear();
            _root.Add(Ui.Muted("读取失败：" + e.Message));
        }
    }
}

/// <summary>演员目录（Netflav）：一排筛选（类型、年龄、身高、胸围、罩杯）+ 名字搜索。</summary>
public sealed class PerformerDirectoryPage : PageBase
{
    // 档位文字来自 Netflav 网页
    private static readonly string[] Ages = ["不限", "超年轻", "年轻", "轻熟女", "熟女", "四十路"];
    private static readonly string[] Heights = ["不限", "160 以下", "161-165", "166-170", "171-175", "176-180", "180 以上"];
    private static readonly string[] Breasts = ["不限", "<28", "28", "30", "32", "34", "36", "38", "40", "42", "44", "44+"];
    private static readonly string[] Cups = ["不限", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "O"];

    private readonly CardGrid _grid = new() { Kind = CardKind.Performer, EmptyText = "没有符合条件的演员" };
    private PerformerQuery _query = new();

    public PerformerDirectoryPage()
    {
        Title = "演员";
        _grid.Loader = async (page, ct) =>
        {
            var r = await MobileServices.Netflav.GetActressDirectoryAsync(_query, page, ct);
            return new PageResult<CardItem>(r.Items.Select(CardItem.From).ToList(), r.HasMore);
        };
        var box = new SearchBar { Placeholder = "演员名字", Margin = new Thickness(8, 4, 8, 0) };
        box.SearchButtonPressed += (_, _) => Apply(_query with { Keyword = box.Text?.Trim() });
        var filters = new HorizontalStackLayout { Spacing = 8, Padding = new Thickness(Ui.Gutter, 6) };
        filters.Add(Picker("类型", ["有码", "无码"], i => _query with { Type = i == 0 ? CensorType.Censored : CensorType.Uncensored }));
        filters.Add(Picker("年龄", Ages, i => _query with { Age = i == 0 ? null : i + 1 }));
        filters.Add(Picker("身高", Heights, i => _query with { Height = i == 0 ? null : i }));
        filters.Add(Picker("胸围", Breasts, i => _query with { Breast = i == 0 ? null : i }));
        filters.Add(Picker("罩杯", Cups, i => _query with { Cup = i == 0 ? null : Cups[i] }));
        _grid.Header = new VerticalStackLayout { box, new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = filters } };
        Content = _grid;
        Loaded += (_, _) => _grid.Reload();
    }

    /// <summary>筛选胶囊：点开是系统的选择列表。</summary>
    private View Picker(string name, string[] options, Func<int, PerformerQuery> change)
    {
        var label = new Label { Text = name + " ▾", FontSize = 13.5, VerticalOptions = LayoutOptions.Center };
        var chip = new Border
        {
            StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 16 }, Padding = new Thickness(14, 7), BackgroundColor = Color.FromArgb("#2A2A2C"), Content = label,
        };
        Cards.Tap(chip, async () =>
        {
            var pick = await Shell.Current.DisplayActionSheetAsync(name, "取消", null, options);
            var i = Array.IndexOf(options, pick);
            if (i < 0) return;
            label.Text = (i == 0 ? name : $"{name}：{options[i]}") + " ▾";
            Apply(change(i));
        });
        return chip;
    }

    private void Apply(PerformerQuery q)
    {
        if (q.Keyword is { Length: 0 }) q = q with { Keyword = null };
        _query = q;
        _grid.Reload();
    }

    protected override void OnClosed()
    {
        base.OnClosed();
        _grid.Cancel();
    }
}

/// <summary>演员页：头像、别名、身高罩杯三围；关注；作品（Netflav，按页往下读）。</summary>
public sealed class PerformerPage : PageBase
{
    private const int MaxPages = 30;
    private readonly string _name;
    private readonly string? _iconUrl;
    private readonly CardGrid _grid = new() { EmptyText = "没有找到作品" };
    private readonly Label _profile = new() { FontSize = 13.5, TextColor = Cards.C("Fg2"), HorizontalTextAlignment = TextAlignment.Center };
    private readonly Label _alt = new() { FontSize = 13, TextColor = Cards.C("Fg3"), HorizontalTextAlignment = TextAlignment.Center };
    private readonly HorizontalStackLayout _actions = new() { Spacing = 10, HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 10, 0, 6) };
    private readonly List<CodedWork> _works = [];
    private bool _following;

    public PerformerPage(string name, string? iconUrl)
    {
        _name = name;
        _iconUrl = iconUrl;
        Title = name;
        var avatar = new Border
        {
            WidthRequest = 96, HeightRequest = 96, StrokeThickness = 0, StrokeShape = new Ellipse(), BackgroundColor = Cards.C("Card"), HorizontalOptions = LayoutOptions.Center,
            Content = new Grid { new Label { Text = name[..1], FontSize = 34, TextColor = Cards.C("Fg3"), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }, new RemoteImage { Url = iconUrl } },
        };
        var header = new VerticalStackLayout
        {
            Spacing = 6, Padding = new Thickness(Ui.Gutter, 14, Ui.Gutter, 8),
            Children = { avatar, new Label { Text = name, FontSize = 24, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center }, _alt, _profile, _actions },
        };
        _grid.Header = header;
        _grid.Loader = LoadPageAsync;
        Content = _grid;
        RenderActions();
        Loaded += (_, _) => { if (_works.Count == 0) _grid.Reload(); };
    }

    private async Task<PageResult<CardItem>> LoadPageAsync(int page, CancellationToken ct)
    {
        if (page == 1) _works.Clear();
        var p = await MobileServices.Netflav.GetActressPageAsync(_name, page, ct);
        if (page == 1) ShowProfile(p.Profile);
        var known = _works.Select(w => Core.Text.VideoCode.Compact(w.Code)).ToHashSet();
        // 同一番号会重复上架（如 [中字] 版），只保留一条
        var fresh = p.Works.Items.Where(w => known.Add(Core.Text.VideoCode.Compact(w.Code))).ToList();
        _works.AddRange(fresh);
        return new PageResult<CardItem>(fresh.Select(w => CardItem.From(w)).ToList(), p.Works.HasMore && page < MaxPages);
    }

    private void ShowProfile(PerformerProfile? p)
    {
        if (p is null) return;
        _alt.Text = string.Join(" / ", new[] { p.NameJa, p.NameZh }.Where(n => !string.IsNullOrEmpty(n) && n != _name).Distinct());
        var parts = new List<string>();
        if (p.AgeOn(DateOnly.FromDateTime(DateTime.Today)) is { } age) parts.Add($"{age} 岁");
        if (p.Height is { } h) parts.Add($"{h} cm");
        if (!string.IsNullOrEmpty(p.Cup)) parts.Add($"{p.Cup} 罩杯");
        if (p.Bust is not null || p.Waist is not null || p.Hip is not null) parts.Add($"B{p.Bust?.ToString() ?? "?"} W{p.Waist?.ToString() ?? "?"} H{p.Hip?.ToString() ?? "?"}");
        if (p.VideoCount is { } n) parts.Add($"{n} 部作品");
        _profile.Text = string.Join("  ·  ", parts);
    }

    private void RenderActions()
    {
        _actions.Clear();
        var following = MobileServices.Library.IsFollowing(_name);
        _actions.Add(Ui.Action(_following ? "正在读取全部作品…" : following ? "✓ 已关注" : "＋ 关注", ToggleFollow, primary: !following));
        _actions.Add(Ui.Action("在 Jable 搜索", () => _ = Nav.Push(new SearchResultPage(_name)), primary: false));
    }

    /// <summary>关注时要先知道现有的全部作品（之后多出来的才算新作），没读完就先读完。</summary>
    private async void ToggleFollow()
    {
        if (_following) return;
        if (MobileServices.Library.IsFollowing(_name))
        {
            MobileServices.Library.ToggleFollow(_name, _iconUrl, []);
            RenderActions();
            return;
        }
        _following = true;
        RenderActions();
        try
        {
            var codes = new HashSet<string>(_works.Select(w => w.Code));
            for (var page = 1; page <= MaxPages; page++)
            {
                var p = await MobileServices.Netflav.GetActressPageAsync(_name, page, Life.Token);
                if (!p.Works.HasMore) break;
                foreach (var w in p.Works.Items) codes.Add(w.Code);
            }
            MobileServices.Library.ToggleFollow(_name, _iconUrl, codes);
            Nav.Toast("已关注，有新作会出现在首页和「关注」里");
        }
        catch (Exception e) when (e is SourceException or OperationCanceledException)
        {
            Nav.Toast("关注失败：" + e.Message);
        }
        _following = false;
        RenderActions();
    }

    protected override void OnClosed()
    {
        base.OnClosed();
        _grid.Cancel();
    }
}

/// <summary>关注的演员 + 检查到的新作。</summary>
public sealed class FollowsPage : PageBase
{
    private readonly CardGrid _grid = new() { EmptyText = "暂无新作" };
    private readonly FlexLayout _people = Ui.ChipWrap();
    private readonly Label _status = Ui.Muted("");

    public FollowsPage()
    {
        Title = "关注";
        var buttons = new HorizontalStackLayout { Spacing = 10, Padding = new Thickness(Ui.Gutter, 8) };
        buttons.Add(Ui.Action("检查新作", async () =>
        {
            _status.Text = "检查中…";
            var found = await MobileServices.Checker.CheckAllAsync();
            _status.Text = found.Count == 0 ? $"{DateTime.Now:HH:mm} 检查完毕，没有新作" : $"{DateTime.Now:HH:mm} 发现 {found.Count} 部新作";
            Render();
        }));
        buttons.Add(Ui.Action("全部标为已读", () =>
        {
            MobileServices.Library.Mutate(d => d.Follows.ForEach(f => f.NewWorks.Clear()));
            Render();
        }, primary: false));
        _status.Margin = new Thickness(Ui.Gutter, 0);
        _grid.Header = new VerticalStackLayout { buttons, _status, Ui.H2("关注的演员"), _people, Ui.H2("新作") };
        Content = _grid;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Render();
    }

    private void Render()
    {
        var follows = MobileServices.Library.Read(d => d.Follows.Select(f => (f.Name, f.IconUrl, New: f.NewWorks.ToList())).ToList());
        _people.Clear();
        if (follows.Count == 0) _people.Add(Ui.Muted("还没有关注演员（在演员页点「关注」）"));
        foreach (var f in follows.OrderByDescending(f => f.New.Count).ThenBy(f => f.Name))
        {
            var name = f.Name;
            var icon = f.IconUrl;
            _people.Add(Ui.Chip(f.New.Count > 0 ? $"{name} · {f.New.Count} 新" : name, () => Nav.OpenPerformer(name, icon), selected: f.New.Count > 0));
        }
        _grid.SetItems(follows.SelectMany(f => f.New.Select(w => CardItem.From(w, f.Name))));
    }
}
