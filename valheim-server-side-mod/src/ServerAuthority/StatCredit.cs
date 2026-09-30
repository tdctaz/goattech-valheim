namespace ServerAuthority
{
    internal static class StatCredit
    {
        private const string Rpc = "ServerAuthority_StatCredit";

        private enum Kind : byte
        {
            Stat,
            ItemCraft,
            Skill,
            ToolStat
        }

        internal static void Register()
        {
            ZRoutedRpc.instance?.Register<ZPackage>(Rpc, RPC_StatCredit);
        }

        internal static bool Stat(long peer, PlayerStatType stat, float amount = 1f, bool cheated = false)
        {
            ZPackage package = new ZPackage();
            package.Write((byte)Kind.Stat);
            package.Write((int)stat);
            package.Write(amount);
            package.Write(cheated);
            return Send(peer, package);
        }

        internal static bool ToolStat(long peer, PlayerStatType stat, float amount = 1f)
        {
            ZPackage package = new ZPackage();
            package.Write((byte)Kind.ToolStat);
            package.Write((int)stat);
            package.Write(amount);
            return Send(peer, package);
        }

        internal static bool ItemCraft(long peer, string name, float amount = 1f, bool cheated = false)
        {
            ZPackage package = new ZPackage();
            package.Write((byte)Kind.ItemCraft);
            package.Write(name);
            package.Write(amount);
            package.Write(cheated);
            return Send(peer, package);
        }

        internal static bool Skill(long peer, Skills.SkillType skill, float amount)
        {
            ZPackage package = new ZPackage();
            package.Write((byte)Kind.Skill);
            package.Write((int)skill);
            package.Write(amount);
            return Send(peer, package);
        }

        private static bool Send(long peer, ZPackage package)
        {
            if (peer == 0L || ZRoutedRpc.instance == null || ZNet.instance == null || ZNet.instance.GetPeer(peer) == null)
            {
                return false;
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(peer, Rpc, package);
            return true;
        }

        private static void RPC_StatCredit(long sender, ZPackage package)
        {
            ZNet znet = ZNet.instance;
            if (znet == null || znet.IsServer() || znet.GetServerPeer()?.m_uid != sender)
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
                    Profile()?.IncrementStat(stat, amount, cheated);
                    break;
                }
                case Kind.ToolStat:
                {
                    PlayerStatType stat = (PlayerStatType)package.ReadInt();
                    float amount = package.ReadSingle();
                    Player player = Player.m_localPlayer;
                    bool cheated = player != null && player.GetInventory().CheatedDamagingItemEquipped();
                    Profile()?.IncrementStat(stat, amount, cheated);
                    break;
                }
                case Kind.ItemCraft:
                {
                    string name = package.ReadString();
                    float amount = package.ReadSingle();
                    bool cheated = package.ReadBool();
                    Profile()?.IncrementStatItemCraft(name, amount, cheated);
                    break;
                }
                case Kind.Skill:
                {
                    Skills.SkillType skill = (Skills.SkillType)package.ReadInt();
                    float amount = package.ReadSingle();
                    Player player = Player.m_localPlayer;
                    Skills skills = player != null ? player.GetSkills() : null;
                    if (skills == null)
                    {
                        break;
                    }

                    if (skills.GetSkillDef(skill) == null)
                    {
                        Plugin.Log.LogWarning($"Skill gain for undefined skill {(int)skill} from the server ignored.");
                        break;
                    }

                    skills.RaiseSkill(skill, amount);
                    break;
                }
                default:
                    Plugin.Log.LogWarning($"Stat credit of unknown kind {(byte)kind} from the server ignored.");
                    break;
            }
        }

        private static PlayerProfile Profile()
        {
            return Game.instance != null ? Game.instance.GetPlayerProfile() : null;
        }
    }
}
