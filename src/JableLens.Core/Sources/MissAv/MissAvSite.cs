using System.Globalization;
using AngleSharp.Dom;
using JableLens.Core.Http;
using JableLens.Core.Models;

namespace JableLens.Core.Sources.MissAv;

public sealed record MissAvSection(string Name, string Path);

/// <summary>MissAV 女优目录里的一位。Info 如「5701 条影片 · 2008 出道」或「第 1 名」。</summary>
public sealed record MissAvActress(string Name, string Url, string? AvatarUrl, string Info);

/// <summary>MissAV 类型 / 发行商目录里的一项。</summary>
public sealed record MissAvTerm(string Name, string Url, int? VideoCount);

/// <summary>MissAV 网站菜单里的一组（二级菜单），下面是各个栏目（三级菜单）。</summary>
public sealed record MissAvGroup(string Name, IReadOnlyList<MissAvSection> Sections);

public static class MissAvUrls
{
    public const string Base = "https://missav.ws";
    public const string Referer = "https://missav.ws/";

    /// <summary>
    /// 栏目分组，照搬 MissAV 网页的导航菜单（2026-10-25 抄录；抽查了 SIRO、FC2、HEYZO、VR、无码流出、今日 / 本周热门，都能读出列表）。
    /// 网页菜单里还有「更多好站」，全是 bit.ly 第三方广告链接，不收。
    /// </summary>
    public static readonly IReadOnlyList<MissAvGroup> Groups =
    [
        new("日本 AV", [
            new("最近更新", "new"), new("新作上市", "release"), new("中文字幕", "chinese-subtitle"),
            new("无码流出", "uncensored-leak"), new("VR", "genres/VR"),
        ]),
        new("热门", [new("今日热门", "today-hot"), new("本周热门", "weekly-hot"), new("本月热门", "monthly-hot")]),
        new("素人", [
            new("SIRO", "siro"), new("LUXU", "luxu"), new("GANA", "gana"), new("PRESTIGE PREMIUM", "maan"),
            new("S-CUTE", "scute"), new("ARA", "ara"),
        ]),
        new("无码", [
            new("FC2", "fc2"), new("HEYZO", "heyzo"), new("东京热", "tokyohot"), new("一本道", "1pondo"),
            new("Caribbeancom", "caribbeancom"), new("Caribbeancompr", "caribbeancompr"), new("10musume", "10musume"),
            new("pacopacomama", "pacopacomama"), new("Gachinco", "gachinco"), new("XXX-AV", "xxxav"),
            new("人妻斩", "marriedslash"), new("顽皮 4610", "naughty4610"), new("顽皮 0930", "naughty0930"),
        ]),
        new("亚洲", [
            new("麻豆传媒", "madou"), new("TWAV", "twav"), new("Furuke", "furuke"), new("韩国直播", "klive"), new("中国直播", "clive"),
        ]),
    ];

    /// <summary>全部栏目（按分组顺序摊平）。</summary>
    public static readonly IReadOnlyList<MissAvSection> Sections = Groups.SelectMany(g => g.Sections).ToArray();

    public static string List(string path, int page = 1) => page <= 1 ? $"{Base}/cn/{path}" : $"{Base}/cn/{path}?page={page}";

    public static string Search(string keyword, int page = 1) => List("search/" + Uri.EscapeDataString(keyword.Trim()), page);

    public static string Video(string code) => $"{Base}/cn/{code.Trim().ToLowerInvariant()}";
}

public static partial class MissAvParser
{
    public const string Source = "MissAV";

    [System.Text.RegularExpressions.GeneratedRegex(@"\}\('((?:[^'\\]|\\.)*)',(\d+),(\d+),'([^']*)'\.split\('\|'\)")]
    private static partial System.Text.RegularExpressions.Regex Packed();

    [System.Text.RegularExpressions.GeneratedRegex(@"\b\w+\b")]
    private static partial System.Text.RegularExpressions.Regex Word();

    /// <summary>
    /// 播放地址藏在 Dean Edwards packer 压缩的脚本里（运行后生成 source / source1280 / source842）。
    /// 这里在本地解包取出，不用执行页面脚本。优先完整清单 source，其次 1280、842。
    /// </summary>
    public static string? StreamUrl(string html)
    {
        foreach (System.Text.RegularExpressions.Match m in Packed().Matches(html))
        {
            var code = Unpack(m.Groups[1].Value.Replace("\\'", "'"), int.Parse(m.Groups[2].Value), m.Groups[4].Value.Split('|'));
            string? Var(string name) =>
                System.Text.RegularExpressions.Regex.Match(code, $@"\b{name}\s*=\s*'([^']+)'") is { Success: true } v ? v.Groups[1].Value : null;
            var url = Var("source") ?? Var("source1280") ?? Var("source842");
            if (url is not null && url.Contains(".m3u8")) return url;
        }
        return null;
    }

    internal static string Unpack(string payload, int radix, string[] words) =>
        Word().Replace(payload, m =>
        {
            var i = ParseBase(m.Value, radix);
            return i >= 0 && i < words.Length && words[i].Length > 0 ? words[i] : m.Value;
        });

