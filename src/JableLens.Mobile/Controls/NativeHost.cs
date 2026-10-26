using Android.Content;
using Android.Widget;
using Microsoft.Maui.Handlers;
using AView = Android.Views.View;

namespace JableLens.Mobile.Controls;

/// <summary>把一个原生安卓控件（播放器、直播间用的 WebView）放进 MAUI 页面。</summary>
public sealed class NativeHost(Func<Context, AView> factory) : View
{
    public Func<Context, AView> Factory => factory;

    public AView? Native { get; internal set; }
}

/// <summary>
/// 子控件总是铺满自己。MAUI 量尺寸时给的高度是「不限」，WebView 这时会量出 0 高；
/// 所以摆放时按实际大小再量一次。
/// </summary>
public sealed class FillLayout(Context context) : FrameLayout(context)
{
    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
        int w = right - left, h = bottom - top;
        for (var i = 0; i < ChildCount; i++)
        {
            var child = GetChildAt(i)!;
            child.Measure(MeasureSpec.MakeMeasureSpec(w, Android.Views.MeasureSpecMode.Exactly), MeasureSpec.MakeMeasureSpec(h, Android.Views.MeasureSpecMode.Exactly));
            child.Layout(0, 0, w, h);
        }
    }
}

public sealed class NativeHostHandler() : ViewHandler<NativeHost, FrameLayout>(Mapper)
{
    public static readonly PropertyMapper<NativeHost, NativeHostHandler> Mapper = new(ViewMapper);

    protected override FrameLayout CreatePlatformView()
    {
        var frame = new FillLayout(Context);
        var native = VirtualView.Factory(Context);
        VirtualView.Native = native;
        frame.AddView(native, new FrameLayout.LayoutParams(FrameLayout.LayoutParams.MatchParent, FrameLayout.LayoutParams.MatchParent));
        return frame;
    }
}
