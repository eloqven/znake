using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.IO;
using System.Runtime.Serialization;
using System.Windows.Forms;
using System.Drawing.Text;

namespace Znake
{
    internal sealed class ScreenOverlay : Form
    {
        private const int SquareSize = WidgetViewport.DefaultSquare;
        private const double CheckpointInterval = 15;
        private const int ShowFrameSteps = 8;
        private const double MaxFrameSeconds = 0.32;
        private readonly Timer timer = new Timer { Interval = 80 };
        private readonly Stopwatch clock = new Stopwatch();
        private readonly StateStore store;
        private SaverSettings settings;
        private readonly Queue<SpeciesTemplate> births = new Queue<SpeciesTemplate>();
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
        private int debugSteps;
        private ManualHealthOverlay manualHealth;
        private long? inspectedSnakeId;
        private Font showFont;
        private bool rateStarted;
        private double rateClock;
        private long rateMoves;
        private double movesPerSecond;
        private ShowLog showLog;
        private bool showLogUnavailable;
        private bool resetShowLog;
        internal bool DebugPaused { get; private set; }
        internal double TimeScale { get { return settings.TimeScale ?? 1; } }
        internal bool SimulationPaused { get { return DebugPaused || TimeScale == 0; } }
        internal long Tick { get { return world == null ? 0 : world.Ticks; } }
        internal bool GenePanelVisible { get; set; }
        internal bool BrowserSkin { set { settings.BrowserSkin = value; } }
        private readonly bool monitorInput;
        private readonly Func<Rectangle> cornerBounds = null;
        private WidgetViewport? viewport;
#if ZNAKE_SMOKE
        private Rectangle previousHead;
        private int previousSkin;
        private bool showFrameSaved;
#endif

        public ScreenOverlay(StateStore store, SaverSettings settings, World world,
            WidgetViewport? viewport = null, bool resetShowLog = false)
        {
            this.store = store;
            this.settings = settings;
            this.world = world;
            this.viewport = viewport;
            this.resetShowLog = resetShowLog;
#if ZNAKE_SMOKE
            if (!viewport.HasValue)
            {
                Rectangle? smokeBounds = SmokeTrace.ReadBounds();
                if (smokeBounds.HasValue)
                    this.cornerBounds = () => smokeBounds.Value;
            }
#endif
            monitorInput = !viewport.HasValue && cornerBounds == null;
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
                if (!monitorInput)
                    parameters.ExStyle |= 0x08000000 | 0x00000020; // WS_EX_NOACTIVATE and WS_EX_TRANSPARENT
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
            if (viewport.HasValue) return Rectangle.Intersect(bounds, viewport.Value.Bounds);
            return cornerBounds == null ? bounds : Rectangle.Intersect(bounds, cornerBounds());
        }

        internal WidgetViewport Viewport { get { return viewport.Value; } }
        internal Snake Resident { get { return world == null ? null : world.Snakes.Find(snake => snake.IsResident); } }
        internal Snake InspectedSnake
        {
            get
            {
                if (world == null) return null;
                Snake selected = inspectedSnakeId.HasValue ? world.Snakes.Find(snake => snake.Id == inspectedSnakeId) : null;
                return selected ?? Resident;
            }
        }
        internal double Night { get; private set; }
        internal bool HasDaylightLocation { get { return settings.Latitude.HasValue && settings.Longitude.HasValue; } }

        internal void ApplySettings(SaverSettings updated) { settings = updated; }

        internal void SetDebugPause(bool paused)
        {
            DebugPaused = paused;
            debugSteps = 0;
            pendingTime = 0;
        }

        internal void StepDebug()
        {
            if (SimulationPaused) debugSteps++;
        }

        internal static double VisibleElapsed(double elapsed)
        {
            return Math.Min(MaxFrameSeconds, Math.Max(0, elapsed));
        }

        internal static double SimulationElapsed(double elapsed, double multiplier)
        {
            return Math.Min(1, VisibleElapsed(elapsed) * multiplier);
        }
        internal bool ManualActive { get { return world != null && world.Manual != null; } }
        internal event Action ManualEnded;

        internal void BringSpecies(SpeciesTemplate template)
        {
            births.Enqueue(template);
        }

        internal AgentReply HandleAgent(string id, AgentRequest request)
        {
            bool changed;
            AgentReply reply = AgentControl.Apply(world, Night, SimulationPaused, id, request, out changed);
            if (changed)
            {
                lastSavedTick = -1;
                reply.Saved = Save();
            }
            return reply;
        }

