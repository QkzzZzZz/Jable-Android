using System.Text.Json;
using JableLens.Core.Sources.Live;

namespace JableLens.Core.Sites;

/// <summary>
/// 直播列表：后台浏览器直接打开网站自己的列表接口（返回 JSON），一次一页。
/// 不走 HttpClient：网站有 Cloudflare，普通请求会被拒。
/// </summary>
public sealed class LiveClient(IPageReader reader)
{
    public async Task<PageResult<LiveModel>> ModelsAsync(string? tag, int page, CancellationToken ct = default)
    {
        var raw = await reader.ReadAsync(LiveUrls.Models(tag, page), PageScripts.JsonWhenReadyJs, "直播", ct: ct);
        return await Task.Run(() =>
        {
            var (models, total) = LiveSite.ParseModels(JsonSerializer.Deserialize<string>(raw)!);
            return new PageResult<LiveModel>(models, page * LiveUrls.PageSize < total && models.Count > 0);
        }, ct);
    }
}
