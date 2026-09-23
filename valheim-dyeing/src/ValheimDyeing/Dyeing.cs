using System.Collections.Generic;
using UnityEngine;

namespace ValheimDyeing
{
    internal sealed class DyeJob
    {
        internal Dye Dye;
        internal string BaseItem;
        internal GameObject ToPrefab;
        internal ItemDrop ToDrop;
        internal ItemDrop Material;
        internal Recipe Recipe;
    }

    internal static class Dyeing
    {
        internal const string TubPrefab = "piece_goattech_dyetub";
        internal const string TubStationName = "$goattech_dyetub";
        internal const string SourcePiece = "piece_cauldron";

        internal static readonly string[] BaseItems =
        {
            "ArmorBronzeChest",
            "ArmorBronzeLegs",
            "HelmetBronze",
        };

        private static GameObject _holder;
        private static GameObject _tub;
        private static CraftingStation _station;

        private static readonly List<GameObject> Minted = new List<GameObject>();
        private static readonly List<Recipe> Recipes = new List<Recipe>();
        private static readonly Dictionary<Recipe, DyeJob> Jobs = new Dictionary<Recipe, DyeJob>();
        private static readonly Dictionary<string, GameObject> ByName = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, string> BaseOf = new Dictionary<string, string>();

        internal static bool Built { get; private set; }

        internal static GameObject Tub => _tub;

        internal static DyeJob JobFor(Recipe recipe)
        {
            if (recipe == null)
            {
                return null;
            }

            return Jobs.TryGetValue(recipe, out DyeJob job) ? job : null;
        }

        internal static string DyedName(string baseName, Dye dye)
        {
            return baseName + dye.Suffix;
        }

        internal static void Build()
        {
            ZNetScene scene = ZNetScene.instance;
            ObjectDB db = ObjectDB.instance;
            if (scene == null || db == null || db.m_items.Count == 0)
            {
                return;
            }

            if (Built)
            {
                Attach(scene, db);
                return;
            }

            _holder = new GameObject("ValheimDyeing_Prefabs");
            _holder.SetActive(false);
            Object.DontDestroyOnLoad(_holder);

            Names.Register();

            if (!BuildTub(scene))
            {
                Plugin.Log.LogError(
                    $"Could not build the dyeing tub: there is no prefab named {SourcePiece}. " +
                    "Nothing is added this session.");
                return;
            }

            foreach (string baseName in BaseItems)
            {
                GameObject plain = db.GetItemPrefab(baseName);
                if (plain == null)
                {
                    Plugin.Log.LogWarning($"There is no item prefab named {baseName}, skipping it.");
                    continue;
                }

                foreach (Dye dye in Dye.All)
                {
                    BuildDyed(plain, baseName, dye);
                }
            }

            MintRecipes(db);
            Built = true;
            Attach(scene, db);

            Plugin.Log.LogInfo(
                $"Minted {Minted.Count} prefab(s) and {Jobs.Count} dye recipe(s): " +
                $"{BaseItems.Length} armor piece(s) in {Dye.All.Length} colour(s).");

            Refresh();
        }

        private static void Attach(ZNetScene scene, ObjectDB db)
        {
            Names.Register();

            foreach (GameObject prefab in Minted)
            {
                int hash = prefab.name.GetStableHashCode();
                if (!scene.m_namedPrefabs.ContainsKey(hash))
                {
                    scene.m_prefabs.Add(prefab);
                    scene.m_namedPrefabs[hash] = prefab;
                }

                if (prefab.GetComponent<ItemDrop>() != null && !db.m_items.Contains(prefab))
                {
                    db.m_items.Add(prefab);
                }
            }

            db.UpdateRegisters();

            foreach (Recipe recipe in Recipes)
            {
                if (!db.m_recipes.Contains(recipe))
                {
                    db.m_recipes.Add(recipe);
                }
            }

            AddToBuildMenu(db);
        }

        private static void AddToBuildMenu(ObjectDB db)
        {
            if (_tub == null)
            {
                return;
            }

            GameObject hammer = db.GetItemPrefab("Hammer");
            ItemDrop drop = hammer != null ? hammer.GetComponent<ItemDrop>() : null;
            PieceTable table = drop?.m_itemData.m_shared.m_buildPieces;

            if (table == null)
            {
                Plugin.Log.LogWarning("The hammer has no piece table, so the dyeing tub is not buildable.");
                return;
            }

            if (!table.m_pieces.Contains(_tub))
            {
                table.m_pieces.Add(_tub);
            }
        }