        internal Snake HeadAt(Point pointer)
        {
            if (world == null || !display.Contains(pointer)) return null;
            return world.Snakes.FindLast(snake =>
            {
                if (snake.AgentToken != null || !world.Contains(snake.Body[0])) return false;
                Rectangle head = Tile(snake.Body[0], CellSize, GridOffset);
                head.Offset(display.Location);
                return head.Contains(pointer);
            });
        }

        internal void PauseSnake(Snake snake)
        {
            inspectedSnakeId = snake.Id;
            world.Manual = new ManualSteering { Heading = snake.Heading, SnakeId = snake.Id };
        }

        internal void SteerSnake(int heading)
        {
            Snake snake = world == null ? null : world.ManualSnake;
            if (snake == null) return;
            if (heading != (snake.Heading + 2) % Cell.Directions.Length)
                world.Manual.Heading = heading;
            world.Manual.Paused = false;
        }

        internal void BoostSnake()
        {
            if (ManualActive) world.Manual.Boost = true;
        }

        internal void BiteTail()
        {
            if (ManualActive && world.BiteTail(Night, settings.BrowserSkin))
            {
                lastSavedTick = -1;
                Save();
            }
        }

        internal void ResumeSnake()
        {
            if (ManualActive) world.Manual = null;
            if (ManualEnded != null) ManualEnded();
        }

        internal double GeneEditStep { get { return world == null || world.GeneEditStep == 0 ? 1 : world.GeneEditStep; } }

        internal bool SetSnakeGene(long id, Gene gene, double value, double? step = null)
        {
            Snake snake = InspectedSnake;
            if (snake == null || snake.Id != id || !Genome.IsValid(gene, value)) return false;
            double oldValue = snake.Genes[gene], oldStep = world.GeneEditStep;
            long previousSave = lastSavedTick;
            snake.Genes[gene] = value;
            if (step.HasValue) world.GeneEditStep = step.Value;
            lastSavedTick = -1;
            if (Save()) return true;
            snake.Genes[gene] = oldValue;
            world.GeneEditStep = oldStep;
            lastSavedTick = previousSave;
            return false;
        }

        internal bool SetViewport(WidgetViewport next)
        {
            WidgetViewport current = Viewport;
            if (current.Bounds == next.Bounds && current.Square == next.Square &&
                current.FullScreen == next.FullScreen && current.Anchor == next.Anchor) return true;
            if (world != null && !world.TryReframe(next.Columns, next.Rows, Viewport.ShiftTo(next)))
                return false;
            viewport = next;
            if (world != null) RememberViewport();
            lastSavedTick = -1;
            return true;
        }

        private void RememberViewport()
        {
            world.WidgetSquare = Viewport.Square;
            world.WidgetFullScreen = Viewport.FullScreen;
            world.WidgetAnchor = new Cell(Viewport.Anchor.X, Viewport.Anchor.Y);
        }

        private int CellSize { get { return viewport.HasValue ? Viewport.Square : SquareSize; } }
        private Point GridOffset { get { return viewport.HasValue ? Viewport.GridOffset : Point.Empty; } }

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
                ColorGrid raw = ColorGrid.Sample(pixels, display.Size, CellSize, GridOffset);
                if (world == null)
                    world = new World(raw.Width, raw.Height, unchecked((uint)Guid.NewGuid().GetHashCode()),
                        settings.BirthOverrides(), viewport.HasValue ? CellSize : 0);
                if (viewport.HasValue) RememberViewport();
                while (births.Count > 0)
                {
                    world.Birth(births.Dequeue(), true);
                    lastSavedTick = -1;
                }

                double time = clock.Elapsed.TotalSeconds;
                double frameSeconds = time - lastTime;
                if (SimulationPaused)
                {
                    pendingTime = debugSteps > 0 ? World.StepSeconds : 0;
                    if (debugSteps > 0) debugSteps--;
                }
                else
                    pendingTime += SimulationElapsed(frameSeconds, TimeScale);
                lastTime = time;
                if (world.ShowActive && !world.ShowComplete) world.ShowSeconds += VisibleElapsed(frameSeconds);
                DateTimeOffset now = DateTimeOffset.Now;
                Night = viewport.HasValue ? Daylight.WidgetNightAt(now, settings.Latitude, settings.Longitude) :
                    Daylight.NightAt(now, settings.Latitude, settings.Longitude);
                bool manualBefore = ManualActive;
                int stepBudget = world.ShowActive
                    ? Math.Min(16, Math.Max(ShowFrameSteps, (int)Math.Ceiling(TimeScale)))
                    : Int32.MaxValue;
                int stepped = 0;
                while (pendingTime >= World.StepSeconds && stepped < stepBudget && !world.ShowComplete)
                {
                    world.Step(raw, Night, now, settings.Latitude, settings.Longitude);
                    pendingTime -= World.StepSeconds;
                    stepped++;
                }
                if (world.ShowActive)
                {
                    if (stepped >= stepBudget || world.ShowComplete) pendingTime = 0;
                    SampleShowRate(time);
                    if (world.ShowComplete && lastSavedTick != world.Ticks) Save();
                }
                if (manualBefore && !ManualActive && ManualEnded != null) ManualEnded();

