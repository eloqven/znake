using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Znake
{
    internal sealed class ManualHealthOverlay : Form
    {
        private DibSurface paint;
        private Size paintedSize;
        private int lastPercent = -1;
        private Rectangle lastBounds;

        public ManualHealthOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Text = "Znake Health";
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WindowsDesktop.LayeredWindowStyle | 0x08000000 | 0x00000080 | 0x00000020;
                return parameters;
            }
        }

        internal static Rectangle Place(Rectangle sandbox, Rectangle screen, bool fullScreen)
        {
            Rectangle area = fullScreen ? screen : sandbox;
            int width = Math.Min(144, area.Width);
            const int height = 18, gap = 5;
            int x = area.Left + (area.Width - width) / 2;
            int y = fullScreen ? screen.Top : Math.Max(screen.Top, area.Top - height - gap);
            return new Rectangle(x, y, width, height);
        }

        internal static double Calm(Snake snake, double night)
        {
            return 1 - Numbers.Unit(snake.Stress / snake.Capacity(night, false));
        }

        public bool Present(Form owner, Rectangle bounds, double calm)
        {
            int percent = (int)Math.Round(100 * calm);
            if (Visible && bounds == lastBounds && percent == lastPercent) return true;
            if (!Visible)
            {
                Bounds = bounds;
                Show(owner);
                if (!WindowsDesktop.ExcludeFromCapture(Handle)) return false;
            }
            if (paint == null || paintedSize != bounds.Size)
            {
                if (paint != null) paint.Dispose();
                paint = new DibSurface(bounds.Size);
                paintedSize = bounds.Size;
                lastPercent = -1;
            }
            if (percent != lastPercent)
            {
                using (Graphics graphics = Graphics.FromImage(paint.Image))
                using (var fill = new SolidBrush(Color.FromArgb(192, 42, 62)))
                using (var label = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    graphics.Clear(Color.FromArgb(28, 30, 35));
                    graphics.FillRectangle(fill, 1, 1, bounds.Width - 2, bounds.Height - 2);
                    fill.Color = Color.FromArgb(26, 129, 73);
                    graphics.FillRectangle(fill, 1, 1, (bounds.Width - 2) * percent / 100, bounds.Height - 2);
                    graphics.DrawString((percent / 100.0).ToString("0.00", CultureInfo.InvariantCulture), Font,
                        Brushes.White, new RectangleF(0, 0, bounds.Width, bounds.Height), label);
                    graphics.Flush(FlushIntention.Sync);
                }
            }
            bool updated = paint.Publish(Handle, bounds.Location);
            bool moved = bounds != lastBounds;
            lastBounds = bounds;
            lastPercent = percent;
            return updated && (!moved || WindowsDesktop.RaiseOverlay(Handle));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && paint != null)
            {
                paint.Dispose();
                paint = null;
            }
            base.Dispose(disposing);
        }
    }
}
