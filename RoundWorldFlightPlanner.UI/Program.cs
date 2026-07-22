using RoundWorldFlightPlanner.Data;
using RoundWorldFlightPlanner.UI.Forms;

namespace RoundWorldFlightPlanner.UI;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Safety net: an unhandled exception on the UI thread (e.g. a data lookup gap) would otherwise
        // crash the whole app with no explanation. Show what happened and keep the app running instead.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowError(e.ExceptionObject as Exception);

        ApplicationConfiguration.Initialize();

        var context = DbContextFactory.Create(AppPaths.DatabasePath);
        AirportSeedImporter.EnsureSeeded(context);

        Application.Run(new MainForm(context));
    }

    private static void ShowError(Exception? ex)
    {
        MessageBox.Show(
            $"Something went wrong and this action didn't complete:\n\n{ex?.Message}\n\nIf this keeps happening, note what you were doing when it occurred.",
            "Unexpected Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
