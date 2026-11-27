using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Znake
{
    internal static class BrowserSnakeSkin
    {
        // The regular-size snake and berry renderer in snake_fibonacci_subpixel_tail_fusion_fixed.html.
        public static void Draw(Graphics graphics, World world, double night, int square, Point offset, long? inspectedSnake = null)
        {
            GraphicsState state = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                DrawBerries(graphics, world, square, offset);
                float pad = Math.Max(0.12f, square * 0.075f);
                float width = square - 2 * pad;
                using (var tint = new SolidBrush(Color.Transparent))
                using (var eye = new SolidBrush(Color.FromArgb(23, 37, 44)))
                using (var pen = new Pen(Color.Transparent))
                {
                    foreach (Snake snake in world.Snakes)
                    {
                        for (int index = snake.Body.Count - 1; index >= 0; index--)
                        {
                            Cell cell = snake.Body[index];
                            if (!world.Contains(cell)) continue;
                            var bounds = new RectangleF(offset.X + cell.X * square + pad,
                                offset.Y + cell.Y * square + pad, width, width);
                            double? split = snake.SplitOpacity(index);
                            double alpha = split ?? (index == snake.Body.Count - 1
                                ? snake.BrowserTailOpacity(night)
                                : index == 0 ? 1 : 0.65);
                            using (GraphicsPath shape = Rounded(bounds, Math.Max(0.5f, width * 0.20f)))
                            {
                                tint.Color = snake.Skin.ToColor(alpha);
                                graphics.FillPath(tint, shape);
                                if (width >= 5.5)
                                {
                                    pen.Color = snake.Skin.ToColor(split.HasValue ? alpha * 0.5 : 0.5);
                                    pen.Width = Math.Min(1.5f, Math.Max(0.5f, width * 0.065f));
                                    graphics.DrawPath(pen, shape);
                                }
                                if (index != 0) continue;

                                bool selected = snake.Id == inspectedSnake;
                                pen.Color = selected ? Color.FromArgb(204, Color.White) : Color.FromArgb(170, 16, 35, 44);
                                if (split.HasValue) pen.Color = Color.FromArgb((int)(pen.Color.A * alpha), pen.Color);
                                pen.Width = selected ? Math.Max(1.4f, width * 0.16f) : Math.Max(0.8f, width * 0.07f);
                                graphics.DrawPath(pen, shape);
                                Cell heading = Cell.Directions[snake.Heading];
                                DrawHead(graphics, eye, bounds, heading);
                                double stress = snake.SmoothedStress ?? Numbers.Unit(snake.Stress / snake.Capacity(night, false));
                                int gray = (int)Math.Round((1 - stress) * 255);
                                tint.Color = Color.FromArgb(gray, gray, gray);
                                float margin = width * 0.2f;
                                if (heading.X != 0)
                                    graphics.FillRectangle(tint, heading.X > 0 ? bounds.Right - 1 : bounds.Left,
                                        bounds.Top + margin, 1, width - 2 * margin);
                                else
                                    graphics.FillRectangle(tint, bounds.Left + margin,
                                        heading.Y > 0 ? bounds.Bottom - 1 : bounds.Top, width - 2 * margin, 1);
                            }
                        }
                    }
                }
            }
            finally { graphics.Restore(state); }
        }

        private static void DrawHead(Graphics graphics, Brush eye, RectangleF bounds, Cell direction)
        {
            float width = bounds.Width;
            float radius = Math.Max(0.7f, width * 0.08f);
            float x = bounds.X + width * (direction.X == 0 ? 0.48f : direction.X > 0 ? 0.7f : 0.3f);
            float y = bounds.Y + width * (direction.Y == 0 ? 0.48f : direction.Y > 0 ? 0.7f : 0.3f);
            for (int side = -1; side <= 1; side += 2)
                graphics.FillEllipse(eye, x + (direction.X == 0 ? side * width * 0.2f : 0) - radius,
                    y + (direction.Y == 0 ? side * width * 0.2f : 0) - radius, radius * 2, radius * 2);
        }

        private static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            float diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawBerries(Graphics graphics, World world, int square, Point offset)
        {
            float radius = (float)(Math.Max(1.8, square * 0.28) *
                (1 + 0.09 * Math.Sin(world.Ticks * World.StepSeconds / 0.34)));
            foreach (Fruit fruit in world.Fruits)
            {
                float x = offset.X + (fruit.Position.X + 0.5f) * square;
                float y = offset.Y + (fruit.Position.Y + 0.5f) * square;
                float glowSize = radius + 6;
                double alpha = Numbers.Unit(fruit.Remaining);
                using (var halo = new GraphicsPath())
                {
                    halo.AddEllipse(x - glowSize, y - glowSize, glowSize * 2, glowSize * 2);
                    using (var glow = new PathGradientBrush(halo))
                    {
                        glow.CenterColor = fruit.Tone.ToColor(alpha * 0.6);
                        glow.SurroundColors = new[] { fruit.Tone.ToColor(0) };
                        graphics.FillPath(glow, halo);
                    }
                }
                using (var fill = new SolidBrush(fruit.Tone.ToColor(alpha)))
                    graphics.FillEllipse(fill, x - radius, y - radius, radius * 2, radius * 2);
            }
        }
    }
}
