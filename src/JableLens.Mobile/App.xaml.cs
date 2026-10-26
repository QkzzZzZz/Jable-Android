namespace JableLens.Mobile;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(new AppShell());

    protected override void OnSleep()
    {
        base.OnSleep();
        // 切到后台时把本地数据和 cookie 写盘（手机随时可能杀掉后台应用）
        if (Services.MobileServices.Library is { } lib) _ = lib.FlushAsync();
        Android.Webkit.CookieManager.Instance?.Flush();
    }
}
