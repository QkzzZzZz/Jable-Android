using JableLens.Core.Library;
using JableLens.Mobile.Controls;
using JableLens.Mobile.Platforms.Android;
using JableLens.Mobile.Services;
using Microsoft.Maui.Controls.Shapes;

namespace JableLens.Mobile.Pages;

/// <summary>「我的」：画像摘要 + 片单、观看记录、关注、下载、设置（Windows 版头像菜单里的东西）。</summary>
public sealed class MinePage : PageBase
{
    private readonly VerticalStackLayout _root = new() { Padding = new Thickness(0, 0, 0, 30) };

    public MinePage()
    {
        Title = "我的";
        Content = new ScrollView { Content = _root };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await MobileServices.InitAsync();
        Render();
    }

    private void Render()
    {
        _root.Clear();
        var p = MobileServices.Library.Read(d => Portrait.Build(d, DateTimeOffset.Now));
        var (favs, follows, newWorks, downloads) = MobileServices.Library.Read(d =>
            (d.Favorites.Count, d.Follows.Count, d.Follows.Sum(f => f.NewWorks.Count), d.Downloads.Count(r => r.State is DownloadState.Queued or DownloadState.Running)));

        // 画像卡片：一句话摘要 + 三个数字，点进去看完整画像
        var card = new Border
        {
            StrokeThickness = 1, Stroke = Color.FromArgb("#1AFFFFFF"), StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Background = new LinearGradientBrush([new GradientStop(Color.FromArgb("#3AE50914"), 0), new GradientStop(Color.FromArgb("#141414"), 1)], new Point(0, 0), new Point(1, 1)),
            Margin = new Thickness(Ui.Gutter, 12, Ui.Gutter, 8), Padding = new Thickness(18, 16),
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Label { Text = "我的画像 ›", FontSize = 18, FontAttributes = FontAttributes.Bold },
                    new Label { Text = PortraitPage.Summary(p), FontSize = 13.5, TextColor = Cards.C("Fg2") },
                    new HorizontalStackLayout
                    {
                        Spacing = 26,
                        Children = { Stat(PortraitPage.Hours(p.WatchedSeconds), "累计观看"), Stat($"{p.Works}", "看过"), Stat($"{p.Favorites}", "片单") },
                    },
                },
            },
        };
        Cards.Tap(card, () => _ = Nav.Push(new PortraitPage()));
        _root.Add(card);

        void Row(string text, string? detail, Func<Page> page)
        {
            _root.Add(Ui.MenuRow(text, detail, () => _ = Nav.Push(page())));
            _root.Add(Ui.Divider());
        }
        Row("我的片单", favs > 0 ? $"{favs} 部" : null, () => new FavoritesPage());
        Row("观看记录", p.Works > 0 ? $"{p.Works} 部" : null, () => new HistoryPage());
        Row("猜我喜欢", null, () => new GuessPage());
        Row("关注的演员", newWorks > 0 ? $"{newWorks} 部新作" : follows > 0 ? $"{follows} 位" : null, () => new FollowsPage());
        Row("下载", downloads > 0 ? $"{downloads} 个进行中" : null, () => new DownloadsPage());
        Row("设置", null, () => new SettingsPage());
        _ = Motion.Enter(_root);
    }

    private static View Stat(string value, string label) => new VerticalStackLayout
    {
        Children = { new Label { Text = value, FontSize = 18, FontAttributes = FontAttributes.Bold }, new Label { Text = label, FontSize = 11.5, TextColor = Cards.C("Fg3") } },
    };
}

/// <summary>下载列表：进度、暂停 / 继续、删除，下好的点一下用手机上的播放器打开。</summary>
public sealed class DownloadsPage : PageBase
{
    private readonly VerticalStackLayout _list = new() { Spacing = 10, Padding = new Thickness(Ui.Gutter, 10, Ui.Gutter, 40) };

    public DownloadsPage()
    {
        Title = "下载";
        Content = new ScrollView { Content = new VerticalStackLayout { Ui.Muted("下好的视频在手机的「下载 / Jable」文件夹").WithMargin(new Thickness(Ui.Gutter, 10, Ui.Gutter, 0)), _list } };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        MobileServices.Downloads.Changed += OnChanged;
        Render();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        MobileServices.Downloads.Changed -= OnChanged;
    }

    private void OnChanged() => MainThread.BeginInvokeOnMainThread(Render);

    private void Render()
    {
        _list.Clear();
        var all = MobileServices.Downloads.All;
        if (all.Count == 0)
        {
            _list.Add(Ui.Muted("还没有下载。在作品详情或播放器里点 ↓ 下载。"));
            return;
        }
        foreach (var r in all) _list.Add(Row(r));
    }

