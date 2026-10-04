using System.Collections.Generic;
using UnityEngine;

namespace ValheimCreatures
{
    internal static class RaidDirector
    {
        private const string RaidsRpc = "ValheimCreatures_Raids";
        private const string WaveRpc = "ValheimCreatures_RaidWave";
        private const float SendSeconds = 2f;
        private const float ComfortSearchRadius = 30f;
        private const float SweepSeconds = 5f;
        private const float LostSeconds = 60f;
        private const float DyingSeconds = 10f;

        private sealed class Wave
        {
            internal int Number;
            internal readonly List<ZDOID> Creatures = new List<ZDOID>();
            internal readonly Dictionary<ZDOID, float> Away = new Dictionary<ZDOID, float>();
            internal readonly Dictionary<ZDOID, float> Dying = new Dictionary<ZDOID, float>();
        }

        private sealed class Line
        {
            internal SpawnSystem.SpawnData Data;
            internal bool Boss;
            internal float Next;
            internal readonly List<ZDOID> Creatures = new List<ZDOID>();
        }

        private sealed class Raid
        {
            internal RandomEvent Template;
            internal Vector3 Position;
            internal string BossPrefab;
            internal bool BossDown;
            internal readonly List<ZDOID> Bosses = new List<ZDOID>();
            internal readonly HashSet<ZDOID> Crowned = new HashSet<ZDOID>();
            internal readonly List<Line> Lines = new List<Line>();
            internal int Waves;
            internal bool Sized;
            internal bool HeldLogged;
            internal int Spawned;
            internal int Defeated;
            internal int Comfort;
            internal int Players;
            internal float InArea;
            internal float NextWave;
            internal float Age;
            internal readonly List<Wave> Live = new List<Wave>();
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
        private static readonly List<string> Lost = new List<string>();

        private static ZRoutedRpc _registeredOn;
        private static float _sendTimer;
        private static float _sweepTimer;
        private static bool _applying;
        private static bool _showing;
        private static bool _heldOffLogged;

        internal static void Reset()
        {
            Active.Clear();
            Raiders.Reset();
            Known.Clear();
            _registeredOn = null;
            _sendTimer = 0f;
            _sweepTimer = 0f;
            _showing = false;
            _heldOffLogged = false;
        }

        internal static bool RunsHere()
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
                BossPrefab = BossFor(ev, ConfigSync.Current.RaidBossRaids),
                NextWave = Mathf.Max(0f, ev.m_spawnerDelay),
            };

            Active.Add(raid);
            Plugin.Log.LogInfo($"Raid {ev.m_name} started at ({position.x:0}, {position.z:0}).");
            Send();
        }

