using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace Znake
{
    internal sealed class ScreenOverlay : Form
    {
        private const int SquareSize = 24;
        private const double CheckpointInterval = 15;
        private readonly Timer timer = new Timer { Interval = 80 };
        private readonly Stopwatch clock = new Stopwatch();
        private readonly StateStore store;
        private readonly SaverSettings settings;
        private World world;
        private DibSurface capture;
        private DibSurface paint;
        private int[] pixels;
        private Rectangle display;
        private Point startingPointer;
        private bool observingInput;
        private bool cursorHidden;
        private bool ordered;
        private double lastTime;
        private double nextSave;
        private double pendingTime = World.StepSeconds;
        private long lastSavedTick;
        private readonly bool monitorInput;
#if ZNAKE_SMOKE
        private readonly Rectangle? smokeBounds;
        private Rectangle previousHead;
        private int previousSkin;
#endif

        public ScreenOverlay(StateStore store, SaverSettings settings, World world)
        {
            this.store = store;
            this.settings = settings;
            this.world = world;
#if ZNAKE_SMOKE
            smokeBounds = SmokeTrace.ReadBounds();
            monitorInput = !smokeBounds.HasValue;
#else
            monitorInput = true;
#endif
            lastSavedTick = world == null ? 0 : world.Ticks;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Bounds = FrameBounds();
            timer.Tick += delegate { Frame(); };
        }

        protected override bool ShowWithoutActivation { get { return !monitorInput; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                const int ToolWindow = 0x00000080;
                parameters.ExStyle |= WindowsDesktop.LayeredWindowStyle | ToolWindow;
#if ZNAKE_SMOKE
                if (!monitorInput)
                    parameters.ExStyle |= 0x08000000 | 0x00000020; // WS_EX_NOACTIVATE and WS_EX_TRANSPARENT
#endif
                return parameters;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!WindowsDesktop.IsCurrent() || !WindowsDesktop.ExcludeFromCapture(Handle) ||
                !WindowsDesktop.RaiseOverlay(Handle))
            {
                Error("The live desktop overlay is unavailable.");
                Close();
                return;
            }

            if (monitorInput)
            {
                startingPointer = WindowsDesktop.Pointer();
                observingInput = WindowsDesktop.ObserveInput(Handle);
                Cursor.Hide();
                cursorHidden = true;
            }
            clock.Start();
            nextSave = CheckpointInterval;
#if ZNAKE_SMOKE
            SmokeTrace.Started(Handle, world, settings.Debug);
#endif
            Frame();
            if (!IsDisposed) timer.Start();
        }

        private Rectangle FrameBounds()
        {
            Rectangle bounds = WindowsDesktop.DisplayBounds;
#if ZNAKE_SMOKE
            if (smokeBounds.HasValue)
                return Rectangle.Intersect(bounds, smokeBounds.Value);
#endif
            return bounds;
        }

        private bool PointerMoved()
        {
            if (!monitorInput) return false;
            Point pointer = WindowsDesktop.Pointer();
            if (clock.ElapsedMilliseconds < 250) startingPointer = pointer;
            return Math.Abs(pointer.X - startingPointer.X) >= 4 || Math.Abs(pointer.Y - startingPointer.Y) >= 4;
        }

        private void Frame()
        {
            bool moved = PointerMoved();
            if (moved || !WindowsDesktop.IsCurrent())
            {
#if ZNAKE_SMOKE
                SmokeTrace.Dismissed(moved ? "pointer movement" : "desktop change");
#endif
                Close();
                return;
            }

            Rectangle current = FrameBounds();
            if (current.Width <= 0 || current.Height <= 0)
            {
                Close();
                return;
            }

            try
            {
                if (capture == null || current != display)
                {
                    ReleaseSurfaces();
                    display = current;
                    Bounds = display;
                    capture = new DibSurface(display.Size);
                    paint = new DibSurface(display.Size);
                    pixels = new int[checked(display.Width * display.Height)];
#if ZNAKE_SMOKE
                    previousHead = Rectangle.Empty;
#endif
                }

                if (!capture.CopyDesktop(display.Location) || !WindowsDesktop.IsCurrent())
                {
                    Error("Desktop sampling stopped.");
                    Close();
                    return;
                }

                capture.ReadPixels(pixels);
                ColorGrid raw = ColorGrid.Sample(pixels, display.Size, SquareSize);
                if (world == null)
                    world = new World(raw.Width, raw.Height, unchecked((uint)Guid.NewGuid().GetHashCode()),
                        settings.BirthOverrides());

                double time = clock.Elapsed.TotalSeconds;
                pendingTime += Math.Min(0.32, time - lastTime);
                lastTime = time;
                double night = Daylight.NightAt(DateTimeOffset.UtcNow, settings.Latitude, settings.Longitude);
                while (pendingTime >= World.StepSeconds)
                {
                    world.Step(raw, night);
                    pendingTime -= World.StepSeconds;
                }

                Render(night);
                if (!paint.Publish(Handle, display.Location))
                {
                    Error("The layered window could not display the world.");
                    Close();
                    return;
                }
                if (!ordered)
                {
                    ordered = WindowsDesktop.RaiseOverlay(Handle);
                    if (!ordered) { Close(); return; }
                }
#if ZNAKE_SMOKE
                TraceFrame();
#endif
                if (time >= nextSave)
                {
                    Save();
                    nextSave = time + CheckpointInterval;
                }
            }
            catch (Win32Exception error)
            {
                Error(error.Message);
                Close();
            }
        }

        private void Render(double night)
        {
            using (Graphics graphics = Graphics.FromImage(paint.Image))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceOver;
                foreach (var entry in world.Field.Tints(settings.Debug))
                {
                    if (entry.Value.Alpha < 0.001) continue;
                    using (var brush = new SolidBrush(entry.Value.Color.ToColor(entry.Value.Alpha)))
                        graphics.FillRectangle(brush, Tile(entry.Key));
                }

                if (!settings.Debug)
                {
                    foreach (Fruit fruit in world.Fruits)
                    {
                        Rectangle mark = Tile(fruit.Position);
                        mark.Inflate(-7, -7);
                        using (var brush = new SolidBrush(Rgb.Mix(fruit.Tone, Rgb.FromBytes(237, 204, 88), 0.3).ToColor(0.55)))
                            graphics.FillEllipse(brush, mark);
                    }
                }

                foreach (Snake snake in world.Snakes)
                {
                    using (var body = new SolidBrush(snake.Skin.ToColor(1)))
                    {
                        for (int segment = snake.Body.Count - 2; segment >= 0; segment--)
                            if (world.Contains(snake.Body[segment]))
                                graphics.FillRectangle(body, Tile(snake.Body[segment]));
                    }
                    Cell last = snake.Body[snake.Body.Count - 1];
                    if (world.Contains(last))
                        using (var tail = new SolidBrush(snake.Skin.ToColor(snake.TailOpacity(night))))
                            graphics.FillRectangle(tail, Tile(last));
                }
                graphics.Flush(FlushIntention.Sync);
            }
        }

        private static Rectangle Tile(Cell cell)
        {
            return new Rectangle(cell.X * SquareSize, cell.Y * SquareSize, SquareSize, SquareSize);
        }

#if ZNAKE_SMOKE
        private void TraceFrame()
        {
            Snake resident = world.Snakes.Find(snake => snake.IsResident);
            Rectangle primary = Screen.PrimaryScreen.Bounds;
            Point marker = smokeBounds.HasValue ? new Point(display.Width - 34, 24) :
                new Point(primary.Left - display.Left + primary.Width / 2 + 132,
                    primary.Top - display.Top + primary.Height / 2);
            Rectangle nextHead = Tile(resident.Body[0]);
            SmokeTrace.Frame(Handle, display, capture, paint, previousHead, previousSkin, nextHead, marker,
                smokeBounds.HasValue);
            previousHead = nextHead;
            previousSkin = resident.Skin.ToColor(1).ToArgb() & 0xFFFFFF;
        }
#endif

        protected override void WndProc(ref Message message)
        {
            const int RawInput = 0x00FF;
            bool dismiss = observingInput && message.Msg == RawInput &&
                WindowsDesktop.IsDismissalInput(message.LParam);
            base.WndProc(ref message);
            if (dismiss && !IsDisposed)
            {
#if ZNAKE_SMOKE
                SmokeTrace.Dismissed("device input");
#endif
                Close();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e) { Close(); }
        protected override void OnMouseDown(MouseEventArgs e) { Close(); }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            Save();
            base.OnFormClosed(e);
        }

        private void Save()
        {
            if (world == null || world.Ticks == lastSavedTick) return;
            try
            {
                store.Save(world, DateTime.UtcNow);
                lastSavedTick = world.Ticks;
#if ZNAKE_SMOKE
                SmokeTrace.Saved(world);
#endif
            }
            catch (IOException error) { Error(error.Message); }
            catch (InvalidDataException error) { Error(error.Message); }
            catch (UnauthorizedAccessException error) { Error(error.Message); }
            catch (SerializationException error) { Error(error.Message); }
        }

        private static void Error(string message)
        {
            Trace.TraceWarning(message);
#if ZNAKE_SMOKE
            SmokeTrace.Error(message);
#endif
        }

        private void ReleaseSurfaces()
        {
            if (capture != null) { capture.Dispose(); capture = null; }
            if (paint != null) { paint.Dispose(); paint = null; }
            pixels = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                ReleaseSurfaces();
                if (cursorHidden) { Cursor.Show(); cursorHidden = false; }
            }
            base.Dispose(disposing);
        }
    }
}
