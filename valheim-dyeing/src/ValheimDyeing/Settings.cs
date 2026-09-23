using System.Collections.Generic;

namespace ValheimDyeing
{
    internal sealed class Settings
    {
        internal bool RequireFullDurability;
        internal int TubBronze;
        internal int TubFineWood;
        internal bool TubRequiresFire;

        internal readonly Dictionary<string, bool> DyeEnabled = new Dictionary<string, bool>();
        internal readonly Dictionary<string, int> DyeAmount = new Dictionary<string, int>();
        internal readonly Dictionary<string, string> DyeColour = new Dictionary<string, string>();

        internal static Settings Vanilla
        {
            get
            {
                Settings settings = new Settings
                {
                    RequireFullDurability = true,
                    TubBronze = 5,
                    TubFineWood = 10,
                    TubRequiresFire = false,
                };

                foreach (Dye dye in Dye.All)
                {
                    settings.DyeEnabled[dye.Key] = true;
                    settings.DyeAmount[dye.Key] = dye.Amount;
                    settings.DyeColour[dye.Key] = dye.Describe();
                }

                return settings;
            }
        }

        internal static Settings FromConfig()
        {
            Settings settings = new Settings
            {
                RequireFullDurability = ModConfig.RequireFullDurability.Value,
                TubBronze = ModConfig.TubBronze.Value,
                TubFineWood = ModConfig.TubFineWood.Value,
                TubRequiresFire = ModConfig.TubRequiresFire.Value,
            };

            foreach (Dye dye in Dye.All)
            {
                settings.DyeEnabled[dye.Key] = ModConfig.DyeEnabled[dye.Key].Value;
                settings.DyeAmount[dye.Key] = ModConfig.DyeAmount[dye.Key].Value;
                settings.DyeColour[dye.Key] = ModConfig.DyeColour[dye.Key].Value;
            }

            return settings;
        }

        internal bool IsEnabled(string key)
        {
            return DyeEnabled.TryGetValue(key, out bool enabled) && enabled;
        }

        internal int AmountFor(string key)
        {
            return DyeAmount.TryGetValue(key, out int amount) ? amount : 5;
        }

        internal string ColourFor(string key)
        {
            return DyeColour.TryGetValue(key, out string colour) ? colour : null;
        }

        internal void Write(ZPackage package)
        {
            package.Write(RequireFullDurability);
            package.Write(TubBronze);
            package.Write(TubFineWood);
            package.Write(TubRequiresFire);

            package.Write(Dye.All.Length);
            foreach (Dye dye in Dye.All)
            {
                package.Write(dye.Key);
                package.Write(IsEnabled(dye.Key));
                package.Write(AmountFor(dye.Key));
                package.Write(ColourFor(dye.Key) ?? dye.Describe());
            }
        }

        internal static Settings Read(ZPackage package)
        {
            Settings settings = new Settings
            {
                RequireFullDurability = package.ReadBool(),
                TubBronze = package.ReadInt(),
                TubFineWood = package.ReadInt(),
                TubRequiresFire = package.ReadBool(),
            };

            int count = package.ReadInt();
            for (int i = 0; i < count; i++)
            {
                string key = package.ReadString();
                settings.DyeEnabled[key] = package.ReadBool();
                settings.DyeAmount[key] = package.ReadInt();
                settings.DyeColour[key] = package.ReadString();
            }

            return settings;
        }

        public override string ToString()
        {
            int offered = 0;
            foreach (Dye dye in Dye.All)
            {
                if (IsEnabled(dye.Key))
                {
                    offered++;
                }
            }

            return $"{offered} of {Dye.All.Length} colours offered, " +
                   $"full durability {(RequireFullDurability ? "required" : "not required")}";
        }
    }
}