        private static string BossFor(RandomEvent ev, string mapping)
        {
            foreach (string entry in (mapping ?? "").Split(new[] { ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = entry.Split(':');
                if (parts.Length != 2)
                {
                    Plugin.Log.LogWarning($"Raids.BossRaids: '{entry.Trim()}' is not a RaidName:BossPrefab pair.");
                    continue;
                }

                if (parts[0].Trim() != ev.m_name)
                {
                    continue;
                }

                string boss = parts[1].Trim();
                foreach (SpawnSystem.SpawnData data in ev.m_spawn)
                {
                    if (WaveSpawner.Usable(data) && data.m_prefab.name == boss)
                    {
                        return boss;
                    }
                }

                Plugin.Log.LogWarning(
                    $"Raids.BossRaids names {boss} as the boss of {ev.m_name}, but that raid spawns no {boss}, so it " +
                    "runs as an ordinary raid.");
            }

            return null;
        }

        private static void Size(Raid raid, Balance balance, int players)
        {
            if (raid.Sized && players <= raid.Players)
            {
                return;
            }

            if (raid.BossPrefab != null)
            {
                if (!raid.Sized)
                {
                    raid.Sized = true;
                    raid.Waves = 1;
                    Plugin.Log.LogInfo(
                        $"Raid {raid.Template.m_name} at ({raid.Position.x:0}, {raid.Position.z:0}) is a boss raid: " +
                        $"one wave with {raid.BossPrefab}, and the rest keep coming as in vanilla until it is down.");
                }

                raid.Players = players;
                return;
            }

            bool first = !raid.Sized;
            if (first)
            {
                raid.Sized = true;
                raid.Comfort = BaseComfort(raid.Position);
            }

            raid.Players = players;
            int waves = Mathf.Min(Mathf.Max(0, raid.Players - 1), balance.RaidPlayerWavesMax) +
                        (balance.RaidComfortPerWave > 0 ? raid.Comfort / balance.RaidComfortPerWave : 0);
            waves = Mathf.Clamp(waves, 1, Mathf.Max(1, balance.RaidMaxWaves));
            if (!first && waves <= raid.Waves)
            {
                return;
            }

            raid.Waves = Mathf.Max(raid.Waves, waves);
            Plugin.Log.LogInfo(
                $"Raid {raid.Template.m_name} at ({raid.Position.x:0}, {raid.Position.z:0}) " +
                $"{(first ? "has" : "grows to")} {raid.Waves} waves for {raid.Players} players in the area and base " +
                $"comfort {raid.Comfort}.");
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

                SweepRaiders(Time.deltaTime);
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
                    Ended.Add(raid);
                    continue;
                }

                List<Vector3> players = PlayersInArea(raid);
                bool present = players.Count > 0;
                if (present)
                {
                    Size(raid, balance, players.Count);

                    raid.InArea += dt;
                    raid.Template.m_time = raid.InArea;
                }

                Prune(raid, present, dt, balance);

                if (raid.BossPrefab != null)
                {
                    TrackBoss(raid);
                    if (present && raid.Spawned == 0 && raid.InArea >= raid.NextWave)
                    {
                        if (!StartBossWave(raid, players, balance))
                        {
                            Ended.Add(raid);
                            continue;
                        }
                    }
                    else if (present && raid.Spawned > 0)
                    {
                        Trickle(raid, players, balance);
                    }
                }
                else if (present && raid.Spawned < raid.Waves && raid.InArea >= raid.NextWave)
                {
                    int most = Mathf.Max(1, balance.RaidMaxActiveWaves);
                    if (raid.Live.Count < most)
                    {
                        if (!SpawnWave(raid, players, balance))
                        {
                            Ended.Add(raid);
                            continue;
                        }

                        raid.NextWave = raid.InArea + NextWaveSeconds(balance);
                        raid.HeldLogged = false;
                    }
                    else if (!raid.HeldLogged)
                    {
                        raid.HeldLogged = true;
                        Plugin.Log.LogInfo(
                            $"Raid {raid.Template.m_name} wave {raid.Spawned + 1} of {raid.Waves} is due but held back: " +
                            $"{raid.Live.Count} of its waves are alive with {Alive(raid)} creatures, and {most} is the " +
                            "most at once.");
                    }
                }

                if (raid.Sized && raid.Spawned >= raid.Waves && raid.Live.Count == 0)
                {
                    Plugin.Log.LogInfo($"Raid {raid.Template.m_name} is over: all {raid.Waves} waves are defeated.");
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

            SweepRaiders(dt);
        }

        private static void SweepRaiders(float dt)
        {
            _sweepTimer += dt;
            if (_sweepTimer < SweepSeconds)
            {
                return;
            }

            _sweepTimer = 0f;
            Raiders.Sweep(Running);
        }

        private static bool Running(Vector3 center)
        {
            foreach (Raid raid in Active)
            {
                if (Utils.DistanceXZ(raid.Position, center) < 1f)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SpawnWave(Raid raid, List<Vector3> players, Balance balance)
        {
            List<string> names = new List<string>();
            List<ZDOID> spawned = WaveSpawner.Spawn(raid.Template,
                data => WaveSpawner.Around(players[Random.Range(0, players.Count)], balance.RaidSpawnDistance, data),
                "raid", null, names);
            if (names.Count == 0)
            {
                Plugin.Log.LogInfo(
                    $"Raid {raid.Template.m_name} wave {raid.Spawned + 1} of {raid.Waves} found no open ground outside " +
                    $"the base, so the raid ends: the base leaves raiders nowhere to stand.{WaveSpawner.Rejected()}");
                return false;
            }

            raid.Spawned++;
            Wave wave = new Wave { Number = raid.Spawned };
            wave.Creatures.AddRange(spawned);
            raid.Live.Add(wave);
            foreach (ZDOID id in spawned)
            {
                Raiders.Enlist(id, raid.Position);
            }

            Plugin.Log.LogInfo(
                $"Raid {raid.Template.m_name} wave {raid.Spawned} of {raid.Waves}: {string.Join(", ", names)}. " +
                $"{raid.Live.Count} of its waves are alive; the next is due in {NextWaveSeconds(balance):0} s with a " +
                $"player in the area, sooner if these die.{WaveSpawner.Placements()}{WaveSpawner.Rejected()}");
            return true;
        }

        private static bool StartBossWave(Raid raid, List<Vector3> players, Balance balance)
        {
            List<string> names = new List<string>();
            List<ZDOID> spawned = new List<ZDOID>();
            SpawnSystem.SpawnData bossLine = null;
            WaveSpawner.Begin();
            foreach (SpawnSystem.SpawnData data in raid.Template.m_spawn)
            {
                if (WaveSpawner.Usable(data) && data.m_prefab.name == raid.BossPrefab)
                {
                    bossLine = data;
                    Line line = new Line { Data = data, Boss = true };
                    raid.Lines.Add(line);
                    WaveSpawner.Group(data, 1, Center(players, balance), "raid", null, names, line.Creatures);
                    raid.Bosses.AddRange(line.Creatures);
                    spawned.AddRange(line.Creatures);
                    break;
                }
            }

            if (raid.Bosses.Count == 0)
            {
                Plugin.Log.LogInfo(
                    $"Raid {raid.Template.m_name} found no open ground outside the base for {raid.BossPrefab}, so the " +
                    $"raid ends.{WaveSpawner.Rejected()}");
                return false;
            }

            foreach (SpawnSystem.SpawnData data in raid.Template.m_spawn)
            {
                if (!WaveSpawner.Usable(data) || data == bossLine)
                {
                    continue;
                }

                Line line = new Line { Data = data, Next = raid.InArea + Mathf.Max(1f, data.m_spawnInterval) };
                raid.Lines.Add(line);
                int count = Due(line);
                if (count > 0)
                {
                    WaveSpawner.Group(data, count, Center(players, balance), "raid", null, names, line.Creatures);
                    spawned.AddRange(line.Creatures);
                }
            }

            raid.Spawned = 1;
            Wave wave = new Wave { Number = 1 };
            wave.Creatures.AddRange(spawned);
            raid.Live.Add(wave);
            foreach (ZDOID id in spawned)
            {
                Raiders.Enlist(id, raid.Position);
            }

            foreach (ZDOID id in raid.Bosses)
            {
                raid.Crowned.Add(id);
                Raiders.Crown(id);
            }

            Plugin.Log.LogInfo(
                $"Raid {raid.Template.m_name} boss wave: {string.Join(", ", names)}. The rest keep coming as in vanilla " +
                $"until {raid.BossPrefab} is down.{WaveSpawner.Placements()}{WaveSpawner.Rejected()}");
            return true;
        }

        private static void Trickle(Raid raid, List<Vector3> players, Balance balance)
        {
            if (raid.BossDown || raid.Live.Count == 0)
            {
                return;
            }

            Wave wave = raid.Live[0];
            List<string> names = null;
            foreach (Line line in raid.Lines)
            {
                if (line.Boss || raid.InArea < line.Next)
                {
                    continue;
                }

                SpawnSystem.SpawnData data = line.Data;
                line.Next = raid.InArea + Mathf.Max(1f, data.m_spawnInterval);
                int count = Due(line);
                if (count <= 0)
                {
                    continue;
                }

                if (names == null)
                {
                    names = new List<string>();
                    WaveSpawner.Begin();
                }

                List<ZDOID> spawned = new List<ZDOID>();
                WaveSpawner.Group(data, count, Center(players, balance), "raid", null, names, spawned);
                line.Creatures.AddRange(spawned);
                wave.Creatures.AddRange(spawned);
                foreach (ZDOID id in spawned)
                {
                    Raiders.Enlist(id, raid.Position);
                }
            }

            if (names != null && names.Count > 0)
            {
                Plugin.Log.LogInfo(
                    $"Raid {raid.Template.m_name} sends more while {raid.BossPrefab} stands: {string.Join(", ", names)}." +
                    $"{WaveSpawner.Placements()}{WaveSpawner.Rejected()}");
            }
        }

        private static int Due(Line line)
        {
            SpawnSystem.SpawnData data = line.Data;
            if (Random.Range(0f, 100f) > data.m_spawnChance || (!data.m_spawnAtDay && EnvMan.IsDay()) ||
                (!data.m_spawnAtNight && EnvMan.IsNight()))
            {
                return 0;
            }

            int room = data.m_maxSpawned > 0 ? data.m_maxSpawned - line.Creatures.Count : int.MaxValue;
            return Mathf.Min(Random.Range(data.m_groupSizeMin, data.m_groupSizeMax + 1), room);
        }

        private static void TrackBoss(Raid raid)
        {
            if (raid.Spawned == 0)
            {
                return;
            }

            List<ZDOID> live = raid.Live.Count > 0 ? raid.Live[0].Creatures : null;
            foreach (Line line in raid.Lines)
            {
                line.Creatures.RemoveAll(id => live == null || !live.Contains(id));
            }

            raid.Bosses.RemoveAll(id => live == null || !live.Contains(id) || Fallen(id));
            bool down = raid.Bosses.Count == 0;
            if (down == raid.BossDown)
            {
                return;
            }

            raid.BossDown = down;
            Plugin.Log.LogInfo(down
                ? $"Raid {raid.Template.m_name}: {raid.BossPrefab} is down, so no more raiders come; the raid ends once " +
                  $"the {Alive(raid)} left are defeated."
                : $"Raid {raid.Template.m_name}: {raid.BossPrefab} split in {raid.Bosses.Count}, so the rest keep coming " +
                  "until those are down too.");
        }

        private static bool Fallen(ZDOID id)
        {
            ZDO zdo = ZDOMan.instance.GetZDO(id);
            return zdo == null || zdo.GetFloat(ZDOVars.s_health, 1f) <= 0f;
        }

        private static System.Func<SpawnSystem.SpawnData, Vector3?> Center(List<Vector3> players, Balance balance)
        {
            return data => WaveSpawner.Around(players[Random.Range(0, players.Count)], balance.RaidSpawnDistance, data);
        }

        private static float NextWaveSeconds(Balance balance)
        {
            return Mathf.Max(30f, balance.RaidNextWaveMinutes * 60f);
        }

        private static void Prune(Raid raid, bool present, float dt, Balance balance)
        {
            for (int i = 0; i < raid.Live.Count; i++)
            {
                Wave wave = raid.Live[i];
                Lost.Clear();
                for (int j = wave.Creatures.Count - 1; j >= 0; j--)
                {
                    if (Gone(wave, wave.Creatures[j], present, dt))
                    {
                        wave.Creatures.RemoveAt(j);
                    }
                }

                if (Lost.Count > 0)
                {
                    Plugin.Log.LogInfo(
                        $"Raid {raid.Template.m_name} lost {string.Join(", ", Lost)} of wave {wave.Number}: beyond every " +
                        $"player's loaded area for {LostSeconds:0} s while a player was in the raid, so they no longer " +
                        "count toward the wave.");
                }

                if (wave.Creatures.Count > 0)
                {
                    continue;
                }

                raid.Live.RemoveAt(i);
                i--;
                raid.Defeated++;
                WaveDefeated(raid, wave, balance);
            }
        }

        private static bool Gone(Wave wave, ZDOID id, bool present, float dt)
        {
            ZDO zdo = ZDOMan.instance.GetZDO(id);
            if (zdo == null || !Raiders.Enlisted(zdo) || zdo.GetBool(ZDOVars.s_tamed))
            {
                Forget(wave, id);
                return true;
            }

            bool loaded = ZNetScene.instance.FindInstance(zdo) != null;
            if (zdo.GetFloat(ZDOVars.s_health, 1f) <= 0f)
            {
                wave.Dying.TryGetValue(id, out float dying);
                dying += dt;
                if (!loaded || dying >= DyingSeconds)
                {
                    Forget(wave, id);
                    return true;
                }

                wave.Dying[id] = dying;
                return false;
            }

            if (loaded)
            {
                wave.Away.Remove(id);
                return false;
            }

            if (!present)
            {
                return false;
            }

            wave.Away.TryGetValue(id, out float away);
            away += dt;
            if (away < LostSeconds)
            {
                wave.Away[id] = away;
                return false;
            }

            Forget(wave, id);
            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            Vector3 position = zdo.GetPosition();
            Lost.Add($"{(prefab != null ? prefab.name : "a creature")} at ({position.x:0}, {position.z:0})");
            return true;
        }

        private static void Forget(Wave wave, ZDOID id)
        {
            wave.Away.Remove(id);
            wave.Dying.Remove(id);
        }

        private static void WaveDefeated(Raid raid, Wave wave, Balance balance)
        {
            int remaining = Mathf.Max(0, raid.Waves - raid.Defeated);
            if (remaining == 0)
            {
                Plugin.Log.LogInfo($"Raid {raid.Template.m_name} wave {wave.Number} of {raid.Waves} is defeated.");
                return;
            }

            string next;
            if (raid.Spawned >= raid.Waves)
            {
                next = $"the last {remaining} are already out";
            }
            else if (raid.Live.Count == 0)
            {
                raid.NextWave = Mathf.Min(raid.NextWave, raid.InArea + Mathf.Max(10f, balance.RaidWaveInterval));
                next = $"none is alive, so the next comes in {Mathf.Max(0f, raid.NextWave - raid.InArea):0} s with a " +
                       "player in the area";
            }
            else
            {
                next = $"{raid.Live.Count} still alive, the next due in {Mathf.Max(0f, raid.NextWave - raid.InArea):0} s " +
                       "with a player in the area";
            }

            int stage = RaidMessages.Stage(remaining, raid.Waves);
            string text = RaidMessages.For(raid.Template.m_name, stage);
            Plugin.Log.LogInfo(
                $"Raid {raid.Template.m_name} wave {wave.Number} of {raid.Waves} is defeated; {remaining} waves remain, " +
                $"{next}. Players in the area are told \"{text}\".");
            SendWave(raid, stage);
        }

        private static void SendWave(Raid raid, int stage)
        {
            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            ZPackage package = new ZPackage();
            package.Write(raid.Template.m_name);
            package.Write(raid.Position);
            package.Write(raid.Template.m_eventRange);
            package.Write(stage);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, WaveRpc, package);
        }

        private static void OnWave(long sender, ZPackage package)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer())
            {
                return;
            }

            string name = package.ReadString();
            Vector3 position = package.ReadVector3();
            float range = package.ReadSingle();
            int stage = package.ReadInt();
            Player player = Player.m_localPlayer;
            if (player == null || MessageHud.instance == null)
            {
                return;
            }

            Vector3 at = player.transform.position;
            if (at.y > 3000f || Utils.DistanceXZ(at, position) >= range)
            {
                return;
            }

            string text = RaidMessages.For(name, stage);
            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, text);
            Plugin.Log.LogInfo($"Raid {name} lost a wave: \"{text}\".");
        }

