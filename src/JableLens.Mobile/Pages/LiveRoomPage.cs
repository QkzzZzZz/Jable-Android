using Android.Content.PM;
using Android.Webkit;
using JableLens.Core.Sources.Live;
using JableLens.Mobile.Controls;
using JableLens.Mobile.Platforms.Android;
using AWebView = Android.Webkit.WebView;

namespace JableLens.Mobile.Pages;

/// <summary>
/// 直播间：只看视频（和 Windows 版一样）。用真实的直播间网页播放（视频流是网站自己的播放器解出来的，软件不碰），
/// 注入一段样式把网页上除了视频以外的东西全部藏起来。第一次进会先显示网站自己的「已满 18 岁」确认。
/// 横过手机就是全屏。
/// </summary>
public sealed class LiveRoomPage : ContentPage
{
    private const string IsolateJs = """
        (() => {
          if (window.__jlIso) return; window.__jlIso = true;
          const css = `
            html, body { background: #000 !important; }
            body:not(.jl-gate):not(.jl-iso) { opacity: 0 !important; }
            body.jl-iso { overflow: hidden !important; }
            body.jl-iso * { visibility: hidden !important; }
            body.jl-iso video.jl-v {
              visibility: visible !important; position: fixed !important; left: 0 !important; top: 0 !important;
              width: 100vw !important; height: 100vh !important; max-width: none !important; max-height: none !important;
              object-fit: contain !important; z-index: 2147483647 !important; background: #000 !important; transform: none !important;
            }`;
          const st = document.createElement('style');
          st.textContent = css;
          const add = () => (document.head || document.documentElement).appendChild(st);
          if (document.documentElement) add(); else document.addEventListener('DOMContentLoaded', add);
          window.__jlState = 'waiting';
          setInterval(() => {
            if (!document.body) return;
            const vids = [...document.querySelectorAll('video')].filter(v => v.videoWidth > 0);
            vids.sort((a, b) => b.videoWidth * b.videoHeight - a.videoWidth * a.videoHeight);
            const v = vids[0];
            if (v) {
              // 视频在隐藏的容器里（量出来 0 大小）：搬到 body 下面，样式才能让它铺满
              document.querySelectorAll('video.jl-v').forEach(x => { if (x !== v) x.classList.remove('jl-v'); });
              v.classList.add('jl-v');
              document.body.classList.add('jl-iso');
              document.body.classList.remove('jl-gate');
              window.__jlState = (v.muted || v.volume === 0) ? 'muted' : 'playing';
              return;
            }
            const gate = [...document.querySelectorAll('button')].some(b => /年满\s*18|18\s*岁|I am 18|over 18/i.test(b.textContent || ''));
            document.body.classList.toggle('jl-gate', gate);
            document.body.classList.remove('jl-iso');
            window.__jlState = gate ? 'gate' : 'waiting';
          }, 400);
          window.jlSetMuted = (m) => { const v = document.querySelector('video.jl-v'); if (v) { v.muted = m; if (!m && v.volume === 0) v.volume = 1; } };
        })();
        """;

    private readonly LiveModel _model;
    private readonly Label _status = new() { FontSize = 13, TextColor = Cards.C("Fg2") };
    private readonly Label _sound = new() { Text = "🔇 开声音", FontSize = 14, Padding = new Thickness(12, 8), IsVisible = false };
    private AWebView? _view;
    private IDispatcherTimer? _timer;
    private DateTime _waitingSince = DateTime.MaxValue;
    private string _state = "";
    private bool _dumped;

