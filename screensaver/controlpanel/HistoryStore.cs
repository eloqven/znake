using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Znake.Headless;

namespace Znake.ControlPanel
{
    [DataContract]
    internal sealed class RenderSidecar
    {
        [DataMember(Name = "fps")] public double Fps;
        [DataMember(Name = "speed")] public double Speed;
        [DataMember(Name = "visionOverlay")] public bool VisionOverlay;
        [DataMember(Name = "encoder")] public string Encoder;
        [DataMember(Name = "mp4File")] public string Mp4File;
        [DataMember(Name = "renderedUtc")] public string RenderedUtc;
    }

    [DataContract]
    internal sealed class HistoryEntry
    {
        [DataMember(Name = "folder")] public string Folder;
        [DataMember(Name = "seed")] public uint Seed;
        [DataMember(Name = "startedUtc")] public string StartedUtc;
        [DataMember(Name = "status")] public string Status;
        [DataMember(Name = "gameSeconds")] public double GameSeconds;
        [DataMember(Name = "totalGameSeconds")] public double TotalGameSeconds;
        [DataMember(Name = "moves")] public long Moves;
        [DataMember(Name = "population")] public int Population;
        [DataMember(Name = "cast")] public int Cast;
        [DataMember(Name = "savedSnakeAlive")] public bool SavedSnakeAlive;
        [DataMember(Name = "starred")] public bool Starred;
        [DataMember(Name = "mp4Path")] public string Mp4Path;
        [DataMember(Name = "renderInfo")] public RenderSidecar RenderInfo;
    }

    [DataContract]
    internal sealed class HistoryCatalog
    {
        [DataMember(Name = "runs")] public List<HistoryEntry> Runs = new List<HistoryEntry>();
    }

    internal sealed class HistoryStore
    {
        private readonly string root;

        public HistoryStore(string root)
        {
            this.root = root;
        }

        public List<HistoryEntry> List()
        {
            string path = Path.Combine(root, "history.json");
            HistoryCatalog catalog = JsonFiles.Read<HistoryCatalog>(path, false);
            List<HistoryEntry> entries = catalog != null && catalog.Runs != null
                ? catalog.Runs.Where(r => r != null && !String.IsNullOrWhiteSpace(r.Folder)).ToList()
                : new List<HistoryEntry>();
            foreach (HistoryEntry entry in entries)
                RefreshFromFolder(entry);
            return entries
                .OrderByDescending(e => e.Starred)
                .ThenByDescending(e => e.StartedUtc ?? "", StringComparer.Ordinal)
                .ToList();
        }

        public void RecordRun(HistoryEntry entry)
        {
            if (entry == null || String.IsNullOrWhiteSpace(entry.Folder)) return;
            List<HistoryEntry> entries = List();
            HistoryEntry existing = entries.FirstOrDefault(item =>
                String.Equals(item.Folder, entry.Folder, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                entry.Starred = existing.Starred || entry.Starred;
                if (entry.RenderInfo == null) entry.RenderInfo = existing.RenderInfo;
                if (String.IsNullOrWhiteSpace(entry.Mp4Path)) entry.Mp4Path = existing.Mp4Path;
                entries.Remove(existing);
            }
            RefreshFromFolder(entry);
            entries.Add(entry);
            WriteAll(entries);
        }

        public void RecordStopped(string folder, uint seed)
        {
            var entry = new HistoryEntry
            {
                Folder = folder,
                Seed = seed,
                StartedUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                Status = "stopped"
            };
            RefreshFromFolder(entry);
            if (String.IsNullOrWhiteSpace(entry.Status) || entry.Status == "running" || entry.Status == "starting")
                entry.Status = "stopped";
            RecordRun(entry);
        }

        public void RecordRender(string folder, RenderSidecar render)
        {
            if (String.IsNullOrWhiteSpace(folder) || render == null) return;
            string sidecarPath = Path.Combine(folder, "render.json");
            JsonFiles.Write(sidecarPath, null, render, false);
            List<HistoryEntry> entries = List();
            HistoryEntry entry = entries.FirstOrDefault(item =>
                String.Equals(item.Folder, folder, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                entry = new HistoryEntry { Folder = folder };
                RefreshFromFolder(entry);
                entries.Add(entry);
            }
            entry.RenderInfo = render;
            entry.Mp4Path = String.IsNullOrEmpty(render.Mp4File) ? entry.Mp4Path : Path.Combine(folder, render.Mp4File);
            WriteAll(entries);
        }

        public bool ToggleStar(string folder)
        {
            List<HistoryEntry> entries = List();
            HistoryEntry entry = entries.FirstOrDefault(item =>
                String.Equals(item.Folder, folder, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return false;
            entry.Starred = !entry.Starred;
            WriteAll(entries);
            return entry.Starred;
        }

        public static void RefreshFromFolder(HistoryEntry entry)
        {
            if (entry == null || String.IsNullOrWhiteSpace(entry.Folder) || !Directory.Exists(entry.Folder)) return;
            ProgressRecord progress = JsonFiles.Read<ProgressRecord>(Path.Combine(entry.Folder, "progress.json"), false);
            if (progress != null)
            {
                entry.Seed = progress.Seed;
                if (!String.IsNullOrEmpty(progress.StartedUtc)) entry.StartedUtc = progress.StartedUtc;
                if (!String.IsNullOrEmpty(progress.Status) &&
                    !(entry.Status == "stopped" && (progress.Status == "running" || progress.Status == "starting")))
                    entry.Status = progress.Status;
                entry.GameSeconds = progress.GameSeconds;
                entry.TotalGameSeconds = progress.TotalGameSeconds;
                entry.Moves = progress.Moves;
                entry.Population = progress.Population;
                entry.Cast = progress.Cast;
                entry.SavedSnakeAlive = progress.SavedSnakeAlive;
            }
            RenderSidecar render = JsonFiles.Read<RenderSidecar>(Path.Combine(entry.Folder, "render.json"), false);
            if (render != null)
            {
                entry.RenderInfo = render;
                if (!String.IsNullOrEmpty(render.Mp4File))
                    entry.Mp4Path = Path.Combine(entry.Folder, render.Mp4File);
            }
        }

        public static List<SeriesEntry> ReadSeries(string folder)
        {
            var list = new List<SeriesEntry>();
            if (String.IsNullOrWhiteSpace(folder)) return list;
            string path = Path.Combine(folder, "series.jsonl");
            if (!File.Exists(path)) return list;
            var serializer = new DataContractJsonSerializer(typeof(SeriesEntry));
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(file, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (String.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                        {
                            var row = serializer.ReadObject(ms) as SeriesEntry;
                            if (row != null) list.Add(row);
                        }
                    }
                    catch (SerializationException) { }
                }
            }
            return list;
        }

        private void WriteAll(List<HistoryEntry> entries)
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "history.json");
            string backup = File.Exists(path) ? Path.Combine(root, "history.previous.json") : null;
            JsonFiles.Write(path, backup, new HistoryCatalog { Runs = entries }, false);
        }
    }
}
