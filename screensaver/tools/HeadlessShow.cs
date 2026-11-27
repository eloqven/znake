using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace Znake.Headless
{
    [DataContract]
    internal sealed class ProgressRecord
    {
        [DataMember(Name = "show")] public string Show;
        [DataMember(Name = "status")] public string Status;
        [DataMember(Name = "startedUtc")] public string StartedUtc;
        [DataMember(Name = "updatedUtc")] public string UpdatedUtc;
        [DataMember(Name = "gameClockStart")] public string GameClockStart;
        [DataMember(Name = "gameSeconds")] public double GameSeconds;
        [DataMember(Name = "totalGameSeconds")] public double TotalGameSeconds;
        [DataMember(Name = "wallSeconds")] public double WallSeconds;
        [DataMember(Name = "percent")] public double Percent;
        [DataMember(Name = "remainingGameSeconds")] public double RemainingGameSeconds;
        [DataMember(Name = "steps")] public long Steps;
        [DataMember(Name = "totalSteps")] public long TotalSteps;
        [DataMember(Name = "moves")] public long Moves;
        [DataMember(Name = "movesPerSecond")] public double MovesPerSecond;
        [DataMember(Name = "population")] public int Population;
        [DataMember(Name = "originalAlive")] public int OriginalAlive;
        [DataMember(Name = "cast")] public int Cast;
        [DataMember(Name = "tick")] public long Tick;
        [DataMember(Name = "seed")] public uint Seed;
        [DataMember(Name = "pace")] public double Pace;
        [DataMember(Name = "background")] public string Background;
        [DataMember(Name = "log")] public string Log;
        [DataMember(Name = "mp4")] public string Mp4;
        [DataMember(Name = "replayScript")] public string ReplayScript;
    }

    [DataContract]
    internal sealed class SavedWorld
    {
        [DataMember(Name = "world")] public World World { get; set; }
    }

    internal static class Program
    {
        private static volatile bool stopping;

        [STAThread]
        private static int Main(string[] args)
        {
            string background = null, folder = null, replayExe = null, savedSnakePath = null;
            double seconds = 7200, snapshot = 10, progress = 2, statusEvery = 300, hold = 0.2;
            double pace = 0;
            int cast = 89, square = 24, fps = 30;
            uint seed = 20261127;
            bool replay = true;

            for (int index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--background": background = Next(args, ref index); break;
                    case "--folder": folder = Next(args, ref index); break;
                    case "--seconds": seconds = ParseDouble(Next(args, ref index)); break;
                    case "--cast": cast = ParseInt(Next(args, ref index)); break;
                    case "--square": square = ParseInt(Next(args, ref index)); break;
                    case "--seed": seed = ParseUInt(Next(args, ref index)); break;
                    case "--snapshot": snapshot = ParseDouble(Next(args, ref index)); break;
                    case "--progress": progress = ParseDouble(Next(args, ref index)); break;
                    case "--status": statusEvery = ParseDouble(Next(args, ref index)); break;
                    case "--pace": pace = ParseDouble(Next(args, ref index)); break;
                    case "--hold": hold = ParseDouble(Next(args, ref index)); break;
                    case "--fps": fps = ParseInt(Next(args, ref index)); break;
                    case "--replay-exe": replayExe = Next(args, ref index); break;
                    case "--saved-snake": savedSnakePath = Next(args, ref index); break;
                    case "--no-replay": replay = false; break;
                    default:
                        Console.Error.WriteLine("Unknown argument: " + args[index]);
                        return 2;
                }
            }

            folder = folder ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "show-89");
            background = background ?? Path.Combine(folder, "background.png");
            replayExe = replayExe ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShowReplay.exe");
            savedSnakePath = savedSnakePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Znake", "Widget", "world.json");

            if (!File.Exists(background))
            {
                Console.Error.WriteLine("No background image at " + background);
                return 1;
            }
            if (seconds <= 0) seconds = 7200;
            if (cast < 1) cast = 89;
            if (square < 4) square = 24;
            if (snapshot <= 0) snapshot = 10;
            if (progress <= 0) progress = 2;
            if (statusEvery <= 0) statusEvery = 300;
            if (pace < 0) pace = 0;
            if (hold <= 0) hold = 0.2;
            if (fps <= 0) fps = 30;

            long totalSteps = Math.Max(1L, (long)Math.Round(seconds / World.StepSeconds));
            seconds = totalSteps * World.StepSeconds;

            Directory.CreateDirectory(folder);
            ShowLog.Clear(folder);
            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs cancel)
            {
                cancel.Cancel = true;
                stopping = true;
            };

            string startedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            DateTimeOffset gameClockStart = DateTimeOffset.Now;
            string mp4 = Path.Combine(folder, "show-89.mp4");
            string replayScript = Path.Combine(folder, "replay.ps1");
            string logPath = Path.Combine(folder, "show.log.jsonl");
            World world = null;
            HashSet<long> initial = null;
            string state = "starting";
            long steps = 0;
            double gameSeconds = 0, rate = 0;
            var clock = new Stopwatch();

            try
            {
                using (var source = new Bitmap(background))
                {
                    ColorGrid grid = Sample(source, square);
                    var settings = new SaverSettings { TimeScale = 1 };
                    world = new World(grid.Width, grid.Height, seed, settings.BirthOverrides(), square);
                    world.StartShow(cast, true);
                    Snake savedSnake = LoadSavedSnake(savedSnakePath);
                    if (savedSnake != null)
                        AdoptSavedSnake(world, savedSnake);
                    EnsureDistinctGenomes(world);
                    initial = new HashSet<long>(world.Snakes.Select(snake => snake.Id));

                    Console.WriteLine("background " + background + " " + source.Width + "x" + source.Height);
                    Console.WriteLine("grid " + grid.Width + "x" + grid.Height + " square " + square +
                        " cast " + world.Snakes.Count + " seed " + seed);
                    Console.WriteLine("saved snake " + (savedSnake == null ? "not found at " + savedSnakePath :
                        "id " + savedSnake.Id + " adopted as resident"));
                    Console.WriteLine("game clock " + gameClockStart.ToString("o", CultureInfo.InvariantCulture) +
                        " duration " + Format(seconds) + " steps " + totalSteps +
                        " pace " + (pace > 0 ? pace.ToString("0.###", CultureInfo.InvariantCulture) + "x" : "unlimited"));
                    WriteRunInfo(folder, background, seconds, totalSteps, gameClockStart, world.Snakes.Count,
                        seed, square, grid.Width, grid.Height, snapshot, progress, statusEvery, pace,
                        savedSnakePath);
                    WriteScripts(folder, replayExe, mp4, seconds, cast, seed, snapshot, progress,
                        statusEvery, pace, hold, fps, savedSnakePath);

                    using (ShowLog log = ShowLog.Open(folder, snapshot))
                    {
                        log.Start(world, settings, new Rectangle(0, 0, source.Width, source.Height),
                            source, Point.Empty, seconds);
                        log.Record(world, 0, true);

                        state = "running";
                        WriteProgress(folder, state, startedUtc, gameClockStart, 0, 0, seconds, totalSteps,
                            0, pace, 0, world, initial, seed, background, logPath, mp4, replayScript);
                        WriteStatus(folder, state, 0, 0, seconds, world, cast, initial.Count, 0);
                        clock.Start();
                        double nextProgress = 0, nextStatus = 0;

                        while (!stopping)
                        {
                            if (steps >= totalSteps)
                            {
                                state = "time-limit";
                                break;
                            }

                            int chunk = pace > 0 ? 16 : 64;
                            for (int index = 0; index < chunk && steps < totalSteps; index++)
                            {
                                steps++;
                                gameSeconds = steps * World.StepSeconds;
                                world.ShowSeconds = gameSeconds;
                                DateTimeOffset now = gameClockStart.AddSeconds(gameSeconds);
                                double night = Daylight.NightAt(now, null, null);
                                world.Step(grid, night, now, null, null);
                                rate = world.ShowMoves / Math.Max(1e-9, gameSeconds);
                                log.Record(world, rate);
                                if (world.ShowComplete)
                                {
                                    state = world.Snakes.Count == 0 ? "extinct" : "complete";
                                    break;
                                }
                            }
                            if (state != "running") break;

                            double wall = clock.Elapsed.TotalSeconds;
                            if (wall >= nextProgress)
                            {
                                WriteProgress(folder, state, startedUtc, gameClockStart, gameSeconds, wall,
                                    seconds, totalSteps, steps, pace, rate, world, initial, seed,
                                    background, logPath, mp4, replayScript);
                                nextProgress = wall + progress;
                            }
                            if (wall >= nextStatus)
                            {
                                WriteStatus(folder, state, wall, gameSeconds, seconds, world, cast,
                                    CountOriginal(world, initial), rate);
                                nextStatus = wall + statusEvery;
                            }

                            if (pace > 0)
                            {
                                double target = steps / (12.5 * pace);
                                double ahead = target - clock.Elapsed.TotalSeconds;
                                if (ahead > 0.002)
                                    Thread.Sleep((int)Math.Min(50, ahead * 1000));
                            }
                            else
                            {
                                Thread.Sleep(0);
                            }
                        }

                        if (stopping) state = "stopped";
                        if (state == "time-limit") gameSeconds = seconds;
                        double finalWall = clock.Elapsed.TotalSeconds;
                        world.ShowSeconds = gameSeconds;
                        if (state != "extinct" && state != "complete") world.ShowComplete = true;
                        log.Record(world, rate, true);
                        WriteProgress(folder, state, startedUtc, gameClockStart, gameSeconds, finalWall,
                            seconds, totalSteps, steps, pace, rate, world, initial, seed,
                            background, logPath, mp4, replayScript);
                        WriteStatus(folder, state, finalWall, gameSeconds, seconds, world, cast,
                            CountOriginal(world, initial), rate);
                        Console.WriteLine("finished state=" + state + " game=" + Format(gameSeconds) +
                            " wall=" + Format(finalWall) + " moves=" +
                            world.ShowMoves.ToString("N0", CultureInfo.InvariantCulture) +
                            " population=" + world.Snakes.Count);
                    }

                    if (replay && File.Exists(replayExe))
                    {
                        Console.WriteLine("rendering replay to " + mp4);
                        var info = new ProcessStartInfo(replayExe,
                            "--log " + Quote(logPath) + " --out " + Quote(mp4) +
                            " --hold " + hold.ToString("0.###", CultureInfo.InvariantCulture) +
                            " --fps " + fps.ToString(CultureInfo.InvariantCulture))
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using (Process process = Process.Start(info))
                        {
                            process.WaitForExit();
                            if (process.ExitCode != 0)
                            {
                                state = "replay-failed";
                                Console.Error.WriteLine("ShowReplay exited with " + process.ExitCode);
                            }
                        }
                    }
                    else if (replay)
                    {
                        Console.Error.WriteLine("ShowReplay.exe was not found at " + replayExe);
                        state = "replay-missing";
                    }
                }
            }
            catch (Exception error)
            {
                state = "error";
                Console.Error.WriteLine(error);
                if (world != null && initial != null)
                {
                    try
                    {
                        WriteProgress(folder, state, startedUtc, gameClockStart, gameSeconds,
                            clock.Elapsed.TotalSeconds, seconds, totalSteps, steps, pace, rate, world,
                            initial, seed, background, logPath, mp4, replayScript);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                return 1;
            }
            return 0;
        }

        private static Snake LoadSavedSnake(string path)
        {
            if (String.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            SavedWorld saved = JsonFiles.Read<SavedWorld>(path, false);
            if (saved == null || saved.World == null || saved.World.Snakes == null) return null;
            return saved.World.Snakes.FirstOrDefault(snake => snake.IsResident) ??
                saved.World.Snakes.FirstOrDefault();
        }

        private static void AdoptSavedSnake(World world, Snake saved)
        {
            if (saved.Genes == null || !saved.Genes.IsValid()) return;
            Snake resident = world.Snakes.FirstOrDefault(snake => snake.IsResident);
            if (resident == null) return;
            resident.Genes = saved.Genes.Copy();
            resident.Skin = saved.Skin;
            resident.BirthColor = saved.BirthColor;
            resident.MutatedChannels = saved.MutatedChannels;
            resident.StressColor = saved.StressColor;
            resident.HasStressColor = saved.HasStressColor;
            resident.DriftColor = saved.DriftColor;
            if (saved.Learned != null && saved.Learned.Length == (int)Gene.Count)
                resident.Learned = (double[])saved.Learned.Clone();
        }

        private static void EnsureDistinctGenomes(World world)
        {
            var seen = new List<double[]>();
            var random = new SaverRandom(world.RandomState);
            int rerolled = 0;
            foreach (Snake snake in world.Snakes)
            {
                while (snake.Genes == null ||
                    seen.Any(values => SameGenome(values, snake.Genes.Values)))
                {
                    snake.Genes = Genome.Roll(random, null);
                    rerolled++;
                }
                seen.Add(snake.Genes.Values);
            }
            world.RandomState = random.State;
            if (rerolled > 0)
                Console.WriteLine("rerolled " + rerolled + " duplicate genome(s)");
        }

        private static bool SameGenome(double[] first, double[] second)
        {
            if (first == null || second == null || first.Length != second.Length) return false;
            for (int index = 0; index < first.Length; index++)
                if (first[index] != second[index]) return false;
            return true;
        }

        private static ColorGrid Sample(Bitmap bitmap, int square)
        {
            int width = bitmap.Width, height = bitmap.Height;
            var pixels = new int[checked(width * height)];
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int row = 0; row < height; row++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, row * data.Stride), pixels, row * width, width);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return ColorGrid.Sample(pixels, new Size(width, height), square, Point.Empty);
        }

        private static void WriteProgress(string folder, string state, string startedUtc,
            DateTimeOffset gameClockStart, double gameSeconds, double wallSeconds, double seconds,
            long totalSteps, long steps, double pace, double rate, World world, HashSet<long> initial,
            uint seed, string background, string logPath, string mp4, string replayScript)
        {
            double percent = Math.Min(100, Math.Max(0, gameSeconds / seconds * 100));
            var record = new ProgressRecord
            {
                Show = Path.GetFileName(folder),
                Status = state,
                StartedUtc = startedUtc,
                UpdatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                GameClockStart = gameClockStart.ToString("o", CultureInfo.InvariantCulture),
                GameSeconds = gameSeconds,
                TotalGameSeconds = seconds,
                WallSeconds = wallSeconds,
                Percent = percent,
                RemainingGameSeconds = Math.Max(0, seconds - gameSeconds),
                Steps = steps,
                TotalSteps = totalSteps,
                Moves = world.ShowMoves,
                MovesPerSecond = rate,
                Population = world.Snakes.Count,
                OriginalAlive = CountOriginal(world, initial),
                Cast = world.ShowCastSize,
                Tick = world.Ticks,
                Seed = seed,
                Pace = pace,
                Background = Path.GetFileName(background),
                Log = Path.GetFileName(logPath),
                Mp4 = Path.GetFileName(mp4),
                ReplayScript = Path.GetFileName(replayScript)
            };
            WriteJson(Path.Combine(folder, "progress.json"), record);
        }

        private static void WriteStatus(string folder, string state, double wallSeconds,
            double gameSeconds, double seconds, World world, int cast, int originalAlive, double rate)
        {
            string line = "[" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "] game=" +
                Format(gameSeconds) + "/" + Format(seconds) + " " +
                (gameSeconds / seconds * 100).ToString("F2", CultureInfo.InvariantCulture) + "%" +
                "  wall=" + Format(wallSeconds) + "  moves=" +
                world.ShowMoves.ToString("N0", CultureInfo.InvariantCulture) + "  snakes=" +
                world.Snakes.Count + "/" + cast + "  original=" + originalAlive + "/" + cast +
                "  rate=" + rate.ToString("F1", CultureInfo.InvariantCulture) + "/s  " + state;
            File.AppendAllText(Path.Combine(folder, "status.log"), line + Environment.NewLine,
                new UTF8Encoding(false));
            Console.WriteLine(line);
        }

        private static void WriteRunInfo(string folder, string background, double seconds,
            long totalSteps, DateTimeOffset gameClockStart, int cast, uint seed, int square,
            int width, int height, double snapshot, double progress, double statusEvery, double pace,
            string savedSnakePath)
        {
            string[] lines =
            {
                "startedUtc=" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                "gameClockStart=" + gameClockStart.ToString("o", CultureInfo.InvariantCulture),
                "background=" + Path.GetFullPath(background),
                "gameSeconds=" + seconds.ToString(CultureInfo.InvariantCulture),
                "totalSteps=" + totalSteps.ToString(CultureInfo.InvariantCulture),
                "cast=" + cast.ToString(CultureInfo.InvariantCulture),
                "seed=" + seed.ToString(CultureInfo.InvariantCulture),
                "square=" + square.ToString(CultureInfo.InvariantCulture),
                "grid=" + width.ToString(CultureInfo.InvariantCulture) + "x" +
                    height.ToString(CultureInfo.InvariantCulture),
                "snapshotSeconds=" + snapshot.ToString(CultureInfo.InvariantCulture),
                "progressSeconds=" + progress.ToString(CultureInfo.InvariantCulture),
                "statusSeconds=" + statusEvery.ToString(CultureInfo.InvariantCulture),
                "pace=" + (pace > 0 ? pace.ToString(CultureInfo.InvariantCulture) + "x wall" :
                    "unlimited wall; simulation stays 1x"),
                "savedSnake=" + (String.IsNullOrEmpty(savedSnakePath) ? "(none)" :
                    Path.GetFullPath(savedSnakePath)),
                "executable=" + Path.GetFullPath(typeof(Program).Assembly.Location)
            };
            File.WriteAllLines(Path.Combine(folder, "run-info.txt"), lines, new UTF8Encoding(false));
        }

        private static void WriteScripts(string folder, string replayExe, string mp4, double seconds,
            int cast, uint seed, double snapshot, double progress, double statusEvery, double pace,
            double hold, int fps, string savedSnakePath)
        {
            string replay =
                "$ErrorActionPreference = 'Stop'" + Environment.NewLine +
                "$here = Split-Path -Parent $MyInvocation.MyCommand.Path" + Environment.NewLine +
                "& " + QuoteSingle(replayExe) + " --log (Join-Path $here 'show.log.jsonl') " +
                "--out (Join-Path $here '" + Path.GetFileName(mp4) + "') " +
                "--frames (Join-Path $here 'replay-frames') " +
                "--hold " + hold.ToString("0.###", CultureInfo.InvariantCulture) +
                " --fps " + fps.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                "Write-Host ('Wrote ' + (Join-Path $here '" + Path.GetFileName(mp4) + "'))" +
                Environment.NewLine;
            File.WriteAllText(Path.Combine(folder, "replay.ps1"), replay, new UTF8Encoding(false));

            string run =
                "$ErrorActionPreference = 'Stop'" + Environment.NewLine +
                "$here = Split-Path -Parent $MyInvocation.MyCommand.Path" + Environment.NewLine +
                "& " + QuoteSingle(Path.GetFullPath(typeof(Program).Assembly.Location)) +
                " --background (Join-Path $here 'background.png') --folder $here" +
                " --seconds " + seconds.ToString(CultureInfo.InvariantCulture) +
                " --cast " + cast.ToString(CultureInfo.InvariantCulture) +
                " --seed " + seed.ToString(CultureInfo.InvariantCulture) +
                " --snapshot " + snapshot.ToString(CultureInfo.InvariantCulture) +
                " --progress " + progress.ToString(CultureInfo.InvariantCulture) +
                " --status " + statusEvery.ToString(CultureInfo.InvariantCulture) +
                " --pace " + pace.ToString(CultureInfo.InvariantCulture) +
                (String.IsNullOrEmpty(savedSnakePath) ? "" :
                    " --saved-snake " + QuoteSingle(Path.GetFullPath(savedSnakePath))) +
                Environment.NewLine;
            File.WriteAllText(Path.Combine(folder, "run.ps1"), run, new UTF8Encoding(false));

            string status =
                "$here = Split-Path -Parent $MyInvocation.MyCommand.Path" + Environment.NewLine +
                "if (Test-Path (Join-Path $here 'status.log')) { Get-Content (Join-Path $here 'status.log') -Tail 12 }" +
                Environment.NewLine +
                "if (Test-Path (Join-Path $here 'progress.json')) { Get-Content (Join-Path $here 'progress.json') -Raw }" +
                Environment.NewLine;
            File.WriteAllText(Path.Combine(folder, "status.ps1"), status, new UTF8Encoding(false));
        }

        private static int CountOriginal(World world, HashSet<long> initial)
        {
            var alive = new HashSet<long>(world.Snakes.Select(snake => snake.Id));
            return initial.Count(id => alive.Contains(id));
        }

        private static void WriteJson<T>(string path, T record)
        {
            string temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var serializer = new DataContractJsonSerializer(typeof(T),
                    new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                serializer.WriteObject(stream, record);
            }
            try
            {
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch (IOException)
            {
                File.Copy(temp, path, true);
                File.Delete(temp);
            }
        }

        private static string Format(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return ((int)span.TotalHours).ToString("00", CultureInfo.InvariantCulture) + ":" +
                span.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
                span.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string QuoteSingle(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        private static string Next(string[] args, ref int index)
        {
            if (index + 1 >= args.Length) throw new ArgumentException("Missing value after " + args[index]);
            return args[++index];
        }

        private static int ParseInt(string value)
        {
            return Int32.Parse(value, CultureInfo.InvariantCulture);
        }

        private static uint ParseUInt(string value)
        {
            return UInt32.Parse(value, CultureInfo.InvariantCulture);
        }

        private static double ParseDouble(string value)
        {
            return Double.Parse(value, CultureInfo.InvariantCulture);
        }
    }
}
