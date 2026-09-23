using UnityEngine;

namespace ValheimCreatures
{
    internal enum StarColor
    {
        None = 0,
        Magenta = 1,
        Red = 2,
        Green = 3,
        Cyan = 4,
        White = 5,
        Blue = 6,
    }

    internal static class StarColors
    {
        internal const int Count = 7;

        internal static readonly StarColor[] Creature =
        {
            StarColor.Magenta, StarColor.Red, StarColor.Green, StarColor.Cyan, StarColor.White, StarColor.Blue,
        };

        internal static readonly StarColor[] Boss =
        {
            StarColor.Magenta, StarColor.Red, StarColor.Green, StarColor.Cyan, StarColor.White, StarColor.Blue,
        };

        internal static Color Tint(StarColor color)
        {
            switch (color)
            {
                case StarColor.Magenta: return new Color(1f, 0.3f, 1f);
                case StarColor.Red: return new Color(1f, 0.25f, 0.2f);
                case StarColor.Green: return new Color(0.35f, 1f, 0.3f);
                case StarColor.Cyan: return new Color(0.3f, 1f, 1f);
                case StarColor.White: return new Color(1f, 1f, 1f);
                case StarColor.Blue: return new Color(0.35f, 0.5f, 1f);
                default: return new Color(1f, 0.85f, 0.2f);
            }
        }

        internal static string Describe(StarColor color, bool boss)
        {
            switch (color)
            {
                case StarColor.Magenta: return "Fast";
                case StarColor.Red: return "Aggressive";
                case StarColor.Green: return "Regenerating";
                case StarColor.Cyan: return boss ? "Summoning" : "Curious";
                case StarColor.White: return "Splitting";
                case StarColor.Blue: return boss ? "Shielded" : "Armored";
                default: return "Normal";
            }
        }
    }
}
