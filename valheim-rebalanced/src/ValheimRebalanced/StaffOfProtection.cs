using System.Collections.Generic;
using UnityEngine;

namespace ValheimRebalanced
{
    internal static class StaffOfProtection
    {
        private const string StaffPrefab = "StaffShield";
        private const string BoltName = "staff_shield_projectile_rebalanced";
        private const string CastEffectName = "fx_shield_cast_rebalanced";
        private const float BoltSpeed = 30f;
        private const float BoltRange = 20f;
        private const float BoltRayRadius = 0.4f;
        private const float BoltScale = 0.5f;
        private const string AimAnimation = "staff_fireball";
        private const int AimChainLevels = 2;
        private const float AimHeight = 1.2f;

        private static readonly int BoltHash = BoltName.GetStableHashCode();

        private static GameObject _holder;
        private static GameObject _bolt;
        private static GameObject _castEffect;

        private static Attack _attack;
        private static GameObject _originalProjectile;
        private static float _originalVel;
        private static float _originalVelMin;
        private static float _originalAccuracy;
        private static float _originalAccuracyMin;
        private static float _originalLaunchAngle;
        private static EffectList _originalStartEffect;
        private static string _originalAnimation;
        private static int _originalChainLevels;
        private static float _originalHeight;
        private static Attack _selfCastTemplate;
        private static Attack _selfCast;

        private static bool Enabled => ConfigSync.Current.StaffOfProtectionSingleTarget;

        internal static void BuildPrefabs(ZNetScene scene)
        {
            if (_holder != null)
            {
                Object.Destroy(_holder);
            }

            _holder = new GameObject("ValheimRebalanced_Prefabs");
            _holder.SetActive(false);

            _bolt = BuildBolt(scene);
            _castEffect = BuildCastEffect(scene);
            Register(scene, _bolt);
            Register(scene, _castEffect);

            Refresh();
        }

        internal static void Refresh()
        {
            ObjectDB db = ObjectDB.instance;
            GameObject prefab = db != null ? db.GetItemPrefab(StaffPrefab) : null;
            ItemDrop item = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (item == null)
            {
                return;
            }

            Attack attack = item.m_itemData.m_shared.m_attack;
            if (_attack != attack)
            {
                _attack = attack;
                _originalProjectile = attack.m_attackProjectile;
                _originalVel = attack.m_projectileVel;
                _originalVelMin = attack.m_projectileVelMin;
                _originalAccuracy = attack.m_projectileAccuracy;
                _originalAccuracyMin = attack.m_projectileAccuracyMin;
                _originalLaunchAngle = attack.m_launchAngle;
                _originalStartEffect = attack.m_startEffect;
                _originalAnimation = attack.m_attackAnimation;
                _originalChainLevels = attack.m_attackChainLevels;
                _originalHeight = attack.m_attackHeight;
                _selfCastTemplate = attack.Clone();
                _selfCastTemplate.m_attackType = Attack.AttackType.None;
                _selfCastTemplate.m_attackProjectile = null;
            }

            if (Enabled && _bolt != null)
            {
                attack.m_attackProjectile = _bolt;
                attack.m_projectileVel = BoltSpeed;
                attack.m_projectileVelMin = BoltSpeed;
                attack.m_projectileAccuracy = 0f;
                attack.m_projectileAccuracyMin = 0f;
                attack.m_launchAngle = 0f;
                attack.m_startEffect = _castEffect != null ? WithPrefab(_originalStartEffect, _castEffect) : _originalStartEffect;
                attack.m_attackAnimation = AimAnimation;
                attack.m_attackChainLevels = AimChainLevels;
                attack.m_attackHeight = AimHeight;
            }
            else
            {
                attack.m_attackProjectile = _originalProjectile;
                attack.m_projectileVel = _originalVel;
                attack.m_projectileVelMin = _originalVelMin;
                attack.m_projectileAccuracy = _originalAccuracy;
                attack.m_projectileAccuracyMin = _originalAccuracyMin;
                attack.m_launchAngle = _originalLaunchAngle;
                attack.m_startEffect = _originalStartEffect;
                attack.m_attackAnimation = _originalAnimation;
                attack.m_attackChainLevels = _originalChainLevels;
                attack.m_attackHeight = _originalHeight;
            }
        }

