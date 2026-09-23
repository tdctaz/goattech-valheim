using System.Collections.Generic;
using UnityEngine;

namespace ValheimCreatures
{
    internal sealed class CreatureTraits : MonoBehaviour
    {
        private static readonly int ColorKey = "ValheimCreatures_Color".GetStableHashCode();
        private static readonly int SecondColorKey = "ValheimCreatures_Color2".GetStableHashCode();
        private static readonly Dictionary<Character, CreatureTraits> ByCharacter = new Dictionary<Character, CreatureTraits>();

        internal static bool SpawningSplitBoss;

        private Character _character;
        private BaseAI _ai;
        private MonsterAI _monsterAI;

        private float _speed;
        private float _crouchSpeed;
        private float _walkSpeed;
        private float _runSpeed;
        private float _swimSpeed;
        private float _flySlowSpeed;
        private float _flyFastSpeed;
        private float _turnSpeed;
        private float _runTurnSpeed;
        private float _swimTurnSpeed;
        private float _flyTurnSpeed;
        private float _viewRange;
        private float _hearRange;
        private float _minAttackInterval;
        private float _circleTargetInterval;
        private float _circleTargetDuration;
        private string _defeatKey;
        private string _dreamCinematic;

        private float _regenTimer;
        private float _recheckTimer;

        internal StarColor Color { get; private set; }
        internal StarColor SecondColor { get; private set; }
        internal bool Boss { get; private set; }
        internal float AttackSpeed { get; private set; }
        internal float AttackIntervalReduction { get; private set; }
        internal float HearingMultiplier { get; private set; } = 1f;
        internal bool DropsSuppressed { get; set; }

        internal Character Character => _character;

        internal bool Has(StarColor color)
        {
            return color != StarColor.None && (Color == color || SecondColor == color);
        }

        internal static CreatureTraits Of(Character character)
        {
            if (character == null)
            {
                return null;
            }

            ByCharacter.TryGetValue(character, out CreatureTraits traits);
            return traits;
        }

        internal static void Attach(Character character)
        {
            if (character.IsPlayer() || character.m_nview == null || character.m_nview.GetZDO() == null ||
                ByCharacter.ContainsKey(character))
            {
                return;
            }

            CreatureTraits traits = character.gameObject.AddComponent<CreatureTraits>();
            traits.Init(character);
        }

        private static StarColor Read(ZDO zdo, int key)
        {
            int value = zdo != null ? zdo.GetInt(key, 0) : 0;
            return value > 0 && value < StarColors.Count ? (StarColor)value : StarColor.None;
        }

        private static int Stored(StarColor color)
        {
            return color == StarColor.None ? -1 : (int)color;
        }

        internal static void SetColor(Character character, StarColor color)
        {
            SetColors(character, color, StarColor.None);
        }

        internal static void SetColors(Character character, StarColor first, StarColor second)
        {
            ZDO zdo = character.m_nview.GetZDO();
            if (zdo == null)
            {
                return;
            }

            zdo.Set(ColorKey, Stored(first));
            zdo.Set(SecondColorKey, Stored(second));
            Of(character)?.Apply();
        }

        internal static void RefreshAll()
        {
            foreach (CreatureTraits traits in ByCharacter.Values)
            {
                if (traits != null)
                {
                    traits.Apply();
                }
            }
        }

        private void Init(Character character)
        {
            _character = character;
            _ai = character.GetComponent<BaseAI>();
            _monsterAI = _ai as MonsterAI;

            _speed = character.m_speed;
            _crouchSpeed = character.m_crouchSpeed;
            _walkSpeed = character.m_walkSpeed;
            _runSpeed = character.m_runSpeed;
            _swimSpeed = character.m_swimSpeed;
            _flySlowSpeed = character.m_flySlowSpeed;
            _flyFastSpeed = character.m_flyFastSpeed;
            _turnSpeed = character.m_turnSpeed;
            _runTurnSpeed = character.m_runTurnSpeed;
            _swimTurnSpeed = character.m_swimTurnSpeed;
            _flyTurnSpeed = character.m_flyTurnSpeed;
            _defeatKey = character.m_defeatSetGlobalKey;
            _dreamCinematic = character.m_dreamCinematic;

            if (_ai != null)
            {
                _viewRange = _ai.m_viewRange;
                _hearRange = _ai.m_hearRange;
            }

            if (_monsterAI != null)
            {
                _minAttackInterval = _monsterAI.m_minAttackInterval;
                _circleTargetInterval = _monsterAI.m_circleTargetInterval;
                _circleTargetDuration = _monsterAI.m_circleTargetDuration;
            }

            ByCharacter[character] = this;

            if (SpawningSplitBoss && character.IsBoss() && character.m_nview.IsOwner())
            {
                ZDO zdo = character.m_nview.GetZDO();
                zdo.Set(ColorKey, -1);
                zdo.Set(SecondColorKey, -1);
            }

            Apply();
        }

