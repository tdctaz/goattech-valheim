using System;
using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority
{
    internal static class ServerParticles
    {
        private const float SweepSeconds = 5f;

        private static float _nextSweep;
        private static bool _failed;
        private static int _paused;
        private static int _sweeps;
        private static int _total;
        private static int _held;
        private static int _playing;
        private static int _family;
        private static readonly List<ParticleSystem> Children = new List<ParticleSystem>();

        internal static bool Active => Plugin.ServerActive && !_failed && ModConfig.PauseServerParticles.Value;

        internal static void Reset()
        {
            _nextSweep = 0f;
            _failed = false;
            _paused = 0;
            _sweeps = 0;
        }

        internal static void Tick()
        {
            if (!Active || Time.realtimeSinceStartup < _nextSweep)
            {
                return;
            }

            _nextSweep = Time.realtimeSinceStartup + SweepSeconds;
            try
            {
                Sweep();
            }
            catch (Exception e)
            {
                _failed = true;
                Plugin.Log.LogError($"Server particles: pausing failed and is off until the next session: {e}");
            }
        }

        internal static string ClosePeriod()
        {
            if (!Active || _sweeps == 0)
            {
                return null;
            }

            string text = $"Particle systems: {_total} on the server, {_held} paused ({_paused} newly this period), " +
                          $"{_playing} left playing because they end or remove themselves, {_family} because a system " +
                          "under them or one they emit into does.";
            _paused = 0;
            _sweeps = 0;
            return text;
        }

        private static void Sweep()
        {
            _sweeps++;
            ParticleSystem[] systems = UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
            _total = systems.Length;
            _held = 0;
            _playing = 0;
            _family = 0;
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem system = systems[i];
                if (system == null)
                {
                    continue;
                }

                if (system.isPaused)
                {
                    _held++;
                    continue;
                }

                if (!system.isPlaying)
                {
                    continue;
                }

                if (!Holdable(system))
                {
                    _playing++;
                    continue;
                }

                if (!FamilyHoldable(system))
                {
                    _family++;
                    continue;
                }

                system.Pause(true);
                _paused++;
                _held++;
            }
        }

        private static bool Holdable(ParticleSystem system)
        {
            ParticleSystem.MainModule main = system.main;
            return main.loop && main.stopAction == ParticleSystemStopAction.None;
        }

        private static bool FamilyHoldable(ParticleSystem system)
        {
            if (system.subEmitters.enabled)
            {
                return false;
            }

            Children.Clear();
            system.GetComponentsInChildren(true, Children);
            for (int i = 0; i < Children.Count; i++)
            {
                ParticleSystem child = Children[i];
                if (child != system && (!Holdable(child) || child.subEmitters.enabled))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
