using System.Collections.Generic;

namespace ValheimRebalanced
{
    internal static class StaffOfTheWild
    {
        private static readonly int RootPrefab = "staff_greenroots_tentaroot".GetStableHashCode();

        private static readonly List<ZNetView> Pending = new List<ZNetView>();
        private static readonly List<Character> Mine = new List<Character>();

        internal static void Observe(ZNetView view)
        {
            if (view != null && ConfigSync.Current.MaxStaffOfTheWildRoots > 0)
            {
                Pending.Add(view);
            }
        }

        internal static void Tick()
        {
            if (Pending.Count == 0)
            {
                return;
            }

            long session = ZDOMan.GetSessionID();
            bool castByMe = false;

            foreach (ZNetView view in Pending)
            {
                if (view != null && view.IsValid() && IsRootCastBy(view.GetZDO(), session))
                {
                    castByMe = true;
                }
            }

            Pending.Clear();

            int max = ConfigSync.Current.MaxStaffOfTheWildRoots;
            if (!castByMe || max <= 0)
            {
                return;
            }

            Mine.Clear();
            foreach (Character character in Character.GetAllCharacters())
            {
                ZNetView view = character.m_nview;
                if (view != null && view.IsValid() && !character.IsDead() && IsRootCastBy(view.GetZDO(), session))
                {
                    Mine.Add(character);
                }
            }

            Mine.Sort((a, b) => a.m_nview.GetZDO().m_uid.ID.CompareTo(b.m_nview.GetZDO().m_uid.ID));

            for (int i = 0; i < Mine.Count - max; i++)
            {
                Desummon(Mine[i]);
            }

            Mine.Clear();
        }

        private static bool IsRootCastBy(ZDO zdo, long session)
        {
            return zdo != null && zdo.GetPrefab() == RootPrefab && zdo.m_uid.UserID == session;
        }

        private static void Desummon(Character root)
        {
            ZNetView view = root.m_nview;
            if (!view.IsOwner())
            {
                view.ClaimOwnership();
            }

            HitData hit = new HitData();
            hit.m_damage.m_damage = 99999f;
            hit.m_point = root.transform.position;
            root.ApplyDamage(hit, showDamageText: false, triggerEffects: true);
        }
    }
}
