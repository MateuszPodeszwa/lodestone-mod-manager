using Velopack;

namespace Lodestone.App;

/// <summary>
/// Process entry point. <see cref="VelopackApp"/> must run first so install/update/uninstall hooks
/// are handled (and the process exits) before any WPF UI spins up; in a normal launch it returns
/// immediately and we hand off to the generated WPF <c>App.Main</c>.
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();
        App.Main();
    }
}
