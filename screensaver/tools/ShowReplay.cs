using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Diagnostics;

namespace Znake.Replay
{
    [DataContract]
    internal sealed class Record
    {
        [DataMember(Name = "type")] public string Type;
        [DataMember(Name = "version")] public int Version;
        [DataMember(Name = "show")] public string Show;
        [DataMember(Name = "utcTicks")] public long UtcTicks;
        [DataMember(Name = "seed")] public uint Seed;
        [DataMember(Name = "width")] public int Width;
        [DataMember(Name = "height")] public int Height;
        [DataMember(Name = "square")] public int Square;
        [DataMember(Name = "cast")] public int Cast;
        [DataMember(Name = "target")] public long Target;
        [DataMember(Name = "runSeconds")] public double RunSeconds;
        [DataMember(Name = "timeScale")] public double TimeScale;
        [DataMember(Name = "genes")] public Dictionary<string, double> Genes;
        [DataMember(Name = "display")] public ShowRect Display;
        [DataMember(Name = "gridOffsetX")] public int GridOffsetX;
        [DataMember(Name = "gridOffsetY")] public int GridOffsetY;
        [DataMember(Name = "background")] public string Background;
        [DataMember(Name = "notes")] public string Notes;
        [DataMember(Name = "visibleSeconds")] public double VisibleSeconds;
        [DataMember(Name = "tick")] public long Tick;
        [DataMember(Name = "moves")] public long Moves;
        [DataMember(Name = "population")] public int Population;
        [DataMember(Name = "movesPerSecond")] public double MovesPerSecond;
        [DataMember(Name = "complete")] public bool Complete;
        [DataMember(Name = "bodies")] public List<Body> Bodies;
        [DataMember(Name = "fruits")] public List<Mark> Fruits;
        [DataMember(Name = "eggs")] public List<Mark> Eggs;
        [DataMember(Name = "vines")] public List<Mark> Vines;
    }

    [DataContract]
    internal sealed class Body
    {
        [DataMember(Name = "id")] public long Id;
        [DataMember(Name = "resident")] public bool Resident;
        [DataMember(Name = "entering")] public bool Entering;
        [DataMember(Name = "heading")] public int Heading;
        [DataMember(Name = "red")] public int Red;
        [DataMember(Name = "green")] public int Green;
        [DataMember(Name = "blue")] public int Blue;
        [DataMember(Name = "points")] public List<Mark> Points;
    }

    [DataContract]
    internal struct Mark
    {
        [DataMember(Name = "x")] public int X;
        [DataMember(Name = "y")] public int Y;
        [DataMember(Name = "red")] public int Red;
        [DataMember(Name = "green")] public int Green;
        [DataMember(Name = "blue")] public int Blue;
    }

    [DataContract]
    internal sealed class ShowRect
    {
        [DataMember(Name = "x")] public int X;
        [DataMember(Name = "y")] public int Y;
        [DataMember(Name = "width")] public int Width;
        [DataMember(Name = "height")] public int Height;
    }

    internal static class Program
    {
        private static int Main(string[] args)
        {
            string log = null, output = null, frames = null, backgroundOverride = null;
            double hold = 1.0;
            int fps = 30;
            bool keepFrames = false;
            for (int index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--log": log = Next(args, ref index); break;
                    case "--out": output = Next(args, ref index); break;
                    case "--frames": frames = Next(args, ref index); break;
                    case "--background": backgroundOverride = Next(args, ref index); break;
                    case "--hold": hold = Double.Parse(Next(args, ref index), CultureInfo.InvariantCulture); break;
                    case "--fps": fps = Int32.Parse(Next(args, ref index), CultureInfo.InvariantCulture); break;
                    case "--keep-frames": keepFrames = true; break;
                    default:
                        Console.Error.WriteLine("Unknown argument: " + args[index]);
                        return 2;
                }
            }

