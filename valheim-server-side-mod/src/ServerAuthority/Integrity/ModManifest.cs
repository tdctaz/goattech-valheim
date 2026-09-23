using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;

namespace ServerAuthority.Integrity
{
    internal sealed class ModEntry
    {
        internal string Guid;
        internal string Name;
        internal string Version;
        internal string Hash;
    }

    internal sealed class ModManifest
    {
        private static List<ModEntry> _local;

        internal int ProtocolVersion;
        internal string GameVersion;
        internal string ProfileHash;
        internal List<ModEntry> Mods = new List<ModEntry>();

        internal static List<ModEntry> LocalMods()
        {
            if (_local != null)
            {
                return _local;
            }

            List<ModEntry> mods = new List<ModEntry>();
            foreach (PluginInfo info in Chainloader.PluginInfos.Values)
            {
                if (info?.Metadata == null)
                {
                    continue;
                }

                mods.Add(new ModEntry
                {
                    Guid = info.Metadata.GUID,
                    Name = info.Metadata.Name,
                    Version = info.Metadata.Version.ToString(),
                    Hash = HashFile(info.Location),
                });
            }

            mods.Sort((a, b) => string.CompareOrdinal(a.Guid, b.Guid));
            _local = mods;
            return _local;
        }

        internal static ModManifest BuildLocal(byte[] profileData)
        {
            return new ModManifest
            {
                ProtocolVersion = Protocol.Version,
                GameVersion = global::Version.CurrentVersion.ToString(),
                ProfileHash = HashBytes(profileData),
                Mods = LocalMods(),
            };
        }

        internal ZPackage Write()
        {
            ZPackage pkg = new ZPackage();
            pkg.Write(ProtocolVersion);
            pkg.Write(GameVersion ?? "");
            pkg.Write(ProfileHash ?? "");
            pkg.Write(Mods.Count);
            foreach (ModEntry mod in Mods)
            {
                pkg.Write(mod.Guid ?? "");
                pkg.Write(mod.Name ?? "");
                pkg.Write(mod.Version ?? "");
                pkg.Write(mod.Hash ?? "");
            }

            return pkg;
        }

        internal static ModManifest Read(ZPackage pkg)
        {
            ModManifest manifest = new ModManifest
            {
                ProtocolVersion = pkg.ReadInt(),
            };

            if (manifest.ProtocolVersion != Protocol.Version)
            {
                return manifest;
            }

            manifest.GameVersion = pkg.ReadString();
            manifest.ProfileHash = pkg.ReadString();
            int count = pkg.ReadInt();
            if (count < 0 || count > 1024)
            {
                throw new InvalidDataException($"implausible mod count {count}");
            }

            for (int i = 0; i < count; i++)
            {
                manifest.Mods.Add(new ModEntry
                {
                    Guid = pkg.ReadString(),
                    Name = pkg.ReadString(),
                    Version = pkg.ReadString(),
                    Hash = pkg.ReadString(),
                });
            }

            return manifest;
        }

        internal List<string> Problems()
        {
            List<string> problems = new List<string>();

            if (ProtocolVersion != Protocol.Version)
            {
                problems.Add(
                    $"Server Authority on this server speaks protocol {Protocol.Version}, yours speaks " +
                    $"{ProtocolVersion}. Install the same Server Authority version as the server.");
                return problems;
            }

            string serverGame = global::Version.CurrentVersion.ToString();
            if (ModConfig.RequireSameGameVersion.Value && GameVersion != serverGame)
            {
                problems.Add($"Valheim {GameVersion} does not match the server's {serverGame}.");
            }

            if (!ModConfig.ModValidationEnabled.Value)
            {
                return problems;
            }

            HashSet<string> serverOnly = new HashSet<string>(
                Protocol.ParseList(ModConfig.ServerOnlyMods.Value), StringComparer.OrdinalIgnoreCase);
            serverOnly.Remove(Plugin.Guid);

            List<string> allowedList = Protocol.ParseList(ModConfig.AllowedClientMods.Value);
            bool allowAny = allowedList.Contains("*");
            HashSet<string> allowed = new HashSet<string>(allowedList, StringComparer.OrdinalIgnoreCase);
            HashSet<string> forbidden = new HashSet<string>(
                Protocol.ParseList(ModConfig.ForbiddenClientMods.Value), StringComparer.OrdinalIgnoreCase);

            Dictionary<string, ModEntry> client = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (ModEntry mod in Mods)
            {
                client[mod.Guid] = mod;
            }

            Dictionary<string, ModEntry> server = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (ModEntry mod in LocalMods())
            {
                server[mod.Guid] = mod;
            }

            foreach (ModEntry required in server.Values)
            {
                if (serverOnly.Contains(required.Guid))
                {
                    continue;
                }

                if (!client.TryGetValue(required.Guid, out ModEntry theirs))
                {
                    problems.Add($"Missing mod {required.Name} {required.Version} ({required.Guid}).");
                    continue;
                }

                if (theirs.Version != required.Version)
                {
                    problems.Add(
                        $"{required.Name} is version {theirs.Version}, the server requires {required.Version}.");
                    continue;
                }

                if (ModConfig.CompareFileHashes.Value && !string.IsNullOrEmpty(required.Hash) && theirs.Hash != required.Hash)
                {
                    problems.Add($"{required.Name} {required.Version} is not the same build as the server's.");
                }
            }

            foreach (ModEntry extra in client.Values)
            {
                if (forbidden.Contains(extra.Guid))
                {
                    problems.Add($"{extra.Name} ({extra.Guid}) is not allowed on this server.");
                    continue;
                }

                if (server.ContainsKey(extra.Guid) || allowAny || allowed.Contains(extra.Guid))
                {
                    continue;
                }

                problems.Add($"{extra.Name} {extra.Version} ({extra.Guid}) is not allowed on this server.");
            }

            return problems;
        }

        internal string Describe()
        {
            return string.Join(", ", Mods.Select(m => $"{m.Guid} {m.Version}"));
        }

        internal static string HashBytes(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return "";
            }

            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
            }
        }

        private static string HashFile(string path)
        {
            try
            {
                return string.IsNullOrEmpty(path) || !File.Exists(path) ? "" : HashBytes(File.ReadAllBytes(path));
            }
            catch (IOException)
            {
                return "";
            }
            catch (UnauthorizedAccessException)
            {
                return "";
            }
        }
    }
}
