using System.Collections.Generic;
using UnityEngine;

namespace ValheimCreatures
{
    internal static class Looks
    {
        private const int SetupsForFiveStars = 5;

        private static readonly Dictionary<string, int> VanillaSetups = new Dictionary<string, int>();

        internal static void ExtendLevelSetups(ZNetScene scene)
        {
            int extended = 0;
            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab == null)
                {
                    continue;
                }

                foreach (LevelEffects effects in prefab.GetComponentsInChildren<LevelEffects>(true))
                {
                    int vanilla = effects.m_levelSetups.Count;
                    if (Extend(effects))
                    {
                        VanillaSetups[prefab.name] = vanilla;
                        extended++;
                    }
                }
            }

            if (extended > 0)
            {
                Plugin.Log.LogInfo($"Added size and tint for 3 to 5 stars to {extended} creature prefabs.");
            }
        }

        internal static void KeepVanillaSizeIndoors(LevelEffects effects, int level)
        {
            if (!Character.InInterior(effects.transform))
            {
                return;
            }

            ZNetView view = effects.GetComponentInParent<ZNetView>();
            if (view == null || !VanillaSetups.TryGetValue(Utils.GetPrefabName(view.gameObject), out int vanilla) ||
                vanilla == 0 || level - 1 <= vanilla)
            {
                return;
            }

            float scale = effects.m_levelSetups[vanilla - 1].m_scale;
            effects.transform.localScale = new Vector3(scale, scale, scale);
        }

        private static bool Extend(LevelEffects effects)
        {
            int count = effects.m_levelSetups.Count;
            if (count == 0 || count >= SetupsForFiveStars)
            {
                return false;
            }

            LevelEffects.LevelSetup last = effects.m_levelSetups[count - 1];
            List<float> taken = new List<float> { 0f };
            foreach (LevelEffects.LevelSetup setup in effects.m_levelSetups)
            {
                taken.Add(Mathf.Repeat(setup.m_hue, 1f));
            }

            for (int extra = 1; count < SetupsForFiveStars; count++, extra++)
            {
                float hue = FarthestHue(taken);
                taken.Add(Mathf.Repeat(hue, 1f));
                bool top = count == SetupsForFiveStars - 1;
                float saturation = last.m_saturation + ModConfig.SaturationPerStar.Value * extra +
                                   (top ? ModConfig.TopSaturation.Value : 0f);
                float value = last.m_value + ModConfig.ValuePerStar.Value * extra + (top ? ModConfig.TopValue.Value : 0f);
                effects.m_levelSetups.Add(new LevelEffects.LevelSetup
                {
                    m_scale = last.m_scale + ModConfig.ScalePerStar.Value * extra,
                    m_hue = hue,
                    m_saturation = Mathf.Clamp(saturation, -1f, 1f),
                    m_value = Mathf.Clamp(value, -1f, 1f),
                    m_setEmissiveColor = last.m_setEmissiveColor,
                    m_emissiveColor = last.m_emissiveColor,
                    m_enableObject = last.m_enableObject,
                });
            }

            return true;
        }

        private static float FarthestHue(List<float> hues)
        {
            List<float> taken = new List<float>(hues);
            taken.Sort();
            float best = 0f;
            float bestGap = -1f;
            for (int i = 0; i < taken.Count; i++)
            {
                float from = taken[i];
                float to = i + 1 < taken.Count ? taken[i + 1] : taken[0] + 1f;
                if (to - from > bestGap)
                {
                    bestGap = to - from;
                    best = from + (to - from) * 0.5f;
                }
            }

            return Mathf.Repeat(best + 0.5f, 1f) - 0.5f;
        }
    }
}
