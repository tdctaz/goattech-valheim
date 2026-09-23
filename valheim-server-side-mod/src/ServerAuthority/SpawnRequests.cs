#if DEBUG_TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace ServerAuthority
{
    internal static class SpawnRequests
    {
        private const int MaxPerLine = 500;

        private static readonly Vector3[] Points = new Vector3[HullWater.PointCount];

        private static string _path;
        private static DateTime _lastSeen = DateTime.MinValue;

        internal static void Reset()
        {
            _path = null;
            _lastSeen = DateTime.MinValue;
        }

        internal static void Poll(List<Anchor> anchors)
        {
            if (!ModConfig.EnableSpawnRequests.Value || anchors.Count == 0)
            {
                return;
            }

            if (_path == null)
            {
                _path = Path.Combine(Paths.ConfigPath, "serverauthority_spawn.txt");
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

            bool didSomething = false;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue;
                }

                didSomething |= Handle(line, anchors[0]);
            }

            if (didSomething)
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



        private static void ReportOwners(Anchor anchor)
        {
            ZNet znet = ZNet.instance;
            ZDOMan zdoMan = ZDOMan.instance;
            if (znet == null || zdoMan == null)
            {
                return;
            }

            SimulationDistance synced = znet.GetSyncedSimulationDistance();
            SimulationDistance nearOnly = new SimulationDistance(
                synced.NearSimulationDistance, 0, synced.IsClassic);

            List<ZDO> found = new List<ZDO>();
            zdoMan.FindSectorObjects(anchor.Zone, nearOnly, found);

            long server = ZDOMan.GetSessionID();
            int byServer = 0;
            int byClient = 0;
            int unowned = 0;
            Dictionary<int, int> serverPrefabs = new Dictionary<int, int>();

            foreach (ZDO zdo in found)
            {
                if (!zdo.Persistent || !ZNetScene.InActiveArea(zdo.GetPosition(), anchor.Zone))
                {
                    continue;
                }

                long owner = zdo.GetOwner();
                if (owner == server)
                {
                    byServer++;
                    serverPrefabs.TryGetValue(zdo.GetPrefab(), out int n);
                    serverPrefabs[zdo.GetPrefab()] = n + 1;
                }
                else if (owner == 0L)
                {
                    unowned++;
                }
                else
                {
                    byClient++;
                }
            }

            List<KeyValuePair<int, int>> ranked = new List<KeyValuePair<int, int>>(serverPrefabs);
            ranked.Sort((a, b) => b.Value.CompareTo(a.Value));

            System.Text.StringBuilder top = new System.Text.StringBuilder();
            for (int i = 0; i < ranked.Count && i < 12; i++)
            {
                ZNetScene.instance.m_namedPrefabs.TryGetValue(ranked[i].Key, out GameObject prefab);
                top.Append(prefab != null ? prefab.name : ranked[i].Key.ToString());
                top.Append(" x").Append(ranked[i].Value);
                if (i < ranked.Count - 1 && i < 11)
                {
                    top.Append(", ");
                }
            }

            Plugin.Log.LogInfo(
                $"Ownership around peer {anchor.Uid}: server {byServer}, clients {byClient}, " +
                $"unowned {unowned}. Server-owned prefabs: {top}");
        }

        /// <summary>
        /// Prints, for every hull the server has instantiated, the five points it is about to
        /// measure the water at, which zone each one falls in, whether that zone exists, and what
        /// Ship.CustomFixedUpdate will therefore conclude. This is the read-out the boat bug needs:
        /// a run where no hull ever reported an incomplete neighbourhood has not tested the fix, it
        /// has only failed to provoke the fault.
        /// </summary>
        private static void ReportHulls()
        {
            ZoneSystem zones = ZoneSystem.instance;
            ZNetScene scene = ZNetScene.instance;
            if (zones == null || scene == null)
            {
                return;
            }

            long server = ZDOMan.GetSessionID();
            List<IMonoUpdater> instances = Ship.Instances;
            int reported = 0;

            for (int i = 0; i < instances.Count; i++)
            {
                Ship ship = instances[i] as Ship;
                if (ship == null || ship.m_nview == null || !ship.m_nview.IsValid())
                {
                    continue;
                }

                ZDO zdo = ship.m_nview.GetZDO();
                reported++;

                if (!HullWater.TryPoints(ship, Points))
                {
                    Plugin.Log.LogInfo($"Hull {zdo.m_uid}: no float collider or rigidbody to measure.");
                    continue;
                }

                float total = 0f;
                int missing = 0;
                System.Text.StringBuilder detail = new System.Text.StringBuilder();

                for (int p = 0; p < HullWater.PointCount; p++)
                {
                    WaterVolume probe = null;
                    float level = Floating.GetWaterLevel(Points[p], ref probe);
                    total += level;

                    Vector2s zone = ZoneSystem.GetZone(Points[p]);
                    bool loaded = zones.IsZoneLoaded(zone);
                    if (!loaded)
                    {
                        missing++;
                    }

                    if (p > 0)
                    {
                        detail.Append("; ");
                    }

                    detail.Append($"{HullWater.PointNames[p]} zone ({zone.x},{zone.y}) ")
                        .Append(loaded ? "loaded" : "MISSING")
                        .Append($" water {level:0.00}");
                }

                float ground = ZoneSystem.instance.GetGroundHeight(ship.transform.position);
                float average = total / HullWater.PointCount;
                float depth = ship.m_body.worldCenterOfMass.y - average - ship.m_waterLevelOffset;
                bool buoyant = !(depth > ship.m_disableLevel);
                WearNTear wear = ship.GetComponent<WearNTear>();
                Vector3 position = ship.transform.position;

                Plugin.Log.LogInfo(
                    $"Hull {zdo.m_uid} {PrefabName(zdo, scene)} at " +
                    $"({position.x:0}, {position.y:0.00}, {position.z:0}): " +
                    $"owner {Owner(zdo.GetOwner(), server)}, " +
                    $"health {(wear != null ? (wear.GetHealthPercentage() * 100f).ToString("0") : "?")}%, " +
                    $"seabed {ground:0.0} so {position.y - ground:0.0}m of fall available, " +
                    $"wind {(EnvMan.instance != null ? EnvMan.instance.GetWindIntensity() : 0f):0.00}, " +
                    $"vel {ship.m_body.linearVelocity.magnitude:0.0}, upY {ship.transform.up.y:0.00}, " +
                    $"{missing} of {HullWater.PointCount} zone(s) missing, " +
                    $"average water {average:0.00}, depth {depth:0.00} against disableLevel " +
                    $"{ship.m_disableLevel:0.00}, so buoyancy " +
                    (buoyant ? "ON" : missing > 0 ? "OFF (water unreadable, THE FAULT)" : "OFF (riding high, normal)") +
                    $". {detail}");
            }

            if (reported == 0)
            {
                Plugin.Log.LogInfo("No hulls instantiated on the server right now.");
            }
        }

        private static string Owner(long uid, long server)
        {
            if (uid == 0L)
            {
                return "nobody";
            }

            return uid == server ? "SERVER" : uid.ToString();
        }

        private static string PrefabName(ZDO zdo, ZNetScene scene)
        {
            scene.m_namedPrefabs.TryGetValue(zdo.GetPrefab(), out GameObject prefab);
            return prefab != null ? prefab.name : zdo.GetPrefab().ToString();
        }

        /// <summary>
        /// Forces the server's weather, which is the only way to test a storm on demand: weather is
        /// not sent over the network, both ends recompute it from the synced world time, and
        /// EnvMan.SetForceEnvironment is local. So this changes what the SERVER believes, which is
        /// what matters for a hull the server simulates. A client-owned hull would keep using the
        /// client's own weather and is not affected by this.
        ///
        /// Wave height follows from it: EnvMan.UpdateWind drives WaterVolume's global wind through
        /// WaterVolume.StaticUpdate, which MonoUpdaters calls on a server too, and GetWaterSurface
        /// scales CalcWave by that wind. A storm therefore really does move the water under a hull
        /// here, rather than only looking different.
        /// </summary>
        /// <summary>
        /// Removes every instantiated hull, without drops or destruction effects. A boat cannot be
        /// taken down with a hammer, its Piece has m_canBeRemoved false, so clearing the water for a
        /// clean test otherwise means beating a thousand health out of it.
        ///
        /// Worth having for a reason beyond convenience: two hulls moored in one spot collide with
        /// each other, and ImpactEffect on a ship carries m_damageToSelf, so boat on boat contact
        /// damages both. A second hull left in the water turns any test of what damages a moored
        /// boat into a test of whether two boats were touching.
        /// </summary>
        private static void RemoveShips()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null)
            {
                return;
            }

            List<Ship> doomed = new List<Ship>();
            List<IMonoUpdater> instances = Ship.Instances;
            for (int i = 0; i < instances.Count; i++)
            {
                Ship ship = instances[i] as Ship;
                if (ship != null && ship.m_nview != null && ship.m_nview.IsValid())
                {
                    doomed.Add(ship);
                }
            }

            for (int i = 0; i < doomed.Count; i++)
            {
                ZDO zdo = doomed[i].m_nview.GetZDO();
                Vector3 position = doomed[i].transform.position;

                // The server must own a ZDO to destroy it, and a moored hull may be unowned or held
                // by a client under ServerOwnsWaterborne false.
                zdo.SetOwner(ZDOMan.GetSessionID());
                scene.Destroy(doomed[i].gameObject);

                Plugin.Log.LogInfo(
                    $"Removed hull {zdo.m_uid} at ({position.x:0}, {position.z:0}).");
            }

            if (doomed.Count == 0)
            {
                Plugin.Log.LogInfo("No hulls instantiated to remove.");
            }
        }

        private static void Weather(string env)
        {
            EnvMan envMan = EnvMan.instance;
            if (envMan == null)
            {
                return;
            }

            if (env.Length == 0)
            {
                System.Text.StringBuilder names = new System.Text.StringBuilder();
                for (int i = 0; i < envMan.m_environments.Count && i < 40; i++)
                {
                    if (i > 0)
                    {
                        names.Append(", ");
                    }

                    names.Append(envMan.m_environments[i].m_name);
                }

                Plugin.Log.LogInfo(
                    $"Weather: wind intensity {envMan.GetWindIntensity():0.00}, " +
                    $"direction {envMan.GetWindDir()}. Environments: {names}");
                return;
            }

            if (string.Equals(env, "clear", StringComparison.OrdinalIgnoreCase))
            {
                envMan.SetForceEnvironment(string.Empty);
                Plugin.Log.LogInfo("Weather: forcing released, back to the world's own schedule.");
                return;
            }

            envMan.SetForceEnvironment(env);
            Plugin.Log.LogInfo(
                $"Weather: server environment forced to '{env}'. Wind intensity is " +
                $"{envMan.GetWindIntensity():0.00} and will ramp over the wind transition.");
        }

        private static void Diagnose(Anchor anchor)
        {
            Vector3 p = anchor.Position;
            WaterVolume probe = null;
            float sampled = Floating.GetWaterLevel(p, ref probe);

            WaterVolume[] volumes = UnityEngine.Object.FindObjectsByType<WaterVolume>(FindObjectsSortMode.None);
            int mask = LayerMask.GetMask("WaterVolume");
            Collider[] hits = Physics.OverlapSphere(p, 0.01f, mask);

            Plugin.Log.LogInfo(
                $"Water diagnostic at ({p.x:0}, {p.y:0}, {p.z:0}): " +
                $"GetWaterLevel returned {sampled:0.00} " +
                $"(-10000 means no volume found); " +
                $"{volumes.Length} WaterVolume instance(s) in the scene; " +
                $"{hits.Length} collider(s) on the WaterVolume layer at that point; " +
                $"layer mask {mask}; " +
                $"global water level {ZoneSystem.instance.m_waterLevel:0.00}; " +
                $"zone loaded {ZoneSystem.instance.IsZoneLoaded(ZoneSystem.GetZone(p))}.");

            int correctMask = LayerMask.GetMask("WaterVolume");
            int zeroRadius = Physics.OverlapSphereNonAlloc(
                p, 0f, new Collider[8], correctMask);

            Plugin.Log.LogInfo(
                $"Water query at ({p.x:0}, {p.z:0}): Floating's layer mask is " +
                $"{Floating.s_waterVolumeMask} and the correct one is {correctMask}" +
                (Floating.s_waterVolumeMask == correctMask ? string.Empty : "  <- WRONG, this is the fault") +
                $"; a zero radius overlap with the correct mask, which is exactly what " +
                $"GetWaterLevel does, finds {zeroRadius} collider(s).");

            ReportWaterLookup(p, hits, volumes);
        }

        /// <summary>
        /// Says why a point with a water collider on it can still answer "no water".
        ///
        /// Floating.GetWaterLevel does not ask the collider for its WaterVolume each time. It
        /// keeps a static Dictionary&lt;int, WaterVolume&gt; keyed by the collider's instance id,
        /// fills it once, and never invalidates it. Unity recycles instance ids, and a destroyed
        /// WaterVolume compares equal to null without being a null reference, so a stale entry
        /// makes the lookup answer nothing at all rather than re-resolving. Every caller reads
        /// that as -10000, which a Ship reads as being two kilometres above the sea, and it stops
        /// floating and falls.
        ///
        /// This prints the three things that tell those cases apart, per collider: what a fresh
        /// GetComponent finds now, what the cache holds, and whether the two agree. A live
        /// component next to a cached destroyed one is the cache fault; no component at all on a
        /// collider that is on the WaterVolume layer is something else entirely and means this
        /// hypothesis is wrong.
        /// </summary>
        private static void ReportWaterLookup(Vector3 p, Collider[] hits, WaterVolume[] volumes)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                int id = hit.GetInstanceID();
                WaterVolume direct = hit.GetComponent<WaterVolume>();
                bool haveEntry = Floating.s_waterVolumeCache.TryGetValue(id, out WaterVolume cached);

                Plugin.Log.LogInfo(
                    $"Water lookup at ({p.x:0}, {p.z:0}), collider '{hit.name}' " +
                    $"on '{hit.gameObject.name}' layer {hit.gameObject.layer} id {id}: " +
                    $"GetComponent says {Describe(direct)}; " +
                    $"cache {(haveEntry ? "holds " + Describe(cached) : "has no entry")}; " +
                    $"cache size {Floating.s_waterVolumeCache.Count}.");
            }

            int covering = 0;
            for (int i = 0; i < volumes.Length; i++)
            {
                Collider collider = volumes[i].GetComponent<Collider>();
                if (collider != null && collider.bounds.Contains(p))
                {
                    covering++;
                    Plugin.Log.LogInfo(
                        $"Water lookup at ({p.x:0}, {p.z:0}): live WaterVolume '{volumes[i].name}' " +
                        $"id {collider.GetInstanceID()} covers this point, surface " +
                        $"{volumes[i].GetWaterSurface(p):0.00}.");
                }
            }

            if (covering == 0)
            {
                Plugin.Log.LogInfo(
                    $"Water lookup at ({p.x:0}, {p.z:0}): none of the {volumes.Length} live " +
                    "WaterVolume instances has bounds covering this point, so the collider found " +
                    "there belongs to something already destroyed or to something else.");
            }
        }

        /// <summary>
        /// Unity's null is three different states and only one of them is a null reference. A
        /// destroyed object still has a managed reference but compares equal to null, which is
        /// exactly the case that turns a cached WaterVolume into silent missing water.
        /// </summary>
        private static string Describe(WaterVolume volume)
        {
            if (ReferenceEquals(volume, null))
            {
                return "no component";
            }

            return volume == null ? "a DESTROYED WaterVolume" : $"live WaterVolume '{volume.name}'";
        }

        /// <summary>
        /// Starts a random event on the first player, as the vanilla <c>event</c> console command
        /// does, or ends the running one with <c>event stop</c>. Here so that raid weather can be
        /// tested without devcommands, which flag the character as cheated.
        /// </summary>
        private static void StartEvent(string eventName, Anchor anchor)
        {
            RandEventSystem events = RandEventSystem.instance;
            if (events == null)
            {
                return;
            }

            if (eventName.Length == 0 || string.Equals(eventName, "stop", StringComparison.OrdinalIgnoreCase))
            {
                events.ResetRandomEvent();
                Plugin.Log.LogInfo("Spawn request: random event stopped.");
                return;
            }

            if (events.GetEvent(eventName) == null)
            {
                Plugin.Log.LogWarning($"Spawn request: no event named '{eventName}'.");
                return;
            }

            events.SetRandomEventByName(eventName, anchor.Position);
            Plugin.Log.LogInfo(
                $"Spawn request: started event '{eventName}' at ({anchor.Position.x:0}, {anchor.Position.z:0}).");
        }

        private static bool Handle(string line, Anchor anchor)
        {
            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string name = parts[0];

            if (string.Equals(name, "water", StringComparison.OrdinalIgnoreCase))
            {
                NearestWater.Rescan(anchor.Uid);
                return true;
            }

            if (string.Equals(name, "diag", StringComparison.OrdinalIgnoreCase))
            {
                Diagnose(anchor);
                return true;
            }

            if (string.Equals(name, "owners", StringComparison.OrdinalIgnoreCase))
            {
                ReportOwners(anchor);
                return true;
            }

            if (string.Equals(name, "hulls", StringComparison.OrdinalIgnoreCase))
            {
                ReportHulls();
                return true;
            }

            if (string.Equals(name, "weather", StringComparison.OrdinalIgnoreCase))
            {
                Weather(parts.Length > 1 ? parts[1] : string.Empty);
                return true;
            }

            if (string.Equals(name, "event", StringComparison.OrdinalIgnoreCase))
            {
                StartEvent(parts.Length > 1 ? parts[1] : string.Empty, anchor);
                return true;
            }

            if (string.Equals(name, "removeships", StringComparison.OrdinalIgnoreCase))
            {
                RemoveShips();
                return true;
            }

            int count = 1;
            if (parts.Length > 1 && !int.TryParse(parts[1], out count))
            {
                count = 1;
            }

            count = Mathf.Clamp(count, 1, MaxPerLine);

            bool tame = parts.Length > 2 && string.Equals(parts[2], "tame", StringComparison.OrdinalIgnoreCase);

            GameObject prefab = ZNetScene.instance.GetPrefab(name);
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"Spawn request: no prefab named '{name}'.");
                return true;
            }

            int spawned = 0;
            int remaining = count;
            int guard = 0;

            while (remaining > 0 && guard++ < MaxPerLine)
            {
                Vector3 jitter = UnityEngine.Random.insideUnitSphere * 1.5f;
                jitter.y = 0f;
                Vector3 position = anchor.Position + Vector3.up + jitter;

                GameObject go = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
                ItemDrop item = go.GetComponent<ItemDrop>();
                ItemDrop.OnCreateNew(go);

                if (tame)
                {
                    MonsterAI ai = go.GetComponent<MonsterAI>();
                    if (ai != null)
                    {
                        ai.MakeTame();
                    }
                }

                if (item != null)
                {
                    int stack = Mathf.Min(remaining, Mathf.Max(1, item.m_itemData.m_shared.m_maxStackSize));
                    item.m_itemData.m_stack = stack;
                    item.m_itemData.m_durability = item.m_itemData.GetMaxDurability();
                    item.Save();
                    remaining -= stack;
                }
                else
                {
                    remaining--;
                }

                spawned++;
            }

            Plugin.Log.LogInfo(
                $"Spawn request: {name} x{count}{(tame ? " tame" : "")} as {spawned} object(s) at " +
                $"({anchor.Position.x:0}, {anchor.Position.z:0}).");
            return true;
        }
    }
}
#endif
