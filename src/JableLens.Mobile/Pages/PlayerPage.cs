using System.Text.Json;
using System.Text.Json.Nodes;
using Android.Content.PM;
using Android.Webkit;
using AndroidX.Core.View;
using JableLens.Core.Http;
using JableLens.Core.Models;
using JableLens.Core.Sites;
using JableLens.Mobile.Controls;
using JableLens.Mobile.Platforms.Android;
using JableLens.Mobile.Services;
using AWebView = Android.Webkit.WebView;

namespace JableLens.Mobile.Pages;

/// <summary>
/// 播放（和 Windows 版同一种做法）：浏览器打开作品页同一个网址，但页面由软件本地应答成自己的播放器（同源，视频请求能通过 Cloudflare）。
/// 横屏全屏；进度每 5 秒记一次，下次从这里继续；字幕由软件下载后交给页面叠加显示。
/// </summary>
public sealed class PlayerPage : ContentPage
{
    private const string Prefix = "/__jablelens/";

    private static readonly Dictionary<string, string> LanguageNames = new()
    {
        ["zh-CN"] = "简体中文", ["zh-TW"] = "繁体中文", ["en"] = "英语", ["ja"] = "日语", ["ko"] = "韩语",
        ["th"] = "泰语", ["vi"] = "越南语", ["id"] = "印尼语", ["es"] = "西班牙语", ["fr"] = "法语", ["de"] = "德语", ["ru"] = "俄语",
    };

    private readonly VideoCard _card;
    private readonly CancellationTokenSource _life = new();
    private readonly HashSet<string> _extraHosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Label _status = new() { TextColor = Colors.White, FontSize = 15, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(30) };
    private readonly NativeHost _host;
    private readonly List<SubtitleTrack> _tracks = [];
    private AWebView? _view;
    private string? _syntheticHtml;
    private bool _started;
    private double _lastPosition;
    private double _lastDuration;
    private IDispatcherTimer? _pump;

