using System.Windows;
using MiliastraArchiveTool.Localization;

namespace MiliastraArchiveTool;

public partial class App : Application
{
    public static AppLocalization Localization { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Infrastructure.DiagnosticLogRetention.TryClean();
    }
}
