using UnityEngine;

namespace ValheimDyeing
{
    internal sealed class Dye
    {
        internal string Key;
        internal string Material;
        internal int Amount;
        internal string NameToken;
        internal string EnglishName;
        internal float HueShift;
        internal float Saturation;
        internal float Value;
        internal Color Tint;

        internal string Suffix => "_Dye" + Key;

        internal static readonly Dye[] All =
        {
            new Dye
            {
                Key = "Red",
                Material = "Raspberry",
                Amount = 5,
                NameToken = "$goattech_dye_red",
                EnglishName = "Red",
                HueShift = 0.01f,
                Saturation = 0.00f,
                Value = -0.12f,
                Tint = new Color(0.62f, 0.20f, 0.17f),
            },
            new Dye
            {
                Key = "Blue",
                Material = "Blueberries",
                Amount = 5,
                NameToken = "$goattech_dye_blue",
                EnglishName = "Blue",
                HueShift = 0.52f,
                Saturation = 0.10f,
                Value = -0.10f,
                Tint = new Color(0.30f, 0.45f, 1.00f),
            },
            new Dye
            {
                Key = "Black",
                Material = "Coal",
                Amount = 5,
                NameToken = "$goattech_dye_black",
                EnglishName = "Black",
                HueShift = 0.00f,
                Saturation = -0.95f,
                Value = -0.12f,
                Tint = new Color(0.063f, 0.063f, 0.078f),
            },
            new Dye
            {
                Key = "Green",
                Material = "Guck",
                Amount = 5,
                NameToken = "$goattech_dye_green",
                EnglishName = "Green",
                HueShift = 0.25f,
                Saturation = -0.25f,
                Value = -0.20f,
                Tint = new Color(0.275f, 0.380f, 0.247f),
            },
            new Dye
            {
                Key = "Yellow",
                Material = "Dandelion",
                Amount = 5,
                NameToken = "$goattech_dye_yellow",
                EnglishName = "Yellow",
                HueShift = 0.07f,
                Saturation = 0.05f,
                Value = -0.10f,
                Tint = new Color(0.70f, 0.60f, 0.22f),
            },
            new Dye
            {
                Key = "Purple",
                Material = "Thistle",
                Amount = 5,
                NameToken = "$goattech_dye_purple",
                EnglishName = "Purple",
                HueShift = 0.70f,
                Saturation = -0.15f,
                Value = -0.18f,
                Tint = new Color(0.431f, 0.306f, 0.522f),
            },
            new Dye
            {
                Key = "Orange",
                Material = "Carrot",
                Amount = 5,
                NameToken = "$goattech_dye_orange",
                EnglishName = "Orange",
                HueShift = 0.04f,
                Saturation = 0.05f,
                Value = -0.12f,
                Tint = new Color(0.68f, 0.38f, 0.14f),
            },
        };

        internal static Dye Find(string key)
        {
            foreach (Dye dye in All)
            {
                if (dye.Key == key)
                {
                    return dye;
                }
            }

            return null;
        }

        internal void ApplyOverride(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            string[] parts = text.Split(',');
            if (parts.Length < 4)
            {
                Plugin.Log.LogWarning(
                    $"Dye {Key}: could not read \"{text}\". Expected hue,saturation,value,#RRGGBB. Keeping the built in colour.");
                return;
            }

            if (!float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float hue) ||
                !float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float saturation) ||
                !float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float value) ||
                !ColorUtility.TryParseHtmlString(parts[3].Trim(), out Color tint))
            {
                Plugin.Log.LogWarning(
                    $"Dye {Key}: could not read \"{text}\". Expected hue,saturation,value,#RRGGBB. Keeping the built in colour.");
                return;
            }

            HueShift = Mathf.Repeat(hue, 1f);
            Saturation = Mathf.Clamp(saturation, -1f, 1f);
            Value = Mathf.Clamp(value, -1f, 1f);
            Tint = tint;
        }

        internal string Describe()
        {
            return $"{HueShift.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}," +
                   $"{Saturation.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}," +
                   $"{Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}," +
                   $"#{ColorUtility.ToHtmlStringRGB(Tint)}";
        }
    }
}
