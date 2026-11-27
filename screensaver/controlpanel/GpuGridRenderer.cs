using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Znake.ControlPanel
{
    internal sealed class GpuGridRenderer : IGridRenderer
    {
        private readonly ElementHost host;
        private readonly VisualCanvas visualCanvas;
        private GridSceneState current;
        private BitmapImage cachedBackground;
        private string cachedBackgroundPath;

        public GpuGridRenderer()
        {
            visualCanvas = new VisualCanvas();
            host = new ElementHost
            {
                Dock = DockStyle.Fill,
                Child = visualCanvas
            };
            visualCanvas.SizeChanged += delegate { RedrawVisual(); };
        }

        public bool IsGpuAccelerated { get { return true; } }
        public Control ViewControl { get { return host; } }

        public void UpdateScene(GridSceneState scene)
        {
            current = scene;
            EnsureBackground(scene == null ? null : scene.BackgroundPath);
            RedrawVisual();
        }

        public Bitmap RenderToBitmap(GridSceneState scene, int width, int height)
        {
            Bitmap result = null;
            Exception error = null;
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                result = RenderSta(scene, width, height);
            }
            else
            {
                var thread = new Thread(delegate()
                {
                    try { result = RenderSta(scene, width, height); }
                    catch (Exception ex) { error = ex; }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
            }
            if (error != null) throw new InvalidOperationException("GPU offscreen render failed.", error);
            return result;
        }

        private static Bitmap RenderSta(GridSceneState scene, int width, int height)
        {
            int w = Math.Max(1, width), h = Math.Max(1, height);
            BitmapImage bg = LoadBackgroundImage(scene == null ? null : scene.BackgroundPath);
            var visual = new DrawingVisual();
            using (DrawingContext ctx = visual.RenderOpen())
                Draw(ctx, scene, bg, w, h);
            var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            var bitmap = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, w, h),
                ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                target.CopyPixels(Int32Rect.Empty, data.Scan0, checked(data.Stride * h), data.Stride);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }

        private void RedrawVisual()
        {
            double width = visualCanvas.ActualWidth;
            double height = visualCanvas.ActualHeight;
            if (width <= 0 || height <= 0) return;
            using (DrawingContext ctx = visualCanvas.Open())
                Draw(ctx, current, cachedBackground, width, height);
        }

        private void EnsureBackground(string path)
        {
            if (String.Equals(cachedBackgroundPath, path, StringComparison.OrdinalIgnoreCase)) return;
            cachedBackgroundPath = path;
            cachedBackground = LoadBackgroundImage(path);
        }

        private static BitmapImage LoadBackgroundImage(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = new MemoryStream(bytes);
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void Draw(DrawingContext ctx, GridSceneState scene, BitmapImage bg, double width, double height)
        {
            System.Drawing.Color bd = scene == null ? System.Drawing.Color.FromArgb(14, 18, 20) : scene.BackdropColor;
            var backdropBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(bd.A, bd.R, bd.G, bd.B));
            backdropBrush.Freeze();
            ctx.DrawRectangle(backdropBrush, null, new Rect(0, 0, width, height));
            if (scene == null || scene.Start == null || width <= 0 || height <= 0) return;

            LayerSettings layers = scene.Layers ?? new LayerSettings();
            int arenaWidth = scene.Start.Width > 0 ? scene.Start.Width * Math.Max(1, scene.Start.Square) : (bg != null ? bg.PixelWidth : 1920);
            int arenaHeight = scene.Start.Height > 0 ? scene.Start.Height * Math.Max(1, scene.Start.Square) : (bg != null ? bg.PixelHeight : 1080);
            double scale = Math.Min(width / Math.Max(1, arenaWidth), height / Math.Max(1, arenaHeight));
            double drawW = arenaWidth * scale;
            double drawH = arenaHeight * scale;
            double originX = (width - drawW) / 2.0;
            double originY = (height - drawH) / 2.0;
            double cell = Math.Max(1, scene.Start.Square) * scale;

            if (layers.ShowGrid && bg != null)
                ctx.DrawImage(bg, new Rect(originX, originY, drawW, drawH));
            else if (layers.ShowGrid)
            {
                var dark = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 22, 24));
                dark.Freeze();
                ctx.DrawRectangle(dark, null, new Rect(originX, originY, drawW, drawH));
            }

            Znake.Replay.Record snap = scene.Snapshot;
            if (snap == null) return;

            ctx.PushClip(new RectangleGeometry(new Rect(originX, originY, drawW, drawH)));
            try
            {
                if (layers.ShowVision && layers.VisionOpacity > 0 && snap.Bodies != null)
                {
                    foreach (Znake.Replay.Body body in snap.Bodies)
                    {
                        if (body.Points == null || body.Points.Count == 0 || body.VisionRadius <= 0) continue;
                        Znake.Replay.Mark head = body.Points[0];
                        double radius = Math.Min(100.0, body.VisionRadius) * cell;
                        double cx = originX + (head.X + 0.5) * cell;
                        double cy = originY + (head.Y + 0.5) * cell;
                        byte r = ByteClamp(body.Red), g = ByteClamp(body.Green), b = ByteClamp(body.Blue);
                        var fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(AlphaByte(26, layers.VisionOpacity), r, g, b));
                        fill.Freeze();
                        var ringBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(AlphaByte(92, layers.VisionOpacity), r, g, b));
                        ringBrush.Freeze();
                        var pen = new System.Windows.Media.Pen(ringBrush, 1.25);
                        pen.Freeze();
                        ctx.DrawEllipse(fill, pen, new System.Windows.Point(cx, cy), radius, radius);
                    }
                }

                if (layers.ShowGrid)
                {
                    if (layers.ShowVines && layers.VinesOpacity > 0 && snap.Vines != null)
                    {
                        byte alpha = AlphaByte(140, layers.VinesOpacity);
                        double size = Math.Max(1.0, cell - 0.5);
                        foreach (Znake.Replay.Mark mark in snap.Vines)
                        {
                            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, ByteClamp(mark.Red), ByteClamp(mark.Green), ByteClamp(mark.Blue)));
                            brush.Freeze();
                            ctx.DrawRectangle(brush, null, new Rect(originX + mark.X * cell, originY + mark.Y * cell, size, size));
                        }
                    }

                    if (layers.ShowFruits && layers.FruitsOpacity > 0 && snap.Fruits != null)
                    {
                        byte alpha = AlphaByte(255, layers.FruitsOpacity);
                        double r = cell * 0.32;
                        foreach (Znake.Replay.Mark mark in snap.Fruits)
                        {
                            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, ByteClamp(mark.Red), ByteClamp(mark.Green), ByteClamp(mark.Blue)));
                            brush.Freeze();
                            ctx.DrawEllipse(brush, null, new System.Windows.Point(originX + (mark.X + 0.5) * cell, originY + (mark.Y + 0.5) * cell), r, r);
                        }
                    }

                    if (snap.Eggs != null)
                    {
                        byte alpha = AlphaByte(235, layers.SnakesOpacity);
                        var eggBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 236, 226, 190));
                        eggBrush.Freeze();
                        double rx = cell * 0.34, ry = cell * 0.34;
                        foreach (Znake.Replay.Mark mark in snap.Eggs)
                            ctx.DrawEllipse(eggBrush, null, new System.Windows.Point(originX + (mark.X + 0.5) * cell, originY + (mark.Y + 0.5) * cell), rx, ry);
                    }

                    if (layers.ShowSnakes && layers.SnakesOpacity > 0 && snap.Bodies != null)
                    {
                        byte alpha = AlphaByte(255, layers.SnakesOpacity);
                        double size = Math.Max(1.0, cell - 0.5);
                        foreach (Znake.Replay.Body body in snap.Bodies)
                        {
                            if (body.Points == null || body.Points.Count == 0) continue;
                            byte r = ByteClamp(body.Red), g = ByteClamp(body.Green), b = ByteClamp(body.Blue);
                            byte hr = ByteClamp(r + (int)((255 - r) * 0.25));
                            byte hg = ByteClamp(g + (int)((255 - g) * 0.25));
                            byte hb = ByteClamp(b + (int)((255 - b) * 0.25));
                            var bodyBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, r, g, b));
                            bodyBrush.Freeze();
                            var headBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, hr, hg, hb));
                            headBrush.Freeze();
                            for (int idx = 0; idx < body.Points.Count; idx++)
                            {
                                Znake.Replay.Mark pt = body.Points[idx];
                                ctx.DrawRectangle(idx == 0 ? headBrush : bodyBrush, null,
                                    new Rect(originX + pt.X * cell, originY + pt.Y * cell, size, size));
                            }
                            if (body.Resident)
                            {
                                Znake.Replay.Mark head = body.Points[0];
                                var resBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 250, 236, 160));
                                resBrush.Freeze();
                                var resPen = new System.Windows.Media.Pen(resBrush, 1.5);
                                resPen.Freeze();
                                ctx.DrawRectangle(null, resPen, new Rect(originX + head.X * cell, originY + head.Y * cell, size, size));
                            }
                        }
                    }
                }
            }
            finally
            {
                ctx.Pop();
            }
        }

        private static byte AlphaByte(int baseAlpha, float opacity)
        {
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(baseAlpha * Math.Max(0f, Math.Min(1f, opacity)))));
        }

        private static byte ByteClamp(int value)
        {
            return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
        }

        public void Dispose()
        {
            host.Dispose();
        }

        private sealed class VisualCanvas : FrameworkElement
        {
            private readonly DrawingVisual visual = new DrawingVisual();

            public VisualCanvas()
            {
                AddVisualChild(visual);
            }

            protected override int VisualChildrenCount { get { return 1; } }

            protected override Visual GetVisualChild(int index)
            {
                return visual;
            }

            public DrawingContext Open()
            {
                return visual.RenderOpen();
            }
        }
    }
}
