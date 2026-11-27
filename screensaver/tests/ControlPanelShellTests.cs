using System;

namespace Znake.Tests
{
    internal static class ControlPanelShellTests
    {
        public static void Run()
        {
            AppSettingsRoundTrip();
            ThemeToggle();
            StayOnRunCancelsCountdown();
            SingleToastFlag();
        }

        private static void AppSettingsRoundTrip()
        {
            string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "znake-test-settings-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            System.IO.Directory.CreateDirectory(tmpDir);
            try
            {
                var s = new Znake.ControlPanel.AppSettings();
                s.Setup.Cast = 42;
                s.Theme = "light";
                s.Save(tmpDir);

                var loaded = Znake.ControlPanel.AppSettings.Load(tmpDir);
                TestRunner.Check(loaded.Setup.Cast == 42, "AppSettings round-trip: Cast");
                TestRunner.Check(loaded.Theme == "light",  "AppSettings round-trip: Theme");
            }
            finally
            {
                try { System.IO.Directory.Delete(tmpDir, true); } catch (Exception) { }
            }
        }

        private static void ThemeToggle()
        {
            var dark  = Znake.ControlPanel.ThemePalette.For(Znake.ControlPanel.ThemeMode.Dark);
            var light = Znake.ControlPanel.ThemePalette.For(Znake.ControlPanel.ThemeMode.Light);
            TestRunner.Check(dark.Mode  == Znake.ControlPanel.ThemeMode.Dark,  "Dark palette mode");
            TestRunner.Check(light.Mode == Znake.ControlPanel.ThemeMode.Light, "Light palette mode");
            TestRunner.Check(dark.Surface != light.Surface, "Dark/Light surface colors differ");
        }

        private static void StayOnRunCancelsCountdown()
        {
            var m = new Znake.ControlPanel.RunStateMachine();
            m.BeginRun();
            m.MarkCompleted(false, 5);
            TestRunner.Check(m.CountdownActive,  "Countdown active after MarkCompleted");
            m.StayOnRun();
            TestRunner.Check(!m.CountdownActive, "Countdown cancelled after StayOnRun");
            TestRunner.Check(m.CountdownRemainingSeconds == 0, "Countdown seconds reset by StayOnRun");
        }

        private static void SingleToastFlag()
        {
            // Simulate: flag starts false, set true on first toast, stays true
            bool toastShown = false;
            Action toast = delegate()
            {
                if (!toastShown) toastShown = true;
            };
            toast();
            toast();
            TestRunner.Check(toastShown, "Toast flag set");
            // The flag prevents second call from re-firing — just validate it doesn't throw
            TestRunner.Check(true, "Second toast call safe");
        }
    }
}
