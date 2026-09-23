using System.Collections.Generic;
using UnityEngine;

namespace ValheimCreatures
{
    internal static class RaidDirector
    {
        private const string RaidsRpc = "ValheimCreatures_Raids";
        private const float SendSeconds = 2f;
        private const float ComfortSearchRadius = 30f;

        private sealed class Raid
        {
            internal RandomEvent Template;
            internal Vector3 Position;
            internal int Waves;
            internal bool Sized;
            internal int Spawned;
            internal int Comfort;
            internal int Players;
            internal float InArea;
            internal float NextWave;
            internal float Age;
            internal readonly List<ZDOID> Creatures = new List<ZDOID>();
        }

        private sealed class Shown
        {
            internal string Name;
            internal Vector3 Position;
            internal float Time;
        }

        private static readonly List<Raid> Active = new List<Raid>();
        private static readonly List<Raid> Ended = new List<Raid>();
        private static readonly List<Shown> Known = new List<Shown>();
        private static readonly List<Piece> Pieces = new List<Piece>();

        private static ZRoutedRpc _registeredOn;
        private static float _sendTimer;
        private static bool _applying;
        private static bool _showing;
        private static bool _heldOffLogged;

        internal static void Reset()
        {
            Active.Clear();
            Known.Clear();
            _registeredOn = null;
            _sendTimer = 0f;
            _showing = false;
            _heldOffLogged = false;
        }

        private static bool RunsHere()
        {
            Balance balance = ConfigSync.Current;
            ZNet znet = ZNet.instance;
            return balance.Enabled && balance.RaidWaves && znet != null && znet.IsServer() && znet.IsDedicated();
        }

        internal static bool Intercept(RandomEvent ev, Vector3 position)
        {
            if (_applying || ev == null || !RunsHere() || !HasCreatures(ev))
            {
                return false;
            }

            foreach (Raid raid in Active)
            {
                if (Utils.DistanceXZ(raid.Position, position) < Mathf.Max(raid.Template.m_eventRange, ev.m_eventRange))
                {
                    Plugin.Log.LogInfo(
                        $"Raid {ev.m_name} skipped: the base at ({position.x:0}, {position.z:0}) is already under attack " +
                        $"by {raid.Template.m_name}.");
                    return true;
                }
            }

            Start(ev, position);
            return true;
        }

