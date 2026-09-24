using UnityEngine;

namespace ValheimRebalanced
{
    internal static class BurntWood
    {
        private const string CoalPrefab = "Coal";

        private static bool _grouping;
        private static int _groupWood;
        private static int _groupCoal;

        internal static void BeginGroup()
        {
            _grouping = true;
            _groupWood = 0;
            _groupCoal = 0;
        }

        internal static void EndGroup(string source)
        {
            if (_groupWood > 0)
            {
                Plugin.Log.LogInfo($"Burnt wood: {source} dropped {_groupCoal} coal for {_groupWood} wood.");
            }

            _grouping = false;
            _groupWood = 0;
            _groupCoal = 0;
        }

        internal static int Coal(GameObject dropPrefab, GameObject result, int count)
        {
            int divisor = ConfigSync.Current.BurntWoodCoalDivisor;
            if (divisor <= 1 || result == null || result == dropPrefab || count <= 0 ||
                Utils.GetPrefabName(result.name) != CoalPrefab)
            {
                return count;
            }

            if (!_grouping)
            {
                int coal = Ceil(count, divisor);
                Plugin.Log.LogInfo($"Burnt wood: a piece dropped {coal} coal for {count} wood.");
                return coal;
            }

            int before = Ceil(_groupWood, divisor);
            _groupWood += count;
            int added = Ceil(_groupWood, divisor) - before;
            _groupCoal += added;
            return added;
        }

        private static int Ceil(int count, int divisor)
        {
            return (count + divisor - 1) / divisor;
        }
    }
}