            if (log == null)
            {
                log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Znake", "Widget", "Show", "show.log.jsonl");
            }
            if (!File.Exists(log))
            {
                Console.Error.WriteLine("No show log at " + log);
                return 1;
            }
            if (hold <= 0) hold = 1;
            if (fps <= 0) fps = 30;

            string folder = Path.GetDirectoryName(Path.GetFullPath(log));
            if (frames == null) frames = Path.Combine(folder, "replay-frames");
            if (output == null) output = Path.Combine(folder, "show-replay.mp4");
            Directory.CreateDirectory(frames);

            List<Record> records = ReadLog(log);
            Record start = records.FirstOrDefault(record => record.Type == "start");
            if (start == null)
            {
                Console.Error.WriteLine("The log has no start record.");
                return 1;
            }
            List<Record> scenes = records
                .Where(record => record.Type == "snapshot" || record.Type == "complete")
                .OrderBy(record => record.VisibleSeconds)
                .ToList();
            if (scenes.Count == 0)
            {
                Console.Error.WriteLine("The log has no scene snapshots.");
                return 1;
            }

            string backgroundPath = backgroundOverride != null
                ? (backgroundOverride == "none" ? null : backgroundOverride)
                : (start.Background == null ? null : Path.Combine(folder, start.Background));
            using (Bitmap background = LoadBackground(backgroundPath, start))
            {
                Console.WriteLine("show " + start.Show + " seed " + start.Seed + " cast " + start.Cast +
                    " target " + start.Target + " scenes " + scenes.Count);
                for (int index = 0; index < scenes.Count; index++)
                {
                    string path = Path.Combine(frames, "frame-" + index.ToString("D6", CultureInfo.InvariantCulture) + ".png");
                    using (Bitmap frame = new Bitmap(background.Width, background.Height, PixelFormat.Format32bppArgb))
                    using (Graphics graphics = Graphics.FromImage(frame))
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.DrawImageUnscaled(background, 0, 0);
                        graphics.CompositingMode = CompositingMode.SourceOver;
                        DrawScene(graphics, start, scenes[index]);
                        frame.Save(path, ImageFormat.Png);
                    }
                    if (index % 100 == 0 || index == scenes.Count - 1)
                        Console.WriteLine("frame " + (index + 1) + "/" + scenes.Count + " t=" +
                            scenes[index].VisibleSeconds.ToString("F1", CultureInfo.InvariantCulture) + "s");
                }

                string list = Path.Combine(frames, "frames.txt");
                using (var writer = new StreamWriter(list, false, new UTF8Encoding(false)))
                {
                    for (int index = 0; index < scenes.Count; index++)
                    {
                        writer.WriteLine("file '" + Path.Combine(frames, "frame-" + index.ToString("D6", CultureInfo.InvariantCulture) + ".png").Replace("'", "'\\''") + "'");
                        writer.WriteLine("duration " + hold.ToString("0.###", CultureInfo.InvariantCulture));
                    }
                    writer.WriteLine("file '" + Path.Combine(frames, "frame-" + (scenes.Count - 1).ToString("D6", CultureInfo.InvariantCulture) + ".png").Replace("'", "'\\''") + "'");
                }

