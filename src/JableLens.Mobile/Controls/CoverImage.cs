using Android.Content;
using Android.Graphics;
using Android.Widget;
using Microsoft.Maui.Handlers;
using AView = Android.Views.View;

namespace JableLens.Mobile.Controls;

/// <summary>
/// 列表卡片用的封面图（固定大小、裁切铺满）。专门为滑动优化，和通用的 <see cref="RemoteImage"/> 区别：
/// <list type="bullet">
/// <item>图片在后台线程按显示大小缩小解码，主线程只负责贴上去；</item>
/// <item>换图不触发重新排版（大小是固定的），滑动时不会因为某张图加载完整个列表重排一次；</item>
/// <item>解码好的图放在内存缓存里，滑回来瞬间显示。</item>
/// </list>
/// </summary>
public sealed class CoverImage : View
{
    public static readonly BindableProperty UrlProperty = BindableProperty.Create(nameof(Url), typeof(string), typeof(CoverImage));

    public string? Url
    {
        get => (string?)GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }
}

public sealed class CoverImageHandler() : ViewHandler<CoverImage, CoverImageView>(Mapper)
{
    public static readonly PropertyMapper<CoverImage, CoverImageHandler> Mapper = new(ViewMapper)
    {
        [nameof(CoverImage.Url)] = (h, v) => h.PlatformView.Load(v.Url),
    };

    protected override CoverImageView CreatePlatformView() => new(Context);

    /// <summary>大小完全由外面决定（铺满给的空间，或 WidthRequest / HeightRequest）。</summary>
    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
    {
        var v = VirtualView;
        var w = v.WidthRequest >= 0 ? v.WidthRequest : double.IsInfinity(widthConstraint) ? 0 : widthConstraint;
        var h = v.HeightRequest >= 0 ? v.HeightRequest : double.IsInfinity(heightConstraint) ? 0 : heightConstraint;
        return new Size(w, h);
    }
}

public sealed class CoverImageView : ImageView
{
    private string? _url;
    private int _token;
    private bool _quiet;

    public CoverImageView(Context context) : base(context) => SetScaleType(ScaleType.CenterCrop!);

    /// <summary>贴图时不向上请求重新排版（大小固定，排版结果不会变）。</summary>
    public override void RequestLayout()
    {
        if (!_quiet) base.RequestLayout();
    }

    private void Show(Bitmap? bitmap)
    {
        _quiet = true;
        try { SetImageBitmap(bitmap); }
        finally { _quiet = false; }
    }

    public async void Load(string? url)
    {
        if (url == _url) return;
        _url = url;
        var token = ++_token;
        Animate()?.Cancel();
        Alpha = 1;
        if (string.IsNullOrEmpty(url)) { Show(null); return; }

        var target = Width > 0 ? Width : (int)(220 * Resources!.DisplayMetrics!.Density);
        if (Bitmaps.Get(url, target) is { } hit) { Show(hit); return; }
        Show(null);

        var file = await ImageCache.GetAsync(url);
        if (file is null || token != _token) return;
        var bitmap = await Task.Run(() => Bitmaps.Decode(url, file, target));
        if (bitmap is null || token != _token) return;
        Show(bitmap);
        Alpha = 0;
        Animate()?.Alpha(1)?.SetDuration(160)?.Start();
    }
}

/// <summary>解码后的封面图内存缓存（按占用字节数淘汰最久没用的）。</summary>
internal static class Bitmaps
{
    private static readonly object Gate = new();
    private static readonly LinkedList<(string Key, Bitmap Bitmap)> Order = new();
    private static readonly Dictionary<string, LinkedListNode<(string Key, Bitmap Bitmap)>> Map = [];
    private static readonly long Budget = Math.Min(256L << 20, Java.Lang.Runtime.GetRuntime()!.MaxMemory() / 5);
    private static long _bytes;

    // 解码宽度按 2 的倍数分档，同一张图在不同大小的卡片里能共用
    private static string Key(string url, int target) => url + "@" + Bucket(target);

    private static int Bucket(int target) => target <= 360 ? 360 : target <= 720 ? 720 : 1440;

    public static Bitmap? Get(string url, int target)
    {
        lock (Gate)
        {
            if (!Map.TryGetValue(Key(url, target), out var node)) return null;
            Order.Remove(node);
            Order.AddFirst(node);
            return node.Value.Bitmap;
        }
    }

    public static Bitmap? Decode(string url, string file, int target)
    {
        try
        {
            var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(file, bounds);
            if (bounds.OutWidth <= 0) return null;
            var sample = 1;
            var want = Bucket(target);
            while (bounds.OutWidth / (sample * 2) >= want) sample *= 2;
            var bitmap = BitmapFactory.DecodeFile(file, new BitmapFactory.Options { InSampleSize = sample });
            if (bitmap is null) return null;
            bitmap.PrepareToDraw(); // 提前把图传给显卡，第一次画的时候不卡
            Put(Key(url, target), bitmap);
            return bitmap;
        }
        catch (Java.Lang.Throwable) { return null; }
        catch (IOException) { return null; }
    }

    private static void Put(string key, Bitmap bitmap)
    {
        lock (Gate)
        {
            if (Map.TryGetValue(key, out var old))
            {
                Order.Remove(old);
                _bytes -= old.Value.Bitmap.ByteCount;
            }
            var node = Order.AddFirst((key, bitmap));
            Map[key] = node;
            _bytes += bitmap.ByteCount;
            while (_bytes > Budget && Order.Last is { } last && last != node)
            {
                Order.RemoveLast();
                Map.Remove(last.Value.Key);
                _bytes -= last.Value.Bitmap.ByteCount;
            }
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Order.Clear();
            Map.Clear();
            _bytes = 0;
        }
    }
}

/// <summary>圆角 / 圆形：用系统的轮廓裁切（显卡完成），不像 Border 那样每帧在 CPU 上算裁切路径。</summary>
public static class Round
{
    public static T Corners<T>(T view, double radius) where T : VisualElement
    {
        void Apply()
        {
            if (view.Handler?.PlatformView is not AView n) return;
            n.OutlineProvider = new Outline((float)(radius * n.Resources!.DisplayMetrics!.Density), oval: false);
            n.ClipToOutline = true;
        }
        view.HandlerChanged += (_, _) => Apply();
        Apply();
        return view;
    }

    public static T Oval<T>(T view) where T : VisualElement
    {
        view.HandlerChanged += (_, _) =>
        {
            if (view.Handler?.PlatformView is not AView n) return;
            n.OutlineProvider = new Outline(0, oval: true);
            n.ClipToOutline = true;
        };
        return view;
    }

    private sealed class Outline(float radius, bool oval) : Android.Views.ViewOutlineProvider
    {
        public override void GetOutline(AView? view, Android.Graphics.Outline? outline)
        {
            if (view is null || outline is null) return;
            if (oval) outline.SetOval(0, 0, view.Width, view.Height);
            else outline.SetRoundRect(0, 0, view.Width, view.Height, radius);
        }
    }
}
