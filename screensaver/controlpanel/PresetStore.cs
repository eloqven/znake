using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace Znake.ControlPanel
{
    [DataContract]
    internal sealed class PresetEntry
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "setup")] public SetupConfig Setup;
    }

    [DataContract]
    internal sealed class PresetCatalog
    {
        [DataMember(Name = "presets")] public List<PresetEntry> Presets = new List<PresetEntry>();
    }

    internal sealed class PresetStore
    {
        private readonly string root;

        public PresetStore(string root)
        {
            this.root = root;
        }

        public List<PresetEntry> List()
        {
            string path = Path.Combine(root, "presets.json");
            PresetCatalog catalog = JsonFiles.Read<PresetCatalog>(path, false);
            if (catalog == null || catalog.Presets == null)
                return new List<PresetEntry>();
            return catalog.Presets
                .Where(entry => entry != null && !String.IsNullOrWhiteSpace(entry.Name) && entry.Setup != null)
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public bool Contains(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return false;
            string trimmed = name.Trim();
            return List().Any(entry => String.Equals(entry.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        }

        public void Save(string name, SetupConfig setup)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentException("Preset name is required.");
            if (setup == null) throw new ArgumentNullException("setup");
            string trimmed = name.Trim();
            List<PresetEntry> entries = List();
            entries.RemoveAll(entry => String.Equals(entry.Name, trimmed, StringComparison.OrdinalIgnoreCase));
            SetupConfig copy = setup.Clone();
            copy.NormalizeDefaults();
            entries.Add(new PresetEntry { Name = trimmed, Setup = copy });
            WriteAll(entries);
        }

        public bool Rename(string oldName, string newName)
        {
            if (String.IsNullOrWhiteSpace(oldName) || String.IsNullOrWhiteSpace(newName)) return false;
            string from = oldName.Trim();
            string to = newName.Trim();
            List<PresetEntry> entries = List();
            PresetEntry target = entries.FirstOrDefault(entry => String.Equals(entry.Name, from, StringComparison.OrdinalIgnoreCase));
            if (target == null) return false;
            if (!String.Equals(from, to, StringComparison.OrdinalIgnoreCase) &&
                entries.Any(entry => String.Equals(entry.Name, to, StringComparison.OrdinalIgnoreCase)))
                return false;
            target.Name = to;
            WriteAll(entries);
            return true;
        }

        public bool Delete(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return false;
            string trimmed = name.Trim();
            List<PresetEntry> entries = List();
            int removed = entries.RemoveAll(entry => String.Equals(entry.Name, trimmed, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return false;
            WriteAll(entries);
            return true;
        }

        private void WriteAll(List<PresetEntry> entries)
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "presets.json");
            string backup = File.Exists(path) ? Path.Combine(root, "presets.previous.json") : null;
            JsonFiles.Write(path, backup, new PresetCatalog { Presets = entries }, false);
        }
    }
}
