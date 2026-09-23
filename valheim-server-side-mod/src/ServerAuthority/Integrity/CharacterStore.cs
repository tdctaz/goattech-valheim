using System;
using System.IO;
using System.Linq;
using System.Text;

namespace ServerAuthority.Integrity
{
    internal static class CharacterStore
    {
        internal static string Root()
        {
            string configured = ModConfig.CharacterStoragePath.Value;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            return Path.Combine(Utils.GetSaveDataPath(FileHelpers.FileSource.Local), "characters_serverauthority");
        }

        internal static string PathFor(string world, string platformUser, string characterName)
        {
            return Path.Combine(
                Root(),
                Sanitize(world),
                Sanitize(platformUser),
                Sanitize(characterName.ToLowerInvariant()) + ".fch");
        }

        internal static byte[] Load(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    return ReadFramed(path, false);
                }
                catch (Exception e)
                {
                    byte[] fallback = Recover(path, $"{path} is unreadable ({e.Message})");
                    if (fallback == null)
                    {
                        throw;
                    }

                    return fallback;
                }
            }

            return Recover(path, $"{path} is missing");
        }

        private static byte[] Recover(string path, string reason)
        {
            string[] candidates = { path + ".new", path + ".old" };
            foreach (string candidate in candidates)
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                byte[] data;
                try
                {
                    data = ReadFramed(candidate, true);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"{reason}. Cannot recover from {candidate}: {e.Message}");
                    continue;
                }

                if (ProfileCodec.Decode(data, "serverauthority", FileHelpers.FileSource.Local) == null)
                {
                    Plugin.Log.LogWarning($"{reason}. Cannot recover from {candidate}: it does not decode as a character.");
                    continue;
                }

                Plugin.Log.LogWarning(
                    $"{reason}, probably from a crash while saving. Recovered the character from {candidate}, " +
                    $"written {File.GetLastWriteTimeUtc(candidate):yyyy-MM-dd HH:mm:ss} UTC.");

                try
                {
                    if (File.Exists(path))
                    {
                        string unreadable = path + ".unreadable";
                        if (File.Exists(unreadable))
                        {
                            File.Delete(unreadable);
                        }

                        File.Move(path, unreadable);
                        Plugin.Log.LogWarning($"Kept the unreadable file as {unreadable}.");
                    }

                    Save(path, data);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Could not write the recovered character back to {path}: {e.Message}");
                }

                return data;
            }

            return null;
        }

        private static byte[] ReadFramed(string path, bool verifyHash)
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(path)))
            {
                int length = reader.ReadInt32();
                if (length < 0 || length > reader.BaseStream.Length - 4)
                {
                    throw new InvalidDataException($"{path} is truncated");
                }

                byte[] data = reader.ReadBytes(length);
                if (data.Length != length)
                {
                    throw new InvalidDataException($"{path} is truncated");
                }

                if (verifyHash)
                {
                    int hashLength = reader.ReadInt32();
                    byte[] hash = reader.ReadBytes(hashLength);
                    byte[] expected = new ZPackage(data).GenerateHash();
                    if (hash.Length != expected.Length || !hash.SequenceEqual(expected))
                    {
                        throw new InvalidDataException($"{path} fails its hash check");
                    }
                }

                return data;
            }
        }

        internal static void WriteFramed(string path, byte[] data)
        {
            byte[] hash = new ZPackage(data).GenerateHash();
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(data.Length);
                    writer.Write(data);
                    writer.Write(hash.Length);
                    writer.Write(hash);
                    writer.Flush();
                    stream.Flush(true);
                }
            }
        }

        internal static void Save(string path, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            string temp = path + ".new";
            WriteFramed(temp, data);

            if (File.Exists(path))
            {
                File.Copy(path, path + ".old", true);
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "_";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                sb.Append(invalid.Contains(c) || c == '.' ? '_' : c);
            }

            return sb.ToString();
        }
    }
}