        private static bool HasCreatures(RandomEvent ev)
        {
            foreach (SpawnSystem.SpawnData data in ev.m_spawn)
            {
                if (data != null && data.m_enabled && data.m_prefab != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Start(RandomEvent ev, Vector3 position)
        {
            RandomEvent template = ev.Clone();
            template.m_pos = position;

            Raid raid = new Raid
            {
                Template = template,
                Position = position,
                NextWave = Mathf.Max(0f, ev.m_spawnerDelay),
            };

            Active.Add(raid);
            Plugin.Log.LogInfo($"Raid {ev.m_name} started at ({position.x:0}, {position.z:0}).");
            Send();
        }

        private static void Size(Raid raid, Balance balance)
        {
            raid.Sized = true;
            raid.Players = ZNet.instance.GetNrOfPlayers();
            raid.Comfort = BaseComfort(raid.Position);
            int waves = Mathf.Min(Mathf.Max(0, raid.Players - 1), balance.RaidPlayerWavesMax) +
                        (balance.RaidComfortPerWave > 0 ? raid.Comfort / balance.RaidComfortPerWave : 0);
            raid.Waves = Mathf.Clamp(waves, 1, Mathf.Max(1, balance.RaidMaxWaves));
            Plugin.Log.LogInfo(
                $"Raid {raid.Template.m_name} at ({raid.Position.x:0}, {raid.Position.z:0}) has {raid.Waves} waves for " +
                $"{raid.Players} players online and base comfort {raid.Comfort}.");
        }

        private static int BaseComfort(Vector3 position)
        {
            int best = SE_Rested.CalculateComfortLevel(true, position);
            Pieces.Clear();
            Piece.GetAllComfortPiecesInRadius(position, ComfortSearchRadius, Pieces);
            List<Vector3> spots = new List<Vector3>();
            foreach (Piece piece in Pieces)
            {
                if (piece != null)
                {
                    spots.Add(piece.transform.position);
                }
            }

            foreach (Vector3 spot in spots)
            {
                best = Mathf.Max(best, SE_Rested.CalculateComfortLevel(true, spot));
            }

            return best;
        }

        internal static void Update(ZNet znet)
        {
            Register();
            if (!znet.IsServer())
            {
                return;
            }

            if (!RunsHere())
            {
                if (Active.Count > 0)
                {
                    Active.Clear();
                    Send();
                }

                return;
            }

            float dt = Time.deltaTime;
            Balance balance = ConfigSync.Current;
            Ended.Clear();
            foreach (Raid raid in Active)
            {
                raid.Age += dt;
                if (balance.RaidTimeoutMinutes > 0f && raid.Age > balance.RaidTimeoutMinutes * 60f)
                {
                    Plugin.Log.LogInfo(
                        $"Raid {raid.Template.m_name} ran out of time after {raid.Spawned} of {raid.Waves} waves; " +
                        $"{Alive(raid)} creatures remain as ordinary monsters.");
                    Release(raid);
                    Ended.Add(raid);
                    continue;
                }

                List<Vector3> players = PlayersInArea(raid);
                if (players.Count > 0)
                {
                    if (!raid.Sized)
                    {
                        Size(raid, balance);
                    }

                    raid.InArea += dt;
                    raid.Template.m_time = raid.InArea;
                    if (raid.Spawned < raid.Waves && raid.InArea >= raid.NextWave)
                    {
                        SpawnWave(raid, players, balance);
                        raid.NextWave = raid.InArea + Mathf.Max(10f, balance.RaidWaveInterval);
                    }
                }

                if (raid.Sized && raid.Spawned >= raid.Waves && Alive(raid) == 0)
                {
                    Plugin.Log.LogInfo($"Raid {raid.Template.m_name} is over: all {raid.Waves} waves are dead.");
                    Ended.Add(raid);
                }
            }

            if (Ended.Count > 0)
            {
                foreach (Raid raid in Ended)
                {
                    Active.Remove(raid);
                }

                Send();
            }

            _sendTimer += dt;
            if (_sendTimer >= SendSeconds)
            {
                _sendTimer = 0f;
                Send();
            }
        }

        private static void SpawnWave(Raid raid, List<Vector3> players, Balance balance)
        {
            raid.Spawned++;
            List<string> names = new List<string>();
            List<ZDOID> spawned = WaveSpawner.Spawn(raid.Template,
                () => WaveSpawner.Around(players[Random.Range(0, players.Count)], balance.RaidSpawnDistance),
                "raid", null, names);
            raid.Creatures.AddRange(spawned);
            Plugin.Log.LogInfo(
                $"Raid {raid.Template.m_name} wave {raid.Spawned} of {raid.Waves}: " +
                $"{(names.Count > 0 ? string.Join(", ", names) : "nothing, no open ground found")}.");
        }

        private static List<Vector3> PlayersInArea(Raid raid)
        {
            List<Vector3> players = new List<Vector3>();
            foreach (ZDO zdo in ZNet.instance.GetAllCharacterZDOS())
            {
                Vector3 position = zdo.GetPosition();
                if (position.y < 3000f && Utils.DistanceXZ(position, raid.Position) < raid.Template.m_eventRange)
                {
                    players.Add(position);
                }
            }

            return players;
        }

        private static int Alive(Raid raid)
        {
            raid.Creatures.RemoveAll(id =>
            {
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                return zdo == null || zdo.GetFloat(ZDOVars.s_health, 1f) <= 0f;
            });
            return raid.Creatures.Count;
        }

        private static void Release(Raid raid)
        {
            foreach (ZDOID id in raid.Creatures)
            {
                GameObject go = ZNetScene.instance.FindInstance(id);
                BaseAI ai = go != null ? go.GetComponent<BaseAI>() : null;
                if (ai != null && ai.m_nview != null && ai.m_nview.IsOwner())
                {
                    ai.SetHuntPlayer(false);
                }
            }
        }

        /// <summary>
        /// Backs <see cref="RaidWeather.Get"/>. Only raids whose template forces an environment
        /// are written, since those are the only ones a weather consumer needs; a template's
        /// position and range are read as its own, not the raid's Position, but the two are the
        /// same because Start clones the template with m_pos set to the raid's position.
        /// </summary>
        internal static int FillWeather(List<Vector3> positions, List<float> ranges, List<Heightmap.Biome> biomes,
            List<string> environments, List<string> names)
        {
            positions.Clear();
            ranges.Clear();
            biomes.Clear();
            environments.Clear();
            names.Clear();

            foreach (Raid raid in Active)
            {
                RandomEvent template = raid.Template;
                if (string.IsNullOrEmpty(template.m_forceEnvironment))
                {
                    continue;
                }

                positions.Add(raid.Position);
                ranges.Add(template.m_eventRange);
                biomes.Add(template.m_biome);
                environments.Add(template.m_forceEnvironment);
                names.Add(template.m_name);
            }

            return positions.Count;
        }

        internal static string Describe()
        {
            if (Active.Count == 0)
            {
                return "no raids";
            }

            List<string> parts = new List<string>();
            foreach (Raid raid in Active)
            {
                parts.Add(
                    $"{raid.Template.m_name} at ({raid.Position.x:0}, {raid.Position.z:0}) wave {raid.Spawned}/" +
                    $"{(raid.Sized ? raid.Waves.ToString() : "?")}, " +
                    $"{Alive(raid)} alive, comfort {raid.Comfort}, {raid.Players} players, {raid.Age / 60f:0.0} min");
            }

            return string.Join("; ", parts);
        }

        private static void Register()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || rpc == _registeredOn)
            {
                return;
            }

            _registeredOn = rpc;
            rpc.Register<ZPackage>(RaidsRpc, OnRaids);
        }

        private static void Send()
        {
            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            ZPackage package = new ZPackage();
            package.Write(Active.Count);
            foreach (Raid raid in Active)
            {
                package.Write(raid.Template.m_name);
                package.Write(raid.Position);
                package.Write(raid.InArea);
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RaidsRpc, package);
        }

        private static void OnRaids(long sender, ZPackage package)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer())
            {
                return;
            }

