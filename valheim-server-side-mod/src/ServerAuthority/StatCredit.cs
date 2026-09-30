namespace ServerAuthority
{
    internal static class StatCredit
    {
        private const string Rpc = "ServerAuthority_StatCredit";

        private enum Kind : byte
        {
            Stat,
            ItemCraft
        }

        internal static void Register()
        {
            ZRoutedRpc.instance?.Register<ZPackage>(Rpc, RPC_StatCredit);
        }

        internal static void Stat(long peer, PlayerStatType stat, float amount = 1f, bool cheated = false)
        {
            ZPackage package = new ZPackage();
            package.Write((byte)Kind.Stat);
            package.Write((int)stat);
            package.Write(amount);
            package.Write(cheated);
            Send(peer, package);
        }

        internal static void ItemCraft(long peer, string name, float amount = 1f, bool cheated = false)
        {
            ZPackage package = new ZPackage();
            package.Write((byte)Kind.ItemCraft);
            package.Write(name);
            package.Write(amount);
            package.Write(cheated);
            Send(peer, package);
        }

        private static void Send(long peer, ZPackage package)
        {
            if (peer == 0L || ZRoutedRpc.instance == null)
            {
                return;
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(peer, Rpc, package);
        }

        private static void RPC_StatCredit(long sender, ZPackage package)
        {
            ZNet znet = ZNet.instance;
            if (znet == null || znet.IsServer() || znet.GetServerPeer()?.m_uid != sender || Game.instance == null)
            {
                return;
            }

            PlayerProfile profile = Game.instance.GetPlayerProfile();
            if (profile == null)
            {
                return;
            }

            Kind kind = (Kind)package.ReadByte();
            switch (kind)
            {
                case Kind.Stat:
                {
                    PlayerStatType stat = (PlayerStatType)package.ReadInt();
                    float amount = package.ReadSingle();
                    bool cheated = package.ReadBool();
                    profile.IncrementStat(stat, amount, cheated);
                    break;
                }
                case Kind.ItemCraft:
                {
                    string name = package.ReadString();
                    float amount = package.ReadSingle();
                    bool cheated = package.ReadBool();
                    profile.IncrementStatItemCraft(name, amount, cheated);
                    break;
                }
                default:
                    Plugin.Log.LogWarning($"Stat credit of unknown kind {(byte)kind} from the server ignored.");
                    break;
            }
        }
    }
}
