using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Xml;

namespace Znake
{
    [DataContract]
    internal sealed class SaverSettings
    {
        [DataMember(Name = "debug")] public bool Debug = true;
        [DataMember(Name = "latitude", EmitDefaultValue = false)] public double? Latitude;
        [DataMember(Name = "longitude", EmitDefaultValue = false)] public double? Longitude;
        [DataMember(Name = "genes")] public Dictionary<string, double> Genes = new Dictionary<string, double>();

        [OnDeserializing]
        private void Defaults(StreamingContext context)
        {
            Debug = true;
            Genes = new Dictionary<string, double>();
        }

        public static SaverSettings Load(string root)
        {
            string path = Path.Combine(root, "settings.json");
            string backup = Path.Combine(root, "settings.previous.json");
            SaverSettings settings = JsonFiles.Read<SaverSettings>(path, true);
            if (settings != null && settings.IsValid()) return settings;
            SaverSettings previous = JsonFiles.Read<SaverSettings>(backup, true);
            if (previous != null && previous.IsValid()) return previous;
            if (File.Exists(path) || File.Exists(backup))
                throw new InvalidDataException("The settings file is invalid.");
            return new SaverSettings();
        }

        public void Save(string root)
        {
            if (!IsValid())
                throw new InvalidDataException("Provide valid coordinates and gene values.");
            string path = Path.Combine(root, "settings.json");
            SaverSettings previous = JsonFiles.Read<SaverSettings>(path, true);
            JsonFiles.Write(path, previous != null && previous.IsValid()
                ? Path.Combine(root, "settings.previous.json") : null, this, true);
        }

        public IDictionary<Gene, double> BirthOverrides()
        {
            return Genes.ToDictionary(entry => (Gene)Enum.Parse(typeof(Gene), entry.Key, true), entry => entry.Value);
        }

        private bool IsValid()
        {
            if (Latitude.HasValue != Longitude.HasValue) return false;
            if (Latitude.HasValue && !(Latitude.Value >= -90 && Latitude.Value <= 90 &&
                Longitude.Value >= -180 && Longitude.Value <= 180)) return false;
            if (Genes == null) return false;
            var seen = new HashSet<Gene>();
            foreach (var entry in Genes)
            {
                Gene key;
                if (!Enum.TryParse(entry.Key, true, out key) ||
                    !String.Equals(key.ToString(), entry.Key, StringComparison.OrdinalIgnoreCase) ||
                    !Genome.IsValid(key, entry.Value) || !seen.Add(key))
                    return false;
            }
            return true;
        }
    }

    internal sealed class StateStore : IDisposable
    {
        private const int Version = 1;
        private readonly string path;
        private readonly string backup;
        private readonly FileStream owner;
        private bool latestValid;

        private StateStore(string root, FileStream owner)
        {
            path = Path.Combine(root, "world.json");
            backup = Path.Combine(root, "world.previous.json");
            this.owner = owner;
        }

