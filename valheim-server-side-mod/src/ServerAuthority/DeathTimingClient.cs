using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace ServerAuthority
{
    internal static class DeathTimingClient
    {
        private const double HitWindowMs = 5000.0;
        private const double MatchMs = 5000.0;
        private const float MatchMetres = 5f;
        private const float NearbyMetres = 64f;
        private const float LootMetres = 5f;
        private const double LootWaitMs = 3000.0;
        private const double FollowMs = 15000.0;
        private const double PruneMs = 1000.0;
        private const int FrameHistory = 4096;

        internal static bool Active;

        private struct Arrival
        {
            internal long At;
            internal int Waiting;
        }

        private sealed class Kill
        {
            internal ZDOID Creature;
            internal string Name;
            internal Vector3 Position;
            internal long HitAt;
            internal long VanishedAt;
        }

        private sealed class Seen
        {
            internal ZDOID Ragdoll;
            internal string Name;
            internal Vector3 Position;
            internal float Distance;
            internal string Clock;
            internal long DiedTicks;
            internal long ShownTicks;
            internal long ArrivedAt;
            internal long ShownAt;
            internal int Waiting;
            internal bool ExpectLoot;
            internal float Ttl;
            internal Kill Kill;
            internal long GoneAt;
            internal long LootArrivedAt;
            internal long LootShownAt;
        }

        private static readonly Dictionary<ZDOID, long> Hits = new Dictionary<ZDOID, long>();
        private static readonly Dictionary<ZDOID, Arrival> Arrived = new Dictionary<ZDOID, Arrival>();
        private static readonly List<Kill> Kills = new List<Kill>();
        private static readonly List<Seen> Open = new List<Seen>();
        private static readonly long[] FrameEnds = new long[FrameHistory];
        private static readonly long[] FrameLengths = new long[FrameHistory];

        private static readonly DeathTiming.Samples DeathToShown = new DeathTiming.Samples();
        private static readonly DeathTiming.Samples HitToShown = new DeathTiming.Samples();
        private static readonly DeathTiming.Samples GoneToLoot = new DeathTiming.Samples();

        private static readonly Dictionary<int, bool> LeavesRagdoll = new Dictionary<int, bool>();

        private static int _lootPending;
        private static int _frameIndex;
        private static long _lastTick;
        private static long _lastPrune;
        private static long _periodStart;
        private static int _periodFrames;
        private static long _periodWorst;
        private static int _seen;
        private static int _kills;
        private static int _noRagdoll;
        private static int _slow;

        internal static void Reset(ZNet znet)
        {
            Active = znet != null && !znet.IsServer() &&
                     (ModConfig.SlowDeathWarningMilliseconds.Value > 0 || ModConfig.PerformanceReportSeconds.Value > 0f);
            Hits.Clear();
            Arrived.Clear();
            Kills.Clear();
            Open.Clear();
            Array.Clear(FrameEnds, 0, FrameHistory);
            Array.Clear(FrameLengths, 0, FrameHistory);
            LeavesRagdoll.Clear();
            _lootPending = 0;
            _frameIndex = 0;
            _lastTick = 0;
            _lastPrune = 0;
            _periodStart = 0;
            ClearPeriod();
        }

        internal static void Fail(Exception e)
        {
            if (!Active)
            {
                return;
            }

            Active = false;
            Hits.Clear();
            Arrived.Clear();
            Kills.Clear();
            Open.Clear();
            Plugin.Log.LogError($"Death timing stopped after an error and stays off until the next session: {e}");
        }

        internal static void LocalHit(Character target, HitData hit)
        {
            if (!Active || hit == null || target == null || target.IsPlayer())
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null || hit.m_attacker != player.GetZDOID())
            {
                return;
            }

            ZNetView view = target.m_nview;
            if (view != null && view.IsValid())
            {
                Hits[view.GetZDO().m_uid] = Stopwatch.GetTimestamp();
            }
        }

        internal static void Received(ZDO zdo)
        {
            if (!Active || zdo.Created)
            {
                return;
            }

            int prefab = zdo.GetPrefab();
            bool ragdoll = PrefabKinds.IsRagdoll(prefab);
            if (!ragdoll && (_lootPending == 0 || !PrefabKinds.IsItem(prefab)))
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null || Arrived.ContainsKey(zdo.m_uid))
            {
                return;
            }

            Vector3 position = zdo.GetPosition();
            if (ragdoll
                    ? (position - player.transform.position).sqrMagnitude > NearbyMetres * NearbyMetres
                    : !PrefabKinds.IsFreshDrop(zdo) || NearestPendingLoot(position) == null)
            {
                return;
            }

            Arrived[zdo.m_uid] = new Arrival { At = Stopwatch.GetTimestamp(), Waiting = ragdoll ? CountWaiting() : -1 };
        }

        internal static void Destroyed(ZDOID uid)
        {
            if (!Active)
            {
                return;
            }

            long now = Stopwatch.GetTimestamp();
            if (Hits.Count > 0 && Hits.TryGetValue(uid, out long hitAt))
            {
                Hits.Remove(uid);
                ZDO zdo = ZDOMan.instance.GetZDO(uid);
                if (zdo != null && Ms(hitAt, now) <= HitWindowMs && ExpectsRagdoll(zdo.GetPrefab()))
                {
                    Kill kill = new Kill
                    {
                        Creature = uid,
                        Name = PrefabName(zdo.GetPrefab()),
                        Position = zdo.GetPosition(),
                        HitAt = hitAt,
                        VanishedAt = now
                    };

                    Seen early = null;
                    float best = MatchMetres * MatchMetres;
                    for (int i = 0; i < Open.Count; i++)
                    {
                        Seen seen = Open[i];
                        float distance = (seen.Position - kill.Position).sqrMagnitude;
                        if (seen.Kill == null && distance <= best && Ms(seen.ShownAt, now) <= MatchMs)
                        {
                            best = distance;
                            early = seen;
                        }
                    }

                    if (early != null)
                    {
                        early.Kill = kill;
                        _kills++;
                    }
                    else
                    {
                        Kills.Add(kill);
                    }
                }
            }

            for (int i = 0; i < Open.Count; i++)
            {
                Seen seen = Open[i];
                if (seen.Ragdoll == uid && seen.GoneAt == 0)
                {
                    seen.GoneAt = now;
                    ZDO zdo = ZDOMan.instance.GetZDO(uid);
                    if (zdo != null)
                    {
                        seen.Position = zdo.GetPosition();
                    }
                }
            }
        }

        internal static void RagdollShown(Ragdoll ragdoll)
        {
            if (!Active || ragdoll == null || ragdoll.m_nview == null || !ragdoll.m_nview.IsValid())
            {
                return;
            }

            Player player = Player.m_localPlayer;
            ZDO zdo = ragdoll.m_nview.GetZDO();
            Arrived.TryGetValue(zdo.m_uid, out Arrival arrival);
            Arrived.Remove(zdo.m_uid);
            if (player == null)
            {
                return;
            }

            Vector3 position = zdo.GetPosition();
            float distance = Vector3.Distance(position, player.transform.position);
            if (distance > NearbyMetres)
            {
                return;
            }

            long now = Stopwatch.GetTimestamp();
            Seen seen = new Seen
            {
                Ragdoll = zdo.m_uid,
                Name = Utils.GetPrefabName(ragdoll.gameObject),
                Position = position,
                Distance = distance,
                Clock = DateTime.Now.ToString("HH:mm:ss.fff"),
                DiedTicks = zdo.GetLong(DeathTiming.StampKey),
                ShownTicks = DateTime.UtcNow.Ticks,
                ArrivedAt = arrival.At != 0 ? arrival.At : now,
                ShownAt = now,
                Waiting = arrival.At != 0 ? arrival.Waiting : -1,
                ExpectLoot = ragdoll.m_dropItems && zdo.GetInt(ZDOVars.s_drops) > 0,
                Ttl = ragdoll.m_ttl
            };

            Kill match = null;
            float best = MatchMetres * MatchMetres;
            for (int i = 0; i < Kills.Count; i++)
            {
                Kill kill = Kills[i];
                float d = (kill.Position - position).sqrMagnitude;
                if (d <= best && Ms(kill.VanishedAt, now) <= MatchMs)
                {
                    best = d;
                    match = kill;
                }
            }

            if (match != null)
            {
                Kills.Remove(match);
                seen.Kill = match;
                _kills++;
            }

            Open.Add(seen);
        }

        internal static void ItemShown(ItemDrop item)
        {
            if (!Active || _lootPending == 0 || item == null || item.m_nview == null || !item.m_nview.IsValid())
            {
                return;
            }

            ZDO zdo = item.m_nview.GetZDO();
            Seen seen = PrefabKinds.IsFreshDrop(zdo) ? NearestPendingLoot(zdo.GetPosition()) : null;
            if (seen == null)
            {
                return;
            }

            long now = Stopwatch.GetTimestamp();
            seen.LootShownAt = now;
            seen.LootArrivedAt = Arrived.TryGetValue(zdo.m_uid, out Arrival arrival) ? arrival.At : now;
            Arrived.Remove(zdo.m_uid);
        }

        internal static void Tick()
        {
            long now = Stopwatch.GetTimestamp();
            if (_lastTick != 0)
            {
                long frame = now - _lastTick;
                FrameEnds[_frameIndex] = now;
                FrameLengths[_frameIndex] = frame;
                _frameIndex = (_frameIndex + 1) % FrameHistory;
                _periodFrames++;
                _periodWorst = Math.Max(_periodWorst, frame);
            }
            else
            {
                _periodStart = now;
            }

            _lastTick = now;

            int pendingLoot = 0;
            for (int i = Open.Count - 1; i >= 0; i--)
            {
                Seen seen = Open[i];
                bool done = seen.LootShownAt != 0 ||
                            (!seen.ExpectLoot && seen.GoneAt != 0) ||
                            (seen.GoneAt != 0 && Ms(seen.GoneAt, now) > LootWaitMs) ||
                            Ms(seen.ShownAt, now) > FollowMs;
                if (!done)
                {
                    if (seen.ExpectLoot)
                    {
                        pendingLoot++;
                    }

                    continue;
                }

                Open.RemoveAt(i);
                Finish(seen);
            }

            _lootPending = pendingLoot;

            for (int i = Kills.Count - 1; i >= 0; i--)
            {
                Kill kill = Kills[i];
                if (Ms(kill.VanishedAt, now) <= MatchMs)
                {
                    continue;
                }

                Kills.RemoveAt(i);
                _noRagdoll++;
                if (ModConfig.SlowDeathWarningMilliseconds.Value > 0)
                {
                    Plugin.Log.LogInfo(
                        $"Kill without a ragdoll: {kill.Name} {kill.Creature} at {Format(kill.Position)} vanished " +
                        $"{Ms(kill.HitAt, kill.VanishedAt):F0} ms after your last hit, and no ragdoll appeared within " +
                        $"{MatchMetres:F0} m in the next {MatchMs / 1000.0:F0} s.");
                }
            }

            if (Ms(_lastPrune, now) >= PruneMs)
            {
                _lastPrune = now;
                Prune(now);
            }

            float interval = ModConfig.PerformanceReportSeconds.Value;
            if (interval > 0f && _periodStart != 0 && Ms(_periodStart, now) >= interval * 1000.0)
            {
                Report(now);
            }
        }

        private static void Finish(Seen seen)
        {
            int threshold = ModConfig.SlowDeathWarningMilliseconds.Value;
            bool slow = false;
            _seen++;

            var text = new StringBuilder();
            text.Append($"Death seen at {seen.Clock}: {seen.Name} {seen.Ragdoll} at {Format(seen.Position)}, ");
            text.Append($"{seen.Distance:F0} m away. ");

            long from = seen.ShownAt;
            if (seen.DiedTicks != 0)
            {
                double deathToShown = (seen.ShownTicks - seen.DiedTicks) / (double)TimeSpan.TicksPerMillisecond;
                DeathToShown.Add(deathToShown);
                from = Math.Min(from, seen.ShownAt - (long)(deathToShown * Stopwatch.Frequency / 1000.0));
                text.Append($"Ragdoll shown {deathToShown:F0} ms after the death on the server, by the clocks of both machines");
            }
            else
            {
                text.Append("The ragdoll carries no death time from the server");
            }

            double waited = Ms(seen.ArrivedAt, seen.ShownAt);
            slow |= waited > threshold;
            text.Append($"; it arrived {waited:F0} ms before it was created");
            if (seen.Waiting >= 0)
            {
                text.Append($", with {seen.Waiting} object(s) waiting to be created");
            }

            text.Append(". ");

            if (seen.Kill != null)
            {
                Kill kill = seen.Kill;
                double hitToVanish = Ms(kill.HitAt, kill.VanishedAt);
                double vanishToShown = Ms(kill.VanishedAt, seen.ShownAt);
                double hitToShown = Ms(kill.HitAt, seen.ShownAt);
                HitToShown.Add(hitToShown);
                slow |= hitToShown > threshold || Math.Abs(vanishToShown) > threshold;
                from = Math.Min(from, kill.HitAt);
                text.Append($"Your last hit on {kill.Name} {kill.Creature} was {hitToVanish:F0} ms before it vanished, ");
                text.Append(vanishToShown >= 0
                    ? $"and the ragdoll showed {vanishToShown:F0} ms after that. "
                    : $"and the ragdoll showed {-vanishToShown:F0} ms before that. ");
            }

            long to = seen.ShownAt;
            if (seen.LootShownAt != 0)
            {
                to = seen.LootShownAt;
                text.Append($"Loot shown {Ms(seen.ShownAt, seen.LootShownAt):F0} ms after the ragdoll");
                if (seen.GoneAt != 0)
                {
                    double goneToLoot = Ms(seen.GoneAt, seen.LootShownAt);
                    GoneToLoot.Add(goneToLoot);
                    slow |= goneToLoot > threshold;
                    text.Append($", {goneToLoot:F0} ms after the ragdoll was removed here");
                }

                text.Append($", arrived {Ms(seen.LootArrivedAt, seen.LootShownAt):F0} ms before it was created. ");
            }
            else if (seen.ExpectLoot)
            {
                text.Append(seen.GoneAt != 0
                    ? "The ragdoll was removed but no loot appeared near it. "
                    : "The ragdoll was not removed while followed. ");
            }

            Frames(from, to, out int frames, out long worst);
            double span = Ms(from, to);
            text.Append($"Worst frame {worst * 1000.0 / Stopwatch.Frequency:F0} ms over {frames} frame(s) in ");
            text.Append($"{span:F0} ms. Ping {Ping()} ms, {ZDOMan.instance.m_zdosRecvLastSec} object update(s) ");
            text.Append("received in the last second.");

            if (slow)
            {
                _slow++;
            }

            if (threshold > 0 && slow)
            {
                Plugin.Log.LogInfo(text.ToString());
            }
        }

        private static void Report(long now)
        {
            double seconds = Ms(_periodStart, now) / 1000.0;
            if (_seen > 0 || _noRagdoll > 0)
            {
                int threshold = ModConfig.SlowDeathWarningMilliseconds.Value;
                Plugin.Log.LogInfo(
                    $"Deaths seen over {seconds:F0}s: {_seen} ragdoll(s) within {NearbyMetres:F0} m, {_kills} of them " +
                    $"your kills, and {_noRagdoll} of your kills without a ragdoll. Server death to ragdoll shown: " +
                    $"{DeathToShown.Describe()}. Your last hit to ragdoll shown: {HitToShown.Describe()}. Ragdoll " +
                    $"removed to loot shown: {GoneToLoot.Describe()}." +
                    (threshold > 0 ? $" {_slow} over {threshold} ms." : "") +
                    $" Frames: {_periodFrames / Math.Max(seconds, 0.001):F1} fps average, worst " +
                    $"{_periodWorst * 1000.0 / Stopwatch.Frequency:F0} ms.");
            }

            _periodStart = now;
            ClearPeriod();
        }

        private static void ClearPeriod()
        {
            DeathToShown.Clear();
            HitToShown.Clear();
            GoneToLoot.Clear();
            _periodFrames = 0;
            _periodWorst = 0;
            _seen = 0;
            _kills = 0;
            _noRagdoll = 0;
            _slow = 0;
        }

        private static void Prune(long now)
        {
            List<ZDOID> stale = null;
            foreach (KeyValuePair<ZDOID, long> entry in Hits)
            {
                if (Ms(entry.Value, now) > HitWindowMs)
                {
                    (stale ?? (stale = new List<ZDOID>())).Add(entry.Key);
                }
            }

            foreach (KeyValuePair<ZDOID, Arrival> entry in Arrived)
            {
                if (Ms(entry.Value.At, now) > FollowMs)
                {
                    (stale ?? (stale = new List<ZDOID>())).Add(entry.Key);
                }
            }

            if (stale == null)
            {
                return;
            }

            for (int i = 0; i < stale.Count; i++)
            {
                Hits.Remove(stale[i]);
                Arrived.Remove(stale[i]);
            }
        }

        private static Seen NearestPendingLoot(Vector3 position)
        {
            long now = Stopwatch.GetTimestamp();
            Seen best = null;
            float bestDistance = LootMetres * LootMetres;
            for (int i = 0; i < Open.Count; i++)
            {
                Seen seen = Open[i];
                if (!seen.ExpectLoot || seen.LootShownAt != 0)
                {
                    continue;
                }

                float distance = (seen.Position - position).sqrMagnitude;
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = seen;
                }
            }

            return best;
        }

        private static void Frames(long from, long to, out int frames, out long worst)
        {
            frames = 0;
            worst = 0;
            for (int i = 0; i < FrameHistory; i++)
            {
                long end = FrameEnds[i];
                if (end == 0 || end < from || end - FrameLengths[i] > to)
                {
                    continue;
                }

                frames++;
                worst = Math.Max(worst, FrameLengths[i]);
            }
        }

        private static bool ExpectsRagdoll(int hash)
        {
            if (LeavesRagdoll.TryGetValue(hash, out bool leaves))
            {
                return leaves;
            }

            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(hash) : null;
            Character character = prefab != null ? prefab.GetComponent<Character>() : null;
            EffectList.EffectData[] effects = character != null ? character.m_deathEffects?.m_effectPrefabs : null;
            if (effects != null)
            {
                foreach (EffectList.EffectData effect in effects)
                {
                    if (effect != null && effect.m_enabled && effect.m_prefab != null &&
                        effect.m_prefab.GetComponent<Ragdoll>() != null)
                    {
                        leaves = true;
                        break;
                    }
                }
            }

            LeavesRagdoll[hash] = leaves;
            return leaves;
        }

        internal static int CountWaiting()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null)
            {
                return -1;
            }

            List<ZDO> near = scene.m_tempCurrentObjects;
            int waiting = 0;
            for (int i = 0; i < near.Count; i++)
            {
                if (!near[i].Created)
                {
                    waiting++;
                }
            }

            return waiting;
        }

        private static int Ping()
        {
            ZNetPeer server = ZNet.instance != null ? ZNet.instance.GetServerPeer() : null;
            if (server?.m_socket is ZSteamSocket socket)
            {
                socket.GetConnectionQuality(out _, out _, out int ping, out _, out _);
                return ping;
            }

            return -1;
        }

        private static string PrefabName(int hash)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(hash) : null;
            return prefab != null ? prefab.name : hash.ToString();
        }

        private static string Format(Vector3 p)
        {
            return $"({p.x:F0}, {p.y:F0}, {p.z:F0})";
        }

        private static double Ms(long from, long to)
        {
            return (to - from) * 1000.0 / Stopwatch.Frequency;
        }
    }
}
