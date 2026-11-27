using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace Znake
{
    internal sealed class Widget : NativeWindow, IDisposable
    {
        private const int HotkeyId = 1;
        private const int GeneHotkeyId = 2;
        private const int FirstManualHotkey = 3;
        private const int HotkeyMessage = 0x0312;
        private const int RawInputMessage = 0x00FF;
        private const int ToggleKey = 'Z';
        private const uint Alt = 0x0001, Control = 0x0002, NoRepeat = 0x4000;

        private readonly string profile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Znake", "Widget");
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem startShow = new ToolStripMenuItem("Start 55-Snake Show");
        private readonly ToolStripMenuItem resumeShow = new ToolStripMenuItem("Resume Show");
        private readonly ToolStripMenuItem regularArena = new ToolStripMenuItem("Return to Regular Arena");
        private readonly ToolStripMenuItem birth = new ToolStripMenuItem("Birth");
        private readonly ToolStripMenuItem timeMenu = new ToolStripMenuItem("Time 1x");
        private readonly ToolStripTextBox customTime = new ToolStripTextBox { AccessibleName = "Custom time multiplier" };
        private readonly ToolStripMenuItem squareSkin = new ToolStripMenuItem("Desktop squares") { Checked = true };
        private readonly ToolStripMenuItem browserSkin = new ToolStripMenuItem("Original game");
        private readonly SpeciesLibrary species;
        private readonly NotifyIcon tray;
        private readonly Icon trayIcon;
        private readonly Timer hotkeyHeld = new Timer { Interval = 25 };
        private readonly Timer agentPoll = new Timer { Interval = 120 };
        private readonly AgentMailbox agentMailbox;
        private readonly Stopwatch inputClock = Stopwatch.StartNew();
        private readonly WindowsTapSequence windowsTaps = new WindowsTapSequence();
        private static readonly Keys[] ManualKeys = { Keys.Right, Keys.Down, Keys.Left, Keys.Up, Keys.Space, Keys.B };
        private ScreenOverlay overlay;
        private bool showingShow;
        private GenePanel panel;
        private Rectangle? panelBounds;
        private bool registered;
        private bool genesRegistered;
        private int wheelRemainder;
        private int manualRegistrations;
        private WidgetViewport? dragView;
        private Point dragPointer;

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
                widget.genesRegistered = RegisterHotKey(widget.Handle, GeneHotkeyId, Control | Alt | NoRepeat, 'G');
                if (!WindowsDesktop.ObserveInput(widget.Handle))
                    widget.ShowError("The sandbox input shortcuts are unavailable.");
#if ZNAKE_SMOKE
                string autostart = Environment.GetEnvironmentVariable("ZNAKE_SHOW_AUTOSTART");
                if (autostart == "start") widget.StartShow();
                else if (autostart == "resume") widget.ResumeShow();
                else widget.ToggleOverlay();
#else
                widget.ToggleOverlay();
#endif
                Application.Run();
                return 0;
            }
        }

        private Widget()
        {
            CreateHandle(new CreateParams { Parent = new IntPtr(-3), Caption = "Znake Widget Control" });
            species = new SpeciesLibrary(Path.Combine(profile, "species.db"));
            agentMailbox = new AgentMailbox(profile);
            startShow.Click += delegate { StartShow(); };
            resumeShow.Click += delegate { ResumeShow(); };
            regularArena.Click += delegate { ReturnToArena(); };
            menu.Items.Add(startShow);
            menu.Items.Add(resumeShow);
            menu.Items.Add(regularArena);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Genes", null, delegate { ToggleGenePanel(); });
            menu.Items.Add(birth);
            var skin = new ToolStripMenuItem("Skin");
            squareSkin.Click += delegate { SelectSkin(false); };
            browserSkin.Click += delegate { SelectSkin(true); };
            skin.DropDownItems.Add(squareSkin);
            skin.DropDownItems.Add(browserSkin);
            menu.Items.Add(skin);
            BuildTimeMenu();
            menu.Items.Add(timeMenu);
            menu.Items.Add("Settings", null, delegate { OpenSettings(); });
            menu.Opening += delegate { RefreshBirthMenu(); RefreshShowMenu(); };
            menu.Items.Add("Quit Znake", null, delegate
            {
                if (overlay != null) overlay.Close();
                Application.ExitThread();
            });
            trayIcon = SnakeHeadIcon();
            tray = new NotifyIcon { Icon = trayIcon, Text = "Znake Widget", ContextMenuStrip = menu };
            hotkeyHeld.Tick += delegate
            {
                if (GetAsyncKeyState(ToggleKey) >= 0) hotkeyHeld.Stop();
            };
            agentPoll.Tick += delegate
            {
                try
                {
                    agentMailbox.Poll((id, request) =>
                    {
                        if (overlay == null && request != null && request.Command == "spawn") ToggleOverlay();
                        return overlay == null ? new AgentReply { Error = "Show the arena first." } :
                            overlay.HandleAgent(id, request);
                    });
                }
                catch (IOException error) { ShowError(error.Message); }
                catch (UnauthorizedAccessException error) { ShowError(error.Message); }
                catch (SerializationException error) { ShowError(error.Message); }
            };
            agentPoll.Start();
        }

        private void BuildTimeMenu()
        {
            foreach (double speed in new[] { 0, 0.5, 1, 2, 3, 5, 10 })
            {
                var item = new ToolStripMenuItem(speed == 0 ? "Pause (0x)" : speed.ToString(CultureInfo.InvariantCulture) + "x")
                {
                    Tag = speed
                };
                item.Click += delegate { SelectTime((double)item.Tag); };
                timeMenu.DropDownItems.Add(item);
            }
            timeMenu.DropDownItems.Add(new ToolStripSeparator());
            timeMenu.DropDownItems.Add(new ToolStripLabel("Custom multiplier. Press Enter."));
            customTime.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode != Keys.Enter) return;
                args.SuppressKeyPress = true;
                double value;
                if ((!Double.TryParse(customTime.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                    !Double.TryParse(customTime.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) ||
                    !Numbers.Nonnegative(value))
                {
                    ShowError("Enter a finite time multiplier of zero or more.");
                    return;
                }
                SelectTime(value);
                timeMenu.HideDropDown();
            };
            timeMenu.DropDownItems.Add(customTime);
            UpdateTimeMenu(1);
        }

        private void SelectTime(double speed)
        {
            try
            {
                SaverSettings settings = SaverSettings.Load(profile);
                settings.TimeScale = speed;
                Directory.CreateDirectory(profile);
                settings.Save(profile);
                UpdateTimeMenu(speed);
                if (overlay != null) overlay.ApplySettings(settings);
            }
            catch (IOException error) { ShowError(error.Message); }
            catch (InvalidDataException error) { ShowError(error.Message); }
            catch (UnauthorizedAccessException error) { ShowError(error.Message); }
            catch (SerializationException error) { ShowError(error.Message); }
        }

        private void UpdateTimeMenu(double speed)
        {
            string value = speed.ToString("G", CultureInfo.InvariantCulture);
            timeMenu.Text = "Time " + value + "x";
            customTime.Text = value;
            foreach (ToolStripItem item in timeMenu.DropDownItems)
            {
                var preset = item as ToolStripMenuItem;
                if (preset != null && preset.Tag is double) preset.Checked = (double)preset.Tag == speed;
            }
        }

        private void OpenSettings()
        {
            ReleaseManualKeys();
            using (var dialog = new SettingsDialog(profile))
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    SaverSettings saved = SaverSettings.Load(profile);
                    UpdateSkinMenu(saved.BrowserSkin);
                    UpdateTimeMenu(saved.TimeScale ?? 1);
                    if (overlay != null) overlay.ApplySettings(saved);
                }
            }
            if (overlay != null && overlay.ManualActive && (panel == null || !panel.ContainsFocus) &&
                !RegisterManualKeys()) overlay.ResumeSnake();
        }

        private void SelectSkin(bool originalGame)
        {
            try
            {
                SaverSettings settings = SaverSettings.Load(profile);
                settings.BrowserSkin = originalGame;
                Directory.CreateDirectory(profile);
                settings.Save(profile);
                UpdateSkinMenu(originalGame);
                if (overlay != null) overlay.BrowserSkin = originalGame;
            }
            catch (IOException error) { ShowError(error.Message); }
            catch (InvalidDataException error) { ShowError(error.Message); }
            catch (UnauthorizedAccessException error) { ShowError(error.Message); }
            catch (SerializationException error) { ShowError(error.Message); }
        }

        private void UpdateSkinMenu(bool originalGame)
        {
            squareSkin.Checked = !originalGame;
            browserSkin.Checked = originalGame;
        }

        private void RefreshBirthMenu()
        {
            while (birth.DropDownItems.Count > 0)
            {
                ToolStripItem previous = birth.DropDownItems[0];
                birth.DropDownItems.RemoveAt(0);
                previous.Dispose();
            }
            birth.DropDownItems.Add("Random snake", null, delegate { SpawnFromTray(null); });
            try
            {
                var savedSpecies = species.List();
                if (savedSpecies.Count > 0) birth.DropDownItems.Add(new ToolStripSeparator());
                foreach (var saved in savedSpecies)
                    birth.DropDownItems.Add(saved.Key.Replace("&", "&&"), null,
                        delegate { SpawnFromTray(saved.Value); });
            }
            catch (IOException error) { ShowBirthError(error.Message); }
            catch (InvalidDataException error) { ShowBirthError(error.Message); }
            catch (UnauthorizedAccessException error) { ShowBirthError(error.Message); }
            catch (SerializationException error) { ShowBirthError(error.Message); }
        }

        private void SpawnFromTray(SpeciesTemplate template)
        {
            if (overlay == null) ToggleOverlay();
            if (overlay != null) overlay.BringSpecies(template);
        }

        private string ShowProfile { get { return Path.Combine(profile, "Show"); } }

        private void RefreshShowMenu()
        {
            bool saved = File.Exists(Path.Combine(ShowProfile, "world.json")) ||
                File.Exists(Path.Combine(ShowProfile, "world.previous.json"));
            bool showOpen = overlay != null && showingShow;
            resumeShow.Enabled = saved && !showOpen;
            startShow.Enabled = !showOpen;
            regularArena.Enabled = showingShow;
        }

        private void StartShow()
        {
            if (overlay != null) overlay.Close();
            try
            {
                ShowOverlay(true, true);
            }
            catch (IOException error) { ShowError(error.Message); }
            catch (InvalidDataException error) { ShowError(error.Message); }
            catch (UnauthorizedAccessException error) { ShowError(error.Message); }
            catch (Win32Exception error) { ShowError(error.Message); }
        }

        private void ResumeShow()
        {
            if (overlay != null) overlay.Close();
            try { ShowOverlay(true, false); }
            catch (IOException error) { ShowError(error.Message); }
            catch (InvalidDataException error) { ShowError(error.Message); }
            catch (UnauthorizedAccessException error) { ShowError(error.Message); }
            catch (Win32Exception error) { ShowError(error.Message); }
        }

        private void ReturnToArena()
        {
            if (!showingShow) return;
            if (overlay != null) overlay.Close();
            try { ShowOverlay(false, false); }
            catch (IOException error) { ShowError(error.Message); }
            catch (InvalidDataException error) { ShowError(error.Message); }
            catch (UnauthorizedAccessException error) { ShowError(error.Message); }
            catch (Win32Exception error) { ShowError(error.Message); }
        }

        private static Icon SnakeHeadIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                using (var skin = new SolidBrush(Color.FromArgb(106, 220, 144)))
                using (var outline = new Pen(Color.FromArgb(22, 58, 46), 2))
                using (var tongue = new Pen(Color.FromArgb(255, 121, 132), 2))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.DrawLines(tongue, new[] { new Point(22, 18), new Point(27, 18), new Point(31, 14) });
                    graphics.DrawLine(tongue, 27, 18, 31, 21);
                    var head = new[]
                    {
                        new Point(3, 27), new Point(3, 13), new Point(9, 5), new Point(21, 5),
                        new Point(26, 9), new Point(26, 18), new Point(21, 22), new Point(12, 22),
                        new Point(12, 27)
                    };
                    graphics.FillPolygon(skin, head);
                    graphics.DrawPolygon(outline, head);
                    graphics.FillEllipse(Brushes.White, 14, 9, 8, 8);
                    graphics.FillEllipse(Brushes.Black, 17, 10, 4, 6);
                }

                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }

        private void ShowBirthError(string error)
        {
            birth.DropDownItems.Add(new ToolStripMenuItem("Species unavailable") { Enabled = false });
            ShowError(error);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == HotkeyMessage && message.WParam.ToInt32() == HotkeyId && !hotkeyHeld.Enabled)
            {
                hotkeyHeld.Start();
                ToggleOverlay();
            }
            if (message.Msg == HotkeyMessage && message.WParam.ToInt32() == GeneHotkeyId)
                ToggleGenePanel();
            if (message.Msg == HotkeyMessage && overlay != null && overlay.ManualActive)
            {
                int manualKey = message.WParam.ToInt32() - FirstManualHotkey;
                if (manualKey >= 0 && manualKey < ManualKeys.Length) ManualInput(manualKey);
            }
            if (message.Msg == RawInputMessage && overlay != null)
                ReadControls(message.LParam);
            base.WndProc(ref message);
        }

        private void ReadControls(IntPtr messageData)
        {
            uint device;
            WindowsDesktop.RawInputData input;
            if (!WindowsDesktop.TryReadInput(messageData, out device, out input)) return;

            const int leftWin = 0x5B, rightWin = 0x5C;
            if (device == 0 && input.MouseButtonFlags != 0) windowsTaps.CancelTaps();
            if (device == 0) ReadDrag(input.MouseButtonFlags);
            if (device == 0 && (input.MouseButtonFlags & 0x0400) != 0)
            {
                if (GetAsyncKeyState(leftWin) >= 0 && GetAsyncKeyState(rightWin) >= 0)
                {
                    wheelRemainder = 0;
                    return;
                }

                wheelRemainder += input.WheelDelta;
                int steps = wheelRemainder / 120;
                wheelRemainder %= 120;
                if (steps != 0) ResizeSandbox(steps);
            }
            if (device != 1) return;
            Keys key = (Keys)input.VirtualKey;
            bool pressed = (input.KeyboardFlags & 1) == 0;
            if (key == Keys.LWin || key == Keys.RWin)
            {
                wheelRemainder = 0;
                if (overlay.ManualActive && windowsTaps.Key(key == Keys.RWin, pressed, inputClock.ElapsedMilliseconds))
                    overlay.ResumeSnake();
                return;
            }
            if (pressed) windowsTaps.CancelTaps();
            if (!pressed || (panel != null && panel.ContainsFocus) || GetAsyncKeyState('S') >= 0) return;
            if (key == Keys.Add || key == Keys.Oemplus) Zoom(4);
            if (key == Keys.Subtract || key == Keys.OemMinus) Zoom(-4);
        }

        private void ResizeSandbox(int steps)
        {
            WidgetViewport current = overlay.Viewport;
            WidgetViewport largest = Layout(Int32.MaxValue, Int32.MaxValue, current.Square, false, current.Anchor);
            int columns = current.Columns + steps, rows = current.Rows + steps;
            WidgetViewport next = Layout(columns, rows, current.Square, false, current.Anchor);
            if (steps > 0 && next.Bounds == largest.Bounds)
                next = Layout(columns, rows, current.Square, true, current.Anchor);
            if (overlay.SetViewport(next) || steps > 0 || current.FullScreen) return;

            // When one edge cannot shrink, the other edge can still move.
            if (overlay.SetViewport(Layout(columns, current.Rows, current.Square, false, current.Anchor))) return;
            overlay.SetViewport(Layout(current.Columns, rows, current.Square, false, current.Anchor));
        }

        private void Zoom(int change)
        {
            WidgetViewport current = overlay.Viewport;
            int square = Math.Max(WidgetViewport.SmallestSquare,
                Math.Min(WidgetViewport.LargestSquare, current.Square + change));
            if (square != current.Square)
                overlay.SetViewport(Layout(current.Columns, current.Rows, square, current.FullScreen, current.Anchor));
        }

        private void ReadDrag(ushort buttons)
        {
            const ushort leftDown = 1, leftUp = 2;
            if ((buttons & leftDown) != 0)
            {
                Point pointer = WindowsDesktop.Pointer();
                if (panel != null && panel.Bounds.Contains(pointer)) return;
                Snake snake = overlay.HeadAt(pointer);
                if (snake == null) return;
                if (!RegisterManualKeys()) return;
                overlay.PauseSnake(snake);
                windowsTaps.Reset();
                dragPointer = pointer;
                dragView = overlay.Viewport;
            }
            if (dragView.HasValue)
            {
                Point pointer = WindowsDesktop.Pointer();
                var delta = new Point(pointer.X - dragPointer.X, pointer.Y - dragPointer.Y);
                overlay.SetViewport(dragView.Value.Drag(delta, Screen.PrimaryScreen.WorkingArea));
                if ((buttons & leftUp) != 0) dragView = null;
            }
        }

        private bool RegisterManualKeys()
        {
            if (manualRegistrations == ManualKeys.Length) return true;
            for (int index = 0; index < ManualKeys.Length; index++)
            {
                if (!RegisterHotKey(Handle, FirstManualHotkey + index, NoRepeat, (uint)ManualKeys[index]))
                {
                    ReleaseManualKeys();
                    ShowError("The manual steering keys could not be registered.");
                    return false;
                }
                manualRegistrations++;
            }
            return true;
        }

        private void ReleaseManualKeys()
        {
            for (int index = 0; index < manualRegistrations; index++)
                UnregisterHotKey(Handle, FirstManualHotkey + index);
            manualRegistrations = 0;
        }

        private void ManualInput(int key)
        {
            if (key < Cell.Directions.Length)
                overlay.SteerSnake(key);
            else if (ManualKeys[key] == Keys.Space)
                overlay.BoostSnake();
            else
                overlay.BiteTail();
        }

        private void ToggleOverlay()
        {
            if (overlay != null)
            {
                overlay.Close();
                return;
            }

            try { ShowOverlay(showingShow, false); }
            catch (IOException error) { ShowError(error.Message); }
            catch (InvalidDataException error) { ShowError(error.Message); }
            catch (UnauthorizedAccessException error) { ShowError(error.Message); }
            catch (Win32Exception error) { ShowError(error.Message); }
        }

        private void ToggleGenePanel()
        {
            if (panel != null)
            {
                panel.Close();
                return;
            }
            if (overlay == null) return;

            var window = new GenePanel(overlay, species);
            if (panelBounds.HasValue && Screen.GetWorkingArea(panelBounds.Value).IntersectsWith(panelBounds.Value))
                window.Bounds = panelBounds.Value;
            window.FormClosed += delegate { panelBounds = window.Bounds; };
            window.Disposed += delegate
            {
                panel = null;
                if (overlay != null) overlay.GenePanelVisible = false;
            };
            window.Activated += delegate { ReleaseManualKeys(); };
            window.Deactivate += delegate
            {
                if (overlay != null && overlay.ManualActive && !RegisterManualKeys()) overlay.ResumeSnake();
            };
            if (!WindowsDesktop.ExcludeFromCapture(window.Handle, false))
            {
                window.Dispose();
                ShowError("The gene panel could not be excluded from desktop sampling.");
                return;
            }
            panel = window;
            overlay.GenePanelVisible = true;
            window.Show();
        }

        private void ShowOverlay(bool show, bool startNew)
        {
            if (!WindowsDesktop.IsCurrent()) return;
            string root = show ? ShowProfile : profile;
            StateStore store = StateStore.Open(root);
            if (store == null)
            {
                ShowError(show ? "The show checkpoint is in use." : "The widget checkpoint is in use.");
                return;
            }

            ScreenOverlay window;
            try
            {
                SaverSettings settings = SaverSettings.Load(profile);
                UpdateSkinMenu(settings.BrowserSkin);
                UpdateTimeMenu(settings.TimeScale ?? 1);
                World world;
                WidgetViewport view;
                if (show)
                {
                    int square = WidgetViewport.DefaultSquare;
                    if (startNew)
                    {
                        view = Layout(0, 0, square, true);
                        world = new World(view.Columns, view.Rows,
                            unchecked((uint)Guid.NewGuid().GetHashCode()), settings.BirthOverrides(), square);
                        world.StartShow(World.OpeningCast);
                        world.WidgetSquare = square;
                        world.WidgetFullScreen = true;
                        store.Save(world, DateTime.UtcNow);
                    }
                    else
                    {
                        world = store.Load(DateTime.UtcNow);
                        if (world == null || !world.ShowActive)
                        {
                            store.Dispose();
                            ShowError("There is no saved show to resume.");
                            return;
                        }
                        if (world.WidgetSquare >= WidgetViewport.SmallestSquare &&
                            world.WidgetSquare <= WidgetViewport.LargestSquare) square = world.WidgetSquare;
                        Point? showAnchor = world.WidgetAnchor.HasValue
                            ? new Point?(new Point(world.WidgetAnchor.Value.X, world.WidgetAnchor.Value.Y)) : null;
                        view = Layout(world.Width, world.Height, square, true, showAnchor);
                    }
                }
                else
                {
                    world = store.Load(DateTime.UtcNow);
                    int square = world == null || world.WidgetSquare < WidgetViewport.SmallestSquare ||
                        world.WidgetSquare > WidgetViewport.LargestSquare ? WidgetViewport.DefaultSquare : world.WidgetSquare;
                    Point? anchor = world != null && world.WidgetAnchor.HasValue
                        ? new Point?(new Point(world.WidgetAnchor.Value.X, world.WidgetAnchor.Value.Y)) : null;
                    view = Layout(world == null ? 14 : world.Width, world == null ? 7 : world.Height,
                        square, world != null && world.WidgetFullScreen, anchor);
                }
                window = new ScreenOverlay(store, settings, world, view, show && startNew);
            }
            catch
            {
                store.Dispose();
                throw;
            }

            window.Disposed += delegate
            {
                overlay = null;
                dragView = null;
                ReleaseManualKeys();
                if (panel != null) panel.Close();
                store.Dispose();
            };
            window.ManualEnded += delegate
            {
                windowsTaps.Reset();
                ReleaseManualKeys();
            };
            showingShow = show;
            overlay = window;
            try { window.Show(); }
            catch { window.Dispose(); throw; }
        }

        private static WidgetViewport Layout(int columns, int rows, int square, bool fullScreen, Point? anchor = null)
        {
            Screen primary = Screen.PrimaryScreen;
            return WidgetViewport.Create(primary.Bounds, primary.WorkingArea, columns, rows, square, fullScreen, anchor);
        }

        private void ShowError(string message)
        {
            tray.ShowBalloonTip(3000, "Znake Widget", message, ToolTipIcon.Error);
        }

        public void Dispose()
        {
            if (overlay != null) overlay.Close();
            ReleaseManualKeys();
            hotkeyHeld.Dispose();
            agentPoll.Dispose();
            tray.Dispose();
            trayIcon.Dispose();
            menu.Dispose();
            if (registered) UnregisterHotKey(Handle, HotkeyId);
            if (genesRegistered) UnregisterHotKey(Handle, GeneHotkeyId);
            DestroyHandle();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr window, int id);
    }
}
