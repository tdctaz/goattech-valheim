using System.Collections.Generic;
using UnityEngine;

namespace ValheimRebalanced
{
    internal static class StaffOfFracturing
    {
        private const string PrefabName = "StaffClusterbomb";

        private static readonly Dictionary<ItemDrop.ItemData.SharedData, Original> Originals =
            new Dictionary<ItemDrop.ItemData.SharedData, Original>();

        private sealed class Original
        {
            internal HitData.DamageTypes Damages;
            internal HitData.DamageTypes DamagesPerLevel;
        }

        internal static void Refresh()
        {
            ItemDrop.ItemData.SharedData shared = Items.Shared(ObjectDB.instance, PrefabName, "the Staff of Fracturing");
            if (shared == null)
            {
                return;
            }

            if (!Originals.TryGetValue(shared, out Original original))
            {
                original = new Original { Damages = shared.m_damages, DamagesPerLevel = shared.m_damagesPerLevel };
                Originals[shared] = original;
            }

            float multiplier = ConfigSync.Current.StaffOfFracturingFire;
            shared.m_damages = Scale(original.Damages, multiplier);
            shared.m_damagesPerLevel = Scale(original.DamagesPerLevel, multiplier);
        }

        private static HitData.DamageTypes Scale(HitData.DamageTypes damages, float multiplier)
        {
            damages.m_fire *= multiplier;
            return damages;
        }
    }
}
