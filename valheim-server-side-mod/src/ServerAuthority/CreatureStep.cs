using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ServerAuthority
{
    internal static class CreatureStep
    {
        private const int Player = 0;
        private const int NotOwned = 1;
        private const int Dead = 2;
        private const int Asleep = 3;
        private const int TameIdle = 4;
        private const int WildIdle = 5;
        private const int Active = 6;
        private const int Unowned = 7;
        private const int States = 8;
        private const float StillSpeed = 0.1f;
        private const int ForgetEvery = 3000;

        private static readonly string[] Names = { "players", "owned by a player", "dead", "AI asleep", "tame idle", "wild idle", "active", "unowned" };
        private static readonly long[] Ticks = new long[States];
        private static readonly long[] Visits = new long[States];
        private static readonly long[] Runs = new long[States];
        private static readonly Dictionary<int, float> LastRun = new Dictionary<int, float>();

        private static int _configured = 1;
        private static int _override;
        private static int _every = 1;
        private static bool _timing;
        private static float _timingUntil;
        private static float _timingStarted;
        private static int _steps;

        internal static bool Engaged => Plugin.ServerActive && (_every > 1 || _timing);

        internal static void Configure(int every)
        {
            _configured = Math.Max(1, every);
            _every = _override > 0 ? _override : _configured;
            LastRun.Clear();
        }

        internal static void SetOverride(int every)
        {
            _override = Math.Max(0, every);
            _every = _override > 0 ? _override : _configured;
            LastRun.Clear();
            Plugin.Log.LogInfo($"Creature step: idle creatures run every {_every} physics step(s).");
        }

        internal static void StartTiming(float seconds)
        {
            Array.Clear(Ticks, 0, States);
            Array.Clear(Visits, 0, States);
            Array.Clear(Runs, 0, States);
            _timingStarted = Time.realtimeSinceStartup;
            _timingUntil = _timingStarted + Mathf.Max(seconds, 5f);
            _timing = true;
            Plugin.Log.LogInfo($"Creature step: timing creature updates by state for {seconds:F0} s, idle step {_every}.");
        }

        internal static void Run(List<IMonoUpdater> container, List<IMonoUpdater> source, float deltaTime)
        {
            container.AddRange(source);
            bool timing = _timing;
            int every = _every;
            float now = Time.fixedTime;
            _steps++;
            if (_steps % ForgetEvery == 0)
            {
                LastRun.Clear();
            }

            foreach (IMonoUpdater item in container)
            {
                Character character = item as Character;
                if (character == null)
                {
                    item.CustomFixedUpdate(deltaTime);
                    continue;
                }

                long started = timing ? Stopwatch.GetTimestamp() : 0L;
                int state = Classify(character);
                bool idle = state == Asleep || state == TameIdle || state == WildIdle || state == Unowned;
                bool ran = true;
                if (every > 1)
                {
                    int id = character.GetInstanceID();
                    if (!idle)
                    {
                        LastRun[id] = now;
                        character.CustomFixedUpdate(deltaTime);
                    }
                    else if (((uint)id + (uint)_steps) % (uint)every == 0u || !LastRun.ContainsKey(id))
                    {
                        float owed = LastRun.TryGetValue(id, out float last) ? now - last : deltaTime;
                        LastRun[id] = now;
                        character.CustomFixedUpdate(Mathf.Clamp(owed, deltaTime, deltaTime * every));
                    }
                    else
                    {
                        ran = false;
                    }
                }
                else
                {
                    character.CustomFixedUpdate(deltaTime);
                }

                if (timing)
                {
                    Ticks[state] += Stopwatch.GetTimestamp() - started;
                    Visits[state]++;
                    if (ran)
                    {
                        Runs[state]++;
                    }
                }
            }

            container.Clear();
            if (timing && Time.realtimeSinceStartup >= _timingUntil)
            {
                Report();
            }
        }

        private static int Classify(Character character)
        {
            if (character.IsPlayer())
            {
                return Player;
            }

            ZNetView view = character.m_nview;
            if (!view.IsValid())
            {
                return NotOwned;
            }

            ZDO zdo = view.GetZDO();
            if (!zdo.IsOwner())
            {
                return zdo.HasOwner() ? NotOwned : Unowned;
            }

            if (character.IsDead())
            {
                return Dead;
            }

            BaseAI ai = character.m_baseAI;
            if (ai == null)
            {
                return Active;
            }

            if (ai.IsSleeping())
            {
                return Asleep;
            }

            if (ai.IsAlerted() || ai.GetTargetCreature() != null || character.m_moveDir.sqrMagnitude > 0f ||
                character.m_pushForce.sqrMagnitude > 0f || character.IsStaggering() || character.InAttack() ||
                character.InLiquid() || character.m_lastGroundBody != null || character.IsAttached())
            {
                return Active;
            }

            Rigidbody body = character.m_body;
            if (body == null || (!body.isKinematic && body.linearVelocity.sqrMagnitude > StillSpeed * StillSpeed))
            {
                return Active;
            }

            return character.IsTamed() ? TameIdle : WildIdle;
        }

        private static void Report()
        {
            _timing = false;
            float seconds = Mathf.Max(Time.realtimeSinceStartup - _timingStarted, 0.001f);
            float steps = seconds / Time.fixedDeltaTime;
            double all = Ticks.Sum() * 1000.0 / Stopwatch.Frequency / seconds;
            string parts = string.Join(", ", Enumerable.Range(0, States)
                .Where(i => Visits[i] > 0)
                .Select(i => $"{Names[i]} {Ticks[i] * 1000.0 / Stopwatch.Frequency / seconds:F1} ms/s for {Visits[i] / steps:F0} creatures " +
                             $"({Runs[i] * 100 / Math.Max(Visits[i], 1)}% of visits updated, {Ticks[i] * 1000000000.0 / Stopwatch.Frequency / Math.Max(Runs[i], 1):F0} ns per update)"));
            Plugin.Log.LogInfo($"Creature step over {seconds:F0} s, idle step {_every}: {all:F1} ms/s in all; {parts}.");
        }
    }

    [HarmonyPatch(typeof(MonoUpdatersExtra), nameof(MonoUpdatersExtra.CustomFixedUpdate))]
    internal static class MonoUpdatersExtra_CustomFixedUpdate_Step_Patch
    {
        private static bool _failed;

        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(List<IMonoUpdater> container, List<IMonoUpdater> source, float deltaTime)
        {
            if (_failed)
            {
                return true;
            }

            if (source == Character.Instances)
            {
                if (!CreatureStep.Engaged)
                {
                    return true;
                }

                try
                {
                    CreatureStep.Run(container, source, deltaTime);
                }
                catch (Exception e)
                {
                    _failed = true;
                    container.Clear();
                    Plugin.Log.LogError($"Creature step failed and is off for this session: {e}");
                }

                return false;
            }

            if (source == ZSyncTransform.Instances)
            {
                try
                {
                    FrameSkips.FloatersFromFixedUpdate();
                }
                catch (Exception e)
                {
                    _failed = true;
                    Plugin.Log.LogError($"Floaters per physics step failed and are off for this session: {e}");
                    return true;
                }

                if (!SyncStep.Engaged)
                {
                    return true;
                }

                try
                {
                    SyncStep.Run(source, deltaTime);
                }
                catch (Exception e)
                {
                    _failed = true;
                    Plugin.Log.LogError($"Sync step failed and is off for this session: {e}");
                }

                return false;
            }

            return true;
        }
    }
}