    public PlayerPage(VideoCard card)
    {
        _card = card;
        BackgroundColor = Colors.Black;
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);
        _host = new NativeHost(ctx =>
        {
            var v = WebKit.Create(ctx, media: true);
            v.SetWebViewClient(new WebKit.FilterClient(lightweight: false, _extraHosts, Serve));
            v.SetWebChromeClient(new WebKit.LoggingChrome());
            v.Visibility = global::Android.Views.ViewStates.Invisible;
            _view = v;
            return v;
        });
        _status.Text = "正在打开播放页…";
        Content = new Grid { _status, _host };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SetFullscreen(true);
        if (_pump is not null) return;
        _pump = Dispatcher.CreateTimer();
        _pump.Interval = TimeSpan.FromMilliseconds(400);
        _pump.Tick += async (_, _) => await DrainMessagesAsync();
        _pump.Start();
        await StartAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SetFullscreen(false);
        if (Navigation.NavigationStack.Contains(this)) return; // 只是被盖住
        _life.Cancel();
        _pump?.Stop();
        if (_started && _lastDuration > 0) MobileServices.Library.RecordProgress(_card, _lastPosition, _lastDuration);
        _view?.StopLoading();
        _view?.LoadUrl("about:blank");
        _view?.Destroy();
    }

    protected override bool OnBackButtonPressed()
    {
        _ = Shell.Current.Navigation.PopAsync();
        return true;
    }

    /// <summary>横屏 + 隐藏状态栏和导航条；离开时恢复。</summary>
    private static void SetFullscreen(bool on)
    {
        var activity = Platform.CurrentActivity;
        if (activity?.Window is not { } window) return;
        activity.RequestedOrientation = on ? ScreenOrientation.SensorLandscape : ScreenOrientation.Unspecified;
        var controller = WindowCompat.GetInsetsController(window, window.DecorView);
        if (on)
        {
            controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
            controller.Hide(WindowInsetsCompat.Type.SystemBars());
            window.AddFlags(global::Android.Views.WindowManagerFlags.KeepScreenOn);
        }
        else
        {
            controller.Show(WindowInsetsCompat.Type.SystemBars());
            window.ClearFlags(global::Android.Views.WindowManagerFlags.KeepScreenOn);
        }
    }

    private async Task StartAsync()
    {
        var ct = _life.Token;
        try
        {
            await MobileServices.InitAsync();
            var quickUrl = await MobileServices.Sites.StreamUrlAsync(_card, ct);
            while (_view is null) await Task.Delay(50, ct); // 等原生控件建好
            if (quickUrl is not null)
            {
                // 快速路径：拦下播放页的请求，直接返回自己的播放器页面（同一个网址 = 同源）
                AddHost(quickUrl);
                _syntheticHtml = await SyntheticPageAsync(quickUrl);
                _view.LoadUrl(_card.PageUrl);
                return; // 页面里的 JL.start 完成后会发 started 消息
            }

            // 慢速路径：打开站点真实的播放页，等页面脚本生成播放地址（遇到人机验证时页面就在眼前，直接点）
            _view.Visibility = global::Android.Views.ViewStates.Visible;
            _view.LoadUrl(_card.PageUrl);
            var url = await WaitForStreamUrlAsync(ct);
            AddHost(url);
            await WebKit.EvalAsync(_view, await HlsBootstrapAsync());
            await WebKit.EvalAsync(_view, await AssetAsync("player_mobile.js"));
            await WebKit.EvalAsync(_view, Shim + $"window.JL.start({Options(url, synthetic: false).ToJsonString()});");
        }
        catch (OperationCanceledException) { }
        catch (SourceException e)
        {
            if (_view is not null) _view.Visibility = global::Android.Views.ViewStates.Invisible;
            _status.Text = "无法播放：" + e.Message;
        }
    }

    private void AddHost(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var u)) lock (_extraHosts) _extraHosts.Add(u.Host);
    }

    /// <summary>页面里的 postMessage 先放进队列，这边每 0.4 秒取一次（和 Windows 版 chrome.webview.postMessage 同样的消息）。</summary>
    private const string Shim = "window.__jlq = window.__jlq || []; window.chrome = window.chrome || {}; window.chrome.webview = { postMessage: m => window.__jlq.push(m) };";

    private static async Task<string> AssetAsync(string name)
    {
        await using var s = await FileSystem.OpenAppPackageFileAsync(name);
        using var r = new StreamReader(s);
        return await r.ReadToEndAsync();
    }

    private static async Task<string> HlsBootstrapAsync() =>
        "(function(){var module=undefined,exports=undefined,define=undefined;" + await AssetAsync("hls.min.js") + "\n;window.JLHls=window.Hls;}).call(window);";

    private JsonObject Options(string url, bool synthetic)
    {
        var history = MobileServices.Library.GetHistory(_card.PageUrl);
        var prefs = MobileServices.Library.Read(d => (d.Settings.PlaybackRate, d.Subtitles.Enabled));
        return new JsonObject
        {
            ["url"] = url,
            ["startAt"] = history?.IsUnfinished == true ? history.Position : 0,
            ["rate"] = prefs.PlaybackRate,
            ["subtitlesEnabled"] = prefs.Enabled,
            ["offset"] = MobileServices.Library.SubtitleOffset(_card.Code),
            ["title"] = $"{_card.Code}  {Text.TitleOnly(_card)}",
            ["synthetic"] = synthetic,
        };
    }

    private Dictionary<string, string> _assets = [];

    private async Task<string> SyntheticPageAsync(string url)
    {
        var origin = new Uri(_card.PageUrl).GetLeftPart(UriPartial.Authority);
        _assets = new Dictionary<string, string>
        {
            [Prefix + "hls.js"] = await HlsBootstrapAsync(),
            [Prefix + "player.js"] = await AssetAsync("player_mobile.js"),
        };
        return $$"""
            <!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,user-scalable=no"><title>Jable</title>
            <style>html,body{margin:0;background:#000;height:100%;overflow:hidden}</style></head>
            <body><script>{{Shim}}</script><script src="{{origin}}{{Prefix}}hls.js"></script><script src="{{origin}}{{Prefix}}player.js"></script>
            <script>window.JL.start({{Options(url, synthetic: true).ToJsonString()}});</script></body></html>
            """;
    }

    /// <summary>把播放页地址和两个脚本地址的请求在本地应答，不发到网站。</summary>
    private WebResourceResponse? Serve(IWebResourceRequest r)
    {
        if (_syntheticHtml is null || r.Url is not { } u) return null;
        var page = new Uri(_card.PageUrl);
        if (!string.Equals(u.Host, page.Host, StringComparison.OrdinalIgnoreCase)) return null;
        if (r.IsForMainFrame && u.Path == page.AbsolutePath) return WebKit.Text(_syntheticHtml, "text/html");
        if (u.Path is { } p && _assets.TryGetValue(p, out var js)) return WebKit.Text(js, "text/javascript");
        return null;
    }

    /// <summary>等页面里出现播放地址。Jable：hlsUrl；MissAV：source（由页面的打包脚本生成）。</summary>
    private async Task<string> WaitForStreamUrlAsync(CancellationToken ct)
    {
        var script = _card.Source == VideoSource.Jable
            ? "typeof hlsUrl === 'string' && hlsUrl ? hlsUrl : null"
            : "typeof source === 'string' && source ? source : (typeof source1280 === 'string' ? source1280 : (typeof source842 === 'string' ? source842 : null))";
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(40);
        var challenge = false;
        while (true)
        {
            await Task.Delay(250, ct);
            var raw = await WebKit.EvalAsync(_view!, $"(() => {{ try {{ return ({script}); }} catch (e) {{ return null; }} }})()");
            var isChallenge = await WebKit.EvalAsync(_view!, PageScripts.IsChallengeJs) == "true";
            if (isChallenge != challenge)
            {
                challenge = isChallenge;
                _status.Text = challenge ? "" : "正在打开播放页…";
                if (challenge) deadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);
            }
            if (raw != "null" && JsonSerializer.Deserialize<string>(raw) is { Length: > 0 } url) return url;
            if (DateTime.UtcNow > deadline)
                throw new SourceException(_card.Source == VideoSource.Jable ? "Jable" : "MissAV",
                    challenge ? "人机验证没有通过（超时）" : "播放页里找不到视频地址（作品可能已下架，或页面结构变了）");
        }
    }

    // ---------------- 页面消息 ----------------

    private bool _draining;

    private async Task DrainMessagesAsync()
    {
        if (_view is null || _draining || _life.IsCancellationRequested) return;
        _draining = true;
        try
        {
            var raw = await WebKit.EvalAsync(_view, "(() => { const q = window.__jlq; if (!q || !q.length) return null; return JSON.stringify(q.splice(0)); })()");
            if (raw == "null") return;
            global::Android.Util.Log.Info("JablePage", "messages " + raw[..Math.Min(raw.Length, 300)]);
            var arr = JsonNode.Parse(JsonSerializer.Deserialize<string>(raw)!) as JsonArray;
            foreach (var msg in arr ?? []) OnMessage(msg);
        }
        catch (JsonException) { }
        finally
        {
            _draining = false;
        }
    }

    private void OnMessage(JsonNode? msg)
    {
        switch ((string?)msg?["type"])
        {
            case "started":
                if (_started) return;
                _started = true;
                _view!.Visibility = global::Android.Views.ViewStates.Visible;
                _ = LoadSubtitlesAsync(_life.Token);
                break;
            case "progress":
                _lastPosition = (double?)msg["position"] ?? 0;
                _lastDuration = (double?)msg["duration"] ?? 0;
                MobileServices.Library.RecordProgress(_card, _lastPosition, _lastDuration);
                break;
            case "offset":
                MobileServices.Library.SetSubtitleOffset(_card.Code, (double?)msg["value"] ?? 0);
                break;
            case "selectTrack":
                if ((int?)msg["id"] is { } id) _ = LoadTrackAsync(id);
                break;
            case "subsEnabled":
                MobileServices.Library.Mutate(d => d.Subtitles.Enabled = (bool?)msg["value"] ?? true);
                break;
            case "rate":
                MobileServices.Library.Mutate(d => d.Settings.PlaybackRate = (double?)msg["value"] ?? 1);
                break;
            case "download":
                var already = MobileServices.Downloads.Find(_card) is not null;
                if (!already) MobileServices.Downloads.Enqueue(_card);
                Js($"window.JL.toast({JsonSerializer.Serialize(already ? "已经在下载列表里了" : "已加入下载")})");
                break;
            case "close":
                _ = Shell.Current.Navigation.PopAsync();
                break;
            case "error":
                _status.Text = (string?)msg["message"] ?? "播放失败";
                _view!.Visibility = global::Android.Views.ViewStates.Invisible;
                break;
        }
    }

    private void Js(string script)
    {
        if (_view is not null) _ = WebKit.EvalAsync(_view, script);
    }

    // ---------------- 字幕 ----------------

    private async Task LoadSubtitlesAsync(CancellationToken ct)
    {
        try
        {
            var hits = await MobileServices.Subtitles.SearchAsync(_card.Code, ct);
            foreach (var h in hits.Take(4))
            {
                try { _tracks.AddRange(await MobileServices.Subtitles.GetTracksAsync(h, ct)); }
                catch (SourceException) { }
            }
        }
        catch (SourceException) { }
        catch (OperationCanceledException) { return; }
        if (ct.IsCancellationRequested) return;

        var multiSource = _tracks.Select(t => t.SourceLabel).Distinct().Count() > 1;
        var list = new JsonArray(_tracks.Select((t, i) => (JsonNode)new JsonObject
        {
            ["id"] = i,
            ["name"] = LanguageNames.GetValueOrDefault(t.LanguageCode, t.DisplayName) + (multiSource ? $" · {t.SourceLabel}" : ""),
        }).ToArray());
        var preferred = MobileServices.Library.Read(d => d.Subtitles.PreferredLanguage);
        int? pick = null;
        foreach (var lang in new[] { preferred, "zh-CN", "zh-TW", "en" })
        {
            var i = _tracks.FindIndex(t => t.LanguageCode.Equals(lang, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) { pick = i; break; }
        }
        if (pick is null && _tracks.Count > 0) pick = 0;
        Js($"window.JL.setTracks({list.ToJsonString()}, {(pick is null ? "null" : pick.Value.ToString())});");
        if (pick is { } p) await LoadTrackAsync(p);
    }

    private async Task LoadTrackAsync(int index)
    {
        if (index < 0 || index >= _tracks.Count) return;
        try
        {
            var cues = await MobileServices.Subtitles.DownloadAsync(_tracks[index], _life.Token);
            var arr = new JsonArray(cues.Select(c => (JsonNode)new JsonObject
            {
                ["s"] = Math.Round(c.Start.TotalSeconds, 3),
                ["e"] = Math.Round(c.End.TotalSeconds, 3),
                ["t"] = c.Text,
            }).ToArray());
            Js($"window.JL.setCues({arr.ToJsonString()}, {MobileServices.Library.SubtitleOffset(_card.Code).ToString(System.Globalization.CultureInfo.InvariantCulture)});");
        }
        catch (Exception e) when (e is SourceException or HttpRequestException or TaskCanceledException)
        {
            Js($"window.JL.toast({JsonSerializer.Serialize("字幕下载失败")})");
        }
    }
}