                if (world.Field == null) world.SampleField(raw, Night);
                Render(Night);
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
                RenderManualHealth();
                LogShow(display);
#if ZNAKE_SMOKE
                if (world.ShowComplete && !showFrameSaved)
                {
                    try
                    {
                        paint.Image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "show-final.png"),
                            ImageFormat.Png);
                        showFrameSaved = true;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (ExternalException) { }
                }
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

        private void LogShow(Rectangle bounds)
        {
            if (world == null || !world.ShowActive || showLogUnavailable) return;
            try
            {
                if (showLog == null)
                {
                    if (resetShowLog)
                    {
                        ShowLog.Clear(store.Folder);
                        resetShowLog = false;
                    }
                    showLog = ShowLog.Open(store.Folder);
                    bool startedNew = showLog.StartedNew;
                    if (startedNew) showLog.Start(world, settings, bounds, capture.Image, GridOffset);
                    showLog.Record(world, movesPerSecond, startedNew);
                }
                else
                    showLog.Record(world, movesPerSecond);
            }
            catch (IOException error) { Error(error.Message); showLogUnavailable = true; }
            catch (UnauthorizedAccessException error) { Error(error.Message); showLogUnavailable = true; }
            catch (SerializationException error) { Error(error.Message); showLogUnavailable = true; }
        }

        private void SampleShowRate(double time)
        {
            if (!rateStarted)
            {
                rateStarted = true;
                rateClock = time;
                rateMoves = world.ShowMoves;
                return;
            }
            double span = time - rateClock;
            if (span < 1) return;
            double sample = (world.ShowMoves - rateMoves) / span;
            movesPerSecond = BlendRate(movesPerSecond, sample);
            rateClock = time;
            rateMoves = world.ShowMoves;
        }

        internal static double BlendRate(double previous, double sample)
        {
            return previous > 0 ? previous * 0.7 + sample * 0.3 : sample;
        }

        private void RenderManualHealth()
        {
            Snake snake = world.ManualSnake;
            if (snake == null)
            {
                if (manualHealth != null) manualHealth.Hide();
                return;
            }
            if (manualHealth == null) manualHealth = new ManualHealthOverlay();
            Rectangle bounds = ManualHealthOverlay.Place(display, Screen.PrimaryScreen.Bounds, Viewport.FullScreen);
            if (!manualHealth.Present(this, bounds, ManualHealthOverlay.Calm(snake, Night)))
            {
                manualHealth.Dispose();
                manualHealth = null;
            }
        }

