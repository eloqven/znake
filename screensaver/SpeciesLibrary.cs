using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Znake
{
    [DataContract]
    internal sealed class SpeciesTemplate
    {
        [DataMember] public Genome Genes;
        [DataMember] public Rgb BirthColor;
        [DataMember] public ColorChannels Channels;

        public static SpeciesTemplate FromSnake(Snake snake)
        {
            return new SpeciesTemplate
            {
                Genes = snake.Genes.Copy(),
                BirthColor = snake.BirthColor.GetValueOrDefault(snake.Skin),
                Channels = snake.Sensitivity
            };
        }

        public bool IsValid()
        {
            return Genes != null && Genes.IsValid() && BirthColor.IsValid() &&
                (Channels & ~ColorChannels.All) == ColorChannels.None;
        }
    }

    internal sealed class SpeciesLibrary
    {
        private const string Schema = "CREATE TABLE IF NOT EXISTS species " +
            "(name TEXT PRIMARY KEY COLLATE NOCASE NOT NULL, template BLOB NOT NULL)";
        private const string SQLite = "winsqlite3.dll";
        private const int Row = 100, Done = 101, Constraint = 19;
        private static readonly IntPtr CopyValue = new IntPtr(-1);
        private readonly string path;

        public SpeciesLibrary(string path) { this.path = path; }

        public void Save(string name, SpeciesTemplate template)
        {
            name = name.Trim();
            if (name.Length == 0 || name.Length > 60 || name.Any(Char.IsControl))
                throw new ArgumentException("Enter a species name of 1 to 60 characters.");
            if (!template.IsValid()) throw new ArgumentException("This snake's gene values could not be saved.");

            byte[] encodedName = Encoding.UTF8.GetBytes(name);
            byte[] encodedSpecies;
            var serializer = new DataContractJsonSerializer(typeof(SpeciesTemplate));
            using (var bytes = new MemoryStream())
            {
                serializer.WriteObject(bytes, template);
                encodedSpecies = bytes.ToArray();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            WithDatabase(database => WithStatement(database, "INSERT INTO species (name, template) VALUES (?, ?)", statement =>
            {
                Check(database, sqlite3_bind_text(statement, 1, encodedName, encodedName.Length, CopyValue), 0);
                Check(database, sqlite3_bind_blob(statement, 2, encodedSpecies, encodedSpecies.Length, CopyValue), 0);
                int result = sqlite3_step(statement);
                if (result == Constraint) throw new ArgumentException("A species already has that name.");
                Check(database, result, Done);
                return true;
            }));
        }

        public List<KeyValuePair<string, SpeciesTemplate>> List()
        {
            if (!File.Exists(path)) return new List<KeyValuePair<string, SpeciesTemplate>>();
            return WithDatabase(database => WithStatement(database,
                "SELECT name, template FROM species ORDER BY name COLLATE NOCASE", statement =>
            {
                var items = new List<KeyValuePair<string, SpeciesTemplate>>();
                var serializer = new DataContractJsonSerializer(typeof(SpeciesTemplate));
                int result;
                while ((result = sqlite3_step(statement)) == Row)
                {
                    var name = Encoding.UTF8.GetString(Column(statement, 0, sqlite3_column_text(statement, 0)));
                    var payload = Column(statement, 1, sqlite3_column_blob(statement, 1));
                    using (var stream = new MemoryStream(payload))
                    {
                        var template = serializer.ReadObject(stream) as SpeciesTemplate;
                        if (template == null || !template.IsValid())
                            throw new InvalidDataException("A saved species is invalid.");
                        items.Add(new KeyValuePair<string, SpeciesTemplate>(name, template));
                    }
                }
                Check(database, result, Done);
                return items;
            }));
        }

        private T WithDatabase<T>(Func<IntPtr, T> action)
        {
            IntPtr database;
            int result = sqlite3_open(Encoding.UTF8.GetBytes(path + "\0"), out database);
            try
            {
                Check(database, result, 0);
                Check(database, sqlite3_busy_timeout(database, 250), 0);
                WithStatement(database, Schema, statement =>
                {
                    Check(database, sqlite3_step(statement), Done);
                    return true;
                });
                return action(database);
            }
            finally { if (database != IntPtr.Zero) sqlite3_close(database); }
        }

        private static T WithStatement<T>(IntPtr database, string sql, Func<IntPtr, T> action)
        {
            IntPtr statement;
            int result = sqlite3_prepare_v2(database, Encoding.UTF8.GetBytes(sql + "\0"), -1, out statement, IntPtr.Zero);
            try
            {
                Check(database, result, 0);
                return action(statement);
            }
            finally { if (statement != IntPtr.Zero) sqlite3_finalize(statement); }
        }

        private static byte[] Column(IntPtr statement, int index, IntPtr pointer)
        {
            byte[] buffer = new byte[sqlite3_column_bytes(statement, index)];
            if (buffer.Length != 0) Marshal.Copy(pointer, buffer, 0, buffer.Length);
            return buffer;
        }

        private static void Check(IntPtr database, int result, int expected)
        {
            if (result == expected) return;
            string error = database == IntPtr.Zero ? "Could not open the species database." :
                Marshal.PtrToStringAnsi(sqlite3_errmsg(database));
            throw new IOException(error ?? "The species database returned " + result + ".");
        }

        [DllImport(SQLite, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open(byte[] filename, out IntPtr connection);
        [DllImport(SQLite, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr connection);
        [DllImport(SQLite, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_busy_timeout(IntPtr connection, int milliseconds);
        [DllImport(SQLite, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_errmsg(IntPtr connection);
        [DllImport(SQLite, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare_v2(IntPtr connection, byte[] sql, int length,
            out IntPtr statement, IntPtr tail);
        [DllImport(SQLite, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_text(IntPtr statement, int index, byte[] value, int length, IntPtr destroy);
        [DllImport(SQLite, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_blob(IntPtr statement, int index, byte[] value, int length, IntPtr destroy);
        [DllImport(SQLite, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr statement);
        [DllImport(SQLite, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr statement);
        [DllImport(SQLite, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_text(IntPtr statement, int index);
        [DllImport(SQLite, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_blob(IntPtr statement, int index);
        [DllImport(SQLite, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_bytes(IntPtr statement, int index);
    }
}
