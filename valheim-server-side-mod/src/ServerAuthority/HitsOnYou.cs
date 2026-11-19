using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace ServerAuthority
{
    internal static class HitsOnYou
    {
        private const double KeepMs = 10000.0;
        private const double FreshMs = 1000.0;
        private const double PruneMs = 5000.0;
        private const int MaxKept = 64;
        private const int MaxListed = 6;

        internal static bool Active;

        private sealed class Hit
        {
            internal long At;
            internal string Name;
            internal ZDOID Attacker;
            internal bool HasAttacker;
            internal bool Present;
            internal string Missing;
            internal bool NotCreated;
            internal double AgeMs;
            internal float Distance;
            internal float Drawn;
            internal float Raw;
            internal float Taken;
            internal bool Ranged;
            internal int Waiting;
        }

        private sealed class Death
        {
            internal long At;
            internal string Clock;
            internal Vector3 Position;
            internal int Waiting;
        }

        private static readonly Dictionary<ZDOID, long> CreatedAt = new Dictionary<ZDOID, long>();
        private static readonly List<Hit> Recent = new List<Hit>();
        private static readonly List<ZDOID> Stale = new List<ZDOID>();
        private static readonly DeathTiming.Samples DrawnOffsets = new DeathTiming.Samples();

        private static Death _death;
        private static long _lastPrune;
        private static long _periodStart;
        private static int _hurt;
        private static int _fresh;
        private static int _ignored;

        internal static void Reset(ZNet znet)
        {
            Active = znet != null && !znet.IsServer();
            CreatedAt.Clear();
            Recent.Clear();
            Stale.Clear();
            DrawnOffsets.Clear();
            _death = null;
            _lastPrune = 0;
            _periodStart = 0;
            _hurt = 0;
            _fresh = 0;
            _ignored = 0;
        }

        internal static void Fail(Exception e)
        {
            if (!Active)
            {
                return;
            }

            Active = false;
            CreatedAt.Clear();
            Recent.Clear();
            _death = null;
            Plugin.Log.LogError($"Logging hits on you stopped after an error and stays off until the next session: {e}");
        }

        internal static void Created(ZDO zdo, GameObject created)
        {
            if (!Active || zdo == null || created == null || zdo.IsOwner() || !PrefabKinds.IsCharacter(zdo.GetPrefab()))
            {
                return;
            }

            CreatedAt[zdo.m_uid] = Stopwatch.GetTimestamp();
        }

        internal static void TakeOver(Player player)
        {
            if (!Active || player == null || player != Player.m_localPlayer || player.m_nview == null ||
                !player.m_nview.IsValid() || !player.m_nview.IsOwner())
            {
                return;
            }

            RpcTakeover.Replace<HitData>(player, player.m_nview, "RPC_Damage", (sender, hit) => Damage(player, sender, hit));
        }

        private static void Damage(Player player, long sender, HitData hit)
        {
            Hit record = null;
            if (Active)
            {
                try
                {
                    record = Before(player, hit);
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }

            player.RPC_Damage(sender, hit);

            if (record != null && Active)
            {
                try
                {
                    After(player, record);
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }
        }

        private static Hit Before(Character target, HitData hit)
        {
            Player player = Player.m_localPlayer;
            if (!Active || hit == null || target == null || target != player || !target.m_nview.IsValid() ||
                !target.m_nview.IsOwner())
            {
                return null;
            }

            long now = Stopwatch.GetTimestamp();
            Hit record = new Hit
            {
                At = now,
                Raw = hit.GetTotalDamage(),
                Taken = target.GetHealth(),
                Ranged = hit.m_ranged,
                Waiting = DeathTimingClient.CountWaiting(),
                HasAttacker = hit.HaveAttacker(),
                Distance = -1f,
                Drawn = -1f,
                AgeMs = -1.0
            };

            if (!record.HasAttacker)
            {
                record.Name = hit.m_hitType.ToString();
                return record;
            }

            record.Attacker = hit.m_attacker;
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(hit.m_attacker) : null;
            Character attacker = hit.GetAttacker();
            record.Present = attacker != null;
            if (attacker != null)
            {
                record.Name = Utils.GetPrefabName(attacker.gameObject);
                Vector3 drawn = attacker.transform.position;
                record.Distance = Vector3.Distance(drawn, target.transform.position);
                if (zdo != null)
                {
                    record.Drawn = Vector3.Distance(drawn, zdo.GetPosition());
                }

                if (CreatedAt.TryGetValue(hit.m_attacker, out long at))
                {
                    record.AgeMs = Ms(at, now);
                }
            }
            else if (zdo != null)
            {
                record.Name = PrefabName(zdo.GetPrefab());
                record.Distance = Vector3.Distance(zdo.GetPosition(), target.transform.position);
            }
            else
            {
                record.Name = "an object this client does not know";
            }

            if (attacker == null)
            {
                bool shielded = target.IsDebugFlying() || target.GetHealth() <= 0f || target.IsDead() ||
                                target.IsTeleporting() || target.InCutscene() ||
                                (hit.m_dodgeable && target.IsDodgeInvincible());
                record.NotCreated = !shielded && zdo != null && !zdo.Created;
                record.Missing = shielded
                    ? "attacker absent here, but the player could not be hit anyway"
                    : zdo == null
                        ? "attacker already gone here so vanilla ignored the hit"
                        : !zdo.Created
                            ? "attacker not created here so vanilla ignored the hit"
                            : "attacker is not a character here so vanilla ignored the hit";
            }

            return record;
        }

        private static void After(Character target, Hit record)
        {
            record.Taken = Mathf.Max(0f, record.Taken - target.GetHealth());
            if (record.HasAttacker && !record.Present)
            {
                if (record.NotCreated)
                {
                    _ignored++;
                }
            }
            else if (record.Taken > 0f)
            {
                _hurt++;
                if (record.AgeMs >= 0.0 && record.AgeMs < FreshMs)
                {
                    _fresh++;
                }

                if (record.Drawn >= 0f)
                {
                    DrawnOffsets.Add(record.Drawn);
                }
            }

            Recent.Add(record);
            if (Recent.Count > MaxKept)
            {
                Recent.RemoveAt(0);
            }
        }

        internal static void Died(Player player)
        {
            if (!Active || player == null || player != Player.m_localPlayer || !player.m_nview.IsValid() ||
                !player.m_nview.IsOwner() || _death != null)
            {
                return;
            }

            _death = new Death
            {
                At = Stopwatch.GetTimestamp(),
                Clock = DateTime.Now.ToString("HH:mm:ss.fff"),
                Position = player.transform.position,
                Waiting = DeathTimingClient.CountWaiting()
            };
        }

        internal static void Tick()
        {
            long now = Stopwatch.GetTimestamp();
            if (_periodStart == 0)
            {
                _periodStart = now;
            }

            if (_death != null)
            {
                Death death = _death;
                _death = null;
                Plugin.Log.LogInfo(DescribeDeath(death));
            }

            if (Ms(_lastPrune, now) >= PruneMs)
            {
                _lastPrune = now;
                Prune(now);
            }

            float interval = ModConfig.PerformanceReportSeconds.Value;
            if (interval > 0f && Ms(_periodStart, now) >= interval * 1000.0)
            {
                Report(now);
            }
        }

        private static string DescribeDeath(Death death)
        {
            var text = new StringBuilder();
            text.Append($"You died at {death.Clock} at {Format(death.Position)}, with {death.Waiting} object(s) ");
            text.Append("waiting to be created. ");

            int listed = 0;
            for (int i = Recent.Count - 1; i >= 0 && listed < MaxListed; i--)
            {
                Hit hit = Recent[i];
                double before = Ms(hit.At, death.At);
                if (before > KeepMs)
                {
                    break;
                }

                text.Append(listed == 0 ? "Hits in the last 10 s, newest first: " : "; ");
                text.Append($"{Math.Max(before, 0.0) / 1000.0:F1} s before, {Describe(hit)}");
                listed++;
            }

            text.Append(listed == 0 ? "No hits in the last 10 s." : ".");
            return text.ToString();
        }

        private static string Describe(Hit hit)
        {
            var text = new StringBuilder();
            text.Append(hit.Name);
            if (hit.HasAttacker)
            {
                text.Append($" {hit.Attacker}");
            }

            text.Append(hit.Ranged ? ", ranged" : ", melee");
            text.Append($", {hit.Raw:F0} raw and {hit.Taken:F0} taken");
            if (hit.Distance >= 0f)
            {
                text.Append($", from {hit.Distance:F1} m");
            }

            if (hit.HasAttacker && !hit.Present)
            {
                text.Append($", {hit.Missing}");
            }
            else if (hit.HasAttacker)
            {
                text.Append(hit.AgeMs >= 0.0
                    ? $", attacker created here {hit.AgeMs / 1000.0:F1} s before"
                    : ", attacker created here before the record began");
                if (hit.Drawn >= 0f)
                {
                    text.Append($" and drawn {hit.Drawn:F1} m from its last server position");
                }
            }

            text.Append($", {hit.Waiting} waiting");
            return text.ToString();
        }

        private static void Report(long now)
        {
            double seconds = Ms(_periodStart, now) / 1000.0;
            CreaturePriority.TakeCounts(out int ahead, out int mostWaiting);
            if (_hurt > 0 || _ignored > 0 || ahead > 0)
            {
                Plugin.Log.LogInfo(
                    $"Hits on you over {seconds:F0}s: {_hurt} that hurt, {_fresh} of them from an attacker created " +
                    $"here less than {FreshMs / 1000.0:F0} s earlier, and {_ignored} ignored because the attacker " +
                    $"was not created here. Attacker drawn from its last server position: " +
                    $"{DrawnOffsets.Describe("m", "F1")}. Creatures, players and projectiles created ahead of " +
                    $"waiting buildings: {ahead}, with up to {mostWaiting} of those waiting.");
            }

            _periodStart = now;
            DrawnOffsets.Clear();
            _hurt = 0;
            _fresh = 0;
            _ignored = 0;
        }

        private static void Prune(long now)
        {
            while (Recent.Count > 0 && Ms(Recent[0].At, now) > KeepMs)
            {
                Recent.RemoveAt(0);
            }

            ZDOMan man = ZDOMan.instance;
            Stale.Clear();
            foreach (KeyValuePair<ZDOID, long> entry in CreatedAt)
            {
                ZDO zdo = man != null ? man.GetZDO(entry.Key) : null;
                if (zdo == null || !zdo.Created)
                {
                    Stale.Add(entry.Key);
                }
            }

            for (int i = 0; i < Stale.Count; i++)
            {
                CreatedAt.Remove(Stale[i]);
            }

            Stale.Clear();
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
