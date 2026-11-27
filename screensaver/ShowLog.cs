using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Znake
{
    /// <summary>
    /// Append-only JSON Lines record of an endurance show. One start record, an initial
    /// scene snapshot, a scene snapshot every minute of visible runtime, and one complete
    /// record. The saved first frame is the only desktop sample a later renderer needs.
    /// </summary>
    internal sealed class ShowLog : IDisposable
    {
        internal const int Version = 1;
        private const double DefaultSnapshotSeconds = 60;
        private readonly string folder;
        private readonly StreamWriter writer;
        private readonly string show;
        private readonly double snapshotSeconds;
        private double nextSnapshot;
        private bool completeWritten;

        private ShowLog(string folder, StreamWriter writer, bool startedNew, bool completeWritten,
            double snapshotSeconds)
        {
            this.folder = folder;
            this.writer = writer;
            show = Guid.NewGuid().ToString("N");
            StartedNew = startedNew;
            this.completeWritten = completeWritten;
            this.snapshotSeconds = snapshotSeconds > 0 ? snapshotSeconds : DefaultSnapshotSeconds;
            nextSnapshot = this.snapshotSeconds;
        }

        internal bool StartedNew { get; private set; }
        internal string BackgroundPath { get { return Path.Combine(folder, "show.first-frame.png"); } }
        internal string LogPath { get { return Path.Combine(folder, "show.log.jsonl"); } }

        internal static ShowLog Open(string folder, double snapshotSeconds = DefaultSnapshotSeconds)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "show.log.jsonl");
            bool startedNew = !File.Exists(path) || new FileInfo(path).Length == 0;
            bool completeWritten = HasCompleteRecord(path);
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            return new ShowLog(folder, writer, startedNew, completeWritten, snapshotSeconds);
        }

        private static bool HasCompleteRecord(string path)
        {
            try
            {
                foreach (string line in File.ReadLines(path))
                    if (line.Contains("\"type\":\"complete\"")) return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return false;
        }

        internal static void Clear(string folder)
        {
            foreach (string name in new[] { "show.log.jsonl", "show.first-frame.png" })
            {
                string path = Path.Combine(folder, name);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        internal void Start(World world, SaverSettings settings, Rectangle display, Image background,
            Point gridOffset, double runSeconds = 0)
        {
            bool saved = false;
            if (background != null && StartedNew)
            {
                try
                {
                    background.Save(BackgroundPath, ImageFormat.Png);
                    saved = true;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (ExternalException) { }
            }

            var record = new ShowLogStart
            {
                Show = show,
                UtcTicks = DateTime.UtcNow.Ticks,
                Seed = world.ShowSeed,
                Width = world.Width,
                Height = world.Height,
                Square = world.WidgetSquare,
                Cast = world.ShowCastSize,
                Target = world.ShowGoal,
                RunSeconds = runSeconds,
                TimeScale = settings.TimeScale ?? 1,
                Genes = settings.Genes == null ? new Dictionary<string, double>() : new Dictionary<string, double>(settings.Genes),
                Display = new ShowLogRect { X = display.X, Y = display.Y, Width = display.Width, Height = display.Height },
                GridOffsetX = gridOffset.X,
                GridOffsetY = gridOffset.Y,
                Background = saved ? Path.GetFileName(BackgroundPath) : null,
                Notes = "Scene snapshots carry snake, fruit, egg and vine geometry with RGB colors. " +
                    "gridOffsetX/Y plus display and square map cell coordinates onto the saved first frame. " +
                    "Desktop-derived imprint tints are omitted; render over the saved first frame."
            };
            Write(record);
        }

        internal void Record(World world, double movesPerSecond, bool force = false, double night = 0)
        {
            if (!world.ShowActive) return;
            if (world.ShowComplete)
            {
                if (completeWritten) return;
                completeWritten = true;
            }
            else
            {
                if (!force && world.ShowSeconds < nextSnapshot) return;
                while (nextSnapshot <= world.ShowSeconds) nextSnapshot += snapshotSeconds;
            }

            var record = new ShowLogSnapshot
            {
                Type = world.ShowComplete ? "complete" : "snapshot",
                UtcTicks = DateTime.UtcNow.Ticks,
                VisibleSeconds = world.ShowSeconds,
                Night = night,
                Tick = world.Ticks,
                Moves = world.ShowMoves,
                Population = world.Snakes.Count,
                MovesPerSecond = movesPerSecond,
                Complete = world.ShowComplete,
                Bodies = new List<ShowLogSnake>(world.Snakes.Count),
                Fruits = new List<ShowLogMark>(world.Fruits.Count),
                Eggs = new List<ShowLogMark>(world.Eggs == null ? 0 : world.Eggs.Count),
                Vines = new List<ShowLogMark>()
            };

            foreach (Snake snake in world.Snakes)
            {
                var body = new ShowLogSnake
                {
                    Id = snake.Id,
                    Resident = snake.IsResident,
                    Entering = snake.Entering,
                    Heading = snake.Heading,
                    VisionRadius = snake.EffectiveGene(Gene.VisionRadius, night),
                    Red = Byte(snake.Skin.R),
                    Green = Byte(snake.Skin.G),
                    Blue = Byte(snake.Skin.B),
                    Points = new List<ShowLogPoint>(snake.Body.Count)
                };
                foreach (Cell point in snake.Body)
                    body.Points.Add(new ShowLogPoint { X = point.X, Y = point.Y });
                record.Bodies.Add(body);
            }

            foreach (Fruit fruit in world.Fruits)
                record.Fruits.Add(Mark(fruit.Position, fruit.Tone));
            if (world.Eggs != null)
                foreach (Egg egg in world.Eggs.Values)
                    record.Eggs.Add(Mark(egg.Position, egg.Parent.BirthColor));
            foreach (Vine vine in world.Vines)
                foreach (VineSquare square in vine.Squares)
                    record.Vines.Add(Mark(square.Position, square.Tone));

            Write(record);
        }

        private static ShowLogMark Mark(Cell point, Rgb tone)
        {
            return new ShowLogMark
            {
                X = point.X,
                Y = point.Y,
                Red = Byte(tone.R),
                Green = Byte(tone.G),
                Blue = Byte(tone.B)
            };
        }

        private static int Byte(double value)
        {
            return (int)Math.Round(Math.Max(0, Math.Min(1, value)) * 255);
        }

        private void Write<T>(T record)
        {
            var serializer = new DataContractJsonSerializer(typeof(T),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, record);
                writer.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
            }
        }

        public void Dispose()
        {
            writer.Dispose();
        }
    }

    [DataContract]
    internal sealed class ShowLogStart
    {
        [DataMember(Name = "type")] public string Type = "start";
        [DataMember(Name = "version")] public int Version = ShowLog.Version;
        [DataMember(Name = "show")] public string Show;
        [DataMember(Name = "utcTicks")] public long UtcTicks;
        [DataMember(Name = "seed")] public uint Seed;
        [DataMember(Name = "width")] public int Width;
        [DataMember(Name = "height")] public int Height;
        [DataMember(Name = "square")] public int Square;
        [DataMember(Name = "cast")] public int Cast;
        [DataMember(Name = "target")] public long Target;
        [DataMember(Name = "runSeconds", EmitDefaultValue = false)] public double RunSeconds;
        [DataMember(Name = "timeScale")] public double TimeScale;
        [DataMember(Name = "genes")] public Dictionary<string, double> Genes;
        [DataMember(Name = "display")] public ShowLogRect Display;
        [DataMember(Name = "gridOffsetX")] public int GridOffsetX;
        [DataMember(Name = "gridOffsetY")] public int GridOffsetY;
        [DataMember(Name = "background")] public string Background;
        [DataMember(Name = "notes")] public string Notes;
    }

    [DataContract]
    internal sealed class ShowLogSnapshot
    {
        [DataMember(Name = "type")] public string Type;
        [DataMember(Name = "utcTicks")] public long UtcTicks;
        [DataMember(Name = "visibleSeconds")] public double VisibleSeconds;
        [DataMember(Name = "night", EmitDefaultValue = false)] public double Night;
        [DataMember(Name = "tick")] public long Tick;
        [DataMember(Name = "moves")] public long Moves;
        [DataMember(Name = "population")] public int Population;
        [DataMember(Name = "movesPerSecond")] public double MovesPerSecond;
        [DataMember(Name = "complete")] public bool Complete;
        [DataMember(Name = "bodies")] public List<ShowLogSnake> Bodies;
        [DataMember(Name = "fruits")] public List<ShowLogMark> Fruits;
        [DataMember(Name = "eggs")] public List<ShowLogMark> Eggs;
        [DataMember(Name = "vines")] public List<ShowLogMark> Vines;
    }

    [DataContract]
    internal sealed class ShowLogSnake
    {
        [DataMember(Name = "id")] public long Id;
        [DataMember(Name = "resident")] public bool Resident;
        [DataMember(Name = "entering")] public bool Entering;
        [DataMember(Name = "heading")] public int Heading;
        [DataMember(Name = "visionRadius")] public double VisionRadius;
        [DataMember(Name = "red")] public int Red;
        [DataMember(Name = "green")] public int Green;
        [DataMember(Name = "blue")] public int Blue;
        [DataMember(Name = "points")] public List<ShowLogPoint> Points;
    }

    [DataContract]
    internal struct ShowLogPoint
    {
        [DataMember(Name = "x")] public int X;
        [DataMember(Name = "y")] public int Y;
    }

    [DataContract]
    internal struct ShowLogMark
    {
        [DataMember(Name = "x")] public int X;
        [DataMember(Name = "y")] public int Y;
        [DataMember(Name = "red")] public int Red;
        [DataMember(Name = "green")] public int Green;
        [DataMember(Name = "blue")] public int Blue;
    }

    [DataContract]
    internal struct ShowLogRect
    {
        [DataMember(Name = "x")] public int X;
        [DataMember(Name = "y")] public int Y;
        [DataMember(Name = "width")] public int Width;
        [DataMember(Name = "height")] public int Height;
    }
}
