using System.Runtime.CompilerServices;
using Android.Animation;
using Android.Views.Animations;
using AView = Android.Views.View;

namespace JableLens.Mobile.Controls;

/// <summary>
/// 全局动画：和 Windows 版一样的苹果式弹性（浮现、弹出、骨架呼吸）。
/// 直接用安卓原生动画（Animator，每帧由系统驱动、不回调 C#），几十张卡片同时动也不占主线程；
/// 动画只改原生视图，结束时都回到原样（不透明、无位移、原大小），和 MAUI 记录的属性一致。
/// </summary>
public static class Motion
{
    private static readonly ConditionalWeakTable<AView, Animator> Running = new();

    /// <summary>MAUI 控件对应的原生视图（有阴影 / 裁剪时外面还包了一层，动外面那层）。</summary>
    private static AView? Native(VisualElement v) =>
        v.Handler is IViewHandler h ? (h.ContainerView ?? h.PlatformView) as AView : null;

    // 轻微过冲后回稳，接近阻尼 0.7 的弹簧
    private static ITimeInterpolator Spring() => new OvershootInterpolator(1.15f);
    private static ITimeInterpolator Out() => new DecelerateInterpolator(1.6f);

    private static ObjectAnimator Prop(AView view, string name, float from, float to, long ms, ITimeInterpolator ease)
    {
        var a = ObjectAnimator.OfFloat(view, name, from, to)!;
        a.SetDuration(ms);
        a.SetInterpolator(ease);
        return a;
    }

    /// <summary>同时播放几段属性动画，返回结束（或被打断）时完成的 Task。</summary>
    private static Task Play(AView view, params Animator[] parts)
    {
        if (Running.TryGetValue(view, out var old)) old.Cancel();
        var set = new AnimatorSet();
        set.PlayTogether(parts);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        set.AnimationEnd += (_, _) => { Running.Remove(view); tcs.TrySetResult(); };
        Running.AddOrUpdate(view, set);
        // 动画期间用硬件层：透明度变化只是合成，不重画内容
        view.SetLayerType(Android.Views.LayerType.Hardware, null);
        set.AnimationEnd += (_, _) => view.SetLayerType(Android.Views.LayerType.None, null);
        set.Start();
        return tcs.Task;
    }

    /// <summary>原生视图还没建好时，等建好再做（列表里的卡片先绑数据、后上屏）。</summary>
    private static void WhenNative(VisualElement v, Action<AView> run)
    {
        if (Native(v) is { } now) { run(now); return; }
        void Once(object? s, EventArgs e)
        {
            v.HandlerChanged -= Once;
            if (Native(v) is { } n) run(n);
        }
        v.HandlerChanged += Once;
    }

    /// <summary>卡片浮现：淡入 + 从下方弹上来。</summary>
    public static void Appear(VisualElement v) => WhenNative(v, n =>
    {
        n.Alpha = 0;
        n.TranslationY = Dp(14);
        _ = Play(n, Prop(n, "alpha", 0, 1, 280, Out()), Prop(n, "translationY", Dp(14), 0, 460, Spring()));
    });

    /// <summary>弹出：从略小弹到原大小 + 淡入（弹层、页面主要内容）。</summary>
    public static Task PopIn(VisualElement v, double from = 0.92)
    {
        if (Native(v) is not { } n) return Task.CompletedTask;
        var s = (float)from;
        n.Alpha = 0;
        n.ScaleX = n.ScaleY = s;
        return Play(n, Prop(n, "alpha", 0, 1, 200, Out()), Prop(n, "scaleX", s, 1, 460, Spring()), Prop(n, "scaleY", s, 1, 460, Spring()));
    }

    /// <summary>页面内容进入：淡入 + 上移。</summary>
    public static Task Enter(VisualElement v)
    {
        if (Native(v) is not { } n) return Task.CompletedTask;
        n.Alpha = 0;
        n.TranslationY = Dp(24);
        return Play(n, Prop(n, "alpha", 0, 1, 260, Out()), Prop(n, "translationY", Dp(24), 0, 480, Spring()));
    }

    /// <summary>淡到某个透明度（首页大图切换用）。</summary>
    public static Task Fade(VisualElement v, double to, long ms)
    {
        if (Native(v) is not { } n) return Task.CompletedTask;
        return Play(n, Prop(n, "alpha", n.Alpha, (float)to, ms, Out()));
    }

    /// <summary>点一下：轻压缩小再弹回。</summary>
    public static async Task Press(VisualElement v)
    {
        if (Native(v) is not { } n) return;
        await Play(n, Prop(n, "scaleX", 1, 0.95f, 70, Out()), Prop(n, "scaleY", 1, 0.95f, 70, Out()));
        _ = Play(n, Prop(n, "scaleX", n.ScaleX, 1, 380, Spring()), Prop(n, "scaleY", n.ScaleY, 1, 380, Spring()));
    }

    /// <summary>骨架屏：明暗缓慢呼吸；离开屏幕就停（列表里滑出去的不再占资源）。</summary>
    public static void Breathe(VisualElement v) => WhenNative(v, n =>
    {
        ObjectAnimator? a = null;
        void Start()
        {
            if (a is not null) return;
            a = ObjectAnimator.OfFloat(n, "alpha", 1f, 0.5f)!;
            a.SetDuration(800);
            a.SetInterpolator(new AccelerateDecelerateInterpolator());
            a.RepeatCount = ValueAnimator.Infinite;
            a.RepeatMode = ValueAnimatorRepeatMode.Reverse;
            a.Start();
        }
        void Stop()
        {
            a?.Cancel();
            a = null;
            n.Alpha = 1;
        }
        n.ViewAttachedToWindow += (_, _) => Start();
        n.ViewDetachedFromWindow += (_, _) => Stop();
        if (n.IsAttachedToWindow) Start();
    });

    private static readonly float Density = Android.App.Application.Context.Resources!.DisplayMetrics!.Density;

    private static float Dp(double dp) => (float)(dp * Density);
}
