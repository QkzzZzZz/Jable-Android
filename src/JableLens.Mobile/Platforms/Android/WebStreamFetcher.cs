using System.Text.Json;
using Android.Webkit;
using JableLens.Core.Http;
using JableLens.Core.Sites;
using AWebView = Android.Webkit.WebView;

namespace JableLens.Mobile.Platforms.Android;

/// <summary>
/// 下载用的后台浏览器：停在作品页同一个网址上（本地应答一个只有下载脚本的空白页，同源），
/// 页面里 fetch 播放列表 / 分片，转成 base64 放进页面变量，这边轮询取走再解码。
/// </summary>
public sealed class WebStreamFetcher : IStreamFetcher
{
    private const string PageJs = """
        window.__jld = {};
        window.JLD = {
          get(url, id) {
            fetch(url)
              .then(r => { if (!r.ok) throw new Error('HTTP ' + r.status); return r.blob(); })
              .then(b => new Promise((ok, fail) => { const f = new FileReader(); f.onload = () => ok(f.result); f.onerror = fail; f.readAsDataURL(b); }))
              .then(d => { window.__jld[id] = { data: d.substring(d.indexOf(',') + 1) }; })
              .catch(e => { window.__jld[id] = { error: String(e && e.message || e) }; });
          }
        };
        """;

    private readonly HashSet<string> _extraHosts = new(StringComparer.OrdinalIgnoreCase);
    private AWebView? _view;
    private string? _pageUrl;
    private TaskCompletionSource? _pageReady;

    public void AllowHost(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var u)) lock (_extraHosts) _extraHosts.Add(u.Host);
    }

    public async Task PrepareAsync(string pageUrl, string streamUrl, CancellationToken ct)
    {
        AllowHost(streamUrl);
        var origin = new Uri(pageUrl).GetLeftPart(UriPartial.Authority);
        if (_pageUrl is not null && new Uri(_pageUrl).GetLeftPart(UriPartial.Authority) == origin) return;
        _pageUrl = pageUrl;
        _pageReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_view is null)
            {
                _view = WebKit.Create(Platform.CurrentActivity!);
                _view.SetWebViewClient(new WebKit.FilterClient(lightweight: false, _extraHosts, Serve, finished: _ => _pageReady?.TrySetResult()));
                WebKit.Parking.Park(_view);
            }
            _view.LoadUrl(pageUrl);
        });
        await _pageReady.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
    }

    private WebResourceResponse? Serve(IWebResourceRequest r)
    {
        if (_pageUrl is null || !r.IsForMainFrame || r.Url is not { } u) return null;
        var page = new Uri(_pageUrl);
        if (!string.Equals(u.Host, page.Host, StringComparison.OrdinalIgnoreCase) || u.Path != page.AbsolutePath) return null;
        return WebKit.Text("<!doctype html><html><head><meta charset=\"utf-8\"></head><body><script>" + PageJs + "</script></body></html>", "text/html");
    }

    public async Task<byte[]> FetchAsync(string url, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        await MainThread.InvokeOnMainThreadAsync(() => WebKit.EvalAsync(_view!, $"window.JLD && window.JLD.get({JsonSerializer.Serialize(url)}, '{id}')"));
        var take = $"(() => {{ const r = window.__jld && window.__jld['{id}']; if (r) delete window.__jld['{id}']; return r ? JSON.stringify(r) : null; }})()";
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (true)
        {
            await Task.Delay(100, ct);
            var raw = await MainThread.InvokeOnMainThreadAsync(() => WebKit.EvalAsync(_view!, take));
            if (raw != "null")
            {
                return await Task.Run(() =>
                {
                    using var doc = JsonDocument.Parse(JsonSerializer.Deserialize<string>(raw)!);
                    if (doc.RootElement.TryGetProperty("error", out var err)) throw new SourceException("下载", err.GetString() ?? "失败");
                    return Convert.FromBase64String(doc.RootElement.GetProperty("data").GetString()!);
                }, ct);
            }
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
        }
    }
}
