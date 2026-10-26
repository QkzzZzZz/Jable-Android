using Android.Views;
using Android.Webkit;
using Android.Widget;
using JableLens.Core.Sites;
using AWebView = Android.Webkit.WebView;

namespace JableLens.Mobile.Platforms.Android;

/// <summary>系统 WebView 的公共设置：只放行白名单主机（顺带挡掉广告和统计脚本），cookie 持久保存。</summary>
public static class WebKit
{
    /// <summary>在 WebView 里执行一段 JS，返回结果的 JSON 文本（null / undefined 都是 "null"）。必须在主线程调用。</summary>
    public static Task<string> EvalAsync(AWebView view, string script)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        view.EvaluateJavascript(script, new Callback(v => tcs.TrySetResult(v ?? "null")));
        return tcs.Task;
    }

    private sealed class Callback(Action<string?> done) : Java.Lang.Object, IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value) => done(value?.ToString());
    }

    public static AWebView Create(global::Android.Content.Context context, bool media = false)
    {
        var view = new AWebView(context);
        var s = view.Settings;
        s.JavaScriptEnabled = true;
        s.DomStorageEnabled = true;
        s.MediaPlaybackRequiresUserGesture = !media;
        s.SetSupportMultipleWindows(false);
        s.JavaScriptCanOpenWindowsAutomatically = false;
        s.LoadWithOverviewMode = true;
        s.UseWideViewPort = true;
        s.SaveFormData = false;
        CookieManager.Instance!.SetAcceptCookie(true);
        CookieManager.Instance.SetAcceptThirdPartyCookies(view, true); // Cloudflare 验证在 iframe 里
        view.SetBackgroundColor(global::Android.Graphics.Color.Black);
        return view;
    }

    /// <summary>页面的 console 输出写到 logcat（标签 JablePage），排查播放器问题用。</summary>
    public sealed class LoggingChrome : WebChromeClient
    {
        public override bool OnConsoleMessage(ConsoleMessage? m)
        {
            if (m is not null) global::Android.Util.Log.Info("JablePage", $"{m.InvokeMessageLevel()} {m.Message()} @{m.SourceId()}:{m.LineNumber()}");
            return true;
        }
    }

    public static WebResourceResponse Blocked() =>
        new("text/plain", "utf-8", 403, "Blocked", new Dictionary<string, string>(), new MemoryStream());

    public static WebResourceResponse Text(string body, string type) =>
        new(type, "utf-8", 200, "OK", new Dictionary<string, string> { ["Cache-Control"] = "no-store", ["Access-Control-Allow-Origin"] = "*" },
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body)));

    /// <summary>读图片 / 视频 / 字体这类只读 HTML 用不上的请求（Cloudflare 验证页的除外）。</summary>
    public static bool IsHeavy(IWebResourceRequest r)
    {
        var host = r.Url?.Host ?? "";
        if (host.EndsWith("cloudflare.com", StringComparison.OrdinalIgnoreCase)) return false;
        var path = r.Url?.Path?.ToLowerInvariant() ?? "";
        if (path.EndsWith(".jpg") || path.EndsWith(".jpeg") || path.EndsWith(".png") || path.EndsWith(".gif") || path.EndsWith(".webp")
            || path.EndsWith(".mp4") || path.EndsWith(".m3u8") || path.EndsWith(".ts") || path.EndsWith(".woff") || path.EndsWith(".woff2") || path.EndsWith(".ttf"))
            return true;
        return r.RequestHeaders?.TryGetValue("Accept", out var accept) == true && accept.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 白名单过滤的 WebViewClient。<paramref name="serve"/> 可以对某些请求直接本地应答（播放器、下载页）。
    /// 页面开始跳转时调用 <paramref name="navigating"/>（正在进行的页面内 fetch 会被打断）。
    /// </summary>
    public sealed class FilterClient(bool lightweight, ISet<string>? extraHosts = null,
        Func<IWebResourceRequest, WebResourceResponse?>? serve = null, Action? navigating = null, Action<AWebView>? finished = null) : WebViewClient
    {
        public override WebResourceResponse? ShouldInterceptRequest(AWebView? view, IWebResourceRequest? request)
        {
            if (request?.Url is not { } url) return null;
            if (serve?.Invoke(request) is { } own) return own;
            var scheme = url.Scheme ?? "";
            if (scheme != "http" && scheme != "https") return null;
            var host = url.Host ?? "";
            var ok = PageScripts.IsAllowedHost(host) || (extraHosts?.Contains(host) ?? false);
            if (ok && lightweight && IsHeavy(request)) ok = false;
            return ok ? null : Blocked();
        }

        public override bool ShouldOverrideUrlLoading(AWebView? view, IWebResourceRequest? request)
        {
            var host = request?.Url?.Host ?? "";
            var scheme = request?.Url?.Scheme ?? "";
            // 只允许在白名单站点之间跳转；别的（广告、应用商店链接）一律不开
            return !(scheme.StartsWith("http") && (PageScripts.IsAllowedHost(host) || (extraHosts?.Contains(host) ?? false)));
        }

        public override void OnPageStarted(AWebView? view, string? url, global::Android.Graphics.Bitmap? favicon)
        {
            base.OnPageStarted(view, url, favicon);
            navigating?.Invoke();
        }

        public override void OnPageFinished(AWebView? view, string? url)
        {
            base.OnPageFinished(view, url);
            CookieManager.Instance?.Flush();
            if (view is not null) finished?.Invoke(view);
        }
    }

    /// <summary>
    /// 后台浏览器的「停车场」：一个铺满屏幕但不可见的容器，读网页用的 WebView 都放在这里（要在窗口里才能正常运行）。
    /// 遇到人机验证时把它显示出来，上面一条提示，用户在里面点完就自动继续。
    /// </summary>
    public static class Parking
    {
        private static FrameLayout? _root;
        private static TextView? _banner;
        private static readonly List<AWebView> Shown = [];

        public static FrameLayout Root
        {
            get
            {
                if (_root is not null) return _root;
                var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("界面还没准备好");
                _root = new FrameLayout(activity) { Visibility = ViewStates.Invisible };
                _root.SetBackgroundColor(global::Android.Graphics.Color.Black);
                _banner = new TextView(activity)
                {
                    Text = "需要人机验证：请在下面完成，完成后自动继续",
                    TextSize = 14,
                };
                _banner.SetTextColor(global::Android.Graphics.Color.White);
                _banner.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#E50914"));
                var pad = (int)(12 * activity.Resources!.DisplayMetrics!.Density);
                _banner.SetPadding(pad, pad, pad, pad);
                // 窗口铺满到状态栏下面：整块往下让出状态栏的高度
                var statusId = activity.Resources.GetIdentifier("status_bar_height", "dimen", "android");
                _root.SetPadding(0, statusId > 0 ? activity.Resources.GetDimensionPixelSize(statusId) : 0, 0, 0);
                activity.AddContentView(_root, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
                return _root;
            }
        }

        public static void Park(AWebView view)
        {
            var bannerHeight = (int)(46 * Root.Context!.Resources!.DisplayMetrics!.Density);
            Root.AddView(view, 0, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) { TopMargin = bannerHeight });
        }

        /// <summary>
        /// 显示 / 隐藏验证用的浏览器。多个站同时要验证时一个一个来：
        /// 一个通过后把下一个还没通过的翻到最上面，全部通过才隐藏。
        /// </summary>
        public static void Show(AWebView view, bool show)
        {
            var root = Root;
            Shown.Remove(view);
            if (show) Shown.Add(view);
            if (Shown.Count == 0)
            {
                root.Visibility = ViewStates.Invisible;
                return;
            }
            Shown[^1].BringToFront();
            if (_banner!.Parent is null) root.AddView(_banner, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
            _banner.BringToFront();
            root.Visibility = ViewStates.Visible;
            root.BringToFront();
        }
    }
}
