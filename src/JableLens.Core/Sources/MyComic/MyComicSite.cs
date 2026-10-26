using System.Text.Json;
using AngleSharp.Dom;
using JableLens.Core.Text;

namespace JableLens.Core.Sources.MyComic;

/// <summary>漫画列表里的一本。Latest 是封面上写的最新一话（「第1150話」）。</summary>
public sealed record ComicCard(int Id, string Title, string? CoverUrl, string? Latest)
{
    public string Url => MyComicUrls.Comic(Id);
}

public sealed record ComicChapter(int Id, string Title)
{
    public string Url => MyComicUrls.Chapter(Id);
}

/// <summary>章节分组：单话 / 单行本 / 番外篇。网站上是倒序（最新在前）。</summary>
public sealed record ChapterGroup(string Name, IReadOnlyList<ComicChapter> Chapters);

public sealed record ComicDetail(
    int Id, string Title, string? CoverUrl, string? Summary, string? Status, string? Author,
    string? Updated, IReadOnlyList<string> Tags, string? Region, string? Intro, IReadOnlyList<ChapterGroup> Groups);

/// <summary>一页漫画图。Ratio = 高 / 宽（网页上写了尺寸，图片没下载完也能先占好位置）。</summary>
public sealed record ComicPage(string Url, double Ratio);

/// <summary>列表筛选项（「地区」「类型」……），Key 是网址里的 filter[key]。</summary>
public sealed record ComicFilter(string Key, string Name, IReadOnlyList<(string Value, string Name)> Options);

public static class MyComicUrls
{
    public const string Base = "https://mycomic.com";
    public const string Referer = "https://mycomic.com/";

    public static string Comic(int id) => $"{Base}/comics/{id}";

    public static string Chapter(int id) => $"{Base}/chapters/{id}";

    /// <param name="sort">"" = 最新上架，"-update" = 最近更新，"-views" = 最高人气。</param>
    public static string List(string sort, IReadOnlyDictionary<string, string> filters, int page)
    {
        var q = new List<string>();
        if (sort.Length > 0) q.Add("sort=" + sort);
        q.AddRange(filters.Where(f => f.Value.Length > 0).Select(f => $"filter%5B{f.Key}%5D={Uri.EscapeDataString(f.Value)}"));
        if (page > 1) q.Add("page=" + page);
        return $"{Base}/comics" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    /// <summary>网站漫画资料库左边的筛选（2026-10-25 抄录），名字转成简体。</summary>
    public static readonly IReadOnlyList<ComicFilter> Filters =
    [
        new("country", "地区", [("japan", "日本"), ("hongkong", "港台"), ("korea", "韩国"), ("china", "内地"), ("europe", "欧美"), ("other", "其他")]),
        new("tag", "类型", [
            ("rexue", "热血"), ("maoxian", "冒险"), ("aiqing", "爱情"), ("xiaoyuan", "校园"), ("gaoxiao", "搞笑"), ("kehuan", "科幻"),
            ("mohuan", "魔幻"), ("mofa", "魔法"), ("xuanyi", "悬疑"), ("tuili", "推理"), ("kongbu", "恐怖"), ("shengui", "神鬼"),
            ("baihe", "百合"), ("hougong", "后宫"), ("danmei", "耽美"), ("mengxi", "萌系"), ("gedou", "格斗"), ("wuxia", "武侠"),
            ("jizhan", "机战"), ("zhanzheng", "战争"), ("lishi", "历史"), ("shehui", "社会"), ("heidao", "黑道"), ("zhichang", "职场"),
            ("shenghuo", "生活"), ("zhiyu", "治愈"), ("lizhi", "励志"), ("jingji", "竞技"), ("tiyu", "体育"), ("meishi", "美食"),
            ("yinyue", "音乐"), ("wudao", "舞蹈"), ("zhentan", "侦探"), ("weiniang", "伪娘"), ("funv", "腐女"), ("zhainan", "宅男"),
            ("sige", "四格"), ("zazhi", "杂志"),
        ]),
        new("audience", "受众", [("shaonian", "少年"), ("shaonv", "少女"), ("qingnian", "青年"), ("ertong", "儿童"), ("tongyong", "通用")]),
        new("end", "状态", [("0", "连载中"), ("1", "已完结")]),
    ];
}

/// <summary>mycomic.com 的页面解析（服务端渲染的 HTML，繁体字转成简体显示）。</summary>
public static class MyComicSite
{
    private static string S(string s) => ChineseConverter.ToSimplified(s);

    private static int? IdFrom(string? href, string kind)
    {
        if (href is null) return null;
        var i = href.IndexOf($"/{kind}/", StringComparison.Ordinal);
        if (i < 0) return null;
        var rest = href[(i + kind.Length + 2)..];
        var end = rest.IndexOfAny(['/', '?', '#']);
        return int.TryParse(end < 0 ? rest : rest[..end], out var n) ? n : null;
    }