        public static StateStore Open(string root)
        {
            Directory.CreateDirectory(root);
            FileStream owner;
            try { owner = new FileStream(Path.Combine(root, "world.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { return null; }
            return new StateStore(root, owner);
        }

        public World Load(DateTime now)
        {
            Checkpoint latest = JsonFiles.Read<Checkpoint>(path, false);
            latestValid = latest != null && Valid(latest);
            Checkpoint loaded = latestValid ? latest : JsonFiles.Read<Checkpoint>(backup, false);
            if (!latestValid && (loaded == null || !Valid(loaded)))
            {
                if (File.Exists(path) || File.Exists(backup))
                    throw new InvalidDataException("No complete Znake checkpoint could be read.");
                return null;
            }

            loaded.World.FadeImprints(Math.Max(0, (now.Ticks - loaded.SavedUtcTicks) / (double)TimeSpan.TicksPerSecond));
            return loaded.World;
        }

        public void Save(World world, DateTime now)
        {
            var next = new Checkpoint { Version = Version, SavedUtcTicks = now.Ticks, World = world };
            if (!Valid(next)) throw new InvalidDataException("The world could not be saved.");
            JsonFiles.Write(path, latestValid ? backup : null, next, false);
            latestValid = true;
        }

        private static bool Valid(Checkpoint data)
        {
            World world = data.World;
            if (data.Version != Version || data.SavedUtcTicks <= 0 || data.SavedUtcTicks > DateTime.MaxValue.Ticks ||
                world == null || world.Width <= 0 || world.Height <= 0 || world.Ticks < 0 || world.NextId < 2 ||
                world.Snakes == null || world.Vines == null || world.Fruits == null || world.Imprints == null)
                return false;

            var ids = new HashSet<long>();
            foreach (Snake snake in world.Snakes)
            {
                if (snake == null || !OwnId(world, ids, snake.Id) || !ValidSnake(world, snake))
                    return false;
            }
            if (world.Snakes.Count(snake => snake.IsResident) != 1) return false;

            var sources = new HashSet<long>();
            foreach (Vine vine in world.Vines)
            {
                if (vine == null || !OwnId(world, ids, vine.Id) || vine.Genes == null || !vine.Genes.IsValid() ||
                    !Numbers.Nonnegative(vine.NextFruitIn) || !vine.FruitColor.IsValid() || vine.Squares == null ||
                    vine.Squares.Any(square => square == null || !world.Contains(square.Position) ||
                        !square.Tone.IsValid() || !(square.Nutrients > 0 && square.Nutrients <= square.Peak) ||
                        !Numbers.Nonnegative(square.Peak)))
                    return false;
                sources.Add(vine.Id);
            }
            foreach (Fruit fruit in world.Fruits)
            {
                if (fruit == null || !OwnId(world, ids, fruit.Id) || !sources.Contains(fruit.VineId) ||
                    !world.Contains(fruit.Position) || !fruit.Tone.IsValid() || fruit.Genes == null ||
                    !fruit.Genes.IsValid() || !Numbers.Nonnegative(fruit.Remaining) || fruit.Remaining == 0)
                    return false;
            }
            return world.Imprints.All(entry => world.Contains(entry.Key) && entry.Value.Color.IsValid() &&
                entry.Value.Alpha > 0 && entry.Value.Alpha <= 1);
        }

        private static bool OwnId(World world, HashSet<long> seen, long id)
        {
            return id > 0 && id < world.NextId && seen.Add(id);
        }

        private static bool ValidSnake(World world, Snake snake)
        {
            if (snake.Genes == null || !snake.Genes.IsValid() || snake.Body == null || snake.Body.Count == 0 ||
                snake.RecentPath == null || snake.RecentPath.Count == 0 || snake.LargestLength < snake.Body.Count ||
                !snake.Body.Any(world.Contains) || (snake.IsResident && !snake.Body.All(world.Contains)) ||
                snake.Heading < 0 || snake.Heading >= Cell.Directions.Length ||
                snake.LastMovingGene < Gene.Length3 || snake.LastMovingGene >= Gene.Count ||
                snake.Mood < Affect.Neutral || snake.Mood > Affect.Stressed ||
                snake.PendingGrowth < 0 || snake.PendingGrowth > 1 ||
                snake.Learned == null || snake.Learned.Length != (int)Gene.Count ||
                snake.Learned.Any(value => !(value >= -1 && value <= 1)) ||
                !snake.Skin.IsValid() || !snake.StressColor.IsValid() || !snake.DriftColor.IsValid())
                return false;

            return Numbers.Nonnegative(snake.Energy) && Numbers.Nonnegative(snake.Stress) &&
                Numbers.Nonnegative(snake.SaturatedFor) && Numbers.Nonnegative(snake.SprintRemaining) &&
                Numbers.Nonnegative(snake.DifferentFor) && snake.Assimilation >= 0 && snake.Assimilation <= 1 &&
                snake.Speed >= 0 && snake.Speed <= 50 && snake.Motion >= 0 && snake.Motion < 1;
        }

        public void Dispose() { owner.Dispose(); }

        [DataContract]
        private sealed class Checkpoint
        {
            [DataMember(Name = "version")] public int Version;
            [DataMember(Name = "savedUtcTicks")] public long SavedUtcTicks;
            [DataMember(Name = "world")] public World World;
        }
    }

    internal static class JsonFiles
    {
        public static T Read<T>(string path, bool simpleDictionary) where T : class
        {
            if (!File.Exists(path)) return null;
            try
            {
                using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                    return Serializer<T>(simpleDictionary).ReadObject(input) as T;
            }
            catch (IOException) { return null; }
            catch (SerializationException) { return null; }
            catch (XmlException) { return null; }
        }

        public static void Write<T>(string path, string backup, T value, bool simpleDictionary)
        {
            string folder = Path.GetDirectoryName(path);
            Directory.CreateDirectory(folder);
            string temporary = Path.Combine(folder, Path.GetRandomFileName());
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    Serializer<T>(simpleDictionary).WriteObject(stream, value);
                    stream.Flush(true);
                }
                if (File.Exists(path))
                    File.Replace(temporary, path, backup);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static DataContractJsonSerializer Serializer<T>(bool simpleDictionary)
        {
            return new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings
            {
                UseSimpleDictionaryFormat = simpleDictionary
            });
        }
    }
}