                string ffmpeg = FindFfmpeg();
                if (ffmpeg == null)
                {
                    Console.WriteLine("Frames written to " + frames + "; ffmpeg was not found, so no mp4 was encoded.");
                    return 0;
                }
                string arguments = "-y -f concat -safe 0 -i \"" + list + "\" -vf \"fps=" + fps +
                    ",format=yuv420p\" -c:v libx264 -preset medium -crf 18 -movflags +faststart \"" + output + "\"";
                var info = new ProcessStartInfo(ffmpeg, arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };
                using (Process process = Process.Start(info))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0) return process.ExitCode;
                }
                Console.WriteLine("wrote " + output);
                if (!keepFrames)
                {
                    try { Directory.Delete(frames, true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            return 0;
        }

        private static string Next(string[] args, ref int index)
        {
            if (index + 1 >= args.Length) throw new ArgumentException("Missing value after " + args[index]);
            return args[++index];
        }

        private static List<Record> ReadLog(string path)
        {
            var records = new List<Record>();
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(file, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (String.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var serializer = new DataContractJsonSerializer(typeof(Record));
                        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                            records.Add((Record)serializer.ReadObject(stream));
                    }
                    catch (SerializationException)
                    {
                        // A live writer can leave the final line incomplete; keep what is complete.
                    }
                }
            }
            return records;
        }

        private static Bitmap LoadBackground(string path, Record start)
        {
            int width = start.Display == null || start.Display.Width <= 0 ? 1920 : start.Display.Width;
            int height = start.Display == null || start.Display.Height <= 0 ? 1080 : start.Display.Height;
            var background = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            if (path != null && File.Exists(path))
            {
                using (var source = new Bitmap(path))
                using (Graphics graphics = Graphics.FromImage(background))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(source, 0, 0, width, height);
                }
            }
            else
            {
                using (Graphics graphics = Graphics.FromImage(background))
                    graphics.Clear(Color.FromArgb(255, 12, 16, 18));
            }
            return background;
        }

        private static void DrawScene(Graphics graphics, Record start, Record scene)
        {
            int square = start.Square > 0 ? start.Square : 24;
            int offsetX = start.GridOffsetX;
            int offsetY = start.GridOffsetY;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            if (scene.Vines != null)
                foreach (Mark mark in scene.Vines)
                    using (var brush = new SolidBrush(Color.FromArgb(110, Clamp(mark.Red), Clamp(mark.Green), Clamp(mark.Blue))))
                        graphics.FillRectangle(brush, offsetX + mark.X * square, offsetY + mark.Y * square, square - 1, square - 1);

            if (scene.Fruits != null)
                foreach (Mark mark in scene.Fruits)
                    using (var brush = new SolidBrush(Color.FromArgb(255, Clamp(mark.Red), Clamp(mark.Green), Clamp(mark.Blue))))
                        graphics.FillEllipse(brush, offsetX + mark.X * square + square * 0.18f,
                            offsetY + mark.Y * square + square * 0.18f, square * 0.64f, square * 0.64f);

            if (scene.Eggs != null)
                foreach (Mark mark in scene.Eggs)
                {
                    var bounds = new RectangleF(offsetX + mark.X * square + square * 0.16f,
                        offsetY + mark.Y * square + square * 0.16f, square * 0.68f, square * 0.68f);
                    using (var brush = new SolidBrush(Color.FromArgb(235, 236, 226, 190)))
                        graphics.FillEllipse(brush, bounds);
                    using (var pen = new Pen(Color.FromArgb(220, Clamp(mark.Red), Clamp(mark.Green), Clamp(mark.Blue)), 2))
                        graphics.DrawEllipse(pen, bounds);
                }

            if (scene.Bodies != null)
                foreach (Body body in scene.Bodies)
                {
                    if (body.Points == null || body.Points.Count == 0) continue;
                    Color skin = Color.FromArgb(255, Clamp(body.Red), Clamp(body.Green), Clamp(body.Blue));
                    Color head = Lighten(skin, 0.25f);
                    for (int index = 0; index < body.Points.Count; index++)
                    {
                        Mark point = body.Points[index];
                        var bounds = new RectangleF(offsetX + point.X * square + 0.5f,
                            offsetY + point.Y * square + 0.5f, square - 1, square - 1);
                        using (var brush = new SolidBrush(index == 0 ? head : skin))
                            graphics.FillRectangle(brush, bounds);
                    }
                    if (body.Resident)
                    {
                        Mark headPoint = body.Points[0];
                        using (var pen = new Pen(Color.FromArgb(235, 250, 236, 160), 2))
                            graphics.DrawRectangle(pen, offsetX + headPoint.X * square, offsetY + headPoint.Y * square,
                                square - 1, square - 1);
                    }
                    DrawEyes(graphics, body, offsetX, offsetY, square);
                }

            DrawHud(graphics, start, scene, square);
        }

        private static void DrawEyes(Graphics graphics, Body body, int offsetX, int offsetY, int square)
        {
            if (body.Points == null || body.Points.Count == 0) return;
            Mark head = body.Points[0];
            float x = offsetX + head.X * square;
            float y = offsetY + head.Y * square;
            float size = Math.Max(2, square * 0.16f);
            float near = square * 0.28f;
            float far = square * 0.62f;
            PointF first, second;
            switch (body.Heading)
            {
                case 0: first = new PointF(x + far, y + near); second = new PointF(x + far, y + far); break;
                case 1: first = new PointF(x + near, y + far); second = new PointF(x + far, y + far); break;
                case 2: first = new PointF(x + near, y + near); second = new PointF(x + near, y + far); break;
                default: first = new PointF(x + near, y + near); second = new PointF(x + far, y + near); break;
            }
            using (var brush = new SolidBrush(Color.FromArgb(240, 20, 24, 22)))
            {
                graphics.FillEllipse(brush, first.X - size / 2, first.Y - size / 2, size, size);
                graphics.FillEllipse(brush, second.X - size / 2, second.Y - size / 2, size, size);
            }
        }

        private static void DrawHud(Graphics graphics, Record start, Record scene, int square)
        {
            int x = start.Display == null ? 24 : start.Display.X + 24;
            int y = start.Display == null ? 20 : start.Display.Y + 20;
            float size = Math.Max(14, square * 0.82f);
            using (var panel = new SolidBrush(Color.FromArgb(170, 8, 12, 12)))
                graphics.FillRectangle(panel, x - 10, y - 8, 640, 124);
            using (var titleFont = new Font("Consolas", size + 2, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var font = new Font("Consolas", size, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var muted = new SolidBrush(Color.FromArgb(190, 220, 226, 210)))
            using (var bright = new SolidBrush(Color.FromArgb(245, 238, 244, 232)))
            using (var accent = new SolidBrush(Color.FromArgb(255, 150, 224, 168)))
            {
                int cast = start.Cast > 0 ? start.Cast : 55;
                graphics.DrawString(cast + "-SNAKE SHOW" + (scene.Complete ? " COMPLETE" : ""),
                    titleFont, accent, x, y);
                graphics.DrawString("MOVES " + scene.Moves.ToString("N0", CultureInfo.InvariantCulture) +
                    "    SNAKES " + scene.Population, font, bright, x, y + 28);
                graphics.DrawString(scene.MovesPerSecond.ToString("F1", CultureInfo.InvariantCulture) +
                    " MOVES/S    TICK " + scene.Tick.ToString("N0", CultureInfo.InvariantCulture),
                    font, bright, x, y + 54);
                string time = "TIME " + FormatTime(scene.VisibleSeconds);
                if (start.RunSeconds > 0) time += " / " + FormatTime(start.RunSeconds);
                graphics.DrawString(time, font, muted, x, y + 80);
            }
        }

        private static string FormatTime(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            if (span.TotalHours >= 24)
                return ((int)span.TotalDays) + "d " + span.Hours.ToString("00", CultureInfo.InvariantCulture) + ":" +
                    span.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
                    span.Seconds.ToString("00", CultureInfo.InvariantCulture);
            return span.Hours.ToString("00", CultureInfo.InvariantCulture) + ":" +
                span.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
                span.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }

        private static int Clamp(int value)
        {
            return value < 0 ? 0 : value > 255 ? 255 : value;
        }

        private static Color Lighten(Color color, float amount)
        {
            return Color.FromArgb(color.A,
                color.R + (int)((255 - color.R) * amount),
                color.G + (int)((255 - color.G) * amount),
                color.B + (int)((255 - color.B) * amount));
        }

        private static string FindFfmpeg()
        {
            string path = Environment.GetEnvironmentVariable("PATH");
            if (path != null)
                foreach (string folder in path.Split(Path.PathSeparator))
                {
                    try
                    {
                        string candidate = Path.Combine(folder, "ffmpeg.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch (ArgumentException) { }
                }
            return null;
        }
    }
}
