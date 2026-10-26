using JableLens.Core.Models;
using JableLens.Core.Sources.Jable;
using JableLens.Core.Sources.Live;
using JableLens.Core.Sources.MissAv;
using JableLens.Core.Sources.MyComic;
using JableLens.Mobile.Controls;
using JableLens.Mobile.Services;

namespace JableLens.Mobile.Pages;

/// <summary>页面跳转（和 Windows 版 MainWindow.OpenCard / OpenPerformer 对应）。</summary>
public static class Nav
{
    private static INavigation Navigation => Shell.Current.Navigation;

    public static Task Push(Page page) => MainThread.InvokeOnMainThreadAsync(() => Navigation.PushAsync(page, true));

    public static void Open(CardItem item)
    {
        switch (item.Payload)
        {
            case VideoCard v: _ = Push(new VideoDetailPage(v)); break;
            case CodedWork w:
                // 关注演员的新作：点开就算看过了
                MobileServices.Library.Mutate(d =>
                {
                    foreach (var f in d.Follows) f.NewWorks.RemoveAll(x => Core.Text.VideoCode.Compact(x.Code) == Core.Text.VideoCode.Compact(w.Code));
                });
                _ = Push(VideoDetailPage.ForCode(w.Code, w));
                break;
            case PerformerSummary p: _ = Push(new PerformerPage(p.Name, p.IconUrl)); break;
            case CategoryLink c: _ = Push(GridPage.ForJableList(c.Name, c.Url)); break;
            case MissAvActress a: _ = Push(GridPage.ForMissAvList(a.Name, a.Url)); break;
            case MissAvTerm t: _ = Push(GridPage.ForMissAvList(t.Name, t.Url)); break;
            case ComicCard c: _ = Push(new ComicDetailPage(c.Id, c.Title, c.CoverUrl)); break;
            case LiveModel m: _ = Push(new LiveRoomPage(m)); break;
            case string performerName: _ = Push(new PerformerPage(performerName, item.IconUrl)); break;
        }
    }

    public static void OpenPerformer(string netflavName, string? iconUrl = null) => _ = Push(new PerformerPage(netflavName, iconUrl));

    /// <summary>Jable / MissAV 上的演员名先对应成 Netflav 的写法再打开。</summary>
    public static async void OpenSitePerformer(string name, string? code, int actorsInWork)
    {
        try
        {
            var match = await MobileServices.Names.ResolveAsync(name, code, actorsInWork);
            OpenPerformer(match?.NetflavName ?? name);
        }
        catch (Exception e) when (e is OperationCanceledException or Core.Http.SourceException)
        {
            OpenPerformer(name);
        }
    }

    /// <summary>标签 / 类型：MissAV 的类型是 MissAV 列表，Jable 的 /tags/ 和 /categories/ 是 Jable 列表。</summary>
    public static void OpenTag(TagLink tag)
    {
        if (tag.PageUrl is not { } url) return;
        _ = Push(url.Contains("missav", StringComparison.OrdinalIgnoreCase)
            ? GridPage.ForMissAvList(tag.Name, url)
            : GridPage.ForJableList(tag.Name, url, pinnable: url.Contains("/tags/") ? tag : null));
    }

    public static void Play(VideoCard card) => _ = Push(new PlayerPage(card));

    public static async Task<bool> Confirm(string title, string message, string ok = "确定") =>
        await Shell.Current.DisplayAlertAsync(title, message, ok, "取消");

    public static void Toast(string text) => MainThread.BeginInvokeOnMainThread(() =>
        global::Android.Widget.Toast.MakeText(Platform.CurrentActivity, text, global::Android.Widget.ToastLength.Short)?.Show());

    public static string JableListUrl(string url, int page) => JableUrls.WithPage(url, page);
}
