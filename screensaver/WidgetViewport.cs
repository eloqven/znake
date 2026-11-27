using System;
using System.Drawing;

namespace Znake
{
    internal struct WidgetViewport
    {
        public const int DefaultSquare = 24;
        public const int SmallestSquare = 12;
        public const int LargestSquare = 32;
        private const int Inset = 16;

        public readonly Rectangle Bounds;
        public readonly int Square;
        public readonly bool FullScreen;
        public readonly Point GridOffset;
        public readonly Point Anchor;
        private readonly Cell origin;

        private WidgetViewport(Rectangle bounds, int square, bool fullScreen, Point anchor)
        {
            Bounds = bounds;
            Square = square;
            FullScreen = fullScreen;
            Anchor = anchor;
            GridOffset = new Point(Offset(bounds.Left - anchor.X, square), Offset(bounds.Top - anchor.Y, square));
            origin = new Cell((bounds.Left + GridOffset.X - anchor.X) / square,
                (bounds.Top + GridOffset.Y - anchor.Y) / square);
        }

        public int Columns { get { return (Bounds.Width - GridOffset.X + Square - 1) / Square; } }
        public int Rows { get { return (Bounds.Height - GridOffset.Y + Square - 1) / Square; } }

        public static WidgetViewport Create(Rectangle screen, Rectangle work, int columns, int rows,
            int square, bool fullScreen, Point? requestedAnchor = null)
        {
            var anchor = new Point(Math.Max(work.Right - Inset, work.Left + 1),
                Math.Max(work.Bottom - Inset, work.Top + 1));
            if (requestedAnchor.HasValue)
                anchor = new Point(Math.Max(work.Left + 1, Math.Min(work.Right, requestedAnchor.Value.X)),
                    Math.Max(work.Top + 1, Math.Min(work.Bottom, requestedAnchor.Value.Y)));
            if (fullScreen) return new WidgetViewport(screen, square, true, anchor);

            int width = Extent(anchor.X - work.Left, columns, square);
            int height = Extent(anchor.Y - work.Top, rows, square);
            return new WidgetViewport(new Rectangle(anchor.X - width, anchor.Y - height, width, height),
                square, false, anchor);
        }

        public Cell ShiftTo(WidgetViewport next)
        {
            return new Cell(origin.X - next.origin.X, origin.Y - next.origin.Y);
        }

        public WidgetViewport Drag(Point delta, Rectangle work)
        {
            if (FullScreen) return this;
            int x = Math.Max(work.Left, Math.Min(work.Right - Bounds.Width, Bounds.Left + delta.X));
            int y = Math.Max(work.Top, Math.Min(work.Bottom - Bounds.Height, Bounds.Top + delta.Y));
            var anchor = new Point(Anchor.X + x - Bounds.Left, Anchor.Y + y - Bounds.Top);
            return new WidgetViewport(new Rectangle(x, y, Bounds.Width, Bounds.Height), Square, false, anchor);
        }

        private static int Offset(int delta, int square)
        {
            return -((delta % square + square) % square);
        }

        private static int Extent(int available, int cells, int square)
        {
            if (available < square) return available;
            return Math.Max(1, Math.Min(cells, available / square)) * square;
        }
    }
}
