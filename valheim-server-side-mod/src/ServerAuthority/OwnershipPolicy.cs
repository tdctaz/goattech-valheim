using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority
{
    internal static class OwnershipPolicy
    {
        private static readonly List<ZDO> Candidates = new List<ZDO>();
        private static readonly HashSet<ZDO> Seen = new HashSet<ZDO>();
        private static readonly List<ZDO> Scratch = new List<ZDO>();
        private static readonly List<int> Players = new List<int>();
        private static readonly Dictionary<ZDOID, double> PeerClaims = new Dictionary<ZDOID, double>();
        private static readonly List<ZDOID> StaleClaims = new List<ZDOID>();

        private const double PeerClaimGraceSeconds = 3.0;

        private static double _clock;
        private static double _lastStatus;

        internal static double Clock => _clock;

        internal static void Reset()
        {
            Candidates.Clear();
            Seen.Clear();
            Scratch.Clear();
            Players.Clear();
            PeerClaims.Clear();
            StaleClaims.Clear();
            OwnershipLeases.Clear();
#if DEBUG_TOOLS
            NearestWater.Reset();
            SpawnRequests.Reset();
#endif
            Waterborne.Reset();
            _clock = 0.0;
            _lastStatus = 0.0;
        }

        internal static void Advance(float dt)
        {
            _clock += dt;
        }

        internal static void Run(ZDOMan zdoMan)
        {
            ZNet znet = ZNet.instance;
            if (ZoneSystem.instance == null || znet == null)
            {
                return;
            }

            List<Anchor> anchors = SimulationAnchors.Current;
            HashSet<ZDOID> playerCharacters = SimulationAnchors.PlayerCharacters;
            long server = ZDOMan.GetSessionID();

#if DEBUG_TOOLS
            NearestWater.ReportFor(anchors);
            SpawnRequests.Poll(anchors);
#endif

            SimulationDistance synced = znet.GetSyncedSimulationDistance();
            SimulationDistance nearOnly = new SimulationDistance(
                synced.NearSimulationDistance, 0, synced.IsClassic);

            Gather(zdoMan, anchors, nearOnly, playerCharacters);
            Measure(anchors);

            int transfers = 0;
            int simulated = 0;

            for (int i = 0; i < Candidates.Count; i++)
            {
                ZDO zdo = Candidates[i];

                if (IsBeingUsedByAPlayer(zdo))
                {
                    continue;
                }

                long target;
                if (OwnershipLeases.TryGetOwner(zdo.m_uid, out long leased))
                {
                    target = leased;
                }
                else if (!ModConfig.ServerOwnsWaterborne.Value && Waterborne.Is(zdo.GetPrefab()))
                {
                    // Only once somebody actually has it in their active area. Gather reaches the
                    // whole near ring, about 160m, while a client only builds zones and instantiates
                    // objects for its own rings and only simulates what it has loaded. Handing a
                    // hull to the nearest peer regardless of distance made that client the owner of
                    // a rigidbody sitting past the edge of its own loaded region, where the water
                    // under the bow or stern cannot be found: Floating.GetWaterLevel answers -10000,
                    // buoyancy switches off, and the hull falls out of the sea and beats itself
                    // apart on the bottom. Vanilla never hands an object to a peer that does not
                    // contain it either, and Players is already that test.
                    target = Players[i] == 0 && ModConfig.RequireLoadedAreaForWaterborneOwner.Value
                        ? 0L
                        : NearestPeer(zdo.GetPosition(), anchors);
                }
                else if (Players[i] == 0)
                {
                    target = 0L;
                }
                else
                {
                    target = server;
                }

                if (target == server)
                {
                    simulated++;
                }

                long current = zdo.GetOwner();
                if (current == target)
                {
                    PeerClaims.Remove(zdo.m_uid);
                    continue;
                }

                if (!HoldOffClaim(zdo, current, target, server))
                {
                    continue;
                }

#if DEBUG_TOOLS
                if (ModConfig.LogOwnershipChanges.Value)
                {
                    Plugin.Log.LogInfo(
                        $"Ownership {zdo.m_uid} prefab {zdo.GetPrefab()}: {Describe(current, server)} " +
                        $"to {Describe(target, server)}");
                }
#endif

                zdo.SetOwner(target);
                transfers++;
            }

            PruneClaims();
            OwnershipLeases.PruneExpired();
            ReportStatus(anchors.Count, simulated, transfers);
        }


        /// <summary>
        /// A client that picks something up, opens it or takes control of it makes itself the owner,
        /// and there is a gap before whatever flag says so gets back to the server. Taking the object
        /// straight back inside that gap is what used to slam chests shut. Anything a connected peer
        /// has just claimed is therefore left alone briefly, which covers every such handover without
        /// patching the RPCs that perform them. Those patches crashed Mono.
        /// </summary>
        private static bool HoldOffClaim(ZDO zdo, long current, long target, long server)
        {
            if (target != server || current == server || current == 0L)
            {
                PeerClaims.Remove(zdo.m_uid);
                return true;
            }

            if (!SimulationAnchors.IsConnectedPeer(current))
            {
                PeerClaims.Remove(zdo.m_uid);
                return true;
            }

            if (!PeerClaims.TryGetValue(zdo.m_uid, out double since))
            {
                PeerClaims[zdo.m_uid] = _clock;
                return false;
            }

            if (_clock - since < PeerClaimGraceSeconds)
            {
                return false;
            }

            PeerClaims.Remove(zdo.m_uid);
            return true;
        }

        private static void PruneClaims()
        {
            if (PeerClaims.Count == 0)
            {
                return;
            }

            StaleClaims.Clear();
            foreach (KeyValuePair<ZDOID, double> claim in PeerClaims)
            {
                if (_clock - claim.Value > PeerClaimGraceSeconds * 2.0)
                {
                    StaleClaims.Add(claim.Key);
                }
            }

            for (int i = 0; i < StaleClaims.Count; i++)
            {
                PeerClaims.Remove(StaleClaims[i]);
            }
        }

        private static string Describe(long uid, long server)
        {
            if (uid == 0L)
            {
                return "nobody";
            }

            return uid == server ? "server" : uid.ToString();
        }

        private static void Gather(
            ZDOMan zdoMan,
            List<Anchor> anchors,
            SimulationDistance nearOnly,
            HashSet<ZDOID> playerCharacters)
        {
            Candidates.Clear();
            Seen.Clear();

            for (int i = 0; i < anchors.Count; i++)
            {
                Scratch.Clear();
                zdoMan.FindSectorObjects(anchors[i].Zone, nearOnly, Scratch);

                for (int j = 0; j < Scratch.Count; j++)
                {
                    ZDO zdo = Scratch[j];
                    if (!zdo.Persistent || playerCharacters.Contains(zdo.m_uid))
                    {
                        continue;
                    }

                    if (Seen.Add(zdo))
                    {
                        Candidates.Add(zdo);
                    }
                }
            }
        }

        private static void Measure(List<Anchor> anchors)
        {
            Players.Clear();

            for (int i = 0; i < Candidates.Count; i++)
            {
                Vector3 position = Candidates[i].GetPosition();

                int players = 0;
                for (int a = 0; a < anchors.Count; a++)
                {
                    if (ZNetScene.InActiveArea(position, anchors[a].Zone))
                    {
                        players++;
                    }
                }

                Players.Add(players);
            }
        }

        private static long NearestPeer(Vector3 position, List<Anchor> anchors)
        {
            long best = 0L;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < anchors.Count; i++)
            {
                float distance = (anchors[i].Position - position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = anchors[i].Uid;
                }
            }

            return best;
        }

        private static bool IsBeingUsedByAPlayer(ZDO zdo)
        {
            if (zdo.GetInt(ZDOVars.s_inUse) == 0)
            {
                return false;
            }

            return SimulationAnchors.IsConnectedPeer(zdo.GetOwner());
        }

        private static void ReportStatus(int players, int simulated, int transfers)
        {
            float interval = ModConfig.StatusIntervalSeconds.Value;
            if (interval <= 0f || _clock - _lastStatus < interval)
            {
                return;
            }

            _lastStatus = _clock;
            Plugin.Log.LogInfo(
                $"Server Authority: {players} player(s), simulating {simulated} object(s) " +
                $"of {Candidates.Count} nearby, {transfers} ownership change(s) this tick.");
        }
    }
}
