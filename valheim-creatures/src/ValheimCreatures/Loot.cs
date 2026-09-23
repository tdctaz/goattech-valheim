using UnityEngine;

namespace ValheimCreatures
{
    internal static class Loot
    {
        private const int VanillaMaxStars = 2;
        private const float PerExtraStar = 2f;

        internal static float LevelMultiplier(float baseValue, float stars)
        {
            if (!ConfigSync.Current.Enabled || stars <= VanillaMaxStars)
            {
                return Mathf.Pow(baseValue, stars);
            }

            float atVanillaMax = Mathf.Pow(baseValue, VanillaMaxStars);
            return atVanillaMax + PerExtraStar * (stars - VanillaMaxStars);
        }
    }
}
