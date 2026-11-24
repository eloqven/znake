using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Znake
{
    internal sealed class Widget : NativeWindow, IDisposable
    {
        private const int HotkeyId = 1;
        private const int HotkeyMessage = 0x0312;
        private const int ToggleKey = 'Z';
        private const uint Alt = 0x0001, Control = 0x0002, NoRepeat = 0x4000;

        private readonly string profile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Znake", "Widget");
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly NotifyIcon tray;
        private readonly Timer hotkeyHeld = new Timer { Interval = 25 };
        private ScreenOverlay overlay;
        private bool registered;

        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var widget = new Widget())
            {
                widget.registered = RegisterHotKey(widget.Handle, HotkeyId, Control | Alt | NoRepeat, ToggleKey);
                if (!widget.registered)
                {
                    int error = Marshal.GetLastWin32Error();
                    string reason = error == 1409 ? "Ctrl+Alt+Z is already in use." :
                        "Ctrl+Alt+Z could not be registered. " + new Win32Exception(error).Message;
                    MessageBox.Show(reason, "Znake Widget", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }

                widget.tray.Visible = true;
                widget.ToggleOverlay();
                Application.Run();
                return 0;
            }
        }

        private Widget()
        {
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE
            menu.Items.Add("Quit Znake", null, delegate
            {
                if (overlay != null) overlay.Close();
                Application.ExitThread();
            });
            tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Znake Widget", ContextMenuStrip = menu };
            hotkeyHeld.Tick += delegate
            {
                if (GetAsyncKeyState(ToggleKey) >= 0) hotkeyHeld.Stop();
            };
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == HotkeyMessage && message.WParam.ToInt32() == HotkeyId && !hotkeyHeld.Enabled)
            {
                hotkeyHeld.Start();
                ToggleOverlay();
            }
            base.WndProc(ref message);
        }

        private void ToggleOverlay()
        {
            if (overlay != null)
            {
                overlay.Close();
                return;
            }

            try { ShowOverlay(); }
            catch (IOException error) { ShowError(error.Message); }
            catch (InvalidDataException error) { ShowError(error.Message); }
            catch (UnauthorizedAccessException error) { ShowError(error.Message); }
            catch (Win32Exception error) { ShowError(error.Message); }
        }

        private void ShowOverlay()
        {
            if (!WindowsDesktop.IsCurrent()) return;
            StateStore store = StateStore.Open(profile);
            if (store == null)
            {
                ShowError("The widget checkpoint is in use.");
                return;
            }

            ScreenOverlay window;
            try
            {
                SaverSettings settings = SaverSettings.Load(profile);
                window = new ScreenOverlay(store, settings, store.Load(DateTime.UtcNow), CornerBounds);
            }
            catch
            {
                store.Dispose();
                throw;
            }

            window.Disposed += delegate
            {
                overlay = null;
                store.Dispose();
            };
            overlay = window;
            try { window.Show(); }
            catch { window.Dispose(); throw; }
        }

        private static Rectangle CornerBounds()
        {
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            return Rectangle.Intersect(work, new Rectangle(work.Right - 352, work.Bottom - 184, 336, 168));
        }

        private void ShowError(string message)
        {
            tray.ShowBalloonTip(3000, "Znake Widget", message, ToolTipIcon.Error);
        }

        public void Dispose()
        {
            if (overlay != null) overlay.Close();
            hotkeyHeld.Dispose();
            tray.Dispose();
            menu.Dispose();
            if (registered) UnregisterHotKey(Handle, HotkeyId);
            DestroyHandle();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr window, int id);
    }
}
