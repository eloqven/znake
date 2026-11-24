using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Serialization;

namespace Znake
{
    [DataContract]
    internal struct Cell : IEquatable<Cell>
    {
        [DataMember] public int X;
        [DataMember] public int Y;

        public Cell(int x, int y) { X = x; Y = y; }
        public bool Equals(Cell other) { return X == other.X && Y == other.Y; }
        public override bool Equals(object value) { return value is Cell && Equals((Cell)value); }
        public override int GetHashCode() { return unchecked(X * 397 ^ Y); }

        public Cell Move(Cell step) { return new Cell(X + step.X, Y + step.Y); }

        public static readonly Cell[] Directions =
        {
            new Cell(1, 0), new Cell(0, 1), new Cell(-1, 0), new Cell(0, -1)
        };
    }

    [DataContract]
    internal struct Rgb
    {
        [DataMember] public double R;
        [DataMember] public double G;
        [DataMember] public double B;

        public Rgb(double red, double green, double blue) { R = red; G = green; B = blue; }
        public static Rgb FromBytes(int red, int green, int blue) { return new Rgb(red / 255.0, green / 255.0, blue / 255.0); }
        public static readonly Rgb StartingGreen = FromBytes(54, 177, 80);

        public static Rgb Mix(Rgb from, Rgb toward, double amount)
        {
            return new Rgb(from.R + (toward.R - from.R) * amount,
                from.G + (toward.G - from.G) * amount, from.B + (toward.B - from.B) * amount);
        }

        public bool IsValid()
        {
            return R >= 0 && R <= 1 && G >= 0 && G <= 1 && B >= 0 && B <= 1;
        }

        public Color ToColor(double alpha)
        {
            return Color.FromArgb(Byte(alpha), Byte(R), Byte(G), Byte(B));
        }

        private static int Byte(double value) { return (int)Math.Round(Numbers.Unit(value) * 255); }

        public static double Distance(Rgb first, Rgb second)
        {
            Rgb a = first.Oklab();
            Rgb b = second.Oklab();
            double l = a.R - b.R, m = a.G - b.G, s = a.B - b.B;
            return Math.Sqrt(l * l + m * m + s * s);
        }

        private Rgb Oklab()
        {
            double r = Linear(R), g = Linear(G), b = Linear(B);
            double l = Math.Pow(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b, 1.0 / 3);
            double m = Math.Pow(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b, 1.0 / 3);
            double s = Math.Pow(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b, 1.0 / 3);
            return new Rgb(0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
        }

        private static double Linear(double value)
        {
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
    }

    [DataContract]
    internal struct Tint
    {
        [DataMember] public Rgb Color;
        [DataMember] public double Alpha;

        public Tint(Rgb color, double alpha) { Color = color; Alpha = alpha; }
        public Rgb Apply(Rgb background) { return Rgb.Mix(background, Color, Alpha); }

        public Tint Over(Tint lower)
        {
            double remaining = lower.Alpha * (1 - Alpha);
            double weight = Alpha + remaining;
            return weight == 0 ? this : new Tint(Rgb.Mix(lower.Color, Color, Alpha / weight), weight);
        }
    }

    internal sealed class ColorGrid
    {
        private readonly Rgb[] cells;
        public int Width { get; private set; }
        public int Height { get; private set; }

        public ColorGrid(int width, int height)
        {
            Width = width;
            Height = height;
            cells = new Rgb[checked(width * height)];
        }

        public bool Contains(Cell point) { return point.X >= 0 && point.X < Width && point.Y >= 0 && point.Y < Height; }

        public Rgb this[Cell point]
        {
            get { return cells[point.Y * Width + point.X]; }
            set { cells[point.Y * Width + point.X] = value; }
        }

        public static ColorGrid Uniform(int width, int height, Rgb color)
        {
            var grid = new ColorGrid(width, height);
            for (int index = 0; index < grid.cells.Length; index++) grid.cells[index] = color;
            return grid;
        }

        public static ColorGrid Sample(int[] pixels, Size area, int square)
        {
            var grid = new ColorGrid((area.Width + square - 1) / square, (area.Height + square - 1) / square);
            for (int row = 0; row < grid.Height; row++)
            {
                for (int column = 0; column < grid.Width; column++)
                {
                    int x = column * square, y = row * square;
                    int endX = Math.Min(x + square, area.Width), endY = Math.Min(y + square, area.Height);
                    int red = 0, green = 0, blue = 0;
                    for (int scan = y; scan < endY; scan++)
                    {
                        int start = scan * area.Width + x;
                        for (int index = start; index < start + endX - x; index++)
                        {
                            int pixel = pixels[index];
                            red += (pixel >> 16) & 255;
                            green += (pixel >> 8) & 255;
                            blue += pixel & 255;
                        }
                    }
                    double scale = 255.0 * (endX - x) * (endY - y);
                    grid[new Cell(column, row)] = new Rgb(red / scale, green / scale, blue / scale);
                }
            }
            return grid;
        }
    }

    internal sealed class ColorField
    {
        private readonly ColorGrid raw;
        private readonly ColorGrid previous;
        private readonly Dictionary<Cell, Tint> sensed = new Dictionary<Cell, Tint>();
        private readonly Dictionary<Cell, Tint> visible = new Dictionary<Cell, Tint>();

        public ColorField(ColorGrid raw, ColorGrid previous, IEnumerable<KeyValuePair<Cell, Tint>> imprints)
        {
            this.raw = raw;
            this.previous = previous;
            if (imprints == null) return;
            foreach (var imprint in imprints)
                Add(imprint.Key, imprint.Value, true);
        }

        public int Width { get { return raw.Width; } }
        public int Height { get { return raw.Height; } }
        public bool Contains(Cell point) { return raw.Contains(point); }

        public void Add(Cell point, Tint tint, bool imprint)
        {
            if (!Contains(point)) return;
            AddTo(sensed, point, tint);
            if (!imprint) AddTo(visible, point, tint);
        }

        private static void AddTo(Dictionary<Cell, Tint> field, Cell point, Tint tint)
        {
            Tint old;
            field.TryGetValue(point, out old);
            field[point] = tint.Over(old);
        }

        public Tint DebugTint(Cell point)
        {
            Tint tint;
            sensed.TryGetValue(point, out tint);
            return tint;
        }

        public IEnumerable<KeyValuePair<Cell, Tint>> Tints(bool debug) { return debug ? sensed : visible; }
        public Rgb At(Cell point) { return DebugTint(point).Apply(raw[point]); }

        public Rgb Average(IList<Cell> body)
        {
            var average = new Rgb();
            int inside = 0;
            foreach (Cell point in body)
            {
                if (Contains(point)) average = Rgb.Mix(average, At(point), 1.0 / ++inside);
            }
            return average;
        }

        public double Flicker(Cell point)
        {
            return previous != null && previous.Contains(point)
                ? Rgb.Distance(previous[point], raw[point]) : 0;
        }
    }

    internal struct LightResponse
    {
        public double Feed;
        public double Pressure;

        public static LightResponse Sense(Rgb skin, Rgb environment)
        {
            double distance = Rgb.Distance(skin, environment);
            const double feedingThreshold = 0.20, stressThreshold = 0.29;
            if (distance < feedingThreshold)
            {
                double match = 1 - distance / feedingThreshold;
                return new LightResponse { Feed = match * match };
            }
            return new LightResponse { Pressure = Numbers.Unit((distance - stressThreshold) / 0.42) };
        }
    }
}
