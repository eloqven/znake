using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Znake
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            string argument = args.Length == 0 ? "/c" : args[0].ToLowerInvariant();
            string[] option = argument.Split(new[] { ':' }, 2);
            string value = option.Length == 2 ? option[1] : (args.Length > 1 ? args[1] : null);
            IntPtr parent = ParseWindow(value);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            switch (option[0].Replace('-', '/'))
            {
                case "/s":
                    if (WindowsDesktop.IsCurrent()) RunScreen();
                    break;
                case "/p":
                    if (WindowsDesktop.IsWindow(parent))
                        using (var preview = new Preview(parent)) Application.Run(preview);
                    break;
                case "/c":
                    using (var settings = new SettingsDialog(ProfileDirectory()))
                    {
                        if (WindowsDesktop.IsWindow(parent))
                            settings.ShowDialog(new WindowOwner(parent));
                        else
                            settings.ShowDialog();
                    }
                    break;
            }
        }

        private static void RunScreen()
        {
            try
            {
                string profile = ProfileDirectory();
                SaverSettings settings = SaverSettings.Load(profile);
                using (StateStore store = StateStore.Open(profile))
                {
                    if (store == null) return;
                    World world = store.Load(DateTime.UtcNow);
                    using (var overlay = new ScreenOverlay(store, settings, world))
                        Application.Run(overlay);
                }
            }
            catch (IOException error) { Report(error); }
            catch (InvalidDataException error) { Report(error); }
            catch (UnauthorizedAccessException error) { Report(error); }
        }

        private static void Report(Exception error)
        {
            Trace.TraceWarning(error.Message);
#if ZNAKE_SMOKE
            SmokeTrace.Error(error.Message);
#endif
        }

        private static string ProfileDirectory()
        {
#if ZNAKE_SMOKE
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(directory, "smoke-profiles",
                File.ReadAllText(Path.Combine(directory, "windows-smoke-profile.txt")).Trim());
#else
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Znake");
#endif
        }

        private static IntPtr ParseWindow(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return IntPtr.Zero;

            long handle;
            bool hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (hex) value = value.Substring(2);
            if (!Int64.TryParse(value, hex ? NumberStyles.HexNumber : NumberStyles.Integer,
                CultureInfo.InvariantCulture, out handle))
                return IntPtr.Zero;
            if (IntPtr.Size == 4 && (handle < Int32.MinValue || handle > UInt32.MaxValue))
                return IntPtr.Zero;
            return IntPtr.Size == 4 ? new IntPtr(unchecked((int)handle)) : new IntPtr(handle);
        }

        private sealed class WindowOwner : IWin32Window
        {
            public WindowOwner(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; private set; }
        }
    }

    internal sealed class Preview : Form
    {
        private readonly IntPtr parent;
        private readonly Timer timer = new Timer { Interval = 80 };
        private readonly Stopwatch clock = new Stopwatch();

        public Preview(IntPtr parent)
        {
            this.parent = parent;
            TopLevel = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(23, 32, 28);
            DoubleBuffered = true;
            timer.Tick += delegate
            {
                if (!WindowsDesktop.IsWindow(parent))
                    Close();
                else
                {
                    ResizeToParent();
                    Invalidate();
                }
            };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            bool attached = WindowsDesktop.AttachPreview(Handle, parent);
#if ZNAKE_SMOKE
            SmokeTrace.Preview(attached);
#endif
            if (!attached)
            {
                Close();
                return;
            }
            ResizeToParent();
            clock.Start();
            timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int size = Math.Max(4, Math.Min(ClientSize.Height / 4, ClientSize.Width / 16));
            double path = 0.5 + Math.Sin(clock.Elapsed.TotalSeconds * 1.25) * 0.28;
            int head = (int)(ClientSize.Width * path + 2 * size);
            int top = (ClientSize.Height - size) / 2;
            using (var brush = new SolidBrush(Rgb.StartingGreen.ToColor(1)))
                for (int segment = 0; segment < 5; segment++)
                    e.Graphics.FillRectangle(brush, head - segment * size, top, size - 1, size - 1);
        }

        private void ResizeToParent()
        {
            Size next = WindowsDesktop.ClientSize(parent);
            if (ClientSize != next) SetBounds(0, 0, next.Width, next.Height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
