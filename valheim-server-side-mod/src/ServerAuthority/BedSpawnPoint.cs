using UnityEngine;

namespace ServerAuthority
{
    internal static class BedSpawnPoint
    {
        private const string Rpc = "ServerAuthority_BedSpawnPoint";

        internal static void Register()
        {
            ZRoutedRpc.instance?.Register<ZPackage>(Rpc, RPC_BedSpawnPoint);
        }

        internal static void Destroyed(WearNTear wear)
        {
            Bed bed = wear.GetComponent<Bed>();
            ZNetView view = wear.m_nview;
            if (bed == null || view == null || !view.IsValid() || !view.IsOwner() || ZRoutedRpc.instance == null)
            {
                return;
            }

            ZDO zdo = view.GetZDO();
            long owner = zdo.GetLong(ZDOVars.s_owner);
            if (owner == 0L)
            {
                return;
            }

            Vector3 point = bed.GetSpawnPoint();
            ZPackage package = new ZPackage();
            package.Write(owner);
            package.Write(point);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, Rpc, package);
            Plugin.Log.LogInfo(
                $"Bed of {zdo.GetString(ZDOVars.s_ownerName)} ({owner}) at {Precise(point)} destroyed; every client " +
                "was told, and the owner's drops it as spawn point if it is set there.");
        }

        private static void RPC_BedSpawnPoint(long sender, ZPackage package)
        {
            ZNet znet = ZNet.instance;
            if (znet == null || znet.IsServer() || znet.GetServerPeer()?.m_uid != sender || Game.instance == null)
            {
                return;
            }

            long owner = package.ReadLong();
            Vector3 point = package.ReadVector3();
            PlayerProfile profile = Game.instance.GetPlayerProfile();
            if (profile == null || profile.GetPlayerID() != owner)
            {
                return;
            }

            if (!profile.HaveCustomSpawnPoint())
            {
                Plugin.Log.LogInfo($"Server reports this character's bed at {Precise(point)} destroyed; no spawn point is set.");
                return;
            }

            Vector3 current = profile.GetCustomSpawnPoint();
            Game.instance.RemoveCustomSpawnPoint(point);
            Plugin.Log.LogInfo(profile.HaveCustomSpawnPoint()
                ? $"Server reports this character's bed at {Precise(point)} destroyed; the spawn point is " +
                  $"{Precise(current)}, not that point, so it is kept."
                : $"Server reports this character's bed at {Precise(point)} destroyed; spawn point cleared.");
        }

        private static string Precise(Vector3 point)
        {
            return $"({point.x:R}, {point.y:R}, {point.z:R})";
        }
    }
}
