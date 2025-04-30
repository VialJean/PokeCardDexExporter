using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using PokeScanner.Services;

namespace PokeScanner
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkitCamera()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif
            builder.Services.AddSingleton<ImageComparerService>();
            builder.Services.AddSingleton<ReferenceCardCacheService>();
            builder.Services.AddSingleton<MainViewModel>();

            return builder.Build();
        }
    }
}
