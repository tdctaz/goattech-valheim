using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ServerAuthority
{
    internal static class SupportGate
    {
        private const float SettleSeconds = 2f;
        private const float GiveUpSeconds = 30f;
        private const int ListedZones = 5;
        private const int ListedPrefabs = 6;
        private const int ListedWaiting = 5;

        private sealed class ZoneLoad
        {
            public float Since;
            public float BuiltAt;
            public bool Built;
            public bool Ready;
            public bool Warned;
            public int Pass;
        }

        private static readonly Dictionary<Vector2s, ZoneLoad> Zones = new Dictionary<Vector2s, ZoneLoad>();
        private static readonly HashSet<Vector2s> Waiting = new HashSet<Vector2s>();
        private static readonly List<Vector2s> Stale = new List<Vector2s>();
        private static readonly Dictionary<Vector2s, bool> Verdicts = new Dictionary<Vector2s, bool>();
        private static readonly Dictionary<Vector2s, int> CollapseZones = new Dictionary<Vector2s, int>();
        private static readonly Dictionary<int, int> CollapsePrefabs = new Dictionary<int, int>();

        private static int _pass;
        private static int _verdictFrame = -1;
        private static int _near;
        private static bool _classic;

        private static int _periodHeld;
        private static int _periodZonesLoaded;
        private static float _periodLongestWait;
        private static int _periodGaveUp;
        private static int _periodCollapses;

        internal static void Reset()
        {
            Zones.Clear();
            Waiting.Clear();
            Stale.Clear();
            Verdicts.Clear();
            _pass = 0;
            _verdictFrame = -1;
            ResetPeriod();
        }

        internal static void Track(List<Anchor> anchors, SimulationDistance synced, List<ZDO> uncreated)
        {
            ZoneSystem zoneSystem = ZoneSystem.instance;
            if (!ModConfig.HoldSupportUntilNeighboursLoad.Value || zoneSystem == null || anchors.Count == 0)
            {
                Zones.Clear();
                return;
            }

            Waiting.Clear();
            for (int i = 0; i < uncreated.Count; i++)
            {
                if (!uncreated[i].Created)
                {
                    Waiting.Add(uncreated[i].GetSector());
                }
            }

            _pass++;
            float now = Time.time;
            int near = synced.NearSimulationDistance;
            bool classic = synced.IsClassic;

            for (int a = 0; a < anchors.Count; a++)
            {
                Vector2s centre = anchors[a].Zone;
                for (int y = centre.y - near; y <= centre.y + near; y++)
                {
                    for (int x = centre.x - near; x <= centre.x + near; x++)
                    {
                        Vector2s zone = new Vector2s((short)x, (short)y);
                        if (!classic && !zoneSystem.ZonesWithinRadius(centre, zone, near))
                        {
                            continue;
                        }

                        Visit(zoneSystem, zone, now, uncreated);
                    }
                }
            }

            Stale.Clear();
            foreach (KeyValuePair<Vector2s, ZoneLoad> entry in Zones)
            {
                if (entry.Value.Pass != _pass)
                {
                    Stale.Add(entry.Key);
                }
            }

            for (int i = 0; i < Stale.Count; i++)
            {
                Zones.Remove(Stale[i]);
            }
        }

        internal static bool Ready(Vector3 position)
        {
            int frame = Time.frameCount;
            if (frame != _verdictFrame)
            {
                _verdictFrame = frame;
                Verdicts.Clear();
                SimulationDistance synced = ZNet.instance.GetSyncedSimulationDistance();
                _near = synced.NearSimulationDistance;
                _classic = synced.IsClassic;
            }

            Vector2s zone = ZoneSystem.GetZone(position);
            if (!Verdicts.TryGetValue(zone, out bool ready))
            {
                ready = NeighbourhoodReady(zone);
                Verdicts.Add(zone, ready);
            }

            return ready;
        }

        internal static void Held()
        {
            _periodHeld++;
        }

        internal static void Unsupported(WearNTear piece)
        {
            ZDO zdo = piece.m_nview.GetZDO();
            if (zdo == null || zdo.GetFloat(ZDOVars.s_health, piece.m_health) <= 0f ||
                !piece.CanBeRemoved() || ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBuildingFall))
            {
                return;
            }

            _periodCollapses++;

            Vector2s zone = ZoneSystem.GetZone(piece.transform.position);
            CollapseZones.TryGetValue(zone, out int inZone);
            CollapseZones[zone] = inZone + 1;

            int prefab = zdo.GetPrefab();
            CollapsePrefabs.TryGetValue(prefab, out int ofPrefab);
            CollapsePrefabs[prefab] = ofPrefab + 1;
        }

        internal static string ClosePeriod(float seconds)
        {
            string text = null;
            if (seconds > 0f && (_periodCollapses > 0 || _periodHeld > 0 || _periodGaveUp > 0))
            {
                string collapses = _periodCollapses == 0
                    ? "no piece fell for lack of support"
                    : $"{_periodCollapses} piece(s) fell for lack of support, " +
                      Listed(CollapseZones, ListedZones, e => $"{e.Value} in {DescribeZone(e.Key)}", "zone(s)") +
                      ": " +
                      Listed(CollapsePrefabs, ListedPrefabs, e => $"{e.Value} {PrefabName(e.Key)}", "prefab(s)");

                text = $"Structural support over {seconds:F0}s: {collapses}.";
                if (ModConfig.HoldSupportUntilNeighboursLoad.Value)
                {
                    text += $" Support checks held back {_periodHeld} time(s) while {_periodZonesLoaded} zone(s) " +
                            $"finished loading, longest load {_periodLongestWait:F1} s, {_periodGaveUp} zone(s) " +
                            "given up on.";
                }
            }

            ResetPeriod();
            return text;
        }

        private static void Visit(ZoneSystem zoneSystem, Vector2s zone, float now, List<ZDO> uncreated)
        {
            if (!Zones.TryGetValue(zone, out ZoneLoad load))
            {
                load = new ZoneLoad { Since = now };
                Zones.Add(zone, load);
            }
            else if (load.Pass == _pass)
            {
                return;
            }

            load.Pass = _pass;

            if (!zoneSystem.IsZoneLoaded(zone))
            {
                load.Built = false;
                if (load.Ready)
                {
                    load.Ready = false;
                    load.Warned = false;
                    load.Since = now;
                }
                else if (!load.Warned && now - load.Since >= GiveUpSeconds)
                {
                    load.Warned = true;
                    Plugin.Log.LogWarning(
                        $"{DescribeZone(zone)} is near a player but after {now - load.Since:F0} s is still " +
                        "not built, or is still loading a location. Support checks next to it stay held " +
                        "until it is done.");
                }

                return;
            }

            if (load.Ready)
            {
                return;
            }

            if (!load.Built)
            {
                load.Built = true;
                load.BuiltAt = now;
            }

            if (Waiting.Contains(zone))
            {
                float creating = now - load.BuiltAt;
                if (creating < GiveUpSeconds)
                {
                    return;
                }

                GiveUp(zone, creating, uncreated);
            }

            float waited = now - load.Since;
            _periodZonesLoaded++;
            _periodLongestWait = Mathf.Max(_periodLongestWait, waited);
            load.Ready = true;
            load.Since = now;
        }

        private static void GiveUp(Vector2s zone, float waited, List<ZDO> uncreated)
        {
            _periodGaveUp++;

            int count = 0;
            var names = new List<string>();
            ZNetScene scene = ZNetScene.instance;
            for (int i = 0; i < uncreated.Count; i++)
            {
                ZDO zdo = uncreated[i];
                if (zdo.Created || zdo.GetSector() != zone)
                {
                    continue;
                }

                count++;
                GameObject prefab = scene != null ? scene.GetPrefab(zdo.GetPrefab()) : null;
                string name = prefab != null ? prefab.name : zdo.GetPrefab().ToString();
                if (names.Count < ListedWaiting && !names.Contains(name))
                {
                    names.Add(name);
                }
            }

            Plugin.Log.LogWarning(
                $"{DescribeZone(zone)} still had {count} object(s) waiting to be created {waited:F0} s after " +
                $"it was built ({string.Join(", ", names)}). Support checks next to it resume anyway, so a " +
                "piece resting on one of those objects can fall.");
        }

        private static bool NeighbourhoodReady(Vector2s zone)
        {
            List<Anchor> anchors = SimulationAnchors.Current;
            ZoneSystem zoneSystem = ZoneSystem.instance;
            float now = Time.time;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    Vector2s neighbour = new Vector2s((short)(zone.x + dx), (short)(zone.y + dy));
                    if (!InNearArea(zoneSystem, anchors, neighbour))
                    {
                        continue;
                    }

                    if (!Zones.TryGetValue(neighbour, out ZoneLoad load) || !load.Ready ||
                        now - load.Since < SettleSeconds)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool InNearArea(ZoneSystem zoneSystem, List<Anchor> anchors, Vector2s zone)
        {
            for (int i = 0; i < anchors.Count; i++)
            {
                Vector2s centre = anchors[i].Zone;
                if (Mathf.Abs(zone.x - centre.x) > _near || Mathf.Abs(zone.y - centre.y) > _near)
                {
                    continue;
                }

                if (_classic || zoneSystem.ZonesWithinRadius(centre, zone, _near))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Listed<TKey>(
            Dictionary<TKey, int> counts, int take, System.Func<KeyValuePair<TKey, int>, string> describe, string kind)
        {
            string listed = string.Join(", ", counts.OrderByDescending(e => e.Value).Take(take).Select(describe));
            if (counts.Count <= take)
            {
                return listed;
            }

            int rest = counts.OrderByDescending(e => e.Value).Skip(take).Sum(e => e.Value);
            return $"{listed} and {rest} in {counts.Count - take} other {kind}";
        }

        private static string PrefabName(int hash)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(hash) : null;
            return prefab != null ? prefab.name : hash.ToString();
        }

        private static string DescribeZone(Vector2s zone)
        {
            Vector3 centre = ZoneSystem.GetZonePos(zone);
            return $"zone ({zone.x}, {zone.y}) at ({centre.x:F0}, {centre.z:F0})";
        }

        private static void ResetPeriod()
        {
            _periodHeld = 0;
            _periodZonesLoaded = 0;
            _periodLongestWait = 0f;
            _periodGaveUp = 0;
            _periodCollapses = 0;
            CollapseZones.Clear();
            CollapsePrefabs.Clear();
        }
    }
}
