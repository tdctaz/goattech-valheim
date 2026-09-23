using System;
using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority.Integrity
{
    internal static class ProfileCodec
    {
        private static byte[] _pending;

        internal static bool Decoding;

        internal static bool TryTakePending(out ZPackage pkg)
        {
            pkg = null;
            if (_pending == null)
            {
                return false;
            }

            pkg = new ZPackage(_pending);
            _pending = null;
            return true;
        }

        internal static PlayerProfile Decode(byte[] data, string filename, FileHelpers.FileSource source)
        {
            if (data == null || data.Length == 0)
            {
                return null;
            }

            PlayerProfile profile = new PlayerProfile(filename, source);
            _pending = data;
            Decoding = true;
            try
            {
                return profile.Load() && profile.m_playerData != null ? profile : null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not decode a character: {e.Message}");
                return null;
            }
            finally
            {
                _pending = null;
                Decoding = false;
            }
        }

        internal static byte[] ReadSaved(PlayerProfile profile)
        {
            try
            {
                return profile?.LoadPlayerDataFromDisk()?.GetArray();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not read the saved character back: {e.Message}");
                return null;
            }
        }
    }

    internal struct ItemSummary
    {
        internal int PrefabHash;
        internal int Stack;
        internal int Quality;
        internal bool Cheated;
    }

    internal sealed class PlayerDataSummary
    {
        internal const int SupportedPlayerDataVersion = 33;

        internal readonly List<ItemSummary> Items = new List<ItemSummary>();
        internal readonly Dictionary<int, float> Skills = new Dictionary<int, float>();
        internal readonly List<string> Uniques = new List<string>();
        internal readonly List<string> Trophies = new List<string>();
        internal string Beard = "";
        internal string Hair = "";
        internal Vector3 SkinColor = Vector3.one;
        internal Vector3 HairColor = Vector3.one;
        internal int Model;

        internal static PlayerDataSummary Read(byte[] playerData, out string error)
        {
            error = null;
            try
            {
                ZPackage pkg = new ZPackage(playerData);
                int version = pkg.ReadInt();
                if (version != SupportedPlayerDataVersion)
                {
                    error = $"player data version {version}, this mod reads {SupportedPlayerDataVersion}";
                    return null;
                }

                PlayerDataSummary summary = new PlayerDataSummary();

                pkg.ReadSingle();
                pkg.ReadSingle();
                pkg.ReadSingle();
                pkg.ReadSingle();
                pkg.ReadString();
                pkg.ReadSingle();

                global::Version.Item itemVersion = (global::Version.Item)pkg.ReadInt();
                if (itemVersion < global::Version.Item.Smaller)
                {
                    error = $"inventory version {(int)itemVersion} is older than this mod reads";
                    return null;
                }

                int itemCount = pkg.ReadUShort();
                for (int i = 0; i < itemCount; i++)
                {
                    (int prefabHash, ItemDrop.ItemData item) = ItemDrop.ItemData.Load(pkg, itemVersion);
                    summary.Items.Add(new ItemSummary
                    {
                        PrefabHash = prefabHash,
                        Stack = item.m_stack,
                        Quality = item.m_quality,
                        Cheated = item.m_cheated,
                    });
                }

                SkipStrings(pkg);

                int stations = pkg.ReadInt();
                for (int i = 0; i < stations; i++)
                {
                    pkg.ReadString();
                    pkg.ReadInt();
                }

                SkipStrings(pkg);
                SkipStrings(pkg);
                ReadStrings(pkg, summary.Uniques);
                ReadStrings(pkg, summary.Trophies);
                SkipStrings(pkg);

                int texts = pkg.ReadInt();
                for (int i = 0; i < texts; i++)
                {
                    pkg.ReadString();
                    pkg.ReadString();
                }

                summary.Beard = pkg.ReadString();
                summary.Hair = pkg.ReadString();
                summary.SkinColor = pkg.ReadVector3();
                summary.HairColor = pkg.ReadVector3();
                summary.Model = pkg.ReadInt();

                int foods = pkg.ReadInt();
                for (int i = 0; i < foods; i++)
                {
                    pkg.ReadString();
                    pkg.ReadSingle();
                }

                int skillsVersion = pkg.ReadInt();
                int skills = pkg.ReadInt();
                for (int i = 0; i < skills; i++)
                {
                    int type = pkg.ReadInt();
                    float level = pkg.ReadSingle();
                    if (skillsVersion >= 2)
                    {
                        pkg.ReadSingle();
                    }

                    summary.Skills[type] = level;
                }

                return summary;
            }
            catch (Exception e)
            {
                error = $"unreadable player data: {e.Message}";
                return null;
            }
        }

        internal static int ReadVersion(byte[] playerData)
        {
            if (playerData == null || playerData.Length < 4)
            {
                return -1;
            }

            return BitConverter.ToInt32(playerData, 0);
        }

        private static void SkipStrings(ZPackage pkg)
        {
            int count = pkg.ReadInt();
            for (int i = 0; i < count; i++)
            {
                pkg.ReadString();
            }
        }

        private static void ReadStrings(ZPackage pkg, List<string> into)
        {
            int count = pkg.ReadInt();
            for (int i = 0; i < count; i++)
            {
                into.Add(pkg.ReadString());
            }
        }
    }
}
