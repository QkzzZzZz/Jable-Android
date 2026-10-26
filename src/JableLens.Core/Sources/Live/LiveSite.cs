using System.Text.Json;

namespace JableLens.Core.Sources.Live;

/// <summary>一位正在直播的主播。</summary>
public sealed record LiveModel(long Id, string Username, string? SnapshotUrl, string? AvatarUrl, int Viewers, bool IsNew, bool IsHd, bool IsMobile, string? Country)
{
    public string RoomUrl => LiveUrls.Room(Username);
}

/// <summary>直播列表的一栏：名字 + 网站接口里的筛选标签（null = 全部女主播）。</summary>
public sealed record LiveSection(string Name, string? Tag);

public static class LiveUrls
{
    public const string Base = "https://zh.myavlive.com";

    public static readonly IReadOnlyList<LiveSection> Sections =
    [
        new("中文", "tagLanguageChinese"),
        new("亚洲", "ethnicityAsian"),
        new("日本", "tagLanguageJapanese"),
        new("新主播", "autoTagNew"),
        new("热门", null),
        new("VR", "autoTagVr"),
    ];

    public const int PageSize = 60;

    /// <summary>网站列表页自己用的接口（返回 JSON，按热度排）。</summary>
    public static string Models(string? tag, int page)
    {
        var url = $"{Base}/api/front/models?limit={PageSize}&offset={(page - 1) * PageSize}&primaryTag=girls&sortBy=stripRanking";
        if (tag is not null) url += "&filterGroupTags=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { new[] { tag } }));
        return url;
    }

    public static string Room(string username) => $"{Base}/{Uri.EscapeDataString(username)}";
}

public static class LiveSite
{
    /// <summary>接口返回：{"models":[…],"filteredCount":243,…}。</summary>
    public static (IReadOnlyList<LiveModel> Models, int Total) ParseModels(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var total = root.TryGetProperty("filteredCount", out var fc) && fc.TryGetInt32(out var n) ? n : 0;
        if (!root.TryGetProperty("models", out var arr) || arr.ValueKind != JsonValueKind.Array) return ([], total);

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
        static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        var list = new List<LiveModel>();
        foreach (var m in arr.EnumerateArray())
        {
            if (Str(m, "username") is not { } user || !m.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id)) continue;
            // 列表封面：网站用的是 img.doppiocdn.org/snapshot/{id}/{时间戳}，时间戳一变就是新截图
            var ts = Str(m, "snapshotTimestamp") ?? (m.TryGetProperty("popularSnapshotTimestamp", out var p) ? p.ToString() : null);
            var snapshot = ts is null ? Str(m, "previewUrlThumbSmall") : $"https://img.doppiocdn.org/snapshot/{id}/{ts}";
            var viewers = m.TryGetProperty("viewersCount", out var vc) && vc.TryGetInt32(out var v) ? v : 0;
            list.Add(new LiveModel(id, user, snapshot, Str(m, "avatarUrl"), viewers, Bool(m, "isNew"), Bool(m, "isHd"), Bool(m, "isMobile"), Str(m, "country")));
        }
        return (list, total);
    }
}