        private void OnDestroy()
        {
            if (_character != null && ByCharacter.TryGetValue(_character, out CreatureTraits traits) && traits == this)
            {
                ByCharacter.Remove(_character);
            }
        }

        internal void Apply()
        {
            if (_character == null || _character.m_nview == null || !_character.m_nview.IsValid())
            {
                return;
            }

            Balance balance = ConfigSync.Current;
            Boss = _character.IsBoss();
            ZDO zdo = _character.m_nview.GetZDO();
            if (Boss && balance.Enabled && _character.m_nview.IsOwner() && zdo.GetInt(ColorKey, 0) == 0)
            {
                StarColor first = StarColor.None;
                StarColor second = StarColor.None;
                if (!IsStarless(balance))
                {
                    LevelRoll.RollBoss(out first, out second);
                }

                zdo.Set(ColorKey, Stored(first));
                zdo.Set(SecondColorKey, Stored(second));
            }

#if DEBUG_TOOLS
            StarColor before = Color;
            StarColor beforeSecond = SecondColor;
#endif
            Color = balance.Enabled ? Read(zdo, ColorKey) : StarColor.None;
            SecondColor = balance.Enabled ? Read(zdo, SecondColorKey) : StarColor.None;
#if DEBUG_TOOLS
            if (ModConfig.LogColors.Value && (Color != before || SecondColor != beforeSecond))
            {
                Plugin.Log.LogInfo(
                    $"Colors: {Utils.GetPrefabName(_character.gameObject)} {zdo.m_uid} level {_character.GetLevel()} " +
                    $"owner {_character.m_nview.IsOwner()} -> {Color}, {SecondColor}.");
            }
#endif

            float speed = 1f;
            float turn = 1f;
            float senses = 1f;
            float attackInterval = 1f;
            float circleDuration = 1f;
            float circleInterval = 1f;
            AttackSpeed = 0f;
            AttackIntervalReduction = 0f;
            HearingMultiplier = 1f;

            if (Has(StarColor.Magenta))
            {
                speed += Boss ? balance.BossFastMoveSpeed : balance.FastMoveSpeed;
                turn += Boss ? balance.BossFastTurnSpeed : balance.FastTurnSpeed;
            }

            if (Has(StarColor.Red))
            {
                AttackSpeed = Boss ? balance.BossAggressiveAttackSpeed : balance.AggressiveAttackSpeed;
                AttackIntervalReduction = Mathf.Clamp01(
                    Boss ? balance.BossAggressiveAttackInterval : balance.AggressiveAttackInterval);
                attackInterval = 1f - AttackIntervalReduction;
                if (!Boss)
                {
                    circleDuration = 1f - Mathf.Clamp01(balance.AggressiveCircleDuration);
                    circleInterval = 1f + Mathf.Max(0f, balance.AggressiveCircleInterval);
                }
            }

            if (Has(StarColor.Cyan) && !Boss)
            {
                senses += balance.CuriousSenseRange;
                HearingMultiplier = senses;
            }

            if (Has(StarColor.Blue) && !Boss)
            {
                speed -= Mathf.Clamp01(balance.ArmoredMoveSpeed);
            }

            _character.m_speed = _speed * speed;
            _character.m_crouchSpeed = _crouchSpeed * speed;
            _character.m_walkSpeed = _walkSpeed * speed;
            _character.m_runSpeed = _runSpeed * speed;
            _character.m_swimSpeed = _swimSpeed * speed;
            _character.m_flySlowSpeed = _flySlowSpeed * speed;
            _character.m_flyFastSpeed = _flyFastSpeed * speed;
            _character.m_turnSpeed = _turnSpeed * turn;
            _character.m_runTurnSpeed = _runTurnSpeed * turn;
            _character.m_swimTurnSpeed = _swimTurnSpeed * turn;
            _character.m_flyTurnSpeed = _flyTurnSpeed * turn;

            if (_ai != null)
            {
                _ai.m_viewRange = _viewRange * senses;
                _ai.m_hearRange = _hearRange * senses;
            }

            if (_monsterAI != null)
            {
                _monsterAI.m_minAttackInterval = _minAttackInterval * attackInterval;
                _monsterAI.m_circleTargetInterval = _circleTargetInterval * circleInterval;
                _monsterAI.m_circleTargetDuration = _circleTargetDuration * circleDuration;
            }

            bool splittingBoss = Boss && Has(StarColor.White);
            _character.m_defeatSetGlobalKey = splittingBoss ? "" : _defeatKey;
            _character.m_dreamCinematic = splittingBoss ? "" : _dreamCinematic;
        }

