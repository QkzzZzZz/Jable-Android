using JableLens.Core.Http;
using JableLens.Core.Library;
using JableLens.Core.Names;
using JableLens.Core.Sites;
using JableLens.Core.Sources.Av01;
using JableLens.Core.Sources.Netflav;
using JableLens.Core.Sources.R18;
using JableLens.Core.Subtitles;
using JableLens.Mobile.Platforms.Android;

namespace JableLens.Mobile.Services;

/// <summary>全局服务（和 Windows 版的 AppServices 对应，站点读取 / 推荐 / 下载都是共用的那一套）。</summary>
public static class MobileServices
{
    public static HttpClient Http { get; } = HttpClients.Create();
    public static NetflavClient Netflav { get; } = new(Http);
    public static Av01Client Av01 { get; } = new(Http);
    public static R18Client R18 { get; } = new(Http);
    public static SubtitleCatClient Subtitles { get; } = new(Http);
    public static PerformerNameResolver Names { get; } = new(Netflav, Av01, PerformerNameResolver.DefaultStore());

    public static UserLibrary Library { get; private set; } = null!;
    public static SiteClient Sites { get; private set; } = null!;
    public static LiveClient Live { get; private set; } = null!;
    public static RecommendationService Recommend { get; private set; } = null!;
    public static DownloadEngine Downloads { get; private set; } = null!;
    public static NewWorkChecker Checker { get; private set; } = null!;

    private static Task? _init;

    /// <summary>读本地数据、建好各站的后台浏览器。第一页显示前调用一次（之后再调直接返回）。</summary>
    public static Task InitAsync() => _init ??= InitCoreAsync();

    private static async Task InitCoreAsync()
    {
        Library = await UserLibrary.LoadAsync();
        // 每个站一个后台浏览器（可以同时读），共用同一份 cookie
        Sites = new SiteClient(new WebReader("Jable"), new WebReader("MissAV"), new WebReader("漫画"));
        Live = new LiveClient(new WebReader("直播"));
        Recommend = new RecommendationService(Sites, Library);
        Checker = new NewWorkChecker(Netflav, Library);
        Downloads = new DownloadEngine(Library, Sites, new WebStreamFetcher(), () => MediaFiles.WorkFolder)
        {
            Publish = MediaFiles.PublishAsync,
        };
        Downloads.Start();
        Sites.WarmUp();
        _ = Task.Run(Controls.ImageCache.Trim);
        _ = CheckNewWorksIfDueAsync();
    }

    /// <summary>启动时：距离上次检查超过设定的间隔，就检查一遍关注演员的新作（手机上不常驻后台）。</summary>
    private static async Task CheckNewWorksIfDueAsync()
    {
        var (interval, last) = Library.Read(d => (d.Settings.CheckIntervalMinutes, d.Follows.Select(f => f.LastChecked).Min()));
        if (Library.Read(d => d.Follows.Count) == 0) return;
        if (last is { } t && DateTimeOffset.Now - t < TimeSpan.FromMinutes(interval)) return;
        try { await Checker.CheckAllAsync(); }
        catch (Exception e) when (e is SourceException or OperationCanceledException) { }
    }
}
