using System.Collections.Generic;

namespace ValheimCreatures
{
    internal static class RaidMessages
    {
        private static readonly string[] Fallback =
        {
            "More of them are coming",
            "Their numbers are thinning",
            "Only a few remain",
        };

        private static readonly string[] Smell =
        {
            "The swamp stench lingers",
            "The stench is thinning",
            "Only a faint rot remains",
        };

        private static readonly Dictionary<string, string[]> ByRaid = new Dictionary<string, string[]>
        {
            ["army_eikthyr"] = new[]
            {
                "The creatures still answer Eikthyr's call",
                "Fewer creatures heed Eikthyr's call",
                "Eikthyr's call is almost spent",
            },
            ["army_theelder"] = new[]
            {
                "The forest is still moving...",
                "The forest grows still in places",
                "Only a few trees still stir",
            },
            ["foresttrolls"] = new[]
            {
                "The ground still shakes",
                "The tremors grow weaker",
                "Only a faint rumble remains",
            },
            ["army_bonemass"] = Smell,
            ["blobs"] = Smell,
            ["skeletons"] = new[]
            {
                "More bones rattle in the dark",
                "The rattling grows quieter",
                "The last skeletons are tiring",
            },
            ["surtlings"] = new[]
            {
                "The sulfur still hangs heavy",
                "The sulfur is thinning",
                "Only a whiff of sulfur remains",
            },
            ["ghosts"] = new[]
            {
                "The chill does not leave you...",
                "The chill is fading",
                "Only a faint whisper remains...",
            },
            ["wolves"] = new[]
            {
                "The pack still hunts you...",
                "Fewer howls answer the pack",
                "The pack is nearly broken",
            },
            ["bats"] = new[]
            {
                "The cauldron still bubbles",
                "The cauldron is cooling",
                "The cauldron barely simmers",
            },
            ["army_moder"] = new[]
            {
                "The cold wind still howls",
                "The wind is losing its bite",
                "Only a chill breeze remains",
            },
            ["army_goblin"] = new[]
            {
                "The horde presses on!",
                "The horde is wavering",
                "The horde is breaking",
            },
            ["army_gjall"] = new[]
            {
                "Gjall is still up",
                "Gjall is winding down",
                "Gjall is nearly done",
            },
            ["army_seekers"] = new[]
            {
                "They are still seeking you",
                "Fewer of them seek you now",
                "The search is nearly over",
            },
            ["army_charred"] = new[]
            {
                "The army marches on",
                "The army's ranks are thinning",
                "The army is faltering",
            },
            ["army_charredspawners"] = new[]
            {
                "More dead answer the summons",
                "The summons grows weak",
                "The dead begin to settle",
            },
            ["hildirboss1"] = new[]
            {
                "She's still on your tail!",
                "She's losing the scent",
                "She's running out of friends",
            },
            ["hildirboss2"] = new[]
            {
                "The chills won't leave you...",
                "The chills are easing",
                "Almost time to chill out",
            },
            ["hildirboss3"] = new[]
            {
                "The bros keep coming, man",
                "Fewer bros now, man",
                "Last of the bros, man",
            },
            ["army_elakingar"] = new[]
            {
                "More emerge from below...",
                "Fewer emerge from below",
                "The burrows are nearly empty",
            },
            ["army_jotuns"] = new[]
            {
                "The Jotun keep coming",
                "The Jotun are faltering",
                "The Jotun are about to withdraw",
            },
        };

        internal static int Stage(int remaining, int total)
        {
            if (remaining <= 1)
            {
                return 2;
            }

            return remaining * 2 <= total ? 1 : 0;
        }

        internal static string For(string raid, int stage)
        {
            if (!ByRaid.TryGetValue(raid ?? "", out string[] lines))
            {
                lines = Fallback;
            }

            return lines[UnityEngine.Mathf.Clamp(stage, 0, lines.Length - 1)];
        }
    }
}
