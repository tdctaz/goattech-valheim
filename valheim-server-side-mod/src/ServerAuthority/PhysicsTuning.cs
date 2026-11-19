using BepInEx.Configuration;
using UnityEngine;

namespace ServerAuthority
{
    internal static class PhysicsTuning
    {
        private const int MinimumRate = 20;

        internal static ConfigEntry<int> ServerPhysicsRate;
        internal static ConfigEntry<int> IdleCreatureStep;
        internal static ConfigEntry<int> OwnedSyncRecheckSteps;
        internal static ConfigEntry<bool> CacheGroundProbes;
        internal static ConfigEntry<bool> CompensatePhysicsRate;
        internal static ConfigEntry<bool> SyncOnlyAfterPhysics;
        internal static ConfigEntry<bool> FloatPerPhysicsStep;

        private static bool _changed;
        private static float _originalStep;
        private static int _originalSolver;
        private static int _originalVelocitySolver;
        private static float _originalSleep;
        private static bool _solverChanged;

        internal static void Bind(ConfigFile config)
        {
            ServerPhysicsRate = config.Bind("Experimental", "ServerPhysicsRate", 0,
                new ConfigDescription(
                    "Physics steps per second on the server. 0 leaves the game's 50.\nVanilla steps physics and " +
                    "every creature's movement code 50 times a second, and on a busy server that is about a third " +
                    "of the frame time. Players' own games keep 50 either way. Character acceleration, ship " +
                    "and floating forces and some per-step smoothing in the game are written per step rather " +
                    "than per second, so a lower rate changes how creatures and ships move unless CompensatePhysicsRate is on. " +
                    "Values below 20 are raised to 20, since creature AI runs at most once per step and is meant to run " +
                    "20 times a second. Experimental.",
                    new AcceptableValueRange<int>(0, 60)));

            CompensatePhysicsRate = config.Bind("Experimental", "CompensatePhysicsRate", true,
                "When ServerPhysicsRate is not 50, scale the game's per-step constants so creatures, ships and " +
                "floating objects keep their per-second behaviour.\nVanilla writes some movement per physics step " +
                "rather than per second: creature acceleration, the speed of attack and dodge lunges, ship sail force " +
                "and drag, the damping of floating objects, how quickly the server's copy of a player follows the " +
                "player, and the AI's stuck timer. At a lower rate those would come out weaker or stronger. Has no " +
                "effect at 50.");

            IdleCreatureStep = config.Bind("Experimental", "IdleCreatureStep", 1,
                new ConfigDescription(
                    "Run the per-step movement code of an idle creature only every this many physics steps, " +
                    "with the time in between handed over at once. 1 is vanilla.\nVanilla runs it every step for " +
                    "every creature, including asleep monsters, tame animals standing in a base, and creatures loaded " +
                    "next to a player's area that nobody simulates. A creature counts as idle while it is asleep, " +
                    "unowned, or standing still with no target and not alerted, attacking, staggered, pushed, " +
                    "swimming or riding something; the moment that changes it is updated every step again. " +
                    "Experimental.",
                    new AcceptableValueRange<int>(1, 10)));

            OwnedSyncRecheckSteps = config.Bind("Experimental", "OwnedSyncRecheckSteps", 0,
                new ConfigDescription(
                    "Every this many physics steps, sort the server's synced objects by owner. In between, skip the " +
                    "ones the server owns, follow the ones a player owns every step, and visit the unowned ones once " +
                    "per period. 0 is vanilla.\nVanilla visits every synced object each physics step to make it follow " +
                    "its owner's last position. On the server an owned object only finds it is the owner, but an " +
                    "unowned one, loaded near a player's area yet outside it, is pulled back onto its saved position " +
                    "and its physics refreshed every step although nothing moves it. An object a player takes over in " +
                    "between follows them from the next sort. Experimental.",
                    new AcceptableValueRange<int>(0, 50)));

            CacheGroundProbes = config.Bind("Experimental", "CacheGroundProbes", false,
                "Let a creature reuse its last ground probes while it stays within a few centimetres of where it " +
                "cast them.\nVanilla casts a ray from 5 km up to the terrain under every creature the server " +
                "simulates on every physics step, to find the biome and lava under it, and two more rays for the " +
                "creatures whose body tilts with the slope. On the server that was a third of each creature's " +
                "per-step cost, and the answer only changes when the creature moves. The ground is " +
                "re-probed after 25cm or a second for the biome, and after 10cm, a slight turn or 0.2 s for the tilt. " +
                "Experimental.");

            SyncOnlyAfterPhysics = config.Bind("Experimental", "SyncOnlyAfterPhysics", false,
                "Copy the position and velocity of the objects the server simulates into their network data only " +
                "on frames that ran a physics step.\nVanilla copies every synced object the server owns on every " +
                "frame. Rigidbodies only move in physics steps, so on a frame without one there is nothing new to " +
                "copy; the server runs at up to 60 frames a second against 50 physics steps, and fewer steps if " +
                "ServerPhysicsRate is lowered. Experimental.");

            FloatPerPhysicsStep = config.Bind("Experimental", "FloatPerPhysicsStep", false,
                "Measure the water surface under floating items, swimming creatures and fish once per physics step, " +
                "just before they use it, instead of once per frame.\nVanilla measures it every frame, and only " +
                "physics steps read it. With wave heights per position each measurement is a few microseconds, and " +
                "with hundreds of objects in water that came to a tenth of the frame time. Experimental.");
        }

