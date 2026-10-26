using System.Collections.Concurrent;
using JableLens.Core.Http;
using JableLens.Core.Models;
using JableLens.Core.Sources.Jable;
using JableLens.Core.Sources.MissAv;
using JableLens.Core.Sources.MyComic;
using JableLens.Core.Text;

namespace JableLens.Core.Sites;

/// <summary>
/// Jable / MissAV / 漫画读取：浏览器取 HTML（各站一个浏览器，可同时进行），后台线程解析。
/// HTML 在内存里缓存几分钟：返回上一页、打开刚预读过的作品都是瞬间完成。
/// </summary>
public sealed class SiteClient(IPageReader jable, IPageReader missAv, IPageReader comic)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    private const int CacheMax = 120;

    /// <summary>耗时日志（排查卡顿用），平台层按需挂上。</summary>
    public static Action<string>? Log { get; set; }

    private readonly ConcurrentDictionary<string, (DateTime At, Task<string> Html)> _cache = new();

    private IPageReader ReaderFor(string url) =>
        url.Contains("missav", StringComparison.OrdinalIgnoreCase) ? missAv : url.Contains("mycomic", StringComparison.OrdinalIgnoreCase) ? comic : jable;

    private static string SourceFor(string url) =>
        url.Contains("missav", StringComparison.OrdinalIgnoreCase) ? MissAvParser.Source : url.Contains("mycomic", StringComparison.OrdinalIgnoreCase) ? "漫画" : JableParser.Source;

    /// <summary>取 HTML（带缓存）。同一地址同时被多处请求时只读一次。</summary>
    public Task<string> HtmlAsync(string url, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (_cache.TryGetValue(url, out var hit) && now - hit.At < CacheFor && !hit.Html.IsFaulted && !hit.Html.IsCanceled)
            return hit.Html.WaitAsync(ct);

        // 底层读取不跟随调用方取消：别的页面可能也在等同一份结果
        var task = ReaderFor(url).FetchHtmlAsync(url, SourceFor(url));
        if (Log is { } log)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _ = task.ContinueWith(t => log($"{sw.ElapsedMilliseconds,6} ms  {(t.IsCompletedSuccessfully ? t.Result.Length + " chars" : "FAILED")}  {url}"));
        }
        _cache[url] = (now, task);
        _ = task.ContinueWith(t => _cache.TryRemove(new KeyValuePair<string, (DateTime, Task<string>)>(url, (now, task))),
            TaskContinuationOptions.NotOnRanToCompletion);
        if (_cache.Count > CacheMax)
            foreach (var old in _cache.OrderBy(kv => kv.Value.At).Take(_cache.Count - CacheMax).ToList())
                _cache.TryRemove(old.Key, out _);
        return task.WaitAsync(ct);
    }

    /// <summary>预读（比如鼠标停在卡片上、或下一页），失败不报错。</summary>
    public void Prefetch(string url)
    {
        if (_cache.ContainsKey(url)) return;
        _ = HtmlAsync(url).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>启动时预热：两个浏览器先各自停到站点上（顺便缓存首页要用的列表），之后的读取都走 fetch。</summary>
    public void WarmUp()
    {
        Prefetch(JableUrls.Latest());
        Prefetch(MissAvUrls.List("new"));
    }

    // ---------------- 漫画（mycomic.com） ----------------

    public async Task<PageResult<ComicCard>> ComicListAsync(string pagedUrl, int page, CancellationToken ct = default)
    {
        var html = await HtmlAsync(pagedUrl, ct);
        return await Task.Run(() => new PageResult<ComicCard>(MyComicSite.ParseList(html), MyComicSite.HasNextPage(html, page)), ct);
    }

    public async Task<ComicDetail> ComicDetailAsync(int id, CancellationToken ct = default)
    {
        var html = await HtmlAsync(MyComicUrls.Comic(id), ct);
        return await Task.Run(() => MyComicSite.ParseDetail(html, id), ct);
    }

    public async Task<IReadOnlyList<ComicPage>> ComicPagesAsync(int chapterId, CancellationToken ct = default)
    {
        var html = await HtmlAsync(MyComicUrls.Chapter(chapterId), ct);
        return await Task.Run(() => MyComicSite.ParsePages(html), ct);
    }

    public async Task<PageResult<VideoCard>> JableListAsync(string pagedUrl, int page, CancellationToken ct = default)
    {
        var html = await HtmlAsync(pagedUrl, ct);
        return await Task.Run(() => new PageResult<VideoCard>(JableParser.ParseList(html), JableParser.HasNextPage(html, page)), ct);
    }

    public async Task<PageResult<VideoCard>> MissAvListAsync(string pagedUrl, int page, CancellationToken ct = default)
    {
        var html = await HtmlAsync(pagedUrl, ct);
        return await Task.Run(() => new PageResult<VideoCard>(MissAvParser.ParseList(html), MissAvParser.HasNextPage(html, page)), ct);
    }

    /// <summary>按地址自动选 Jable / MissAV 的列表解析。</summary>
    public Task<PageResult<VideoCard>> ListAsync(string pagedUrl, int page, CancellationToken ct = default) =>
        pagedUrl.Contains("missav", StringComparison.OrdinalIgnoreCase) ? MissAvListAsync(pagedUrl, page, ct) : JableListAsync(pagedUrl, page, ct);

    /// <summary>MissAV 女优一览（分页）/ 女优排行（一页 100 位）。</summary>
    public async Task<PageResult<MissAvActress>> MissAvActressesAsync(string path, int page, CancellationToken ct = default)
    {
        var html = await HtmlAsync(MissAvUrls.List(path, page), ct);
        return await Task.Run(() => new PageResult<MissAvActress>(MissAvParser.ParseActresses(html), MissAvParser.HasNextPage(html, page)), ct);
    }

    /// <summary>MissAV 类型（genres）/ 发行商（makers）目录。</summary>
    public async Task<PageResult<MissAvTerm>> MissAvTermsAsync(string kind, int page, CancellationToken ct = default)
    {
        var html = await HtmlAsync(MissAvUrls.List(kind, page), ct);
        return await Task.Run(() => new PageResult<MissAvTerm>(MissAvParser.ParseTerms(html, kind), MissAvParser.HasNextPage(html, page)), ct);
    }

    public async Task<VideoDetail> DetailAsync(VideoSource source, string pageUrl, CancellationToken ct = default)
    {
        var html = await HtmlAsync(pageUrl, ct);
        return await Task.Run(() => source == VideoSource.Jable
            ? JableParser.ParseVideo(html, pageUrl)
            : MissAvParser.ParseVideo(html, pageUrl), ct);
    }

    /// <summary>播放地址：Jable 在页面变量 hlsUrl 里；MissAV 在压缩脚本里（本地解包）。取不到返回 null。</summary>
    public async Task<string?> StreamUrlAsync(VideoCard card, CancellationToken ct = default)
    {
        try
        {
            var html = await HtmlAsync(card.PageUrl, ct);
            return await Task.Run(() => card.Source == VideoSource.Jable
                ? JableParser.ParseVideo(html, card.PageUrl).StreamUrl
                : MissAvParser.StreamUrl(html), ct);
        }
        catch (SourceException)
        {
            return null;
        }
    }

    /// <summary>
    /// 按番号找可播放的页面：先试 Jable 的 /videos/番号/，不行就在 Jable 搜，再不行试 MissAV。
    /// 都没有返回 null。
    /// </summary>
    public async Task<VideoDetail?> FindByCodeAsync(string code, CancellationToken ct = default)
    {
        try
        {
            return await DetailAsync(VideoSource.Jable, JableUrls.Video(code), ct);
        }
        catch (SourceException) { }

        try
        {
            var hits = await JableListAsync(JableUrls.Search(code), 1, ct);
            var hit = hits.Items.FirstOrDefault(c => VideoCode.Compact(c.Code) == VideoCode.Compact(code));
            if (hit is not null) return await DetailAsync(VideoSource.Jable, hit.PageUrl, ct);
        }
        catch (SourceException) { }

        try
        {
            var d = await DetailAsync(VideoSource.MissAv, MissAvUrls.Video(code), ct);
            if (VideoCode.Compact(d.Code) == VideoCode.Compact(code)) return d;
        }
        catch (SourceException) { }
        return null;
    }

    public async Task<IReadOnlyList<CategoryLink>> JableCategoriesAsync(CancellationToken ct = default)
    {
        var html = await HtmlAsync(JableUrls.Categories, ct);
        return await Task.Run(() => JableParser.ParseCategories(html), ct);
    }

    /// <summary>Jable 的标签分组（衣着、身材……），组名和标签名转成简体显示。</summary>
    public async Task<IReadOnlyList<TagGroup>> JableTagGroupsAsync(CancellationToken ct = default)
    {
        var html = await HtmlAsync(JableUrls.Categories, ct);
        return await Task.Run(() => JableParser.ParseTagGroups(html)
            .Select(g => new TagGroup(ChineseConverter.ToSimplified(g.Name),
                g.Tags.Select(t => t with { Name = ChineseConverter.ToSimplified(t.Name) }).ToArray()))
            .ToArray(), ct);
    }
}
