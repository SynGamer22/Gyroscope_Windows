using Avalonia;
using Avalonia.Markup.Xaml;

namespace MotionLinkVR.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
