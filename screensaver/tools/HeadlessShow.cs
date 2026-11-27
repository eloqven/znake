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
        [DataMember(Name = "square")] public int Square;
        [DataMember(Name = "spawn")] public string Spawn;
        [DataMember(Name = "savedSnakeId", EmitDefaultValue = false)] public long? SavedSnakeId;
        [DataMember(Name = "savedSnakeAlive")] public bool SavedSnakeAlive;
        [DataMember(Name = "background")] public string Background;
        [DataMember(Name = "log")] public string Log;
        [DataMember(Name = "mp4")] public string Mp4;
        [DataMember(Name = "replayScript")] public string ReplayScript;
    }

    [DataContract]
    internal sealed class SeriesEntry
    {
        [DataMember(Name = "utc")] public string Utc;
        [DataMember(Name = "gameSeconds")] public double GameSeconds;
        [DataMember(Name = "wallSeconds")] public double WallSeconds;
        [DataMember(Name = "steps")] public long Steps;
        [DataMember(Name = "moves")] public long Moves;
        [DataMember(Name = "population")] public int Population;
        [DataMember(Name = "originalAlive")] public int OriginalAlive;
        [DataMember(Name = "savedSnakeAlive")] public bool SavedSnakeAlive;
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
            return Run(args);
        }

        internal static int Run(string[] args)
        {
            stopping = false;
            string background = null, folder = null, replayExe = null, savedSnakePath = null;
            string stopFile = null, spawn = "edge";
            double seconds = 7200, snapshot = 10, progress = 2, statusEvery = 300, hold = 0.2;
            double pace = 0, fps = 12.5;
            int cast = 89, square = 24;
            uint seed = 20261127;
            bool replay = true, resume = false, validateOnly = false;
            bool secondsSet = false, squareSet = false, spawnSet = false;

            try
            {
                for (int index = 0; index < args.Length; index++)
                {
                    switch (args[index])
                    {
                        case "--background": background = Next(args, ref index); break;
                        case "--folder": folder = Next(args, ref index); break;
                        case "--seconds": seconds = ParseDouble(Next(args, ref index)); secondsSet = true; break;
                        case "--cast": cast = ParseInt(Next(args, ref index)); break;
                        case "--square": square = ParseInt(Next(args, ref index)); squareSet = true; break;
                        case "--seed": seed = ParseUInt(Next(args, ref index)); break;
                        case "--snapshot": snapshot = ParseDouble(Next(args, ref index)); break;
                        case "--progress": progress = ParseDouble(Next(args, ref index)); break;
                        case "--status": statusEvery = ParseDouble(Next(args, ref index)); break;
                        case "--pace": pace = ParseDouble(Next(args, ref index)); break;
                        case "--hold": hold = ParseDouble(Next(args, ref index)); break;
                        case "--fps": fps = ParseDouble(Next(args, ref index)); break;
                        case "--replay-exe": replayExe = Next(args, ref index); break;
                        case "--saved-snake": savedSnakePath = Next(args, ref index); break;
                        case "--stop-file": stopFile = Next(args, ref index); break;
                        case "--spawn": spawn = Next(args, ref index).ToLowerInvariant(); spawnSet = true; break;
                        case "--resume": resume = true; break;
                        case "--validate-only": validateOnly = true; break;
                        case "--no-replay": replay = false; break;
                        default:
                            Console.Error.WriteLine("Unknown argument: " + args[index]);
                            return 2;
                    }
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                return 2;
            }

            if (spawn != "edge" && spawn != "random")
            {
                Console.Error.WriteLine("Invalid --spawn mode: " + spawn + " (expected edge or random)");
                return 2;
            }
            if (seconds <= 0 || cast < 1 || square < 4 || snapshot <= 0 || progress <= 0 ||
                statusEvery <= 0 || pace < 0 || hold <= 0 || fps <= 0)
            {
                Console.Error.WriteLine("Numeric arguments must be positive (and --pace >= 0).");
                return 2;
            }

            folder = folder ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "show-89");
            replayExe = replayExe ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShowReplay.exe");
            savedSnakePath = savedSnakePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Znake", "Widget", "world.json");

            ProgressRecord priorProgress = null;
            Dictionary<string, string> runInfo = null;
            if (resume)
            {
                if (!Directory.Exists(folder))
                {
                    Console.Error.WriteLine("Cannot resume; run folder does not exist: " + folder);
                    return 1;
                }
                priorProgress = JsonFiles.Read<ProgressRecord>(Path.Combine(folder, "progress.json"), false);
                runInfo = ReadRunInfo(Path.Combine(folder, "run-info.txt"));
                if (background == null)
                {
                    string infoBg;
                    if (runInfo != null && runInfo.TryGetValue("background", out infoBg) && File.Exists(infoBg))
                        background = infoBg;
                }
                if (!secondsSet && priorProgress != null && priorProgress.TotalGameSeconds > 0)
                    seconds = priorProgress.TotalGameSeconds;
                if (!squareSet && priorProgress != null && priorProgress.Square >= 4)
                    square = priorProgress.Square;
                if (!spawnSet && priorProgress != null && !String.IsNullOrEmpty(priorProgress.Spawn))
                    spawn = priorProgress.Spawn;
            }

            background = background ?? Path.Combine(folder, "background.png");
            if (!File.Exists(background))
            {
                Console.Error.WriteLine("No background image at " + background);
                return 1;
            }

            int imageWidth, imageHeight;
            try
            {
                using (var probe = new Bitmap(background))
                {
                    imageWidth = probe.Width;
                    imageHeight = probe.Height;
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("Invalid background image: " + error.Message);
                return 1;
            }

            int gridWidth = (imageWidth + square - 1) / square;
            int gridHeight = (imageHeight + square - 1) / square;
            int freeCells = checked(gridWidth * gridHeight);
            if (!resume)
            {
                if (spawn == "random" && cast > freeCells)
                {
                    Console.Error.WriteLine("Cast " + cast + " exceeds arena free-cell count (" + freeCells + ").");
                    return 1;
                }
                int edgeSlots = 2 * (gridWidth + gridHeight);
                if (spawn == "edge" && cast > edgeSlots)
                {
                    Console.Error.WriteLine("Cast " + cast + " exceeds edge entry slot count (" + edgeSlots + ").");
                    return 1;
                }
            }

            if (validateOnly)
            {
                if (resume)
                {
                    try
                    {
                        using (StateStore checkStore = StateStore.Open(folder))
                        {
                            if (checkStore == null)
                            {
                                Console.Error.WriteLine("Checkpoint is locked by another process.");
                                return 1;
                            }
                            checkStore.Load(DateTime.UtcNow, false);
                        }
                    }
                    catch (Exception error)
                    {
                        Console.Error.WriteLine("Checkpoint validation failed: " + error.Message);
                        return 1;
                    }
                }
                Console.WriteLine("valid grid=" + gridWidth + "x" + gridHeight +
                    " freeCells=" + freeCells + " cast=" + cast + " spawn=" + spawn);
                return 0;
            }

            long totalSteps = Math.Max(1L, (long)Math.Round(seconds / World.StepSeconds));
            seconds = totalSteps * World.StepSeconds;

            string mp4 = Path.Combine(folder, "show-89.mp4");
            string replayScript = Path.Combine(folder, "replay.ps1");
            string logPath = Path.Combine(folder, "show.log.jsonl");
            string seriesPath = Path.Combine(folder, "series.jsonl");
            string startedUtc = priorProgress != null && !String.IsNullOrEmpty(priorProgress.StartedUtc)
                ? priorProgress.StartedUtc
                : DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            DateTimeOffset gameClockStart = DateTimeOffset.Now;
            if (priorProgress != null && !String.IsNullOrEmpty(priorProgress.GameClockStart))
            {
                DateTimeOffset parsedClock;
                if (DateTimeOffset.TryParse(priorProgress.GameClockStart, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out parsedClock))
                    gameClockStart = parsedClock;
            }

            ConsoleCancelEventHandler cancelHandler = delegate(object sender, ConsoleCancelEventArgs cancel)
            {
                cancel.Cancel = true;
                stopping = true;
            };
            Console.CancelKeyPress += cancelHandler;

            World world = null;
            HashSet<long> initial = null;
            long? savedSnakeId = null;
            string state = "starting";
            long steps = 0;
            double gameSeconds = 0, rate = 0;
            var clock = new Stopwatch();

            try
            {
                if (!resume)
                {
                    Directory.CreateDirectory(folder);
                    ShowLog.Clear(folder);
                    if (File.Exists(seriesPath)) File.Delete(seriesPath);
                }

                using (StateStore store = StateStore.Open(folder))
                {
                    if (store == null)
                    {
                        Console.Error.WriteLine("Another process holds the lock for " + folder);
                        return 1;
                    }

                    using (var source = new Bitmap(background))
                    {
                        ColorGrid grid = Sample(source, square);
                        var settings = new SaverSettings { TimeScale = 1 };
                        if (resume)
                        {
                            world = store.Load(DateTime.UtcNow, false);
                            if (world == null)
                            {
                                Console.Error.WriteLine("No checkpoint found to resume in " + folder);
                                return 1;
                            }
                            if (world.WidgetSquare > 0) square = world.WidgetSquare;
                            seed = world.ShowSeed;
                            cast = world.ShowCastSize;
                            steps = (long)Math.Round(world.ShowSeconds / World.StepSeconds);
                            gameSeconds = world.ShowSeconds;
                            if (world.ShowComplete && world.Snakes.Count > 0 &&
                                world.ShowMoves < world.ShowGoal && steps < totalSteps)
                            {
                                world.ShowComplete = false;
                            }
                            savedSnakeId = priorProgress != null && priorProgress.SavedSnakeId.HasValue
                                ? priorProgress.SavedSnakeId
                                : FindResidentId(world);
                            initial = new HashSet<long>(world.Snakes.Select(snake => snake.Id));
                        }
                        else
                        {
                            world = new World(grid.Width, grid.Height, seed, settings.BirthOverrides(), square);
                            Snake savedSnake = LoadSavedSnake(savedSnakePath);
                            if (savedSnake != null)
                                AdoptSavedSnake(world, savedSnake);
                            world.StartShow(cast, true, spawn == "random");
                            EnsureDistinctGenomes(world);
                            savedSnakeId = FindResidentId(world);
                            initial = new HashSet<long>(world.Snakes.Select(snake => snake.Id));

                            Console.WriteLine("background " + background + " " + source.Width + "x" + source.Height);
                            Console.WriteLine("grid " + grid.Width + "x" + grid.Height + " square " + square +
                                " cast " + world.Snakes.Count + " seed " + seed + " spawn " + spawn);
                            Console.WriteLine("saved snake " + (savedSnake == null ? "not found at " + savedSnakePath :
                                "id " + savedSnake.Id + " adopted as resident"));
                            Console.WriteLine("game clock " + gameClockStart.ToString("o", CultureInfo.InvariantCulture) +
                                " duration " + Format(seconds) + " steps " + totalSteps +
                                " pace " + (pace > 0 ? pace.ToString("0.###", CultureInfo.InvariantCulture) + "x" : "unlimited"));
                            WriteRunInfo(folder, background, seconds, totalSteps, gameClockStart, world.Snakes.Count,
                                seed, square, grid.Width, grid.Height, snapshot, progress, statusEvery, pace,
                                spawn, savedSnakePath);
                            WriteScripts(folder, replayExe, mp4, seconds, cast, square, seed, snapshot, progress,
                                statusEvery, pace, spawn, hold, fps, savedSnakePath);
                        }

                        double lastNight = 0;
                        using (ShowLog log = ShowLog.Open(folder, snapshot))
                        {
                            if (!resume || log.StartedNew)
                            {
                                log.Start(world, settings, new Rectangle(0, 0, source.Width, source.Height),
                                    source, Point.Empty, seconds);
                                log.Record(world, 0, true, 0);
                            }

                            state = "running";
                            store.Save(world, DateTime.UtcNow);
                            WriteProgress(folder, state, startedUtc, gameClockStart, gameSeconds, 0, seconds,
                                totalSteps, steps, pace, rate, square, spawn, world, initial, savedSnakeId,
                                seed, background, logPath, mp4, replayScript);
                            AppendSeries(seriesPath, gameSeconds, 0, steps, world, initial, savedSnakeId);
                            WriteStatus(folder, state, 0, gameSeconds, seconds, world, cast, initial.Count, rate);
                            clock.Start();
                            double nextProgress = 0, nextStatus = 0;

                            while (!stopping)
                            {
                                if (StopFileExists(stopFile))
                                {
                                    stopping = true;
                                    break;
                                }
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
                                    lastNight = Daylight.NightAt(now, null, null);
                                    world.Step(grid, lastNight, now, null, null);
                                    rate = world.ShowMoves / Math.Max(1e-9, gameSeconds);
                                    log.Record(world, rate, false, lastNight);
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
                                    store.Save(world, DateTime.UtcNow);
                                    WriteProgress(folder, state, startedUtc, gameClockStart, gameSeconds, wall,
                                        seconds, totalSteps, steps, pace, rate, square, spawn, world, initial,
                                        savedSnakeId, seed, background, logPath, mp4, replayScript);
                                    AppendSeries(seriesPath, gameSeconds, wall, steps, world, initial, savedSnakeId);
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
                            if (state == "stopped")
                                world.ShowComplete = false;
                            else if (state != "extinct" && state != "complete")
                                world.ShowComplete = true;

                            log.Record(world, rate, true, lastNight);
                            store.Save(world, DateTime.UtcNow);
                            WriteProgress(folder, state, startedUtc, gameClockStart, gameSeconds, finalWall,
                                seconds, totalSteps, steps, pace, rate, square, spawn, world, initial,
                                savedSnakeId, seed, background, logPath, mp4, replayScript);
                            AppendSeries(seriesPath, gameSeconds, finalWall, steps, world, initial, savedSnakeId);
                            WriteStatus(folder, state, finalWall, gameSeconds, seconds, world, cast,
                                CountOriginal(world, initial), rate);
                            Console.WriteLine("finished state=" + state + " game=" + Format(gameSeconds) +
                                " wall=" + Format(finalWall) + " moves=" +
                                world.ShowMoves.ToString("N0", CultureInfo.InvariantCulture) +
                                " population=" + world.Snakes.Count);
                        }

                        if (!stopping && replay && File.Exists(replayExe))
                        {
                            Console.WriteLine("rendering replay to " + mp4);
                            var info = new ProcessStartInfo(replayExe,
                                "--log " + Quote(logPath) + " --out " + Quote(mp4) +
                                " --hold " + hold.ToString("0.###", CultureInfo.InvariantCulture) +
                                " --fps " + fps.ToString("0.###", CultureInfo.InvariantCulture))
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
                        else if (!stopping && replay)
                        {
                            Console.Error.WriteLine("ShowReplay.exe was not found at " + replayExe);
                            state = "replay-missing";
                        }
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
                            clock.Elapsed.TotalSeconds, seconds, totalSteps, steps, pace, rate,
                            square, spawn, world, initial, savedSnakeId, seed, background,
                            logPath, mp4, replayScript);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }
            return 0;
        }

        private static bool StopFileExists(string stopFile)
        {
            return !String.IsNullOrEmpty(stopFile) && File.Exists(stopFile);
        }

        private static long? FindResidentId(World world)
        {
            if (world == null || world.Snakes == null) return null;
            Snake resident = world.Snakes.FirstOrDefault(snake => snake.IsResident);
            return resident == null ? (long?)null : resident.Id;
        }

        private static bool IsSavedSnakeAlive(World world, long? savedSnakeId)
        {
            if (world == null || world.Snakes == null || !savedSnakeId.HasValue) return false;
            return world.Snakes.Any(snake => snake.Id == savedSnakeId.Value);
        }

        private static Dictionary<string, string> ReadRunInfo(string path)
        {
            if (!File.Exists(path)) return null;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return map;
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
            long totalSteps, long steps, double pace, double rate, int square, string spawn,
            World world, HashSet<long> initial, long? savedSnakeId, uint seed, string background,
            string logPath, string mp4, string replayScript)
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
                Square = square,
                Spawn = spawn,
                SavedSnakeId = savedSnakeId,
                SavedSnakeAlive = IsSavedSnakeAlive(world, savedSnakeId),
                Background = Path.GetFileName(background),
                Log = Path.GetFileName(logPath),
                Mp4 = Path.GetFileName(mp4),
                ReplayScript = Path.GetFileName(replayScript)
            };
            WriteJson(Path.Combine(folder, "progress.json"), record);
        }

        private static void AppendSeries(string seriesPath, double gameSeconds, double wallSeconds,
            long steps, World world, HashSet<long> initial, long? savedSnakeId)
        {
            var entry = new SeriesEntry
            {
                Utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                GameSeconds = gameSeconds,
                WallSeconds = wallSeconds,
                Steps = steps,
                Moves = world.ShowMoves,
                Population = world.Snakes.Count,
                OriginalAlive = CountOriginal(world, initial),
                SavedSnakeAlive = IsSavedSnakeAlive(world, savedSnakeId)
            };
            var serializer = new DataContractJsonSerializer(typeof(SeriesEntry));
            string line;
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, entry);
                line = Encoding.UTF8.GetString(stream.ToArray());
            }
            using (var file = new FileStream(seriesPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            using (var writer = new StreamWriter(file, new UTF8Encoding(false)))
                writer.WriteLine(line);
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
            string spawn, string savedSnakePath)
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
                "spawn=" + spawn,
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
            int cast, int square, uint seed, double snapshot, double progress, double statusEvery,
            double pace, string spawn, double hold, double fps, string savedSnakePath)
        {
            string replay =
                "$ErrorActionPreference = 'Stop'" + Environment.NewLine +
                "$here = Split-Path -Parent $MyInvocation.MyCommand.Path" + Environment.NewLine +
                "& " + QuoteSingle(replayExe) + " --log (Join-Path $here 'show.log.jsonl') " +
                "--out (Join-Path $here '" + Path.GetFileName(mp4) + "') " +
                "--frames (Join-Path $here 'replay-frames') " +
                "--hold " + hold.ToString("0.###", CultureInfo.InvariantCulture) +
                " --fps " + fps.ToString("0.###", CultureInfo.InvariantCulture) + Environment.NewLine +
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
                " --square " + square.ToString(CultureInfo.InvariantCulture) +
                " --seed " + seed.ToString(CultureInfo.InvariantCulture) +
                " --spawn " + spawn +
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