        private static bool BuildTub(ZNetScene scene)
        {
            GameObject source = Find(scene, SourcePiece);
            if (source == null)
            {
                return false;
            }

            _tub = Object.Instantiate(source, _holder.transform);
            _tub.name = TubPrefab;

            Piece piece = _tub.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name = TubStationName;
                piece.m_description = "$goattech_dyetub_description";
                piece.m_category = Piece.PieceCategory.Crafting;
                piece.m_craftingStation = FindStation(scene, "piece_workbench");
                piece.m_resources = new[]
                {
                    Requirement(scene, "Bronze", ConfigSync.Current.TubBronze),
                    Requirement(scene, "FineWood", ConfigSync.Current.TubFineWood),
                };
            }

            _station = _tub.GetComponent<CraftingStation>();
            if (_station != null)
            {
                _station.m_name = TubStationName;
                _station.m_craftRequireFire = ConfigSync.Current.TubRequiresFire;
                _station.m_craftRequireRoof = false;
                _station.m_upgrader = false;
                _station.m_canRepair = false;
                _station.m_hasCraftTab = true;
            }

            Minted.Add(_tub);
            ByName[_tub.name] = _tub;
            return true;
        }

        private static void BuildDyed(GameObject plain, string baseName, Dye dye)
        {
            string name = DyedName(baseName, dye);
            GameObject clone = Object.Instantiate(plain, _holder.transform);
            clone.name = name;

            ItemDrop drop = clone.GetComponent<ItemDrop>();
            ItemDrop plainDrop = plain.GetComponent<ItemDrop>();
            if (drop == null || plainDrop == null)
            {
                Object.Destroy(clone);
                Plugin.Log.LogWarning($"{baseName} has no ItemDrop, skipping {name}.");
                return;
            }

            drop.m_itemData.m_dropPrefab = clone;
            drop.m_itemData.m_shared.m_name = dye.NameToken + " " + plainDrop.m_itemData.m_shared.m_name;

            Tint.Apply(clone, dye);

            Minted.Add(clone);
            ByName[name] = clone;
            BaseOf[name] = baseName;
        }

        private static void MintRecipes(ObjectDB db)
        {
            foreach (string baseName in BaseItems)
            {
                GameObject plain = db.GetItemPrefab(baseName);
                ItemDrop plainDrop = plain != null ? plain.GetComponent<ItemDrop>() : null;
                if (plainDrop == null)
                {
                    continue;
                }

                Recipe vanilla = VanillaRecipeFor(db, plainDrop);

                foreach (Dye dye in Dye.All)
                {
                    if (!ByName.TryGetValue(DyedName(baseName, dye), out GameObject dyed))
                    {
                        continue;
                    }

                    MintDyeRecipe(db, baseName, plainDrop, dyed, dye, vanilla);
                    MintUpgradeRecipe(vanilla, dyed);
                }
            }
        }

        private static void MintDyeRecipe(ObjectDB db, string baseName, ItemDrop plainDrop, GameObject dyed,
            Dye dye, Recipe vanilla)
        {
            ItemDrop dyedDrop = dyed.GetComponent<ItemDrop>();
            ItemDrop material = FindItem(db, dye.Material);

            if (dyedDrop == null)
            {
                return;
            }

            if (material == null)
            {
                Plugin.Log.LogWarning(
                    $"There is no item prefab named {dye.Material}, so {dye.Key} gets no recipe.");
                return;
            }

            Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = "Recipe_GoatTechDye_" + dyed.name;
            recipe.m_item = dyedDrop;
            recipe.m_amount = 1;
            recipe.m_enabled = true;
            recipe.m_minStationLevel = 1;
            recipe.m_craftingStation = _station;
            recipe.m_repairStation = vanilla == null ? null : vanilla.m_repairStation ?? vanilla.m_craftingStation;
            recipe.m_requireOnlyOneIngredient = false;
            recipe.m_resources = new[]
            {
                Requirement(plainDrop, 1),
                Requirement(material, ConfigSync.Current.AmountFor(dye.Key)),
            };

            Recipes.Add(recipe);
            Jobs[recipe] = new DyeJob
            {
                Dye = dye,
                BaseItem = baseName,
                ToPrefab = dyed,
                ToDrop = dyedDrop,
                Material = material,
                Recipe = recipe,
            };
        }

        private static void MintUpgradeRecipe(Recipe vanilla, GameObject dyed)
        {
            ItemDrop dyedDrop = dyed.GetComponent<ItemDrop>();
            if (vanilla == null || dyedDrop == null)
            {
                return;
            }

            Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = "Recipe_GoatTechDyeUpgrade_" + dyed.name;
            recipe.m_item = dyedDrop;
            recipe.m_amount = vanilla.m_amount;
            recipe.m_enabled = true;
            recipe.m_noCraftOnlyUpgrade = true;
            recipe.m_minStationLevel = vanilla.m_minStationLevel;
            recipe.m_craftingStation = vanilla.m_craftingStation;
            recipe.m_repairStation = vanilla.m_repairStation;
            recipe.m_requireOnlyOneIngredient = vanilla.m_requireOnlyOneIngredient;
            recipe.m_qualityResultAmountMultiplier = vanilla.m_qualityResultAmountMultiplier;
            recipe.m_resources = vanilla.m_resources;

            Recipes.Add(recipe);
        }

