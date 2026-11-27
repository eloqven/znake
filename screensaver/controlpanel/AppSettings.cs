using System;
using System.IO;
using System.Runtime.Serialization;

namespace Znake.ControlPanel
{
    [DataContract]
    internal sealed class SetupConfig
    {
        [DataMember(Name = "backgroundPath")] public string BackgroundPath = "";
        [DataMember(Name = "square")] public int Square = 8;
        [DataMember(Name = "cast")] public int Cast = 89;
        [DataMember(Name = "seed")] public uint Seed = 20261127;
        [DataMember(Name = "savedSnakePath")] public string SavedSnakePath = "";
        [DataMember(Name = "durationSeconds")] public double DurationSeconds = 7200;
        [DataMember(Name = "snapshotSeconds")] public double SnapshotSeconds = 1;
        [DataMember(Name = "pace")] public double Pace = 0;
        [DataMember(Name = "spawnMode")] public string SpawnMode = "edge";
        [DataMember(Name = "outputRoot")] public string OutputRoot = "";

        public SetupConfig Clone()
        {
            return new SetupConfig
            {
                BackgroundPath = BackgroundPath,
                Square = Square,
                Cast = Cast,
                Seed = Seed,
                SavedSnakePath = SavedSnakePath,
                DurationSeconds = DurationSeconds,
                SnapshotSeconds = SnapshotSeconds,
                Pace = Pace,
                SpawnMode = SpawnMode,
                OutputRoot = OutputRoot
            };
        }

        public static SetupConfig CreateDefault()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new SetupConfig
            {
                BackgroundPath = "",
                Square = 8,
                Cast = 89,
                Seed = 20261127,
                SavedSnakePath = Path.Combine(local, "Znake", "Widget", "world.json"),
                DurationSeconds = 7200,
                SnapshotSeconds = 1,
                Pace = 0,
                SpawnMode = "edge",
                OutputRoot = Path.Combine(local, "Znake", "Shows")
            };
        }

        public void NormalizeDefaults()
        {
            SetupConfig defaults = CreateDefault();
            if (Square != 8 && Square != 13 && Square != 21 && Square != 34) Square = 8;
            if (Cast < 1) Cast = 89;
            if (DurationSeconds <= 0) DurationSeconds = 7200;
            if (SnapshotSeconds <= 0) SnapshotSeconds = 1;
            if (Pace < 0) Pace = 0;
            if (SpawnMode != "edge" && SpawnMode != "random") SpawnMode = "edge";
            if (String.IsNullOrWhiteSpace(SavedSnakePath)) SavedSnakePath = defaults.SavedSnakePath;
            if (String.IsNullOrWhiteSpace(OutputRoot)) OutputRoot = defaults.OutputRoot;
            if (BackgroundPath == null) BackgroundPath = "";
        }
    }

    [DataContract]
    internal sealed class AppSettings
    {
        [DataMember(Name = "setup")] public SetupConfig Setup = SetupConfig.CreateDefault();
        [DataMember(Name = "theme")] public string Theme = "dark";
        [DataMember(Name = "showLiveGrid")] public bool ShowLiveGrid = true;
        [DataMember(Name = "showVisionOverlay")] public bool ShowVisionOverlay = true;
        [DataMember(Name = "showStatusTail")] public bool ShowStatusTail = false;
        [DataMember(Name = "showSnakes")] public bool ShowSnakes = true;
        [DataMember(Name = "showFruits")] public bool ShowFruits = true;
        [DataMember(Name = "showVines")] public bool ShowVines = true;
        [DataMember(Name = "snakesOpacity")] public double SnakesOpacity = 1.0;
        [DataMember(Name = "fruitsOpacity")] public double FruitsOpacity = 1.0;
        [DataMember(Name = "vinesOpacity")] public double VinesOpacity = 0.7;
        [DataMember(Name = "visionOpacity")] public double VisionOpacity = 0.65;
        [DataMember(Name = "useGpuPreview")] public bool UseGpuPreview = true;
        [DataMember(Name = "renderFps")] public double RenderFps = 12.5;
        [DataMember(Name = "playbackSpeed")] public double PlaybackSpeed = 1.0;
        [DataMember(Name = "burnVisionOverlay")] public bool BurnVisionOverlay = true;
        [DataMember(Name = "encoderMode")] public string EncoderMode = "auto";
        [DataMember(Name = "runnerExeOverride")] public string RunnerExeOverride = "";
        [DataMember(Name = "replayExeOverride")] public string ReplayExeOverride = "";

        public static string DefaultDataRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Znake", "ControlPanel");
        }

        public static AppSettings Load(string root)
        {
            string path = Path.Combine(root, "settings.json");
            string backup = Path.Combine(root, "settings.previous.json");
            AppSettings loaded = JsonFiles.Read<AppSettings>(path, false) ??
                JsonFiles.Read<AppSettings>(backup, false) ??
                new AppSettings();
            if (loaded.Setup == null) loaded.Setup = SetupConfig.CreateDefault();
            loaded.Setup.NormalizeDefaults();
            if (loaded.RenderFps != 12.5 && loaded.RenderFps != 30.0) loaded.RenderFps = 12.5;
            if (loaded.PlaybackSpeed <= 0) loaded.PlaybackSpeed = 1.0;
            if (String.IsNullOrWhiteSpace(loaded.EncoderMode)) loaded.EncoderMode = "auto";
            if (String.IsNullOrWhiteSpace(loaded.Theme)) loaded.Theme = "dark";
            return loaded;
        }

        public void Save(string root)
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "settings.json");
            string backup = File.Exists(path) ? Path.Combine(root, "settings.previous.json") : null;
            JsonFiles.Write(path, backup, this, false);
        }

        public string ResolveRunnerExe()
        {
            if (!String.IsNullOrWhiteSpace(RunnerExeOverride) && File.Exists(RunnerExeOverride))
                return RunnerExeOverride;
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZnakeHeadless.exe");
        }

        public string ResolveReplayExe()
        {
            if (!String.IsNullOrWhiteSpace(ReplayExeOverride) && File.Exists(ReplayExeOverride))
                return ReplayExeOverride;
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShowReplay.exe");
        }
    }
}
