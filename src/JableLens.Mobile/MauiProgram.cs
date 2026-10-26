using JableLens.Mobile.Controls;

namespace JableLens.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureMauiHandlers(h =>
            {
                h.AddHandler<NativeHost, NativeHostHandler>();
                h.AddHandler<CoverImage, CoverImageHandler>();
            });
        return builder.Build();
    }
}
