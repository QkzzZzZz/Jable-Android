using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using JableLens.Mobile.Services;

namespace JableLens.Mobile.Controls;

/// <summary>
/// 网络图片：软件自己下载（走白名单 HttpClient，漫画图片要带 Referer），存进磁盘缓存，再交给系统解码显示。
/// 列表滚动时控件会被复用：地址变了就丢弃旧的结果。
/// </summary>
public class RemoteImage : Image
{
    public static readonly BindableProperty UrlProperty = BindableProperty.Create(
        nameof(Url), typeof(string), typeof(RemoteImage), null, propertyChanged: (b, _, n) => ((RemoteImage)b).Load(n as string));

    public string? Url
    {
        get => (string?)GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    public RemoteImage() => Aspect = Aspect.AspectFill;

    private async void Load(string? url)
    {
        if (string.IsNullOrEmpty(url)) { Source = null; return; }
        var pending = ImageCache.GetAsync(url);
        // 已缓存：直接换图（不先清空，少一次重绘）；没缓存：先清掉旧图，免得复用的卡片显示别人的封面
        if (!pending.IsCompleted) Source = null;
        var file = await pending;
        if (file is not null && Url == url) Source = ImageSource.FromFile(file);
    }
}

/// <summary>图片磁盘缓存（同一地址同时只下载一次，最多 6 个同时下载）。</summary>
public static class ImageCache
{
    private static readonly SemaphoreSlim Downloads = new(6);
    private static readonly ConcurrentDictionary<string, Task<string?>> Inflight = new();
    private const long MaxBytes = 400L << 20;

    public static string Folder => Path.Combine(FileSystem.CacheDirectory, "img");

    public static Task<string?> GetAsync(string url)
    {
        var file = Path.Combine(Folder, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))) + ".img");
        if (File.Exists(file)) return Task.FromResult<string?>(file);
        return Inflight.GetOrAdd(url, _ => DownloadAsync(url, file).ContinueWith(t =>
        {
            Inflight.TryRemove(url, out var _);
            return t.IsCompletedSuccessfully ? t.Result : null;
        }));
    }

    private static async Task<string?> DownloadAsync(string url, string file)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !Core.Http.HostAllowListHandler.IsAllowed(uri)) return null;
        await Downloads.WaitAsync().ConfigureAwait(false);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            if (uri.Host.EndsWith("biccam.com", StringComparison.OrdinalIgnoreCase)) req.Headers.Referrer = new Uri("https://mycomic.com/");
            using var resp = await MobileServices.Http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            Directory.CreateDirectory(Folder);
            var tmp = file + "." + Guid.NewGuid().ToString("N")[..6];
            await File.WriteAllBytesAsync(tmp, bytes).ConfigureAwait(false);
            File.Move(tmp, file, true);
            return file;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            Downloads.Release();
        }
    }

    /// <summary>超过上限时删掉最久没用的一半。</summary>
    public static void Trim()
    {
        try
        {
            var dir = new DirectoryInfo(Folder);
            if (!dir.Exists) return;
            var files = dir.GetFiles().OrderBy(f => f.LastAccessTimeUtc).ToList();
            var total = files.Sum(f => f.Length);
            if (total < MaxBytes) return;
            foreach (var f in files)
            {
                if (total < MaxBytes / 2) break;
                total -= f.Length;
                f.Delete();
            }
        }
        catch (IOException) { }
    }

    public static void Clear()
    {
        Bitmaps.Clear();
        try { Directory.Delete(Folder, true); }
        catch (IOException) { }
    }
}
