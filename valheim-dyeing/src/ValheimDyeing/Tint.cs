using System.Collections.Generic;
using UnityEngine;

namespace ValheimDyeing
{
    internal static class Tint
    {
        private static readonly int Hue = Shader.PropertyToID("_Hue");
        private static readonly int Saturation = Shader.PropertyToID("_Saturation");
        private static readonly int Value = Shader.PropertyToID("_Value");
        private static readonly int ArmorHue = Shader.PropertyToID("_ArmorHue");
        private static readonly int ArmorColor = Shader.PropertyToID("_ArmorColor");
        private static readonly int BaseColor = Shader.PropertyToID("_Color");
        private static readonly int MetalColor = Shader.PropertyToID("_MetalColor");

        private sealed class Tinted
        {
            internal Material Source;
            internal Material Copy;
            internal Dye Dye;
        }

        private static readonly Dictionary<string, Tinted> Cache = new Dictionary<string, Tinted>();

        internal static void Forget()
        {
            Cache.Clear();
        }

        internal static void Retune()
        {
            foreach (KeyValuePair<string, Tinted> entry in Cache)
            {
                if (entry.Value.Copy != null && entry.Value.Source != null)
                {
                    Paint(entry.Value.Source, entry.Value.Copy, entry.Value.Dye);
                }
            }
        }

        internal static void Apply(GameObject prefab, Dye dye)
        {
            if (prefab == null || dye == null)
            {
                return;
            }

            List<Renderer> renderers = new List<Renderer>();
            renderers.AddRange(prefab.GetComponentsInChildren<MeshRenderer>(true));
            renderers.AddRange(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true));

            foreach (Renderer renderer in renderers)
            {
                Material[] materials = renderer.sharedMaterials;
                bool touched = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    Material replacement = Dyed(materials[i], dye);
                    if (replacement != null)
                    {
                        materials[i] = replacement;
                        touched = true;
                    }
                }

                if (touched)
                {
                    renderer.sharedMaterials = materials;
                }
            }
        }

        private static Material Dyed(Material source, Dye dye)
        {
            if (source == null)
            {
                return null;
            }

            string key = source.name + "|" + dye.Key;
            if (Cache.TryGetValue(key, out Tinted cached) && cached.Copy != null)
            {
                return cached.Copy;
            }

            if (!Paintable(source))
            {
                return null;
            }

            Material copy = new Material(source) { name = source.name + "_" + dye.Key };
            Paint(source, copy, dye);
            Cache[key] = new Tinted { Source = source, Copy = copy, Dye = dye };
            return copy;
        }

        private static bool Paintable(Material material)
        {
            return material.HasProperty(ArmorHue) || material.HasProperty(ArmorColor) ||
                   material.HasProperty(Hue) || material.HasProperty(Saturation) ||
                   material.HasProperty(Value) || material.HasProperty(BaseColor);
        }

        private static void Paint(Material source, Material copy, Dye dye)
        {
            bool shifted = false;

            if (copy.HasProperty(ArmorHue))
            {
                copy.SetFloat(ArmorHue, dye.HueShift);
                shifted = true;
            }

            if (copy.HasProperty(ArmorColor))
            {
                copy.SetColor(ArmorColor, Multiply(source.GetColor(ArmorColor), Soften(dye)));
                shifted = true;
            }

            if (copy.HasProperty(Hue))
            {
                copy.SetFloat(Hue, dye.HueShift);
                shifted = true;
            }

            if (copy.HasProperty(Saturation))
            {
                copy.SetFloat(Saturation, dye.Saturation);
                shifted = true;
            }

            if (copy.HasProperty(Value))
            {
                copy.SetFloat(Value, dye.Value);
                shifted = true;
            }

            if (copy.HasProperty(MetalColor))
            {
                copy.SetColor(MetalColor, Multiply(source.GetColor(MetalColor), Soften(dye)));
            }

            if (!shifted && copy.HasProperty(BaseColor))
            {
                copy.SetColor(BaseColor, Multiply(source.GetColor(BaseColor), dye.Tint));
            }
        }

        private static Color Soften(Dye dye)
        {
            Color.RGBToHSV(dye.Tint, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Clamp01(s * 0.75f), Mathf.Clamp01(v * 0.5f + 0.5f));
        }

        private static Color Multiply(Color a, Color b)
        {
            return new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a);
        }
    }
}
