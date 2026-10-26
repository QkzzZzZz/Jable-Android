using JableLens.Core.Models;
using JableLens.Core.Sites;
using JableLens.Core.Sources.Jable;
using JableLens.Core.Sources.MissAv;
using JableLens.Mobile.Controls;
using JableLens.Mobile.Services;

namespace JableLens.Mobile.Pages;

public sealed record GridTab(string Name, Func<int, CancellationToken, Task<PageResult<CardItem>>> Loader, CardKind Kind = CardKind.Video, string EmptyText = "没有内容");

/// <summary>标题 + 一排可切换的胶囊 + 卡片网格。分类、MissAV、搜索、片单、观看记录等都用它（和 Windows 版 TabbedGridPage 对应）。</summary>
public class GridPage : PageBase
{
    private readonly IReadOnlyList<GridTab> _tabs;
    protected readonly CardGrid Grid = new();
    private int _selected;
    private bool _loaded;

    public GridPage(string title, IReadOnlyList<GridTab> tabs, int selected = 0, View? extraHeader = null)
    {
        Title = title;
        _tabs = tabs;
        _selected = Math.Clamp(selected, 0, Math.Max(0, tabs.Count - 1));
        var header = new VerticalStackLayout();
        if (tabs.Count > 1) header.Add(Ui.Tabs(tabs.Select(t => t.Name).ToList(), _selected, Select));
        if (extraHeader is not null) header.Add(extraHeader);
        if (header.Count > 0) Grid.Header = header;
        Content = Grid;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_loaded) { _loaded = true; Select(_selected); }
        else OnRevisited();
    }

    protected virtual void OnRevisited() { }

    protected void Select(int index)
    {
        _selected = index;
        var tab = _tabs[index];
        Grid.Kind = tab.Kind;
        Grid.EmptyText = tab.EmptyText;
        Grid.Loader = tab.Loader;
        Grid.Reload();
    }

    protected void ReloadCurrent() => Select(_selected);

    protected override void OnClosed()
    {
        base.OnClosed();
        Grid.Cancel();
    }

    // ---------- 常用的标签 ----------

    public static GridTab JableTab(string name, Func<int, string> url, CardKind kind = CardKind.Video) =>
        new(name, async (p, ct) =>
        {
            var r = await MobileServices.Sites.JableListAsync(url(p), p, ct);
            return new PageResult<CardItem>(r.Items.Select(v => CardItem.From(v)).ToList(), r.HasMore);
        }, kind);

    public static GridTab MissAvTab(string name, Func<int, string> url, CardKind kind = CardKind.Video) =>
        new(name, async (p, ct) =>
        {
            var r = await MobileServices.Sites.MissAvListAsync(url(p), p, ct);
            return new PageResult<CardItem>(r.Items.Select(v => CardItem.From(v)).ToList(), r.HasMore);
        }, kind);

    public static GridTab LocalTab(string name, Func<IEnumerable<CardItem>> items, string emptyText, CardKind kind = CardKind.Video) =>
        new(name, (_, _) => Task.FromResult(new PageResult<CardItem>(items().ToList(), false)), kind, emptyText);

    /// <summary>Jable 的列表页（分类 / 标签）。<paramref name="pinnable"/> 不为空时右上角可以「固定到首页」。</summary>
    public static GridPage ForJableList(string title, string listUrl, TagLink? pinnable = null)
    {
        var page = new GridPage(title, [JableTab(title, p => JableUrls.WithPage(listUrl, p))]);
        if (pinnable is not null) page.AddPinButton(pinnable);
        return page;
    }

    public static GridPage ForMissAvList(string title, string url) =>
        new(title, [MissAvTab(title, p => p <= 1 ? url : $"{url}?page={p}")]);

    private void AddPinButton(TagLink tag)
    {
        var item = new ToolbarItem();
        void Render() => item.Text = MobileServices.Library.IsTagPinned(tag.PageUrl!) ? "已固定到首页" : "固定到首页";
        item.Clicked += (_, _) =>
        {
            var now = MobileServices.Library.ToggleTagPin(tag);
            Render();
            Nav.Toast(now ? "首页会多一行「" + tag.Name + "」" : "已从首页移除");
        };
        Render();
        ToolbarItems.Add(item);
    }
}

