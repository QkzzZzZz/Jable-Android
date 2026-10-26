using Android.App;
using Android.Content.PM;
using Android.OS;

namespace JableLens.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        RequestHighRefreshRate();
    }

    /// <summary>
    /// 主动要屏幕支持的最高刷新率（同分辨率下）。澎湃OS / MIUI 对没有声明的应用默认只给 60Hz，
    /// 声明后滑动、动画能跑满 120Hz；省电模式下系统仍会自行降频。
    /// </summary>
    private void RequestHighRefreshRate()
    {
        try
        {
            var display = OperatingSystem.IsAndroidVersionAtLeast(30) ? Display : WindowManager?.DefaultDisplay;
            var window = Window;
            if (display?.GetMode() is not { } current || window?.Attributes is not { } attrs) return;
            var best = display.GetSupportedModes()?
                .Where(m => m.PhysicalWidth == current.PhysicalWidth && m.PhysicalHeight == current.PhysicalHeight)
                .MaxBy(m => m.RefreshRate);
            if (best is null || attrs.PreferredDisplayModeId == best.ModeId) return;
            attrs.PreferredDisplayModeId = best.ModeId;
            window.Attributes = attrs;
        }
        catch (Java.Lang.Exception) { }
    }
}