        internal static void OnBlockStarted(Humanoid humanoid)
        {
            if (!Enabled || _bolt == null || humanoid != Player.m_localPlayer)
            {
                return;
            }

            Player player = (Player)humanoid;
            ItemDrop.ItemData staff = player.GetCurrentBlocker();
            if (!IsStaff(staff) || (staff.m_shared.m_useDurability && staff.m_durability <= 0f))
            {
                return;
            }

            StatusEffect shield = staff.m_shared.m_attackStatusEffect;
            if (shield == null || _selfCastTemplate == null || player.GetSEMan().HaveStatusEffect(shield.NameHash()))
            {
                return;
            }

            Attack attack = staff.m_shared.m_attack;
            staff.m_shared.m_attack = _selfCastTemplate;
            try
            {
                if (player.StartAttack(null, false))
                {
                    _selfCast = player.m_currentAttack;
                }
            }
            finally
            {
                staff.m_shared.m_attack = attack;
            }
        }

        internal static void OnNonAttack(Attack attack)
        {
            if (attack != _selfCast)
            {
                return;
            }

            _selfCast = null;
            Humanoid caster = attack.m_character;
            ItemDrop.ItemData staff = attack.m_weapon;
            StatusEffect shield = staff.m_shared.m_attackStatusEffect;
            if (caster == null || shield == null)
            {
                return;
            }

            caster.GetSEMan().AddStatusEffect(shield.NameHash(), true, staff.m_quality,
                caster.GetSkillLevel(staff.m_shared.m_skillType));
        }

        internal static bool IsBolt(Projectile projectile)
        {
            ZNetView view = projectile.m_nview;
            return view != null && view.IsValid() && view.GetZDO().GetPrefab() == BoltHash;
        }

        internal static void OnBoltHit(Projectile bolt, Collider collider, Vector3 hitPoint)
        {
            GameObject target = collider != null ? Projectile.FindHitObject(collider) : null;
            Character character = target != null ? target.GetComponent<Character>() : null;
            Character owner = bolt.m_owner;

            if (character != null)
            {
                if (character == owner)
                {
                    return;
                }

                if (!character.IsDead() && IsAlly(owner, character))
                {
                    Shield(bolt, character, collider, hitPoint);
                }
            }

            bolt.m_hitEffects.Create(hitPoint, Quaternion.identity);
            bolt.m_didHit = true;
            ZNetScene.instance.Destroy(bolt.gameObject);
        }

        private static void Shield(Projectile bolt, Character target, Collider collider, Vector3 hitPoint)
        {
            HitData source = bolt.m_originalHitData;
            if (source == null || source.m_statusEffectHash == 0)
            {
                return;
            }

            HitData hit = new HitData
            {
                m_hitCollider = collider,
                m_point = hitPoint,
                m_dir = bolt.transform.forward,
                m_statusEffectHash = source.m_statusEffectHash,
                m_skillLevel = source.m_skillLevel,
                m_itemLevel = source.m_itemLevel,
                m_itemWorldLevel = source.m_itemWorldLevel,
                m_ignorePVP = true,
                m_ranged = true,
                m_blockable = false,
                m_dodgeable = false,
                m_hitType = bolt.m_owner is Player ? HitData.HitType.PlayerHit : HitData.HitType.EnemyHit,
            };
            hit.SetAttacker(bolt.m_owner);
            target.Damage(hit);
        }

        private static bool IsAlly(Character owner, Character target)
        {
            if (owner == null)
            {
                return false;
            }

            BaseAI ai = target.GetBaseAI();
            return !BaseAI.IsEnemy(owner, target) && !(ai != null && ai.IsAggravatable() && owner.IsPlayer());
        }

        private static bool IsStaff(ItemDrop.ItemData item)
        {
            return item != null && item.m_dropPrefab != null && item.m_dropPrefab.name == StaffPrefab;
        }

