using System.Collections.Generic;
using UnityEngine;

namespace ValheimCreatures
{
    internal static class Looks
    {
        private const int SetupsForFiveStars = 5;

        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        private static readonly Dictionary<string, int> VanillaSetups = new Dictionary<string, int>();
        private static readonly Dictionary<string, Color> Tints = new Dictionary<string, Color>();

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
                        AddTints(prefab, effects, vanilla);
                        extended++;
                    }
                }
            }

            if (extended > 0)
            {
                Plugin.Log.LogInfo($"Added size and tint for 3 to 5 stars to {extended} creature prefabs.");
            }
        }

        internal static void ApplyTint(LevelEffects effects, int level)
        {
            if (!effects.m_mainRender || !effects.m_character)
            {
                return;
            }

            string key = Utils.GetPrefabName(effects.m_character.gameObject) + level;
            if (Tints.TryGetValue(key, out Color tint) && LevelEffects.m_materials.TryGetValue(key, out Material material))
            {
                material.SetColor(ColorProperty, tint);
            }
        }

        internal static void TintRagdoll(Ragdoll ragdoll)
        {
            if (!ragdoll.m_mainModel || ragdoll.m_nview == null || !ragdoll.m_nview.IsValid())
            {
                return;
            }

            int level = ragdoll.m_nview.GetZDO().GetInt(ZDOVars.s_level, 1);
            if (Tints.TryGetValue(Utils.GetPrefabName(ragdoll.gameObject) + level, out Color tint))
            {
                ragdoll.m_mainModel.material.SetColor(ColorProperty, tint);
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
                effects.m_levelSetups.Add(new LevelEffects.LevelSetup
                {
                    m_scale = last.m_scale + ModConfig.ScalePerStar.Value * extra,
                    m_hue = hue,
                    m_saturation = Mathf.Clamp(saturation, -1f, 1f),
                    m_value = last.m_value * Brightness(extra, top),
                    m_setEmissiveColor = last.m_setEmissiveColor,
                    m_emissiveColor = last.m_emissiveColor,
                    m_enableObject = last.m_enableObject,
                });
            }

            return true;
        }

        private static void AddTints(GameObject prefab, LevelEffects effects, int vanilla)
        {
            List<Ragdoll> ragdolls = new List<Ragdoll>();
            Character character = prefab.GetComponent<Character>();
            if (character != null)
            {
                foreach (EffectList.EffectData effect in character.m_deathEffects.m_effectPrefabs)
                {
                    Ragdoll ragdoll = effect.m_prefab != null ? effect.m_prefab.GetComponent<Ragdoll>() : null;
                    if (ragdoll != null && ragdoll.m_mainModel != null)
                    {
                        ragdolls.Add(ragdoll);
                    }
                }
            }

            Material[] materials = effects.m_mainRender != null ? effects.m_mainRender.sharedMaterials : null;
            for (int index = 0; index < effects.m_levelSetups.Count; index++)
            {
                float brightness = index < vanilla ? 1f : Brightness(index - vanilla + 1, index == SetupsForFiveStars - 1);
                int level = index + 2;
                if (materials != null && materials.Length > 0)
                {
                    AddTint(prefab.name + level, materials[0], brightness);
                }

                foreach (Ragdoll ragdoll in ragdolls)
                {
                    AddTint(ragdoll.name + level, ragdoll.m_mainModel.sharedMaterial, brightness);
                }
            }
        }

        private static void AddTint(string key, Material material, float brightness)
        {
            if (material == null || !material.HasProperty(ColorProperty))
            {
                return;
            }

            Color color = material.GetColor(ColorProperty);
            Color linear = color.linear;
            Color tint = new Color(linear.r * brightness, linear.g * brightness, linear.b * brightness).gamma;
            tint.a = color.a;
            Tints[key] = tint;
        }

        private static float Brightness(int extra, bool top)
        {
            return Mathf.Max(0f, 1f + ModConfig.ValuePerStar.Value * extra + (top ? ModConfig.TopValue.Value : 0f));
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
