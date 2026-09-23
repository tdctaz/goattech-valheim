#if DEBUG_TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace ValheimCreatures
{
    internal static class TestCommands
    {
        private const float PollSeconds = 1f;
        private const float SettleSeconds = 5f;

        private static readonly System.Random Rng = new System.Random();

        private static readonly HashSet<ZDOID> Tracked = new HashSet<ZDOID>();
        private static readonly List<ZDOID> Gone = new List<ZDOID>();
        private static readonly Queue<string> Pending = new Queue<string>();

        private static string _path;
        private static DateTime _lastSeen = DateTime.MinValue;
        private static float _nextPoll;
        private static float _playerSince = -1f;
        private static float _waitSeconds = -1f;
        private static float _clearedAt = -1f;

        internal static void Reset()
        {
            _lastSeen = DateTime.MinValue;
            _nextPoll = 0f;
            Tracked.Clear();
            Pending.Clear();
            _waitSeconds = -1f;
            _clearedAt = -1f;
        }

        internal static bool IsTracked(Character character)
        {
            return Tracked.Count > 0 && character != null && character.m_nview != null &&
                   character.m_nview.GetZDO() != null && Tracked.Contains(character.m_nview.GetZDO().m_uid);
        }

        internal static void Track(Character character)
        {
            ZDO zdo = character.m_nview != null ? character.m_nview.GetZDO() : null;
            if (zdo != null)
            {
                Tracked.Add(zdo.m_uid);
            }
        }

        internal static void OnDrops(Character character, List<KeyValuePair<GameObject, int>> drops)
        {
            if (!IsTracked(character))
            {
                return;
            }

            List<string> parts = new List<string>();
            foreach (KeyValuePair<GameObject, int> drop in drops)
            {
                parts.Add($"{drop.Key.name} x{drop.Value}");
            }

            CreatureTraits traits = CreatureTraits.Of(character);
            string colors = traits == null ? "None"
                : traits.SecondColor != StarColor.None ? $"{traits.Color}+{traits.SecondColor}" : traits.Color.ToString();
            Plugin.Log.LogInfo(
                $"Test command: {Utils.GetPrefabName(character.gameObject)} with {character.GetLevel() - 1} stars, " +
                $"{colors}, died. Drops: " +
                $"{(parts.Count > 0 ? string.Join(", ", parts) : "nothing")}.");
        }

        internal static void Poll(ZNet znet)
        {
            if (!ModConfig.EnableTestCommands.Value || !znet.IsServer() || ZNetScene.instance == null ||
                Time.time < _nextPoll)
            {
                return;
            }

            _nextPoll = Time.time + PollSeconds;
            if (!TryFindPlayer(znet, out Vector3 here))
            {
                _playerSince = -1f;
                return;
            }

            if (_playerSince < 0f)
            {
                _playerSince = Time.time;
            }

            if (Time.time - _playerSince < SettleSeconds)
            {
                return;
            }

            RunPending(here);

            if (_path == null)
            {
                _path = Path.Combine(Paths.ConfigPath, "valheimcreatures_test.txt");
            }

            if (!File.Exists(_path))
            {
                return;
            }

            DateTime written = File.GetLastWriteTimeUtc(_path);
            if (written == _lastSeen)
            {
                return;
            }

            _lastSeen = written;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(_path);
            }
            catch (IOException)
            {
                _lastSeen = DateTime.MinValue;
                return;
            }

            if (!TryFindPlayer(znet, out Vector3 player))
            {
                Plugin.Log.LogWarning("Test command: no player connected, keeping the file for later.");
                _lastSeen = DateTime.MinValue;
                return;
            }

            bool handled = false;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue;
                }

                Pending.Enqueue(line);
                handled = true;
            }

            RunPending(player);

            if (handled)
            {
                try
                {
                    File.WriteAllText(_path, string.Empty);
                    _lastSeen = File.GetLastWriteTimeUtc(_path);
                }
                catch (IOException)
                {
                }
            }
        }

        private static void RunPending(Vector3 player)
        {
            while (Pending.Count > 0)
            {
                if (_waitSeconds >= 0f)
                {
                    if (AnyTrackedAlive())
                    {
                        _clearedAt = -1f;
                        return;
                    }

                    if (_clearedAt < 0f)
                    {
                        _clearedAt = Time.time;
                    }

                    if (Time.time - _clearedAt < _waitSeconds)
                    {
                        return;
                    }

                    _waitSeconds = -1f;
                    _clearedAt = -1f;
                }

                string line = Pending.Dequeue();
                string[] args = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (args[0].Equals("wait", StringComparison.OrdinalIgnoreCase))
                {
                    _waitSeconds = Mathf.Max(0f, Number(args, 1, 10f));
                    Plugin.Log.LogInfo($"Test command: waiting for {Tracked.Count} test creatures to die, then {_waitSeconds:0}s.");
                    continue;
                }

                try
                {
                    Handle(args, player);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Test command '{line}' failed: {e}");
                }
            }
        }

        private static bool AnyTrackedAlive()
        {
            Gone.Clear();
            foreach (ZDOID id in Tracked)
            {
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (zdo == null || zdo.GetFloat(ZDOVars.s_health, 1f) <= 0f)
                {
                    Gone.Add(id);
                }
            }

            foreach (ZDOID id in Gone)
            {
                Tracked.Remove(id);
            }

            return Tracked.Count > 0;
        }

        private static bool TryFindPlayer(ZNet znet, out Vector3 position)
        {
            if (Player.m_localPlayer != null)
            {
                position = Player.m_localPlayer.transform.position;
                return true;
            }

            foreach (ZNetPeer peer in znet.GetPeers())
            {
                if (peer.IsReady())
                {
                    position = peer.m_refPos;
                    return true;
                }
            }

            position = Vector3.zero;
            return false;
        }

        private static void Handle(string[] args, Vector3 player)
        {
            switch (args[0].ToLowerInvariant())
            {
                case "spawn":
                    Spawn(args, player);
                    break;
                case "item":
                    Item(args, player);
                    break;
                case "roll":
                    Roll(args, player);
                    break;
                case "key":
                    ZoneSystem.instance.SetGlobalKey(Arg(args, 1, ""));
                    Plugin.Log.LogInfo($"Test command: set global key {Arg(args, 1, "")}.");
                    break;
                case "unkey":
                    ZoneSystem.instance.RemoveGlobalKey(Arg(args, 1, ""));
                    Plugin.Log.LogInfo($"Test command: removed global key {Arg(args, 1, "")}.");
                    break;
                case "raid":
                    Raid(Arg(args, 1, ""), args.Length > 3 ? new Vector3(Number(args, 2, 0f), 0f, Number(args, 3, 0f)) : player);
                    break;
                case "bases":
                    Bases(Number(args, 1, 64f));
                    break;
                case "config":
                    SetConfig(args);
                    break;
                case "clear":
                    Clear(player, Number(args, 1, 40f));
                    break;
                case "nearby":
                    Nearby(player, Number(args, 1, 60f));
                    break;
                case "day":
                    if (EnvMan.instance != null)
                    {
                        EnvMan.instance.SkipToMorning();
                        Plugin.Log.LogInfo("Test command: skipping to morning.");
                    }

                    break;
                case "raids":
                    Plugin.Log.LogInfo($"Test command: {RaidDirector.Describe()}.");
                    break;
                case "status":
                    Plugin.Log.LogInfo($"Test command: {BossProgress.DescribeStages()}");
                    break;
                default:
                    Plugin.Log.LogWarning($"Test command: unknown command '{args[0]}'.");
                    break;
            }
        }

        private static void Spawn(string[] args, Vector3 player)
        {
            GameObject prefab = Prefab(Arg(args, 1, ""));
            if (prefab == null)
            {
                return;
            }

            int count = Mathf.Clamp((int)Number(args, 2, 1f), 1, 50);
            int stars = Mathf.Clamp((int)Number(args, 3, 0f), 0, 5);
            StarColor color = StarColor.None;
            StarColor second = StarColor.None;
            if (args.Length > 4)
            {
                string[] names = args[4].Split('+');
                if (!Enum.TryParse(names[0], true, out color) ||
                    (names.Length > 1 && !Enum.TryParse(names[1], true, out second)))
                {
                    Plugin.Log.LogWarning($"Test command: '{args[4]}' is not a color, or two joined by +.");
                    return;
                }
            }

            float distance = Number(args, 5, 10f);
            for (int i = 0; i < count; i++)
            {
                GameObject spawned = UnityEngine.Object.Instantiate(prefab, Around(player, distance), Quaternion.identity);
                Character character = spawned.GetComponent<Character>();
                if (character == null)
                {
                    continue;
                }

                Tracked.Add(character.m_nview.GetZDO().m_uid);
                if (!character.IsBoss())
                {
                    character.SetLevel(stars + 1);
                }

                if (args.Length > 4 || !character.IsBoss())
                {
                    CreatureTraits.SetColors(character, color, second);
                }
            }

            Plugin.Log.LogInfo(
                $"Test command: spawned {prefab.name} x{count}, {stars} stars, {color}" +
                $"{(second != StarColor.None ? "+" + second : "")}.");
        }

        private static void Item(string[] args, Vector3 player)
        {
            GameObject prefab = Prefab(Arg(args, 1, ""));
            if (prefab == null)
            {
                return;
            }

            int count = Mathf.Clamp((int)Number(args, 2, 1f), 1, 500);
            int quality = Mathf.Clamp((int)Number(args, 3, 1f), 1, 10);
            int remaining = count;
            while (remaining > 0)
            {
                GameObject spawned = UnityEngine.Object.Instantiate(prefab, Around(player, 1.5f), Quaternion.identity);
                ItemDrop.OnCreateNew(spawned);
                ItemDrop item = spawned.GetComponent<ItemDrop>();
                if (item == null)
                {
                    break;
                }

                int stack = Mathf.Min(remaining, Mathf.Max(1, item.m_itemData.m_shared.m_maxStackSize));
                item.m_itemData.m_stack = stack;
                item.m_itemData.m_quality = Mathf.Min(quality, Mathf.Max(1, item.m_itemData.m_shared.m_maxQuality));
                item.m_itemData.m_durability = item.m_itemData.GetMaxDurability();
                item.Save();
                remaining -= stack;
            }

            Plugin.Log.LogInfo($"Test command: dropped {prefab.name} x{count} at quality {quality}.");
        }

        private static void Roll(string[] args, Vector3 player)
        {
            GameObject prefab = Prefab(Arg(args, 1, ""));
            Character character = prefab != null ? prefab.GetComponent<Character>() : null;
            if (character == null)
            {
                return;
            }

            int samples = Mathf.Clamp((int)Number(args, 2, 10000f), 1, 1000000);
            int[] levels = new int[8];
            int[] colors = new int[StarColors.Count];
            BossStage stage = BossProgress.StageOf(prefab.name, character.GetFaction());
            for (int i = 0; i < samples; i++)
            {
                int level = LevelRoll.RollLevel(stage, 1, 0f, player);
                levels[Mathf.Clamp(level, 1, 7)]++;
                if (level > 1)
                {
                    colors[(int)LevelRoll.RollColor(level)]++;
                }
            }

            StringBuilder text = new StringBuilder();
            text.Append($"Test command: {samples} rolls for {prefab.name} ({character.GetFaction()}, " +
                        $"{stage}). Stars:");
            for (int level = 1; level < levels.Length; level++)
            {
                if (levels[level] > 0)
                {
                    text.Append($" {level - 1}={levels[level] * 100f / samples:0.00}%");
                }
            }

            int starred = samples - levels[1];
            text.Append(". Colors of starred:");
            for (int i = 0; i < colors.Length; i++)
            {
                text.Append($" {(StarColor)i}={(starred > 0 ? colors[i] * 100f / starred : 0f):0.0}%");
            }

            Plugin.Log.LogInfo(text.ToString());
        }

        private static void Raid(string name, Vector3 player)
        {
            if (!RandEventSystem.instance.HaveEvent(name))
            {
                List<string> names = new List<string>();
                foreach (RandomEvent ev in RandEventSystem.instance.m_events)
                {
                    names.Add(ev.m_name);
                }

                Plugin.Log.LogWarning($"Test command: no raid named '{name}'. Raids: {string.Join(", ", names)}.");
                return;
            }

            RandEventSystem.instance.SetRandomEventByName(name, player);
            Plugin.Log.LogInfo($"Test command: started raid {name}.");
        }

        private static void Bases(float cell)
        {
            Dictionary<Vector2Int, int> counts = new Dictionary<Vector2Int, int>();
            Dictionary<Vector2Int, Vector3> sums = new Dictionary<Vector2Int, Vector3>();
            HashSet<int> pieces = new HashSet<int>();
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab != null && prefab.GetComponent<Piece>() != null && prefab.GetComponent<WearNTear>() != null)
                {
                    pieces.Add(prefab.name.GetStableHashCode());
                }
            }

            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
            {
                if (!pieces.Contains(zdo.GetPrefab()) || zdo.GetLong(ZDOVars.s_creator) == 0L)
                {
                    continue;
                }

                Vector3 position = zdo.GetPosition();
                Vector2Int key = new Vector2Int(Mathf.FloorToInt(position.x / cell), Mathf.FloorToInt(position.z / cell));
                counts.TryGetValue(key, out int count);
                counts[key] = count + 1;
                sums.TryGetValue(key, out Vector3 sum);
                sums[key] = sum + position;
            }

            List<string> parts = new List<string>();
            foreach (KeyValuePair<Vector2Int, int> entry in counts)
            {
                if (entry.Value < 10)
                {
                    continue;
                }

                Vector3 center = sums[entry.Key] / entry.Value;
                parts.Add($"({center.x:0}, {center.z:0}) {entry.Value} pieces");
            }

            parts.Sort(StringComparer.Ordinal);
            Plugin.Log.LogInfo($"Test command: {parts.Count} bases of 10+ player pieces: {string.Join("; ", parts)}.");
        }

        private static void SetConfig(string[] args)
        {
            if (args.Length < 4)
            {
                Plugin.Log.LogWarning("Test command: config needs a section, a key and a value.");
                return;
            }

            foreach (BepInEx.Configuration.ConfigDefinition key in Plugin.ConfigFile.Keys)
            {
                if (key.Section == args[1] && key.Key == args[2])
                {
                    Plugin.ConfigFile[key].SetSerializedValue(string.Join(" ", args, 3, args.Length - 3));
                    Plugin.Log.LogInfo($"Test command: {args[1]}.{args[2]} is now {Plugin.ConfigFile[key].BoxedValue}.");
                    return;
                }
            }

            Plugin.Log.LogWarning($"Test command: no setting {args[1]}.{args[2]}.");
        }

        private static void Nearby(Vector3 player, float radius)
        {
            List<string> parts = new List<string>();
            foreach (Character character in Character.GetAllCharacters())
            {
                if (character == null || character.IsPlayer() ||
                    Vector3.Distance(character.transform.position, player) > radius)
                {
                    continue;
                }

                CreatureTraits traits = CreatureTraits.Of(character);
                string colors = traits == null || traits.Color == StarColor.None ? ""
                    : traits.SecondColor != StarColor.None ? $" {traits.Color}+{traits.SecondColor}" : $" {traits.Color}";
                parts.Add($"{Utils.GetPrefabName(character.gameObject)} {character.GetLevel() - 1}*{colors}");
            }

            parts.Sort(StringComparer.Ordinal);
            Plugin.Log.LogInfo($"Test command: {parts.Count} creatures within {radius:0}m: {string.Join(", ", parts)}.");
        }

        private static void Clear(Vector3 player, float radius)
        {
            int removed = 0;
            foreach (Character character in new List<Character>(Character.GetAllCharacters()))
            {
                if (character == null || character.IsPlayer() || character.IsTamed() ||
                    Vector3.Distance(character.transform.position, player) > radius)
                {
                    continue;
                }

                ZNetView view = character.m_nview;
                if (view == null || !view.IsValid())
                {
                    continue;
                }

                view.ClaimOwnership();
                ZNetScene.instance.Destroy(character.gameObject);
                removed++;
            }

            Plugin.Log.LogInfo($"Test command: removed {removed} creatures within {radius:0}m.");
        }

        private static Vector3 Around(Vector3 center, float distance)
        {
            double angle = Rng.NextDouble() * Math.PI * 2.0;
            Vector2 direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));

            Vector3 position = center + new Vector3(direction.x, 0f, direction.y) * distance;
            position.y = ZoneSystem.instance.FindFloor(position + Vector3.up * 50f, out float height)
                ? height + 0.5f
                : center.y + 1f;
            return position;
        }

        private static GameObject Prefab(string name)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(name);
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"Test command: no prefab named '{name}'.");
            }

            return prefab;
        }

        private static string Arg(string[] args, int index, string fallback)
        {
            return args.Length > index ? args[index] : fallback;
        }

        private static float Number(string[] args, int index, float fallback)
        {
            return args.Length > index && float.TryParse(args[index], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float value)
                ? value
                : fallback;
        }
    }
}
#endif
