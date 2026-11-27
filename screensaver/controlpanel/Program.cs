using System;
using System.Windows.Forms;

namespace Znake.ControlPanel
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string dataRoot = AppSettings.DefaultDataRoot();
            AppSettings settings = AppSettings.Load(dataRoot);
            ThemePalette palette = ThemePalette.For(
                String.Equals(settings.Theme, "light", StringComparison.OrdinalIgnoreCase)
                    ? ThemeMode.Light : ThemeMode.Dark);

            using (var form = new MainForm(dataRoot, settings, palette))
                Application.Run(form);
        }
    }
}
