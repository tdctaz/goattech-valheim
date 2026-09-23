namespace ValheimDyeing
{
    internal static class Names
    {
        private static bool _registered;

        internal static void Register()
        {
            if (_registered || Localization.instance == null)
            {
                return;
            }

            Add("goattech_dyetub", "Dyeing Tub");
            Add("goattech_dyetub_description",
                "A tub of hot dye. Takes a piece of bronze armor and a handful of something colourful, " +
                "and gives the armor back in that colour. The armor has to be whole to go in.");

            Add("goattech_dye_needs_whole", "Must be at full durability");

            foreach (Dye dye in Dye.All)
            {
                Add(dye.NameToken.Substring(1), dye.EnglishName);
            }

            _registered = true;
        }

        internal static void Forget()
        {
            _registered = false;
        }

        private static void Add(string key, string text)
        {
            Localization.instance.AddWord(key, text);
        }
    }
}