        private void Render(double night)
        {
            using (Graphics graphics = Graphics.FromImage(paint.Image))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceOver;
                if (viewport.HasValue)
                    foreach (Vine vine in world.Vines)
                    {
                        if (Ecology.IsActive(vine)) continue;
                        foreach (VineSquare square in vine.Squares)
                        {
                            Tint trace = square.ColorTrace();
                            using (var brush = new SolidBrush(trace.Color.ToColor(trace.Alpha)))
                                graphics.FillRectangle(brush, Tile(square.Position, CellSize, GridOffset));
                        }
                    }
                foreach (var entry in world.Field.Tints(settings.Debug))
                {
                    if (entry.Value.Alpha < 0.001) continue;
                    using (var brush = new SolidBrush(entry.Value.Color.ToColor(entry.Value.Alpha)))
                        graphics.FillRectangle(brush, Tile(entry.Key, CellSize, GridOffset));
                }

                if (viewport.HasValue && settings.BrowserSkin)
                {
                    Snake selected = GenePanelVisible ? InspectedSnake : null;
                    BrowserSnakeSkin.Draw(graphics, world, night, CellSize, GridOffset,
                        selected == null ? (long?)null : selected.Id);
                }
                else
                {
                    if (!settings.Debug)
                    {
                        foreach (Fruit fruit in world.Fruits)
                        {
                            Rectangle mark = Tile(fruit.Position, CellSize, GridOffset);
                            int inset = CellSize * 7 / SquareSize;
                            mark.Inflate(-inset, -inset);
                            using (var brush = new SolidBrush(Rgb.Mix(fruit.Tone, Rgb.FromBytes(237, 204, 88), 0.3).ToColor(0.55)))
                                graphics.FillEllipse(brush, mark);
                        }
                    }
                    DrawBodies(graphics, world, night, CellSize, GridOffset);
                }

                using (var pulse = new Pen(Color.Transparent, 2))
                    foreach (Snake snake in world.Snakes)
                    {
                        if (!snake.LastFruitTick.HasValue || !world.Contains(snake.Body[0])) continue;
                        long age = world.Ticks - snake.LastFruitTick.Value;
                        if (age < 0 || (age >= 12 && snake.FruitPower == 1)) continue;
                        Rectangle halo = Tile(snake.Body[0], CellSize, GridOffset);
                        int expand = 2 + (int)Math.Min(11, age) / 3;
                        halo.Inflate(expand, expand);
                        pulse.Color = Color.FromArgb(age >= 12 ? 110 : (int)(240 * (1 - age / 12.0)),
                            snake.LastFruitWasAnti ? Color.FromArgb(240, 114, 174) : Color.FromArgb(177, 249, 147));
                        graphics.DrawEllipse(pulse, halo);
                    }

                if (world.Eggs != null)
                {
                    long utcTicks = DateTime.UtcNow.Ticks;
                    using (var shell = new SolidBrush(Color.Transparent))
                    using (var edge = new Pen(Color.FromArgb(200, 49, 37, 65)))
                        foreach (Egg egg in world.Eggs.Values)
                        {
                            if (!world.Contains(egg.Position)) continue;
                            Rectangle tile = Tile(egg.Position, CellSize, GridOffset);
                            tile.Inflate(-CellSize / 4, -CellSize / 8);
                            bool ready = egg.DueUtcTicks <= utcTicks;
                            shell.Color = Rgb.Mix(egg.Parent.BirthColor,
                                Rgb.FromBytes(255, 235, 176), ready ? 0.8 : 0.45).ToColor(0.95);
                            graphics.FillEllipse(shell, tile);
                            graphics.DrawEllipse(edge, tile);
                        }
                }
                if (world.ShowActive) DrawShowHud(graphics);
                graphics.Flush(FlushIntention.Sync);
            }

