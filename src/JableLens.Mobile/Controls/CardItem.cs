using JableLens.Core.Models;
using JableLens.Core.Sources.Live;
using JableLens.Core.Sources.MissAv;
using JableLens.Core.Sources.MyComic;
using JableLens.Mobile.Services;

namespace JableLens.Mobile.Controls;

/// <summary>网格 / 横排里的一项（作品 / 演员 / 分类 / 漫画 / 直播通用的显示模型）。Payload 是点开时要打开的东西。</summary>
public sealed class CardItem
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    /// <summary>标题下面那行灰字：番号 · 时长（或上市日）。</summary>
    public string? Meta { get; init; }
    public string? CoverUrl { get; init; }
    public string? IconUrl { get; init; }
    public string? Badge { get; init; }
    /// <summary>观看进度 0..1，0 不显示。</summary>
    public double Progress { get; init; }
    /// <summary>TOP 10 名次（1 起），0 表示不是排行卡。</summary>
    public int Rank { get; init; }
    public required object Payload { get; init; }

    public bool IsSkeleton { get; init; }

    private readonly long _created = Environment.TickCount64;
    private bool _shown;

    /// <summary>
    /// 这次上屏要不要播浮现动画：只有刚读到的内容第一次出现时才播。
    /// 滑动列表时复用的卡片、来回滑回来的卡片都不再播，快速滑动不掉帧。
    /// </summary>
    public bool TakeAppear()
    {
        if (_shown) return false;
        _shown = true;
        return Environment.TickCount64 - _created < 1500;
    }

    public string? RankText => Rank > 0 ? $"TOP {Rank}" : null;
    public bool HasRank => Rank > 0;
    public bool HasBadge => !string.IsNullOrEmpty(Badge);
    public bool HasProgress => Progress > 0;
    public bool HasMeta => !string.IsNullOrEmpty(Meta);
    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);
    public string Initial => Title.Length > 0 ? Title[..1] : "?";

    public static CardItem Skeleton() => new() { Title = "", Payload = new object(), IsSkeleton = true };

    public static CardItem From(VideoCard v, string? badge = null)
    {
        var h = MobileServices.Library.GetHistory(v.PageUrl);
        return new CardItem
        {
            Title = Text.TitleOnly(v),
            Meta = v.Duration is { Length: > 0 } d ? $"{v.Code}  ·  {d}" : v.Code,
            CoverUrl = v.CoverUrl,
            Badge = badge,
            Progress = h?.IsUnfinished == true ? h.ProgressRatio : 0,
            Payload = v,
        };
    }

    public static CardItem From(CodedWork w, string? badge = null) => new()
    {
        Title = w.Title.StartsWith(w.Code, StringComparison.OrdinalIgnoreCase) ? w.Title[w.Code.Length..].TrimStart(' ', '-') : w.Title,
        Meta = w.Date is { } date ? $"{w.Code}  ·  {date:yyyy-MM-dd}" : w.Code,
        CoverUrl = w.CoverUrl,
        Badge = badge,
        Payload = w,
    };

    public static CardItem From(PerformerSummary p) => new() { Title = p.Name, Subtitle = $"{p.VideoCount} 部作品", IconUrl = p.IconUrl, Payload = p };

    public static CardItem From(MissAvActress a) => new() { Title = a.Name, Subtitle = a.Info, IconUrl = a.AvatarUrl, Payload = a };

    public static CardItem From(MissAvTerm t) => new() { Title = t.Name, Subtitle = t.VideoCount is { } n ? $"{n:N0} 部影片" : null, Payload = t };

    public static CardItem From(ComicCard c) => new() { Title = c.Title, Meta = c.Latest, CoverUrl = c.CoverUrl, Payload = c };

    public static CardItem From(LiveModel m) => new()
    {
        Title = m.Username, Meta = $"{m.Viewers:N0} 人在看", CoverUrl = m.SnapshotUrl, IconUrl = m.AvatarUrl, Badge = m.IsNew ? "新人" : null, Payload = m,
    };

    public static CardItem From(CategoryLink c) => new()
    {
        Title = c.Name, Subtitle = c.VideoCount is { } n ? $"{n} 部影片" : null, CoverUrl = c.ImageUrl, Payload = c,
    };
}

public static class Text
{
    /// <summary>标题去掉开头的番号。</summary>
    public static string TitleOnly(VideoCard c) =>
        c.Title.StartsWith(c.Code, StringComparison.OrdinalIgnoreCase) ? c.Title[c.Code.Length..].Trim(' ', '-', '　') : c.Title;

    /// <summary>封面地址的高清版本。</summary>
    public static string? HiRes(VideoCard c) => c.Source == VideoSource.Jable
        ? Core.Sources.Jable.JableUrls.HiResCover(c.CoverUrl)
        : c.CoverUrl?.Replace("cover-t.jpg", "cover-n.jpg");

    public static string Clock(double seconds) => TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
}