        private static Recipe VanillaRecipeFor(ObjectDB db, ItemDrop drop)
        {
            foreach (Recipe recipe in db.m_recipes)
            {
                if (recipe != null && recipe.m_item == drop && !recipe.m_noCraftOnlyUpgrade)
                {
                    return recipe;
                }
            }

            Plugin.Log.LogWarning(
                $"No vanilla recipe makes {drop.gameObject.name}, so its dyed versions cannot be upgraded.");
            return null;
        }

        internal static void Refresh()
        {
            if (!Built)
            {
                return;
            }

            Settings settings = ConfigSync.Current;

            foreach (Dye dye in Dye.All)
            {
                dye.ApplyOverride(settings.ColourFor(dye.Key));
            }

            Tint.Retune();

            foreach (KeyValuePair<Recipe, DyeJob> entry in Jobs)
            {
                DyeJob job = entry.Value;
                entry.Key.m_enabled = settings.IsEnabled(job.Dye.Key);

                foreach (Piece.Requirement requirement in entry.Key.m_resources)
                {
                    if (requirement.m_resItem == job.Material)
                    {
                        requirement.m_amount = settings.AmountFor(job.Dye.Key);
                    }
                }
            }

            if (_station != null)
            {
                _station.m_craftRequireFire = settings.TubRequiresFire;
            }

            Piece piece = _tub != null ? _tub.GetComponent<Piece>() : null;
            if (piece?.m_resources != null)
            {
                foreach (Piece.Requirement requirement in piece.m_resources)
                {
                    if (requirement.m_resItem == null)
                    {
                        continue;
                    }

                    if (requirement.m_resItem.gameObject.name == "Bronze")
                    {
                        requirement.m_amount = settings.TubBronze;
                    }
                    else if (requirement.m_resItem.gameObject.name == "FineWood")
                    {
                        requirement.m_amount = settings.TubFineWood;
                    }
                }
            }
        }

        internal static ItemDrop.ItemData FindSource(Inventory inventory, DyeJob job)
        {
            if (inventory == null || job == null)
            {
                return null;
            }

            bool needsFull = ConfigSync.Current.RequireFullDurability;
            ItemDrop.ItemData best = null;

            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (!IsSourceFor(item, job))
                {
                    continue;
                }

                if (needsFull && Worn(item))
                {
                    continue;
                }

                if (best == null || item.m_quality > best.m_quality)
                {
                    best = item;
                }
            }

            return best;
        }

        private static bool IsSourceFor(ItemDrop.ItemData item, DyeJob job)
        {
            if (item?.m_dropPrefab == null)
            {
                return false;
            }

            string name = item.m_dropPrefab.name;
            if (name == job.ToPrefab.name)
            {
                return false;
            }

            return name == job.BaseItem || (BaseOf.TryGetValue(name, out string owner) && owner == job.BaseItem);
        }

        internal static ItemDrop.ItemData.SharedData FreshShared(GameObject prefab)
        {
            if (prefab == null || _holder == null)
            {
                return null;
            }

            GameObject scratch = Object.Instantiate(prefab, _holder.transform);
            ItemDrop drop = scratch.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData shared = drop != null ? drop.m_itemData.m_shared : null;
            Object.Destroy(scratch);
            return shared;
        }

        internal static bool Worn(ItemDrop.ItemData item)
        {
            return item.m_shared.m_useDurability && item.m_durability < item.GetMaxDurability();
        }

        internal static bool IsDyed(ItemDrop.ItemData item)
        {
            return item?.m_dropPrefab != null && BaseOf.ContainsKey(item.m_dropPrefab.name);
        }


        private static GameObject Find(ZNetScene scene, string name)
        {
            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab != null && prefab.name == name)
                {
                    return prefab;
                }
            }

            return null;
        }

        private static CraftingStation FindStation(ZNetScene scene, string prefabName)
        {
            GameObject prefab = Find(scene, prefabName);
            return prefab != null ? prefab.GetComponent<CraftingStation>() : null;
        }

        private static ItemDrop FindItem(ObjectDB db, string name)
        {
            GameObject prefab = db.GetItemPrefab(name);
            return prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        }

        private static Piece.Requirement Requirement(ZNetScene scene, string itemName, int amount)
        {
            GameObject prefab = Find(scene, itemName);
            return Requirement(prefab != null ? prefab.GetComponent<ItemDrop>() : null, amount);
        }

        private static Piece.Requirement Requirement(ItemDrop drop, int amount)
        {
            return new Piece.Requirement
            {
                m_resItem = drop,
                m_amount = amount,
                m_amountPerLevel = 0,
                m_upgraderResource = false,
                m_recover = true,
            };
        }
    }
}