            Known.Clear();
            int count = package.ReadInt();
            for (int i = 0; i < count; i++)
            {
                Known.Add(new Shown { Name = package.ReadString(), Position = package.ReadVector3(), Time = package.ReadSingle() });
            }

            Show();
        }

        private static void Show()
        {
            RandEventSystem events = RandEventSystem.instance;
            Player player = Player.m_localPlayer;
            if (events == null)
            {
                return;
            }

            Shown chosen = null;
            float closest = float.MaxValue;
            foreach (Shown raid in Known)
            {
                float distance = player != null ? Utils.DistanceXZ(player.transform.position, raid.Position) : 0f;
                if (distance < closest)
                {
                    closest = distance;
                    chosen = raid;
                }
            }

            RandomEvent current = events.m_randomEvent;
            if (current != null && !_showing)
            {
                return;
            }

            _applying = true;
            try
            {
                if (chosen == null)
                {
                    if (current != null)
                    {
                        events.ResetRandomEvent();
                    }

                    _showing = false;
                    return;
                }

                if (current == null || current.m_name != chosen.Name || current.m_pos != chosen.Position)
                {
                    events.SetRandomEventByName(chosen.Name, chosen.Position);
                    _heldOffLogged = false;
                    Plugin.Log.LogInfo(
                        $"Showing raid {chosen.Name} at ({chosen.Position.x:0}, {chosen.Position.z:0}); " +
                        "vanilla event spawning is off on this client while it lasts.");
                }

                if (events.m_randomEvent != null)
                {
                    events.m_randomEvent.m_time = chosen.Time;
                    _showing = true;
                }
            }
            finally
            {
                _applying = false;
            }
        }

        internal static bool HoldOffSpawners()
        {
            if (!_showing || ZNet.instance == null || ZNet.instance.IsServer())
            {
                return false;
            }

            if (!_heldOffLogged)
            {
                _heldOffLogged = true;
                RandomEvent current = RandEventSystem.instance != null ? RandEventSystem.instance.m_randomEvent : null;
                Plugin.Log.LogInfo(
                    $"Held off vanilla spawning for raid {(current != null ? current.m_name : "?")}: its spawner delay " +
                    "has passed, so vanilla would now spawn it on this client alongside the server's waves.");
            }

            return true;
        }

        internal static bool SuppressVanillaBroadcast(RandEventSystem events)
        {
            return RunsHere() && events.m_randomEvent == null;
        }
    }
}