public sealed class FavoritesPage() : GridPage("我的片单", [
    LocalTab("我的片单", () => MobileServices.Library.Read(d => d.Favorites.ToList()).Select(v => CardItem.From(v)), "片单里还没有作品\n在作品详情点「＋ 片单」加入"),
])
{
    protected override void OnRevisited() => ReloadCurrent();
}

public sealed class HistoryPage : GridPage
{
    public HistoryPage() : base("观看记录", [
        LocalTab("没看完的", () => MobileServices.Library.RecentHistory(true).Select(h => CardItem.From(h.Card)), "没有没看完的作品"),
        LocalTab("全部历史", () => MobileServices.Library.RecentHistory().Select(h => CardItem.From(h.Card)), "还没有观看记录"),
    ])
    {
        var clear = new ToolbarItem { Text = "清空" };
        clear.Clicked += async (_, _) =>
        {
            if (!await Nav.Confirm("清空观看记录", "确定清空全部观看记录和播放进度吗？")) return;
            MobileServices.Library.Mutate(d => d.History.Clear());
            ReloadCurrent();
        };
        ToolbarItems.Add(clear);
    }

    protected override void OnRevisited() => ReloadCurrent();
}

public sealed class GuessPage() : GridPage("猜我喜欢", [
    new GridTab("猜我喜欢", async (_, ct) =>
        new PageResult<CardItem>((await MobileServices.Recommend.GuessAsync(120, ct)).Select(v => CardItem.From(v)).ToList(), false),
        EmptyText: "先看几部或加几部到片单，这里就会按你的口味推荐"),
]);

public sealed class SearchResultPage(string q) : GridPage($"搜索：{q}", [
    JableTab("Jable", p => JableUrls.Search(q, p)),
    MissAvTab("MissAV", p => MissAvUrls.Search(q, p)),
    new GridTab("演员", async (p, ct) =>
    {
        var r = await MobileServices.Netflav.SearchActressAsync(q, p, ct);
        return new PageResult<CardItem>(r.Items.Select(CardItem.From).ToList(), r.HasMore && r.Items.Count >= 40);
    }, CardKind.Performer, "没有找到演员"),
]);

public sealed class LivePage : GridPage
{
    public LivePage() : base("直播", Core.Sources.Live.LiveUrls.Sections.Select(s => new GridTab(s.Name, async (p, ct) =>
    {
        var r = await MobileServices.Live.ModelsAsync(s.Tag, p, ct);
        return new PageResult<CardItem>(r.Items.Select(CardItem.From).ToList(), r.HasMore);
    }, CardKind.Live, "现在没有人在直播")).ToList())
    { }

    // 截图几分钟就旧了，回到列表时刷新
    protected override void OnRevisited() => ReloadCurrent();
}

/// <summary>MissAV：各个栏目；最后几项是目录（女优一览 / 排行 / 类型 / 发行商）。</summary>
public sealed class MissAvPage(int selected = 0) : GridPage("MissAV",
    MissAvUrls.Sections.Select(s => MissAvTab(s.Name, p => MissAvUrls.List(s.Path, p)))
        .Concat(DirectoryTabs()).ToList(), selected)
{
    public static readonly (string Kind, string Name)[] Kinds = [("actresses", "女优一览"), ("ranking", "女优排行"), ("genres", "类型"), ("makers", "发行商")];

    private static IEnumerable<GridTab> DirectoryTabs() => Kinds.Select(k => k.Kind switch
    {
        "actresses" or "ranking" => new GridTab(k.Name, async (p, ct) =>
        {
            var r = await MobileServices.Sites.MissAvActressesAsync(k.Kind == "ranking" ? "actresses/ranking" : "actresses", p, ct);
            return new PageResult<CardItem>(r.Items.Select(CardItem.From).ToList(), k.Kind != "ranking" && r.HasMore);
        }, CardKind.Performer),
        _ => new GridTab(k.Name, async (p, ct) =>
        {
            var r = await MobileServices.Sites.MissAvTermsAsync(k.Kind, p, ct);
            return new PageResult<CardItem>(r.Items.Select(CardItem.From).ToList(), r.HasMore);
        }, CardKind.Term),
    });
}
