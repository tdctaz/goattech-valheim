using UnityEngine;

namespace ServerAuthority
{
    internal static class RemotePush
    {
        private const string Rpc = "ServerAuthority_Pushback";
        private const float MaxForce = 50f;

        private static bool _loggedSent;
        private static bool _loggedApplied;

        internal static void Register()
        {
            ZRoutedRpc.instance?.Register<ZPackage>(Rpc, RPC_Pushback);
        }

        internal static void Reset()
        {
            _loggedSent = false;
            _loggedApplied = false;
        }

        internal static bool Forward(Character character, Vector3 dir, float pushForce)
        {
            if (pushForce == 0f || dir == Vector3.zero)
            {
                return false;
            }

            ZNetView view = character.m_nview;
            ZNet znet = ZNet.instance;
            if (view == null || !view.IsValid() || view.IsOwner() || znet == null || znet.IsServer() ||
                ZRoutedRpc.instance == null)
            {
                return false;
            }

            ZNetPeer server = znet.GetServerPeer();
            if (server == null || view.GetZDO().GetOwner() != server.m_uid)
            {
                return false;
            }

            ZPackage package = new ZPackage();
            package.Write(view.GetZDO().m_uid);
            package.Write(dir);
            package.Write(pushForce);
            ZRoutedRpc.instance.InvokeRoutedRPC(server.m_uid, Rpc, package);
            if (!_loggedSent)
            {
                _loggedSent = true;
                Plugin.Log.LogInfo(
                    $"A push of {pushForce:0.#} on server-owned {Utils.GetPrefabName(character.gameObject)} was sent to the server. " +
                    "Later pushes are not logged.");
            }

            return true;
        }

        private static void RPC_Pushback(long sender, ZPackage package)
        {
            if (!Plugin.ServerActive || !SimulationAnchors.IsConnectedPeer(sender) || ZNetScene.instance == null)
            {
                return;
            }

            ZDOID id = package.ReadZDOID();
            Vector3 dir = package.ReadVector3();
            float pushForce = package.ReadSingle();
            if (float.IsNaN(pushForce) || float.IsInfinity(pushForce) || float.IsNaN(dir.x) || float.IsInfinity(dir.x) ||
                float.IsNaN(dir.y) || float.IsInfinity(dir.y) || float.IsNaN(dir.z) || float.IsInfinity(dir.z))
            {
                return;
            }

            pushForce = Mathf.Clamp(pushForce, 0f, MaxForce);
            GameObject instance = ZNetScene.instance.FindInstance(id);
            Character character = instance != null ? instance.GetComponent<Character>() : null;
            if (character == null || character.m_nview == null || !character.m_nview.IsValid() ||
                !character.m_nview.IsOwner())
            {
                return;
            }

            character.ApplyPushback(dir, pushForce);
            if (!_loggedApplied)
            {
                _loggedApplied = true;
                Plugin.Log.LogInfo(
                    $"Applied a push of {pushForce:0.#} from a client to {Utils.GetPrefabName(character.gameObject)}. " +
                    "Later pushes are not logged.");
            }
        }
    }
}