            if (cornerBounds != null || (viewport.HasValue && !Viewport.FullScreen))
                paint.DrawInvertedBorder(pixels);
        }

        private void DrawShowHud(Graphics graphics)
        {
            if (showFont == null)
                showFont = new Font("Consolas", Math.Max(12, Math.Min(22, CellSize)), FontStyle.Bold, GraphicsUnit.Pixel);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            string title = world.ShowComplete ? "55-SNAKE SHOW COMPLETE" : "55-SNAKE SHOW";
            string moves = "MOVES " + world.ShowMoves.ToString("N0", CultureInfo.InvariantCulture) +
                " / " + world.ShowGoal.ToString("N0", CultureInfo.InvariantCulture);
            string living = "SNAKES " + world.Snakes.Count.ToString(CultureInfo.InvariantCulture) +
                "   " + movesPerSecond.ToString("N1", CultureInfo.InvariantCulture) + " MOVES/S";
            string[] lines = { title, moves, living };

            float x = 24, y = 20;
            float lineHeight = showFont.GetHeight(graphics) + 4;
            float widest = 0;
            foreach (string line in lines) widest = Math.Max(widest, graphics.MeasureString(line, showFont).Width);
            using (var panel = new SolidBrush(Color.FromArgb(120, 8, 20, 12)))
                graphics.FillRectangle(panel, x - 10, y - 8, widest + 24, lineHeight * lines.Length + 12);
            using (var shadow = new SolidBrush(Color.FromArgb(190, 0, 0, 0)))
            using (var text = new SolidBrush(Color.FromArgb(236, 214, 255, 206)))
                for (int index = 0; index < lines.Length; index++)
                {
                    graphics.DrawString(lines[index], showFont, shadow, x + 2, y + index * lineHeight + 2);
                    graphics.DrawString(lines[index], showFont, text, x, y + index * lineHeight);
                }
        }

        internal static void DrawBodies(Graphics graphics, World world, double night,
            int square = SquareSize, Point offset = default(Point))
        {
            var coverage = new Dictionary<Cell, int>();
            foreach (Snake snake in world.Snakes)
                foreach (Cell point in snake.Body)
                {
                    if (!world.Contains(point)) continue;
                    int count;
                    coverage.TryGetValue(point, out count);
                    coverage[point] = count + 1;
                }

            foreach (Snake snake in world.Snakes)
            {
                Color solid = snake.Skin.ToColor(1);
                double tailOpacity = snake.TailOpacity(night);
                using (var body = new SolidBrush(solid))
                    for (int segment = snake.Body.Count - 1; segment >= 0; segment--)
                    {
                        Cell point = snake.Body[segment];
                        int layers;
                        if (!coverage.TryGetValue(point, out layers)) continue;
                        double? split = snake.SplitOpacity(segment);
                        double alpha = split ?? (segment == snake.Body.Count - 1 ? tailOpacity : 1);
                        if (layers > 1 && !split.HasValue) alpha *= World.OverlapOpacity / layers;
                        body.Color = alpha == 1 ? solid : snake.Skin.ToColor(alpha);
                        Rectangle tile = Tile(point, square, offset);
                        graphics.FillRectangle(body, tile);
                        if (segment == 0)
                        {
                            double stress = snake.SmoothedStress ?? Numbers.Unit(snake.Stress / snake.Capacity(night, false));
                            int gray = (int)Math.Round(255 * (1 - stress));
                            body.Color = Color.FromArgb((int)Math.Round(255 * alpha), gray, gray, gray);
                            graphics.FillRectangle(body, LeadingEdge(tile, Cell.Directions[snake.Heading]));
                        }
                    }
            }
        }

        private static Rectangle LeadingEdge(Rectangle tile, Cell heading)
        {
            if (heading.X != 0)
                return new Rectangle(heading.X > 0 ? tile.Right - 1 : tile.Left, tile.Top, 1, tile.Height);

            return new Rectangle(tile.Left, heading.Y > 0 ? tile.Bottom - 1 : tile.Top, tile.Width, 1);
        }

        private static Rectangle Tile(Cell cell, int square, Point offset)
        {
            return new Rectangle(offset.X + cell.X * square, offset.Y + cell.Y * square, square, square);
        }

#if ZNAKE_SMOKE
        private void TraceFrame()
        {
            Snake resident = world.Snakes.Find(snake => snake.IsResident);
            Rectangle primary = Screen.PrimaryScreen.Bounds;
            Point marker = !monitorInput ? new Point(display.Width - 34, 24) :
                new Point(primary.Left - display.Left + primary.Width / 2 + 132,
                    primary.Top - display.Top + primary.Height / 2);
            Rectangle nextHead = Tile(resident.Body[0], CellSize, GridOffset);
            SmokeTrace.Frame(Handle, display, capture, paint, previousHead, previousSkin, nextHead, marker,
                !monitorInput);
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

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (ManualActive && (e.KeyCode == Keys.Space ||
                e.KeyCode == Keys.Right || e.KeyCode == Keys.Down ||
                e.KeyCode == Keys.Left || e.KeyCode == Keys.Up))
            {
                if (e.KeyCode == Keys.Space) BoostSnake();
                else if (e.KeyCode == Keys.Right) SteerSnake(0);
                else if (e.KeyCode == Keys.Down) SteerSnake(1);
                else if (e.KeyCode == Keys.Left) SteerSnake(2);
                else if (e.KeyCode == Keys.Up) SteerSnake(3);
                e.Handled = true;
                return;
            }
            Close();
        }
        protected override void OnMouseDown(MouseEventArgs e) { Close(); }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            Save();
            base.OnFormClosed(e);
        }

        private bool Save()
        {
            if (world == null || world.Ticks == lastSavedTick) return true;
            try
            {
                store.Save(world, DateTime.UtcNow);
                lastSavedTick = world.Ticks;
#if ZNAKE_SMOKE
                SmokeTrace.Saved(world);
#endif
                return true;
            }
            catch (IOException error) { Error(error.Message); }
            catch (InvalidDataException error) { Error(error.Message); }
            catch (UnauthorizedAccessException error) { Error(error.Message); }
            catch (SerializationException error) { Error(error.Message); }
            return false;
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
                if (manualHealth != null) { manualHealth.Dispose(); manualHealth = null; }
                if (showLog != null) { showLog.Dispose(); showLog = null; }
                if (showFont != null) { showFont.Dispose(); showFont = null; }
                ReleaseSurfaces();
                if (cursorHidden) { Cursor.Show(); cursorHidden = false; }
            }
            base.Dispose(disposing);
        }
    }
}