        private bool IsStarless(Balance balance)
        {
            string name = Utils.GetPrefabName(_character.gameObject);
            foreach (string entry in (balance.StarlessBosses ?? "").Split(','))
            {
                if (entry.Trim() == name)
                {
                    return true;
                }
            }

            return false;
        }

        internal float DamageTakenFactor(HitData hit)
        {
            if (!Has(StarColor.Blue))
            {
                return 1f;
            }

            Balance balance = ConfigSync.Current;
            if (!Boss)
            {
                return 1f - Mathf.Clamp01(balance.ArmoredDamageTaken);
            }

            switch (hit.m_skill)
            {
                case Skills.SkillType.ElementalMagic:
                case Skills.SkillType.BloodMagic:
                    return 1f - Mathf.Clamp01(balance.BossShieldedMagic);
                case Skills.SkillType.Bows:
                case Skills.SkillType.Crossbows:
                    return 1f - Mathf.Clamp01(balance.BossShieldedArrows);
                default:
                    return 1f;
            }
        }

        private void Update()
        {
            if (_character == null || !_character.m_nview.IsValid())
            {
                return;
            }

            _recheckTimer += Time.deltaTime;
            if (_recheckTimer >= 2f)
            {
                _recheckTimer = 0f;
                ZDO zdo = _character.m_nview.GetZDO();
                bool enabled = ConfigSync.Current.Enabled;
                StarColor stored = enabled ? Read(zdo, ColorKey) : StarColor.None;
                StarColor storedSecond = enabled ? Read(zdo, SecondColorKey) : StarColor.None;
                if (stored != Color || storedSecond != SecondColor)
                {
                    Apply();
                }
            }

            if (Boss && Has(StarColor.Cyan) && _character.m_nview.IsOwner() && !_character.IsDead())
            {
                BossWaves.Check(this);
            }

            if (!Has(StarColor.Green) || !_character.m_nview.IsOwner() || _character.IsDead())
            {
                _regenTimer = 0f;
                return;
            }

            _regenTimer += Time.deltaTime;
            if (_regenTimer < 1f)
            {
                return;
            }

            float seconds = _regenTimer;
            _regenTimer = 0f;

            float health = _character.GetHealth();
            float max = _character.GetMaxHealth();
            if (health <= 0f || health >= max)
            {
                return;
            }

            bool burning = _character.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectBurning);
            float perSecond = HealPerSecond(burning);
            if (perSecond > 0f)
            {
                _character.SetHealth(Mathf.Min(max, health + perSecond * seconds));
            }
        }

        private float HealPerSecond(bool burning)
        {
            Balance balance = ConfigSync.Current;
            if (Boss)
            {
                float rate = _character.GetMaxHealth() * balance.BossRegeneratingPercent / 100f;
                return burning ? rate * 0.5f : rate;
            }

            if (burning)
            {
                return 0f;
            }

            int stars = Mathf.Max(0, _character.GetLevel() - 1);
            float baseHeal = _character.GetMaxHealthBase() * (1f + 0.25f * stars);
            float log = Mathf.Log10(Mathf.Max(10f, baseHeal - 1000f));
            return baseHeal * (10f * log / (baseHeal + 1000f)) * balance.RegeneratingRate;
        }
    }
}