    private static int ParseBase(string s, int radix)
    {
        var n = 0;
        foreach (var ch in s.ToLowerInvariant())
        {
            var d = char.IsAsciiDigit(ch) ? ch - '0' : char.IsAsciiLetterLower(ch) ? ch - 'a' + 10 : 99;
            if (d >= radix) return -1;
            n = n * radix + d;
        }
        return n;
    }

    public static IReadOnlyList<VideoCard> ParseList(string html)
    {
        var doc = Html.Parse(html);
        return doc.QuerySelectorAll("div.thumbnail").Select(ParseCard).OfType<VideoCard>().DistinctBy(c => c.PageUrl).ToArray();
    }

    /// <summary>女优一览 / 女优排行：头像 + 名字 +（影片数 · 出道年 或 「第 N 名」）。</summary>
    public static IReadOnlyList<MissAvActress> ParseActresses(string html)
    {
        var doc = Html.Parse(html);
        return doc.QuerySelectorAll("li")
            .Select(li =>
            {
                var link = li.QuerySelector("a[href*='/actresses/']");
                var name = li.QuerySelector("h4").Text();
                if (link?.Attr("href") is not { } href || name is null || href.EndsWith("/ranking")) return null;
                var info = li.QuerySelectorAll("p, span").Select(e => e.Text()).OfType<string>().Where(t => t.Length > 0);
                return new MissAvActress(name, href, li.QuerySelector("img").Attr("src"), string.Join(" · ", info));
            })
            .OfType<MissAvActress>()
            .DistinctBy(a => a.Url)
            .ToArray();
    }

    /// <summary>类型 / 发行商目录：名字 + 影片数。kind 是 "genres" 或 "makers"。</summary>
    public static IReadOnlyList<MissAvTerm> ParseTerms(string html, string kind)
    {
        var doc = Html.Parse(html);
        return doc.QuerySelectorAll($"div.grid > div")
            .Select(d =>
            {
                var a = d.QuerySelector($"a.text-nord13[href*='/{kind}/']");
                if (a?.Attr("href") is not { } href || a.Text() is not { Length: > 0 } name) return null;
                return new MissAvTerm(name, href, Html.Count(d.QuerySelector("p").Text()));
            })
            .OfType<MissAvTerm>()
            .DistinctBy(t => t.Url)
            .ToArray();
    }

    public static bool HasNextPage(string html, int currentPage) =>
        Html.Parse(html).QuerySelectorAll("a[href*='page=']")
            .Select(a => a.Attr("href")!)
            .Select(h => int.TryParse(h[(h.LastIndexOf("page=", StringComparison.Ordinal) + 5)..].Split('&')[0], out var n) ? n : 0)
            .Any(n => n > currentPage);

    public static VideoDetail ParseVideo(string html, string pageUrl)
    {
        var doc = Html.Parse(html);
        string? Field(string label) =>
            doc.QuerySelectorAll("div.text-secondary")
                .FirstOrDefault(d => d.QuerySelector("span").Text()?.TrimEnd(':', '：') == label) is { } row
                ? row.QuerySelectorAll("span, time, a").Skip(1).Select(e => e.Text()).FirstOrDefault(t => t is not null)
                : null;
        IEnumerable<IElement> Links(string label) =>
            doc.QuerySelectorAll("div.text-secondary")
                .FirstOrDefault(d => d.QuerySelector("span").Text()?.TrimEnd(':', '：') == label)?.QuerySelectorAll("a") ?? Enumerable.Empty<IElement>();

        var title = doc.QuerySelector("meta[property='og:title']").Attr("content")
                    ?? doc.QuerySelector("h1").Text()
                    ?? throw new SourceException(Source, "播放页里找不到标题，页面结构可能变了");
        var code = Field("番号") ?? new Uri(pageUrl).AbsolutePath.Trim('/').Split('/').Last().ToUpperInvariant();
        var dateText = doc.QuerySelector("div.text-secondary time").Attr("datetime");
        DateOnly? date = dateText is { Length: >= 10 } && DateOnly.TryParseExact(dateText[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        return new VideoDetail(VideoSource.MissAv, pageUrl, code.ToUpperInvariant(), title,
            doc.QuerySelector("meta[property='og:image']").Attr("content"),
            date,
            Links("女优").Select(a => new PerformerLink(a.Text() ?? "", a.Attr("href"))).Where(p => p.Name.Length > 0).ToArray(),
            Links("类型").Select(a => new TagLink(a.Text() ?? "", a.Attr("href"))).Where(t => t.Name.Length > 0).ToArray(),
            null, // MissAV 的播放地址由页面脚本运行后生成，只在播放器里读取
            ParseList(html).Where(c => c.PageUrl != pageUrl).ToArray());
    }

    private static VideoCard? ParseCard(IElement box)
    {
        var link = box.QuerySelector("a.text-secondary");
        var href = link.Attr("href");
        if (href is null) return null;
        var title = link.Text() ?? "";
        var code = link.Attr("alt") ?? href.TrimEnd('/').Split('/').Last();
        var img = box.QuerySelector("img");
        var duration = box.QuerySelectorAll("span").Select(s => s.Text()).FirstOrDefault(t => t is not null && t.Contains(':') && t.All(c => char.IsAsciiDigit(c) || c == ':'));
        return new VideoCard(VideoSource.MissAv, href, code.ToUpperInvariant(), title,
            img.Attr("data-src", "src"), box.QuerySelector("video").Attr("data-src"), duration);
    }
}
