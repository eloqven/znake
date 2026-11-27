using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Znake.ControlPanel
{
    internal sealed class GdiGridRenderer : IGridRenderer
    {
        private readonly PreviewCanvas canvas = new PreviewCanvas();
        private GridSceneState current;
        private Bitmap cachedBackground;
        private string cachedBackgroundPath;

        public GdiGridRenderer()
        {
            canvas.Dock = DockStyle.Fill;
            canvas.Paint += OnPaint;
        }

        public bool IsGpuAccelerated { get { return false; } }
        public Control ViewControl { get { return canvas; } }

        public void UpdateScene(GridSceneState scene)
        {
            current = scene;
            EnsureBackground(scene == null ? null : scene.BackgroundPath);
            canvas.Invalidate();
        }

        public Bitmap RenderToBitmap(GridSceneState scene, int width, int height)
        {
            var bitmap = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppArgb);
            Bitmap bg = LoadBitmapCopy(scene == null ? null : scene.BackgroundPath);
            try
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                    Draw(g, scene, bg, width, height);
            }
            finally
            {
                if (bg != null) bg.Dispose();
            }
            return bitmap;
        }

        private void OnPaint(object sender, PaintEventArgs e)
        {
            Draw(e.Graphics, current, cachedBackground, canvas.ClientSize.Width, canvas.ClientSize.Height);
        }

        private void EnsureBackground(string path)
        {
            if (String.Equals(cachedBackgroundPath, path, StringComparison.OrdinalIgnoreCase)) return;
            if (cachedBackground != null) { cachedBackground.Dispose(); cachedBackground = null; }
            cachedBackgroundPath = path;
            cachedBackground = LoadBitmapCopy(path);
        }

        private static Bitmap LoadBitmapCopy(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var src = new Bitmap(fs))
                    return new Bitmap(src);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void Draw(Graphics graphics, GridSceneState scene, Bitmap bg, int width, int height)
        {
            Color backdrop = scene == null ? Color.FromArgb(14, 18, 20) : scene.BackdropColor;
            graphics.Clear(backdrop);
            if (scene == null || scene.Start == null || width <= 0 || height <= 0) return;

            LayerSettings layers = scene.Layers ?? new LayerSettings();
            int arenaWidth = scene.Start.Width > 0 ? scene.Start.Width * Math.Max(1, scene.Start.Square) : (bg != null ? bg.Width : 1920);
            int arenaHeight = scene.Start.Height > 0 ? scene.Start.Height * Math.Max(1, scene.Start.Square) : (bg != null ? bg.Height : 1080);
            float scale = Math.Min((float)width / Math.Max(1, arenaWidth), (float)height / Math.Max(1, arenaHeight));
            float drawW = arenaWidth * scale;
            float drawH = arenaHeight * scale;
            float originX = (width - drawW) / 2f;
            float originY = (height - drawH) / 2f;
            float cell = Math.Max(1, scene.Start.Square) * scale;

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (layers.ShowGrid && bg != null)
                graphics.DrawImage(bg, originX, originY, drawW, drawH);
            else if (layers.ShowGrid)
                using (var dark = new SolidBrush(Color.FromArgb(255, 16, 22, 24)))
                    graphics.FillRectangle(dark, originX, originY, drawW, drawH);

            Znake.Replay.Record snap = scene.Snapshot;
            if (snap == null) return;

            GraphicsState saved = graphics.Save();
            graphics.SetClip(new RectangleF(originX, originY, drawW, drawH));

            if (layers.ShowVision && layers.VisionOpacity > 0 && snap.Bodies != null)
            {
                foreach (Znake.Replay.Body body in snap.Bodies)
                {
                    if (body.Points == null || body.Points.Count == 0 || body.VisionRadius <= 0) continue;
                    Znake.Replay.Mark head = body.Points[0];
                    float radius = (float)(Math.Min(100.0, body.VisionRadius) * cell);
                    float cx = originX + (head.X + 0.5f) * cell;
                    float cy = originY + (head.Y + 0.5f) * cell;
                    var bounds = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);
                    int fillAlpha = Alpha(26, layers.VisionOpacity);
                    int ringAlpha = Alpha(92, layers.VisionOpacity);
                    using (var fill = new SolidBrush(Color.FromArgb(fillAlpha, Clamp(body.Red), Clamp(body.Green), Clamp(body.Blue))))
                        graphics.FillEllipse(fill, bounds);
                    using (var pen = new Pen(Color.FromArgb(ringAlpha, Clamp(body.Red), Clamp(body.Green), Clamp(body.Blue)), 1.25f))
                        graphics.DrawEllipse(pen, bounds);
                }
            }

            if (layers.ShowGrid)
            {
                if (layers.ShowVines && layers.VinesOpacity > 0 && snap.Vines != null)
                {
                    int alpha = Alpha(140, layers.VinesOpacity);
                    foreach (Znake.Replay.Mark mark in snap.Vines)
                        using (var brush = new SolidBrush(Color.FromArgb(alpha, Clamp(mark.Red), Clamp(mark.Green), Clamp(mark.Blue))))
                            graphics.FillRectangle(brush, originX + mark.X * cell, originY + mark.Y * cell, Math.Max(1f, cell - 0.5f), Math.Max(1f, cell - 0.5f));
                }

                if (layers.ShowFruits && layers.FruitsOpacity > 0 && snap.Fruits != null)
                {
                    int alpha = Alpha(255, layers.FruitsOpacity);
                    foreach (Znake.Replay.Mark mark in snap.Fruits)
                        using (var brush = new SolidBrush(Color.FromArgb(alpha, Clamp(mark.Red), Clamp(mark.Green), Clamp(mark.Blue))))
                            graphics.FillEllipse(brush, originX + mark.X * cell + cell * 0.18f,
                                originY + mark.Y * cell + cell * 0.18f, cell * 0.64f, cell * 0.64f);
                }

                if (snap.Eggs != null)
                {
                    int alpha = Alpha(235, layers.SnakesOpacity);
                    foreach (Znake.Replay.Mark mark in snap.Eggs)
                        using (var brush = new SolidBrush(Color.FromArgb(alpha, 236, 226, 190)))
                            graphics.FillEllipse(brush, originX + mark.X * cell + cell * 0.16f,
                                originY + mark.Y * cell + cell * 0.16f, cell * 0.68f, cell * 0.68f);
                }

                if (layers.ShowSnakes && layers.SnakesOpacity > 0 && snap.Bodies != null)
                {
                    int alpha = Alpha(255, layers.SnakesOpacity);
                    foreach (Znake.Replay.Body body in snap.Bodies)
                    {
                        if (body.Points == null || body.Points.Count == 0) continue;
                        int r = Clamp(body.Red), g = Clamp(body.Green), b = Clamp(body.Blue);
                        for (int idx = 0; idx < body.Points.Count; idx++)
                        {
                            Znake.Replay.Mark pt = body.Points[idx];
                            Color c = idx == 0
                                ? Color.FromArgb(alpha, Math.Min(255, r + (int)((255 - r) * 0.25f)), Math.Min(255, g + (int)((255 - g) * 0.25f)), Math.Min(255, b + (int)((255 - b) * 0.25f)))
                                : Color.FromArgb(alpha, r, g, b);
                            using (var brush = new SolidBrush(c))
                                graphics.FillRectangle(brush, originX + pt.X * cell, originY + pt.Y * cell, Math.Max(1f, cell - 0.5f), Math.Max(1f, cell - 0.5f));
                        }
                        if (body.Resident)
                        {
                            Znake.Replay.Mark head = body.Points[0];
                            using (var pen = new Pen(Color.FromArgb(alpha, 250, 236, 160), 1.5f))
                                graphics.DrawRectangle(pen, originX + head.X * cell, originY + head.Y * cell, Math.Max(1f, cell - 0.5f), Math.Max(1f, cell - 0.5f));
                        }
                    }
                }
            }

            graphics.Restore(saved);
        }

        private static int Alpha(int baseAlpha, float opacity)
        {
            return Math.Max(0, Math.Min(255, (int)Math.Round(baseAlpha * Math.Max(0f, Math.Min(1f, opacity)))));
        }

        private static int Clamp(int value)
        {
            return value < 0 ? 0 : value > 255 ? 255 : value;
        }

        public void Dispose()
        {
            if (cachedBackground != null) { cachedBackground.Dispose(); cachedBackground = null; }
            canvas.Dispose();
        }

        private sealed class PreviewCanvas : Panel
        {
            public PreviewCanvas()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
            }
        }
    }
}