        internal static void Apply(bool server)
        {
            if (server)
            {
                CreatureStep.Configure(IdleCreatureStep.Value);
                SyncStep.Configure(OwnedSyncRecheckSteps.Value);
                if (ServerPhysicsRate.Value > 0)
                {
                    SetFixedRate(Mathf.Max(ServerPhysicsRate.Value, MinimumRate));
                }
            }
            else
            {
                CreatureStep.Configure(1);
                SyncStep.Configure(0);
                Restore();
            }
        }

        internal static void SetFixedRate(float hz)
        {
            if (!_changed)
            {
                _originalStep = Time.fixedDeltaTime;
                _changed = true;
            }

            float step = hz > 0f ? 1f / hz : _originalStep;
            Time.fixedDeltaTime = step;
            Plugin.Log.LogInfo($"Server physics: fixed step {step * 1000f:0.##} ms ({1f / step:0.#} per second).");
        }

        internal static void SetSolver(int iterations, int velocityIterations)
        {
            RememberSolver();
            if (iterations <= 0)
            {
                iterations = _originalSolver;
            }

            if (velocityIterations <= 0)
            {
                velocityIterations = _originalVelocitySolver;
            }

            Physics.defaultSolverIterations = iterations;
            Physics.defaultSolverVelocityIterations = velocityIterations;
            int changed = 0;
            foreach (Rigidbody body in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
            {
                body.solverIterations = iterations;
                body.solverVelocityIterations = velocityIterations;
                changed++;
            }

            Plugin.Log.LogInfo($"Server physics: solver iterations {iterations}/{velocityIterations} on {changed} bodies.");
        }

        internal static void SetSleepThreshold(float threshold)
        {
            RememberSolver();
            if (threshold <= 0f)
            {
                threshold = _originalSleep;
            }

            Physics.sleepThreshold = threshold;
            int changed = 0;
            foreach (Rigidbody body in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
            {
                body.sleepThreshold = threshold;
                changed++;
            }

            Plugin.Log.LogInfo($"Server physics: sleep threshold {threshold} on {changed} bodies.");
        }

        private static void RememberSolver()
        {
            if (_solverChanged)
            {
                return;
            }

            _originalSolver = Physics.defaultSolverIterations;
            _originalVelocitySolver = Physics.defaultSolverVelocityIterations;
            _originalSleep = Physics.sleepThreshold;
            _solverChanged = true;
        }

        private static void Restore()
        {
            if (_changed)
            {
                Time.fixedDeltaTime = _originalStep;
                _changed = false;
            }

            if (_solverChanged)
            {
                Physics.defaultSolverIterations = _originalSolver;
                Physics.defaultSolverVelocityIterations = _originalVelocitySolver;
                Physics.sleepThreshold = _originalSleep;
                _solverChanged = false;
            }
        }
    }
}