        internal static void Adopt(ZDOID parent, ZDOID child)
        {
            foreach (Raid raid in Active)
            {
                foreach (Wave wave in raid.Live)
                {
                    if (!wave.Creatures.Contains(parent))
                    {
                        continue;
                    }

                    wave.Creatures.Add(child);
                    if (raid.Crowned.Contains(parent))
                    {
                        raid.Bosses.Add(child);
                        raid.Crowned.Add(child);
                        Raiders.Crown(child);
                    }

                    foreach (Line line in raid.Lines)
                    {
                        if (line.Creatures.Contains(parent))
                        {
                            line.Creatures.Add(child);
                        }
                    }

                    return;
                }
            }
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
            int alive = 0;
            foreach (Wave wave in raid.Live)
            {
                alive += wave.Creatures.Count;
            }

            return alive;
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
                    $"{(raid.Sized ? raid.Waves.ToString() : "?")}, {raid.Defeated} defeated, " +
                    (raid.BossPrefab != null ? $"{raid.BossPrefab} {(raid.BossDown ? "down" : "standing")}, " : "") +
                    $"{raid.Live.Count} waves alive with {Alive(raid)} creatures, next due in " +
                    $"{Mathf.Max(0f, raid.NextWave - raid.InArea):0} s, comfort {raid.Comfort}, {raid.Players} players, " +
                    $"{raid.Age / 60f:0.0} min");
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
            rpc.Register<ZPackage>(WaveRpc, OnWave);
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