    private View Row(DownloadRecord r)
    {
        var ratio = r.Total > 0 ? (double)r.Done / r.Total : 0;
        var status = r.State switch
        {
            DownloadState.Queued => "排队中",
            DownloadState.Running when r.Total == 0 => "正在读取播放列表…",
            DownloadState.Running => $"{ratio:P0}  ·  {r.Bytes / 1048576.0:0} MB",
            DownloadState.Paused => $"已暂停  {ratio:P0}",
            DownloadState.Done => $"已完成  ·  {r.Bytes / 1048576.0:0} MB  ·  点一下播放",
            _ => "失败：" + r.Error,
        };
        var cover = new Border { WidthRequest = 112, HeightRequest = 63, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 8 }, BackgroundColor = Cards.C("Card"), Content = new RemoteImage { Url = r.Card.CoverUrl } };
        var bar = new Grid { HeightRequest = 3, BackgroundColor = Color.FromArgb("#404040"), IsVisible = r.State is DownloadState.Running or DownloadState.Paused };
        var fill = new BoxView { Color = Cards.C("Accent"), HorizontalOptions = LayoutOptions.Start };
        bar.Add(fill);
        bar.SizeChanged += (_, _) => fill.WidthRequest = bar.Width * ratio;
        var text = new VerticalStackLayout
        {
            Spacing = 4, VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = $"{r.Card.Code}  {Text.TitleOnly(r.Card)}", FontSize = 13.5, MaxLines = 2, LineBreakMode = LineBreakMode.TailTruncation },
                new Label { Text = status, FontSize = 12, TextColor = r.State == DownloadState.Failed ? Color.FromArgb("#FF6B6B") : Cards.C("Fg3") },
                bar,
            },
        };
        var row = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        row.Add(cover);
        row.Add(text, 1);
        Cards.Tap(row, async () =>
        {
            if (r.State == DownloadState.Done && r.FilePath is { } path) { MediaFiles.Open(path); return; }
            var actions = new List<string>();
            if (r.State is DownloadState.Queued or DownloadState.Running) actions.Add("暂停");
            if (r.State is DownloadState.Paused or DownloadState.Failed) actions.Add("继续");
            actions.Add("删除");
            var pick = await Shell.Current.DisplayActionSheetAsync(r.Card.Code, "取消", null, [.. actions]);
            if (pick == "暂停") MobileServices.Downloads.Pause(r.Id);
            else if (pick == "继续") MobileServices.Downloads.Resume(r.Id);
            else if (pick == "删除") MobileServices.Downloads.Remove(r.Id, deleteFile: false);
        });
        // 下好的：左滑删除（可以连文件一起删）
        if (r.State == DownloadState.Done)
        {
            var del = new SwipeItem { Text = "删除", BackgroundColor = Cards.C("Accent") };
            del.Invoked += async (_, _) =>
            {
                var withFile = await Nav.Confirm("删除下载", "连视频文件一起删除吗？", "删除文件");
                if (withFile && r.FilePath is { } f) MediaFiles.Delete(f);
                MobileServices.Downloads.Remove(r.Id, deleteFile: false);
            };
            return new SwipeView { RightItems = [del], Content = row };
        }
        return row;
    }
}

/// <summary>设置：字幕、检查新作间隔、缓存、数据位置。</summary>
public sealed class SettingsPage : PageBase
{
    private static readonly int[] Intervals = [30, 60, 180, 360];
    private static readonly (string Code, string Name)[] Languages = [("zh-CN", "简体中文"), ("zh-TW", "繁体中文"), ("en", "English"), ("ja", "日本語")];

    public SettingsPage()
    {
        Title = "设置";
        var lib = MobileServices.Library;
        var s = lib.Read(d => (d.Settings.CheckIntervalMinutes, d.Subtitles.PreferredLanguage, d.Subtitles.Enabled));
        var root = new VerticalStackLayout { Padding = new Thickness(0, 0, 0, 40) };

        root.Add(Ui.H2("字幕"));
        root.Add(Ui.Muted("默认").WithMargin(new Thickness(Ui.Gutter, 0, Ui.Gutter, 6)));
        root.Add(Ui.Tabs(["显示", "不显示"], s.Enabled ? 0 : 1, i => lib.Mutate(d => d.Subtitles.Enabled = i == 0)));
        root.Add(Ui.Muted("优先语言").WithMargin(new Thickness(Ui.Gutter, 8, Ui.Gutter, 6)));
        root.Add(Ui.Tabs(Languages.Select(l => l.Name).ToList(), Math.Max(0, Array.FindIndex(Languages, l => l.Code == s.PreferredLanguage)),
            i => lib.Mutate(d => d.Subtitles.PreferredLanguage = Languages[i].Code)));

        root.Add(Ui.H2("关注"));
        root.Add(Ui.Muted("打开软件时，距离上次检查超过这么久就检查一次新作").WithMargin(new Thickness(Ui.Gutter, 0, Ui.Gutter, 6)));
        root.Add(Ui.Tabs(Intervals.Select(m => m < 60 ? $"{m} 分钟" : $"{m / 60} 小时").ToList(), Math.Max(0, Array.IndexOf(Intervals, s.CheckIntervalMinutes)),
            i => lib.Mutate(d => d.Settings.CheckIntervalMinutes = Intervals[i])));

        root.Add(Ui.H2("数据"));
        root.Add(Ui.Muted("观看记录、片单、关注都只存在这台手机上，不会上传。").WithMargin(new Thickness(Ui.Gutter, 0, Ui.Gutter, 10)));
        var clear = new HorizontalStackLayout { Spacing = 10, Padding = new Thickness(Ui.Gutter, 0) };
        clear.Add(Ui.Action("清空图片缓存", () => { ImageCache.Clear(); Nav.Toast("已清空"); }, primary: false));
        clear.Add(Ui.Action("清除网站登录状态", async () =>
        {
            if (!await Nav.Confirm("清除网站登录状态", "会清掉人机验证的记录，下次打开网站可能要重新验证。")) return;
            global::Android.Webkit.CookieManager.Instance?.RemoveAllCookies(null);
            Nav.Toast("已清除");
        }, primary: false));
        root.Add(clear);
        root.Add(Ui.Muted($"版本 {AppInfo.Current.VersionString}").WithMargin(new Thickness(Ui.Gutter, 30, Ui.Gutter, 0)));
        Content = new ScrollView { Content = root };
    }
}

public static class ViewExtensions
{
    public static T WithMargin<T>(this T view, Thickness margin) where T : View
    {
        view.Margin = margin;
        return view;
    }
}
