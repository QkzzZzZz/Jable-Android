using System.Security.Cryptography;
using System.Text.Json;
using JableLens.Core.Http;
using JableLens.Core.Sites;
using AWebView = Android.Webkit.WebView;

namespace JableLens.Mobile.Platforms.Android;

/// <summary>
/// 用系统 WebView 读有 Cloudflare 防护的站点（和 Windows 版的 WebPageReader 同一套做法）：
/// <list type="bullet">
/// <item>导航读取：打开页面，每 200ms 执行「就绪脚本」，拿到结果就停止加载。遇到人机验证时把浏览器显示出来让用户自己通过。</item>
/// <item>fetch 读取：浏览器已经停在该站点的页面上时，直接在页面里 fetch 目标地址（带同一份 cookie，和用户点链接一样）。
/// 结果先放在页面变量里，这边轮询取走。</item>
/// </list>
/// fetch 可以同时进行多个；导航一次只能一个。
/// </summary>
public sealed class WebReader(string name) : IPageReader
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(35);
    public static readonly TimeSpan ChallengeTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(25);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private AWebView? _view;
    private string? _url;
    private int _generation;
    private Task<string>? _landing;

    public string Name => name;

    private AWebView View
    {
        get
        {
            if (_view is not null) return _view;
            var view = WebKit.Create(Platform.CurrentActivity!);
            view.SetWebViewClient(new WebKit.FilterClient(lightweight: true, navigating: () => _generation++));
            WebKit.Parking.Park(view);
            return _view = view;
        }
    }

    private sealed class RetryFetchException : Exception;

    [System.Diagnostics.Conditional("DEBUG")]
    private void Debug(string text) => global::Android.Util.Log.Info("JableReader", $"[{name}] {text}");

    public async Task<string> FetchHtmlAsync(string url, string sourceName, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            // 有导航正在进行就等它结束（导航会打断 fetch）
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            _gate.Release();

            var onOrigin = await MainThread.InvokeOnMainThreadAsync(() => SameOrigin(View.Url ?? _url, url)).ConfigureAwait(false);
            if (!onOrigin)
            {
                // 第一次访问这个站：只让一个请求完整打开页面（顺便过验证），同时到的其它请求等它完成后再 fetch
                Task<string> landing;
                bool mine;
                lock (_gate)
                {
                    mine = _landing is null || _landing.IsCompleted;
                    if (mine) _landing = ReadHtmlAsync(url, sourceName);
                    landing = _landing!;
                }
                if (mine) return await landing.WaitAsync(ct).ConfigureAwait(false);
                try { await landing.WaitAsync(ct).ConfigureAwait(false); }
                catch (SourceException) { }
                continue;
            }

            string json;
            try
            {
                json = await FetchInPageAsync(url, ct).WaitAsync(FetchTimeout, ct).ConfigureAwait(false);
            }
            catch (RetryFetchException)
            {
                continue;
            }
            catch (TimeoutException)
            {
                return await ReadHtmlAsync(url, sourceName, ct).ConfigureAwait(false);
            }

            var (status, body) = await Task.Run(() => ParseFetchResult(json), ct).ConfigureAwait(false);
            if (status == 404) throw new SourceException(sourceName, "找不到（404）");
            if (status is 0 or 403 or 429 or 503 || PageScripts.LooksLikeChallenge(body))
                return await ReadHtmlAsync(url, sourceName, ct); // 被拦了：交给浏览器正常打开，必要时让用户验证
            if (status >= 400) throw new SourceException(sourceName, $"服务器返回 {status}");
            return body;
        }
        return await ReadHtmlAsync(url, sourceName, ct);
    }

    /// <summary>
    /// 在页面里 fetch，结果存进 window.__jlr[id]，这边轮询取走。页面跳转了就重试。
    /// 结果是对象（不先 JSON.stringify）：只编码一次，体积减半；解码在后台线程，主线程只负责传话。
    /// 轮询间隔从 50ms 逐步放宽到 250ms，首页十几行同时加载时主线程不会被轮询挤满。
    /// </summary>
    private async Task<string> FetchInPageAsync(string url, CancellationToken ct)
    {
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var gen = _generation;
        var start = $$"""
            (() => {
              window.__jlr = window.__jlr || {};
              const id = '{{id}}';
              fetch({{JsonSerializer.Serialize(url)}}, { credentials: 'include' })
                .then(async r => { window.__jlr[id] = { status: r.status, body: await r.text() }; })
                .catch(e => { window.__jlr[id] = { status: 0, body: String(e) }; });
              return 0;
            })()
            """;
        await MainThread.InvokeOnMainThreadAsync(() => WebKit.EvalAsync(View, start)).ConfigureAwait(false);
        var take = $"(() => {{ const r = window.__jlr && window.__jlr['{id}']; if (r) delete window.__jlr['{id}']; return r || null; }})()";
        var delay = 50;
        while (true)
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
            delay = Math.Min(250, delay + 30);
            if (gen != _generation) throw new RetryFetchException();
            var raw = await MainThread.InvokeOnMainThreadAsync(() => WebKit.EvalAsync(View, take)).ConfigureAwait(false);
            if (raw != "null") return raw;
        }
    }

    private static (int Status, string Body) ParseFetchResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return (root.GetProperty("status").GetInt32(), root.GetProperty("body").GetString() ?? "");
    }

    private static bool SameOrigin(string? current, string target) =>
        Uri.TryCreate(current, UriKind.Absolute, out var a) && Uri.TryCreate(target, UriKind.Absolute, out var b)
        && a.Scheme == b.Scheme && a.Host.Equals(b.Host, StringComparison.OrdinalIgnoreCase);

    public async Task<string> ReadHtmlAsync(string url, string sourceName, CancellationToken ct = default)
    {
        var raw = await ReadAsync(url, PageScripts.FullHtmlWhenReadyJs, sourceName, null, ct).ConfigureAwait(false);
        return await Task.Run(() => JsonSerializer.Deserialize<string>(raw)!, ct).ConfigureAwait(false);
    }

    public async Task<string> ReadAsync(string url, string readyScript, string sourceName, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var view = View;
                _url = url;
                Debug($"navigate {url}");
                view.LoadUrl(url);

                var normal = timeout ?? DefaultTimeout;
                var deadline = DateTime.UtcNow + normal;
                var inChallenge = false;
                try
                {
                    while (true)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            view.StopLoading();
                            ct.ThrowIfCancellationRequested();
                        }
                        await Task.Delay(PollInterval, CancellationToken.None);

                        var raw = await WebKit.EvalAsync(view, $"(() => {{ try {{ return ({readyScript}); }} catch (e) {{ return null; }} }})()");
                        if (raw != "null" && raw != "undefined")
                        {
                            Debug($"ready {url} ({raw.Length} chars)");
                            view.StopLoading();
                            return raw;
                        }

                        var challenge = await WebKit.EvalAsync(view, PageScripts.IsChallengeJs) == "true";
                        if (challenge != inChallenge) Debug($"challenge={challenge} {view.Url}");
                        if (challenge != inChallenge)
                        {
                            inChallenge = challenge;
                            deadline = DateTime.UtcNow + (challenge ? ChallengeTimeout : normal);
                            WebKit.Parking.Show(view, challenge);
                        }

                        if (DateTime.UtcNow > deadline)
                        {
                            view.StopLoading();
                            throw new SourceException(sourceName, inChallenge
                                ? "人机验证没有通过（超时），请重试"
                                : "页面加载超时，或页面结构变了读不到内容");
                        }
                    }
                }
                finally
                {
                    if (inChallenge) WebKit.Parking.Show(view, false);
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