    /// <summary>列表页 / 首页的漫画卡片：封面 + 封面上的最新一话 + 下面的书名。</summary>
    public static IReadOnlyList<ComicCard> ParseList(string html)
    {
        var doc = Html.Parse(html);
        return doc.QuerySelectorAll("div.group.relative")
            .Select(card =>
            {
                var a = card.QuerySelector("a[href*='/comics/']");
                if (IdFrom(a.Attr("href"), "comics") is not { } id) return null;
                var img = card.QuerySelector("img");
                var title = card.QuerySelector("[data-flux-subheading]").Text() ?? img.Attr("alt");
                if (title is null) return null;
                var latest = card.QuerySelector("a div div").Text();
                return new ComicCard(id, S(title), img.Attr("src", "data-src"), latest is null ? null : S(latest));
            })
            .OfType<ComicCard>()
            .DistinctBy(c => c.Id)
            .ToArray();
    }

    public static bool HasNextPage(string html, int currentPage) =>
        Html.Parse(html).QuerySelectorAll("a[href*='page=']")
            .Select(a => a.Attr("href")!)
            .Select(h => int.TryParse(h[(h.LastIndexOf("page=", StringComparison.Ordinal) + 5)..].Split('&')[0], out var n) ? n : 0)
            .Any(n => n > currentPage);

    public static ComicDetail ParseDetail(string html, int id)
    {
        var doc = Html.Parse(html);
        var card = doc.QuerySelector("[data-flux-card]");
        var title = card?.QuerySelector("[data-flux-heading]").Text() ?? doc.Title ?? "";

        string? Field(string label)
        {
            var row = card?.QuerySelectorAll("label").FirstOrDefault(l => l.Text()?.StartsWith(label, StringComparison.Ordinal) == true)?.ParentElement;
            return row?.QuerySelector("span").Text();
        }
        IReadOnlyList<string> Links(string label) =>
            card?.QuerySelectorAll("label").FirstOrDefault(l => l.Text()?.StartsWith(label, StringComparison.Ordinal) == true)?.ParentElement?
                .QuerySelectorAll("a").Select(a => a.Text()).OfType<string>().Select(S).ToArray() ?? [];

        // 简介：展开后的那一份（x-show="show"）比较全
        var intro = card?.QuerySelectorAll("div[x-show]").Select(d => d.Text()).OfType<string>().OrderByDescending(t => t.Length).FirstOrDefault();

        var groups = new List<ChapterGroup>();
        foreach (var box in doc.QuerySelectorAll("div[x-data]").Where(d => d.GetAttribute("x-data")!.TrimStart().StartsWith("{", StringComparison.Ordinal)
                                                                            && d.GetAttribute("x-data")!.Contains("chapters:", StringComparison.Ordinal)))
        {
            var chapters = ParseChapterJson(box.GetAttribute("x-data")!);
            if (chapters.Count == 0) continue;
            // 组名是 x-data 那个 div 里第一段文字（「單話」「單行本」「番外篇」）
            var name = box.Children.Select(c => c.Text()).OfType<string>().FirstOrDefault()?.Split(' ')[0] ?? "章节";
            groups.Add(new ChapterGroup(S(name), chapters));
        }

        return new ComicDetail(
            id, S(title),
            card?.QuerySelector("img").Attr("src"),
            card?.QuerySelector("[data-flux-subheading]").Text() is { } sub ? S(sub) : null,
            card?.QuerySelector("[data-flux-badge]").Text() is { } st ? S(st) : null,
            Links("原創作者") is [var a, ..] ? a : Field("原創作者"),
            Field("最後更新"),
            Links("作品類型"),
            Links("作品地區") is [var r, ..] ? r : null,
            intro is null ? null : S(intro),
            groups);
    }

    /// <summary>x-data="{ chapters: [{"id":817597,"title":"第1150話"}, …], … }" 里的章节数组。</summary>
    private static IReadOnlyList<ComicChapter> ParseChapterJson(string xData)
    {
        var start = xData.IndexOf("chapters:", StringComparison.Ordinal);
        start = xData.IndexOf('[', start);
        if (start < 0) return [];
        var end = xData.IndexOf("}]", start, StringComparison.Ordinal);
        if (end < 0) return [];
        try
        {
            using var json = JsonDocument.Parse(xData[start..(end + 2)]);
            return json.RootElement.EnumerateArray()
                .Select(e => new ComicChapter(e.GetProperty("id").GetInt32(), S(e.GetProperty("title").GetString() ?? "")))
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>阅读页：每一页的图片（前几页是 src，后面懒加载的是 data-src）。</summary>
    public static IReadOnlyList<ComicPage> ParsePages(string html)
    {
        var doc = Html.Parse(html);
        return doc.QuerySelectorAll("img[x-ref^='page-']")
            .Select(img =>
            {
                if (img.Attr("data-src", "src") is not { } url) return null;
                var w = double.TryParse(img.GetAttribute("width"), out var a) ? a : 0;
                var h = double.TryParse(img.GetAttribute("height"), out var b) ? b : 0;
                return new ComicPage(url, w > 0 && h > 0 ? h / w : 1.42);
            })
            .OfType<ComicPage>()
            .ToArray();
    }
}