    public LiveRoomPage(LiveModel model)
    {
        _model = model;
        Title = model.Username;
        BackgroundColor = Colors.Black;
        var host = new NativeHost(ctx =>
        {
            var v = WebKit.Create(ctx, media: true);
            // 用电脑版网页（和 Windows 版看到的一样）：手机版把播放器藏起来、只放很小的预览流
            v.Settings.UserAgentString = Core.Http.HttpClients.UserAgent;
            v.SetWebViewClient(new RoomClient());
            v.SetWebChromeClient(new WebChromeClient());
            _view = v;
            return v;
        });
        Cards.Tap(_sound, () => Js($"window.jlSetMuted && window.jlSetMuted({(_state == "muted" ? "false" : "true")})"));

        var bar = new Grid { Padding = new Thickness(Ui.Gutter, 8), BackgroundColor = Cards.C("Bg"), ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        bar.Add(new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new HorizontalStackLayout { Spacing = 8, Children = { Cards.Pill("直播中", Cards.C("Accent")), new Label { Text = $"{model.Viewers:N0} 人在看", FontSize = 13, TextColor = Cards.C("Fg2"), VerticalOptions = LayoutOptions.Center } } },
                _status,
            },
        });
        bar.Add(_sound, 1);
        var grid = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        grid.Add(bar);
        grid.Add(host, 0, 1);
        Content = grid;
        _status.Text = "正在连接…";
    }

    /// <summary>直播间网页：只允许在直播站内部跳转；每次开始加载就注入「只看视频」的脚本。</summary>
    private sealed class RoomClient : WebViewClient
    {
        public override bool ShouldOverrideUrlLoading(AWebView? view, IWebResourceRequest? request) =>
            !(request?.Url?.Host?.EndsWith("myavlive.com", StringComparison.OrdinalIgnoreCase) ?? false);

        public override void OnPageStarted(AWebView? view, string? url, global::Android.Graphics.Bitmap? favicon)
        {
            base.OnPageStarted(view, url, favicon);
            view?.EvaluateJavascript(IsolateJs, null);
        }

        public override void OnPageFinished(AWebView? view, string? url)
        {
            base.OnPageFinished(view, url);
            view?.EvaluateJavascript(IsolateJs, null);
            CookieManager.Instance?.Flush();
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (Platform.CurrentActivity is { } a) a.RequestedOrientation = ScreenOrientation.Sensor;
        if (_timer is not null) return;
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(600);
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();
        Dispatcher.Dispatch(async () =>
        {
            while (_view is null) await Task.Delay(50);
            _view.LoadUrl(_model.RoomUrl);
        });
    }

    private void Js(string script) => _view?.EvaluateJavascript(script, null);

    private async Task PollAsync()
    {
        if (_view is null) return;
        var raw = await WebKit.EvalAsync(_view, "window.__jlState || null");
        var state = raw.Trim('"');
#if DEBUG
        var info = await WebKit.EvalAsync(_view, "(() => { const v = document.querySelector('video.jl-v'); if (!v) return null; const r = v.getBoundingClientRect(); const cs = getComputedStyle(v); return JSON.stringify({ w: v.videoWidth, h: v.videoHeight, t: v.currentTime, p: v.paused, r: [r.x, r.y, r.width, r.height], vis: cs.visibility, op: cs.opacity, n: document.querySelectorAll('video').length }); })()");
        global::Android.Util.Log.Info("JablePage", "live " + state + " " + info);
        if (!_dumped && state is "muted" or "playing")
        {
            _dumped = true;
            var chain = await WebKit.EvalAsync(_view, "(() => { const out = []; let e = document.querySelector('video'); while (e && e !== document.body) { const cs = getComputedStyle(e); const r = e.getBoundingClientRect(); out.push(e.tagName + '.' + (e.className || '').toString().slice(0, 60) + ' d=' + cs.display + ' ' + Math.round(r.width) + 'x' + Math.round(r.height)); e = e.parentElement; } return out.join(' < ') + ' || vw=' + innerWidth + ' ua=' + navigator.userAgent.slice(0, 60); })()");
            global::Android.Util.Log.Info("JablePage", "chain " + chain);
            var modals = await WebKit.EvalAsync(_view, "(() => [...document.querySelectorAll('[role=dialog], [class*=modal i], [class*=Modal]')].filter(e => e.getBoundingClientRect().width > 0).map(e => (e.className || '').toString().slice(0, 80) + ' :: ' + (e.innerText || '').replace(/\\s+/g, ' ').slice(0, 160)).join(' ## '))()");
            global::Android.Util.Log.Info("JablePage", "modals " + modals);
            var buttons = await WebKit.EvalAsync(_view, "(() => { const w = document.querySelector('[class*=ViewCamWrapper__video]'); const cls = [...(w ? w.querySelectorAll('*') : [])].map(e => (e.className || '').toString()).filter(c => /mode|overlay|lock|blur|private|offline|ticket|show/i.test(c)).slice(0, 15).join(' ; '); return 'text=' + (w ? w.textContent.replace(/\\s+/g, ' ').slice(0, 300) : '') + ' || cls=' + cls + ' || btns=' + [...document.querySelectorAll('button')].map(b => b.textContent.trim()).filter(t => t).slice(0, 25).join(' | '); })()");
            global::Android.Util.Log.Info("JablePage", "buttons " + buttons);
        }
#endif
        if (state == _state && state != "waiting") return;
        _state = state;
        switch (state)
        {
            case "playing":
            case "muted":
                _waitingSince = DateTime.MaxValue;
                _status.Text = "";
                _sound.IsVisible = true;
                _sound.Text = state == "muted" ? "🔇 开声音" : "🔊 静音";
                break;
            case "gate":
                _status.Text = "第一次进直播间：请在下面确认已满 18 岁";
                _sound.IsVisible = false;
                break;
            default:
                if (_waitingSince == DateTime.MaxValue) _waitingSince = DateTime.Now;
                _sound.IsVisible = false;
                _status.Text = DateTime.Now - _waitingSince > TimeSpan.FromSeconds(15) ? "现在没有公开画面（主播可能在私人表演或已下播）" : "正在连接…";
                break;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (Platform.CurrentActivity is { } a) a.RequestedOrientation = ScreenOrientation.Unspecified;
        if (Navigation.NavigationStack.Contains(this)) return;
        _timer?.Stop();
        _view?.LoadUrl("about:blank");
        _view?.Destroy();
    }
}
