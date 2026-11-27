using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Znake.ControlPanel
{
    internal sealed class LogTail
    {
        private readonly string path;
        private long offset;
        private string pending = "";

        public LogTail(string path)
        {
            this.path = path;
        }

        public Znake.Replay.Record StartRecord { get; private set; }
        public Znake.Replay.Record LatestSnapshot { get; private set; }

        public bool Poll()
        {
            if (String.IsNullOrEmpty(path) || !File.Exists(path)) return false;

            string chunk;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length < offset)
                {
                    offset = 0;
                    pending = "";
                    StartRecord = null;
                    LatestSnapshot = null;
                }
                if (stream.Length == offset) return false;
                stream.Position = offset;
                byte[] buffer = new byte[checked((int)(stream.Length - offset))];
                int read = 0;
                while (read < buffer.Length)
                {
                    int n = stream.Read(buffer, read, buffer.Length - read);
                    if (n == 0) break;
                    read += n;
                }
                offset += read;
                chunk = Encoding.UTF8.GetString(buffer, 0, read);
            }

            string combined = pending + chunk;
            int lineStart = 0;
            bool updated = false;
            while (true)
            {
                int nl = combined.IndexOf('\n', lineStart);
                if (nl < 0) break;
                string line = combined.Substring(lineStart, nl - lineStart).Trim();
                lineStart = nl + 1;
                if (line.Length == 0) continue;
                Znake.Replay.Record parsed = TryParse(line);
                if (parsed == null) continue;
                if (parsed.Type == "start")
                {
                    StartRecord = parsed;
                    updated = true;
                }
                else if (parsed.Type == "snapshot" || parsed.Type == "complete")
                {
                    LatestSnapshot = parsed;
                    updated = true;
                }
            }
            pending = combined.Substring(lineStart);
            return updated;
        }

        private static Znake.Replay.Record TryParse(string line)
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(Znake.Replay.Record));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                    return serializer.ReadObject(stream) as Znake.Replay.Record;
            }
            catch (SerializationException)
            {
                return null;
            }
        }
    }
}
