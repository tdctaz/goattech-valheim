using UnityEngine;

namespace ValheimCreatures
{
    internal static class Splitting
    {
        private const int Pieces = 2;
        private const float SpreadRadius = 1.5f;

        internal static void OnDeath(Character character)
        {
            CreatureTraits traits = CreatureTraits.Of(character);
            if (traits == null || !traits.Has(StarColor.White) || !ConfigSync.Current.Enabled ||
                character.m_nview == null || !character.m_nview.IsValid() || !character.m_nview.IsOwner() ||
                character.IsTamed())
            {
                return;
            }

            GameObject prefab = ZNetScene.instance != null
                ? ZNetScene.instance.GetPrefab(Utils.GetPrefabName(character.gameObject))
                : null;
            if (prefab == null)
            {
                return;
            }

            if (traits.Boss)
            {
                traits.DropsSuppressed = true;
                character.GetComponent<CharacterDrop>()?.SetDropsEnabled(false);
                for (int i = 0; i < Pieces; i++)
                {
                    CreatureTraits.SpawningSplitBoss = true;
                    try
                    {
                        Character child = Spawn(prefab, character, i);
#if DEBUG_TOOLS
                        if (child != null && TestCommands.IsTracked(character))
                        {
                            TestCommands.Track(child);
                        }
#endif
                    }
                    finally
                    {
                        CreatureTraits.SpawningSplitBoss = false;
                    }
                }

                return;
            }

            int stars = character.GetLevel() - 1;
            if (stars < 1)
            {
                return;
            }

            int childStars = stars / 2;
            for (int i = 0; i < Pieces; i++)
            {
                Character child = Spawn(prefab, character, i);
#if DEBUG_TOOLS
                if (child != null && TestCommands.IsTracked(character))
                {
                    TestCommands.Track(child);
                }
#endif

                if (child == null)
                {
                    continue;
                }

                child.SetLevel(childStars + 1);
                CreatureTraits.SetColor(child, childStars > 0 ? StarColor.White : StarColor.None);
            }
        }

        private static Character Spawn(GameObject prefab, Character parent, int index)
        {
            float angle = (index + Random.value * 0.5f) * Mathf.PI * 2f / Pieces;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * SpreadRadius;
            Vector3 position = parent.transform.position + offset + Vector3.up * 0.5f;

            GameObject spawned = Object.Instantiate(prefab, position, parent.transform.rotation);
            Character child = spawned.GetComponent<Character>();
            if (child == null)
            {
                return null;
            }

            BaseAI parentAI = parent.GetBaseAI();
            BaseAI childAI = child.GetBaseAI();
            if (parentAI != null && childAI != null)
            {
                childAI.SetPatrolPoint();
                if (parentAI.HuntPlayer())
                {
                    childAI.SetHuntPlayer(true);
                }

                if (parentAI.IsAlerted())
                {
                    childAI.Alert();
                }

                if (parentAI is MonsterAI parentMonster && childAI is MonsterAI childMonster)
                {
                    if (parentMonster.DespawnInDay())
                    {
                        childMonster.SetDespawnInDay(true);
                    }

                    if (parentMonster.IsEventCreature())
                    {
                        childMonster.SetEventCreature(true);
                    }
                }
            }

            return child;
        }
    }
}
