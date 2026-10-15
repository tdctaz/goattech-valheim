using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ServerAuthority
{
    internal static class DeathTiming
    {
        internal static readonly int StampKey = "serverauthority_died".GetStableHashCode();

        private const double HitWindowMs = 5000.0;
        private const double FollowMs = 15000.0;
        private const double PruneMs = 10000.0;
        private const float NearbyMetres = 64f;
        private const int MaxSamples = 8192;

        private struct Hit
        {
            internal long At;
            internal long From;
            internal ZDOID Attacker;
        }

        private sealed class Delivery
        {
            internal string Name;
            internal int Ping;
            internal int Rate;
            internal int QueueAtDeath;
            internal int Held;
            internal long RagdollSent;
            internal int RagdollQueue;
            internal long LootSent;
            internal int LootQueue;
        }

        private sealed class Death
        {
            internal ZDOID Creature;
            internal string Name;
            internal Vector3 Position;
            internal string Clock;
            internal long HitAt;
            internal string HitBy;
            internal bool HitByPlayer;
            internal long DiedAt;
            internal ZDOID Ragdoll = ZDOID.None;
            internal bool ExpectLoot;
            internal long LootAt;
            internal int LootCount;
            internal long WorstFrame;
            internal readonly Dictionary<long, Delivery> Peers = new Dictionary<long, Delivery>();
        }

        internal sealed class Samples
        {
            private readonly List<double> _values = new List<double>();

            internal int Count => _values.Count;

            internal void Add(double value)
            {
                if (_values.Count < MaxSamples)
                {
                    _values.Add(value);
                }
            }

            internal void Clear()
            {
                _values.Clear();
            }

            internal string Describe(string unit = "ms")
            {
                if (_values.Count == 0)
                {
                    return "none";
                }

                _values.Sort();
                double median = _values[_values.Count / 2];
                double worst = _values[_values.Count - 1];
                return $"median {median:F0} {unit}, worst {worst:F0} {unit}";
            }
        }

        private static readonly Dictionary<ZDOID, Hit> Hits = new Dictionary<ZDOID, Hit>();
        private static readonly List<Death> Open = new List<Death>();
        private static readonly Dictionary<ZDOID, Death> Watched = new Dictionary<ZDOID, Death>();

        private static readonly Samples HitToDeath = new Samples();
        private static readonly Samples RemovalWait = new Samples();
        private static readonly Samples RagdollAfter = new Samples();
        private static readonly Samples LootAfter = new Samples();
        private static int _deaths;
        private static int _playerKills;
        private static int _neverSent;
        private static int _slow;

        private static Death _dying;
        private static Death _looting;
        private static long _lastTick;
        private static long _lastPrune;
        private static bool _failed;

        internal static bool Following => Watched.Count > 0;

        private static bool Enabled =>
            !_failed &&
            (ModConfig.SlowDeathWarningMilliseconds.Value > 0 || ModConfig.PerformanceReportSeconds.Value > 0f);

        internal static void Reset()
        {
            Hits.Clear();
            Open.Clear();
            Watched.Clear();
            ClearPeriod();
            _dying = null;
            _looting = null;
            _lastTick = 0;
            _lastPrune = 0;
            _failed = false;
        }

        internal static void Fail(Exception e)
        {
            if (_failed)
            {
                return;
            }

            _failed = true;
            Hits.Clear();
            Open.Clear();
            Watched.Clear();
            _dying = null;
            _looting = null;
            Plugin.Log.LogError($"Death timing stopped after an error and stays off until the server restarts: {e}");
        }

        internal static void RecordHit(Character character, long sender, HitData hit)
        {
            if (!Enabled || character == null || hit == null || character.IsPlayer())
            {
                return;
            }

            ZNetView view = character.m_nview;
            if (view == null || !view.IsValid() || !view.IsOwner())
            {
                return;
            }

            Hits[view.GetZDO().m_uid] = new Hit { At = Stopwatch.GetTimestamp(), From = sender, Attacker = hit.m_attacker };
        }

        internal static void BeginDeath(Character character)
        {
            _dying = null;
            if (!Enabled || character == null || character.IsPlayer() || ZNet.instance == null)
            {
                return;
            }

            ZNetView view = character.m_nview;
            if (view == null || !view.IsValid() || !view.IsOwner())
            {
                return;
            }

            long now = Stopwatch.GetTimestamp();
            ZDOID uid = view.GetZDO().m_uid;
            Death death = new Death
            {
                Creature = uid,
                Name = Utils.GetPrefabName(character.gameObject),
                Position = character.transform.position,
                Clock = DateTime.Now.ToString("HH:mm:ss.fff"),
                DiedAt = now
            };

            if (Hits.TryGetValue(uid, out Hit hit))
            {
                Hits.Remove(uid);
                if (Ms(hit.At, now) <= HitWindowMs)
                {
                    death.HitAt = hit.At;
                    death.HitBy = Attacker(hit.Attacker, hit.From, out death.HitByPlayer);
                }
            }

            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                if (peer == null || !peer.IsReady() || peer.m_socket == null)
                {
                    continue;
                }

                Vector3 position = SimulationAnchors.LivePosition(peer);
                if ((position - death.Position).sqrMagnitude > NearbyMetres * NearbyMetres)
                {
                    continue;
                }

                NetworkStats.TryGetLink(peer.m_uid, out int ping, out int rate);
                death.Peers[peer.m_uid] = new Delivery
                {
                    Name = string.IsNullOrEmpty(peer.m_playerName) ? peer.m_uid.ToString() : peer.m_playerName,
                    Ping = ping,
                    Rate = rate,
                    QueueAtDeath = peer.m_socket.GetSendQueueSize()
                };
            }

            _dying = death;
            if (death.Peers.Count > 0)
            {
                Open.Add(death);
            }
        }

        internal static void EndDeath()
        {
            _dying = null;
        }

        internal static void RagdollMade(Ragdoll ragdoll)
        {
            Death death = _dying;
            if (death == null || ragdoll == null || ragdoll.m_nview == null || !ragdoll.m_nview.IsValid())
            {
                return;
            }

            ZDO zdo = ragdoll.m_nview.GetZDO();
            zdo.Set(StampKey, DateTime.UtcNow.Ticks);
            if (death.Peers.Count == 0)
            {
                return;
            }

            death.Ragdoll = zdo.m_uid;
            death.ExpectLoot = ragdoll.m_dropItems && zdo.GetInt(ZDOVars.s_drops) > 0;
            Watched[zdo.m_uid] = death;
        }

        internal static void BeginLoot(Ragdoll ragdoll)
        {
            _looting = null;
            if (Watched.Count == 0 || ragdoll == null || ragdoll.m_nview == null || !ragdoll.m_nview.IsValid() ||
                !ragdoll.m_nview.IsOwner())
            {
                return;
            }

            if (Watched.TryGetValue(ragdoll.m_nview.GetZDO().m_uid, out Death death) && death.LootAt == 0)
            {
                death.LootAt = Stopwatch.GetTimestamp();
                _looting = death;
            }
        }

        internal static void EndLoot()
        {
            _looting = null;
        }

        internal static void Created(ZDO zdo, int prefab)
        {
            Death death = _looting ?? _dying;
            if (death == null || zdo == null || death.Peers.Count == 0 || !PrefabKinds.IsItem(prefab))
            {
                return;
            }

            if (death.LootAt == 0)
            {
                death.LootAt = Stopwatch.GetTimestamp();
            }

            death.ExpectLoot = true;
            death.LootCount++;
            Watched[zdo.m_uid] = death;
        }

        internal static void Held(ZNetPeer peer)
        {
            if (Open.Count == 0)
            {
                return;
            }

            try
            {
                for (int i = 0; i < Open.Count; i++)
                {
                    if (Open[i].Peers.TryGetValue(peer.m_uid, out Delivery delivery) && delivery.RagdollSent == 0)
                    {
                        delivery.Held++;
                    }
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        internal static void Sent(ZNetPeer peer, ZDOID uid, int queue)
        {
            try
            {
                if (!Watched.TryGetValue(uid, out Death death) ||
                    !death.Peers.TryGetValue(peer.m_uid, out Delivery delivery))
                {
                    return;
                }

                long now = Stopwatch.GetTimestamp();
                if (uid == death.Ragdoll)
                {
                    if (delivery.RagdollSent == 0)
                    {
                        delivery.RagdollSent = now;
                        delivery.RagdollQueue = queue;
                    }
                }
                else if (delivery.LootSent == 0)
                {
                    delivery.LootSent = now;
                    delivery.LootQueue = queue;
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        internal static void Tick()
        {
            if (_failed)
            {
                return;
            }

            try
            {
                Advance();
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private static void Advance()
        {
            long now = Stopwatch.GetTimestamp();
            long frame = _lastTick == 0 ? 0 : now - _lastTick;
            _lastTick = now;

            for (int i = Open.Count - 1; i >= 0; i--)
            {
                Death death = Open[i];
                if (frame > death.WorstFrame)
                {
                    death.WorstFrame = frame;
                }

                if (!Complete(death) && Ms(death.DiedAt, now) < FollowMs)
                {
                    continue;
                }

                Open.RemoveAt(i);
                Forget(death);
                Finish(death);
            }

            if (Ms(_lastPrune, now) >= PruneMs)
            {
                _lastPrune = now;
                Prune(now);
            }
        }

        internal static string ClosePeriod(float seconds)
        {
            string text = null;
            if (seconds > 0f && _deaths > 0)
            {
                int threshold = ModConfig.SlowDeathWarningMilliseconds.Value;
                text = $"Creature deaths near players over {seconds:F0}s: {_deaths}, {_playerKills} of them last hit " +
                       $"by a player. Last hit to death: {HitToDeath.Describe()}. Removal queued behind other " +
                       $"data: {RemovalWait.Describe()}. Death to ragdoll sent: {RagdollAfter.Describe()}, " +
                       $"{_neverSent} never sent. Loot released to sent: {LootAfter.Describe()}." +
                       (threshold > 0 ? $" {_slow} over {threshold} ms." : "");
            }

            ClearPeriod();
            return text;
        }

        private static void ClearPeriod()
        {
            HitToDeath.Clear();
            RemovalWait.Clear();
            RagdollAfter.Clear();
            LootAfter.Clear();
            _deaths = 0;
            _playerKills = 0;
            _neverSent = 0;
            _slow = 0;
        }

        private static bool Complete(Death death)
        {
            if (death.ExpectLoot && death.LootAt == 0)
            {
                return false;
            }

            foreach (Delivery delivery in death.Peers.Values)
            {
                if (!death.Ragdoll.IsNone() && delivery.RagdollSent == 0)
                {
                    return false;
                }

                if (death.LootCount > 0 && delivery.LootSent == 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static void Forget(Death death)
        {
            if (Watched.Count == 0)
            {
                return;
            }

            List<ZDOID> stale = null;
            foreach (KeyValuePair<ZDOID, Death> entry in Watched)
            {
                if (entry.Value == death)
                {
                    (stale ?? (stale = new List<ZDOID>())).Add(entry.Key);
                }
            }

            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    Watched.Remove(stale[i]);
                }
            }
        }

        private static void Prune(long now)
        {
            if (Hits.Count == 0)
            {
                return;
            }

            List<ZDOID> stale = null;
            foreach (KeyValuePair<ZDOID, Hit> entry in Hits)
            {
                if (Ms(entry.Value.At, now) > HitWindowMs)
                {
                    (stale ?? (stale = new List<ZDOID>())).Add(entry.Key);
                }
            }

            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    Hits.Remove(stale[i]);
                }
            }
        }

        private static void Finish(Death death)
        {
            int threshold = ModConfig.SlowDeathWarningMilliseconds.Value;
            bool slow = false;

            _deaths++;
            double hitToDeath = -1;
            if (death.HitAt != 0)
            {
                hitToDeath = Ms(death.HitAt, death.DiedAt);
                if (death.HitByPlayer)
                {
                    _playerKills++;
                }

                HitToDeath.Add(hitToDeath);
                slow |= hitToDeath > threshold;
            }

            var text = new StringBuilder();
            text.Append($"Death at {death.Clock}: {death.Name} {death.Creature} at ");
            text.Append($"({death.Position.x:F0}, {death.Position.y:F0}, {death.Position.z:F0})");
            if (!death.Ragdoll.IsNone())
            {
                text.Append($", ragdoll {death.Ragdoll}");
            }

            text.Append(". ");
            if (death.HitAt == 0)
            {
                text.Append("No hit in the last 5 s. ");
            }
            else
            {
                text.Append($"Last hit, by {death.HitBy}, arrived {hitToDeath:F0} ms before the death. ");
            }

            foreach (KeyValuePair<long, Delivery> entry in death.Peers.OrderBy(e => e.Value.Name, StringComparer.Ordinal))
            {
                Delivery delivery = entry.Value;
                bool stayed = StayedNear(entry.Key, death.Position);
                double removal = delivery.Rate > 0 ? delivery.QueueAtDeath * 1000.0 / delivery.Rate : 0;
                RemovalWait.Add(removal);
                slow |= removal > threshold;

                text.Append($"{delivery.Name} (ping {delivery.Ping} ms): removal queued behind ");
                text.Append($"{delivery.QueueAtDeath / 1024f:F1} KiB (about {removal:F0} ms at the send rate)");
                if (!death.Ragdoll.IsNone())
                {
                    if (delivery.RagdollSent != 0)
                    {
                        double ragdoll = Ms(death.DiedAt, delivery.RagdollSent);
                        RagdollAfter.Add(ragdoll);
                        slow |= ragdoll > threshold;
                        text.Append($", ragdoll sent {ragdoll:F0} ms after the death behind ");
                        text.Append($"{delivery.RagdollQueue / 1024f:F1} KiB");
                    }
                    else if (stayed)
                    {
                        _neverSent++;
                        slow = true;
                        text.Append(", ragdoll never sent");
                    }
                    else
                    {
                        text.Append(", ragdoll not sent before the player left the area");
                    }

                    if (delivery.Held > 0)
                    {
                        text.Append($" ({delivery.Held} send(s) held back by a full queue first)");
                    }
                }

                if (death.LootAt != 0 && death.LootCount > 0)
                {
                    if (delivery.LootSent != 0)
                    {
                        double loot = Ms(death.LootAt, delivery.LootSent);
                        LootAfter.Add(loot);
                        slow |= loot > threshold;
                        text.Append($", first loot sent {loot:F0} ms after its release behind {delivery.LootQueue / 1024f:F1} KiB");
                    }
                    else if (stayed)
                    {
                        slow = true;
                        text.Append(", loot never sent");
                    }
                    else
                    {
                        text.Append(", loot not sent before the player left the area");
                    }
                }

                text.Append(". ");
            }

            if (death.LootCount > 0)
            {
                text.Append($"{death.LootCount} loot object(s) released {Ms(death.DiedAt, death.LootAt):F0} ms after the death. ");
            }
            else if (death.ExpectLoot)
            {
                text.Append(death.LootAt != 0
                    ? "The ragdoll released no loot objects. "
                    : "The loot was not released while followed. ");
            }

            text.Append($"Worst server frame {death.WorstFrame * 1000.0 / Stopwatch.Frequency:F0} ms.");

            if (slow)
            {
                _slow++;
            }

            if (threshold > 0 && slow)
            {
                Plugin.Log.LogInfo(text.ToString());
            }
        }

        private static bool StayedNear(long uid, Vector3 position)
        {
            ZNetPeer peer = ZNet.instance != null ? ZNet.instance.GetPeer(uid) : null;
            if (peer == null || !peer.IsReady())
            {
                return false;
            }

            float reach = NearbyMetres + 32f;
            return (SimulationAnchors.LivePosition(peer) - position).sqrMagnitude <= reach * reach;
        }

        private static string Attacker(ZDOID attacker, long sender, out bool player)
        {
            player = false;
            ZNet znet = ZNet.instance;
            if (znet == null)
            {
                return "an unknown attacker";
            }

            if (!attacker.IsNone())
            {
                foreach (ZNetPeer peer in znet.GetPeers())
                {
                    if (peer != null && peer.m_characterID == attacker)
                    {
                        player = true;
                        return string.IsNullOrEmpty(peer.m_playerName) ? peer.m_uid.ToString() : peer.m_playerName;
                    }
                }

                ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(attacker) : null;
                GameObject prefab = zdo != null && ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(zdo.GetPrefab()) : null;
                if (prefab != null)
                {
                    return prefab.name;
                }
            }

            if (sender != ZNet.GetUID())
            {
                ZNetPeer peer = znet.GetPeer(sender);
                if (peer != null)
                {
                    player = true;
                    return string.IsNullOrEmpty(peer.m_playerName) ? peer.m_uid.ToString() : peer.m_playerName;
                }
            }

            return "an unknown attacker";
        }

        private static double Ms(long from, long to)
        {
            return (to - from) * 1000.0 / Stopwatch.Frequency;
        }
    }
}
