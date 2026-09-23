using UnityEngine;

namespace ValheimRebalanced
{
    internal static class Trollstav
    {
        private const string PrefabName = "StaffRedTroll";
        private const string BoundMessage = "The bond is spoken for. Another troll will not heed you";

        private static ItemDrop.ItemData.SharedData _shared;
        private static SpawnAbility _summon;
        private static bool _fatalCast;

        internal static void Refresh()
        {
            ItemDrop.ItemData.SharedData shared = Items.Shared(ObjectDB.instance, PrefabName, "the Trollstav");
            if (shared == null)
            {
                return;
            }

            _shared = shared;
            GameObject projectile = shared.m_attack.m_attackProjectile;
            _summon = projectile != null ? projectile.GetComponent<SpawnAbility>() : null;

            Balance balance = ConfigSync.Current;
            shared.m_attack.m_attackHealth = balance.TrollstavHealthCost;

            if (_summon != null)
            {
                _summon.m_maxSpawned = balance.TrollstavMaxSummons;
            }
        }

        internal static bool CastAllowed(Attack attack, Humanoid character, ItemDrop.ItemData weapon)
        {
            if (!IsSummonCast(attack, weapon) || _summon == null || WillSpawn(_summon))
            {
                return true;
            }

            Player player = character as Player;
            if (player == null || player != Player.m_localPlayer)
            {
                return true;
            }

            player.Message(MessageHud.MessageType.Center, BoundMessage);
            return false;
        }

        internal static void OnAttackStarted(Attack attack, Humanoid character, ItemDrop.ItemData weapon)
        {
            if (!IsSummonCast(attack, weapon))
            {
                return;
            }

            Player player = character as Player;
            if (player == null || player != Player.m_localPlayer)
            {
                return;
            }

            Balance balance = ConfigSync.Current;
            float health = player.GetHealth();
            float cost = Mathf.Max(balance.TrollstavHealthCost, health * attack.m_attackHealthPercentage / 100f);
            attack.m_attackHealth = cost;
            attack.m_attackHealthPercentage = 0f;

            if (!balance.TrollstavLethalHealthCost)
            {
                _fatalCast = false;
                return;
            }

            cost -= cost * 0.33f * player.GetSkillFactor(_shared.m_skillType);
            _fatalCast = cost >= health;
        }

        internal static void OnCastReleased(SpawnAbility ability, Character owner, ItemDrop.ItemData item)
        {
            if (!_fatalCast)
            {
                return;
            }

            _fatalCast = false;

            if (!IsTrollstav(item) || !WillSpawn(ability))
            {
                return;
            }

            Player player = owner as Player;
            if (player == null || player != Player.m_localPlayer || player.IsDead())
            {
                return;
            }

            HitData hit = new HitData();
            hit.m_damage.m_damage = Mathf.Max(player.GetHealth(), 1f);
            hit.m_point = player.GetCenterPoint();
            hit.m_dir = Vector3.up;
            hit.m_blockable = false;
            hit.m_dodgeable = false;
            hit.m_hitType = HitData.HitType.Self;
            player.Damage(hit);
        }

        private static bool WillSpawn(SpawnAbility ability)
        {
            GameObject prefab = ability.m_spawnPrefab != null && ability.m_spawnPrefab.Length > 0
                ? ability.m_spawnPrefab[0]
                : null;
            if (prefab == null)
            {
                return false;
            }

            return ability.m_maxSpawned <= 0 ||
                   SpawnSystem.GetNrOfInstances(prefab, ability.transform.position, 0f) < ability.m_maxSpawned;
        }

        private static bool IsSummonCast(Attack attack, ItemDrop.ItemData weapon)
        {
            return IsTrollstav(weapon) && attack.m_attackAnimation == _shared.m_attack.m_attackAnimation;
        }

        private static bool IsTrollstav(ItemDrop.ItemData item)
        {
            return _shared != null && item != null && item.m_shared != null &&
                   item.m_shared.m_name == _shared.m_name;
        }
    }
}