        private static GameObject BuildBolt(ZNetScene scene)
        {
            GameObject template = scene.GetPrefab("staff_lightning_projectile");
            GameObject bubble = scene.GetPrefab("vfx_StaffShield");
            Transform sphere = bubble != null ? bubble.transform.Find("Sphere") : null;
            if (template == null || template.GetComponent<Projectile>() == null || sphere == null)
            {
                Plugin.Log.LogWarning("Staff of Protection: the projectile or bubble prefab is missing, " +
                                      "leaving the staff vanilla.");
                return null;
            }

            GameObject bolt = Object.Instantiate(template, _holder.transform, false);
            bolt.name = BoltName;
            ClearChildren(bolt.transform, null);

            GameObject visual = Object.Instantiate(sphere.gameObject, bolt.transform, false);
            visual.name = "Sphere";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = Vector3.one * BoltScale;

            Transform light = bubble.transform.Find("Point light");
            if (light != null)
            {
                GameObject glow = Object.Instantiate(light.gameObject, bolt.transform, false);
                glow.name = "Point light";
                glow.transform.localPosition = Vector3.zero;
            }

            GameObject hitEffect = scene.GetPrefab("fx_StaffShield_Hit");

            Projectile projectile = bolt.GetComponent<Projectile>();
            projectile.m_damage = new HitData.DamageTypes();
            projectile.m_aoe = 0f;
            projectile.m_hitMidFlight = false;
            projectile.m_gravity = 0f;
            projectile.m_drag = 0f;
            projectile.m_ttl = BoltRange / BoltSpeed;
            projectile.m_rayRadius = BoltRayRadius;
            projectile.m_hitOwner = false;
            projectile.m_hitFriendly = true;
            projectile.m_dodgeable = false;
            projectile.m_blockable = false;
            projectile.m_adrenaline = 0f;
            projectile.m_hitNoise = 0f;
            projectile.m_statusEffect = "";
            projectile.m_canHitWater = false;
            projectile.m_bounce = false;
            projectile.m_spawnOnHit = null;
            projectile.m_spawnOnTtl = false;
            projectile.m_randomSpawnOnHit = new List<GameObject>();
            projectile.m_stayAfterHitStatic = false;
            projectile.m_stayAfterHitDynamic = false;
            projectile.m_attachToRigidBody = false;
            projectile.m_attachToClosestBone = false;
            projectile.m_hideOnHit = null;
            projectile.m_visual = visual;
            projectile.m_canChangeVisuals = false;
            projectile.m_hitEffects = hitEffect != null ? Effect(hitEffect) : new EffectList();
            projectile.m_hitWaterEffects = new EffectList();

            return bolt;
        }

        private static GameObject BuildCastEffect(ZNetScene scene)
        {
            GameObject template = scene.GetPrefab("fx_shield_start");
            if (template == null)
            {
                return null;
            }

            GameObject effect = Object.Instantiate(template, _holder.transform, false);
            effect.name = CastEffectName;
            ClearChildren(effect.transform, "sfx");
            return effect;
        }

        private static void ClearChildren(Transform parent, string keep)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                if (child.name != keep)
                {
                    Object.DestroyImmediate(child);
                }
            }
        }

        private static void Register(ZNetScene scene, GameObject prefab)
        {
            if (prefab == null)
            {
                return;
            }

            scene.m_prefabs.Add(prefab);
            scene.m_namedPrefabs[prefab.name.GetStableHashCode()] = prefab;
        }

        private static EffectList Effect(GameObject prefab)
        {
            return new EffectList
            {
                m_effectPrefabs = new[] { new EffectList.EffectData { m_prefab = prefab } },
            };
        }

        private static EffectList WithPrefab(EffectList source, GameObject prefab)
        {
            EffectList.EffectData[] entries = new EffectList.EffectData[source.m_effectPrefabs.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                EffectList.EffectData original = source.m_effectPrefabs[i];
                entries[i] = new EffectList.EffectData
                {
                    m_prefab = prefab,
                    m_enabled = original.m_enabled,
                    m_variant = original.m_variant,
                    m_attach = original.m_attach,
                    m_follow = original.m_follow,
                    m_inheritParentRotation = original.m_inheritParentRotation,
                    m_inheritParentScale = original.m_inheritParentScale,
                    m_multiplyParentVisualScale = original.m_multiplyParentVisualScale,
                    m_randomRotation = original.m_randomRotation,
                    m_scale = original.m_scale,
                    m_childTransform = original.m_childTransform,
                };
            }

            return new EffectList { m_effectPrefabs = entries };
        }
    }
}
