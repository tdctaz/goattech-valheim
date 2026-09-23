using HarmonyLib;
using UnityEngine;

namespace ServerAuthority.Patches
{
    /// <summary>
    /// A dedicated server normally never instantiates the objects these systems belong to, so their
    /// headless code paths have effectively never run and some of them throw. None of this is
    /// gameplay, it is the cost of turning on parts of the game that were only ever exercised on a
    /// machine with a screen.
    /// </summary>
    internal static class HeadlessPatches
    {
        [HarmonyPatch(typeof(AudioMan), nameof(AudioMan.Update))]
        internal static class AudioMan_Update_Patch
        {
            private static bool Prefix()
            {
                return !Plugin.ServerActive;
            }
        }

        /// <summary>
        /// Builds its colour from a gradient that is null without the render pipeline present.
        /// Nothing on a server looks at the result.
        /// </summary>
        [HarmonyPatch(typeof(ShieldDomeImageEffect), nameof(ShieldDomeImageEffect.GetDomeColor))]
        internal static class ShieldDomeImageEffect_GetDomeColor_Patch
        {
            private static bool Prefix(ref Color __result)
            {
                if (!Plugin.ServerActive)
                {
                    return true;
                }

                __result = Color.white;
                return false;
            }
        }

        /// <summary>
        /// Moving a sail throws on a headless server, once a frame for as long as it is moving.
        ///
        /// Ship.UpdateSailSize creates the sail-change effect when the sail leaves a settled
        /// position, and to do that it reads Player.m_localPlayer.GetPlayerID() with no null check
        /// at all. There is no local player on a server, so every raise and lower of a sail throws
        /// a NullReferenceException on every frame of the animation. Vanilla never met this because
        /// a server never has a ship instantiated; this mod does, and the first time anybody took
        /// the helm the log filled with them.
        ///
        /// The whole method is skipped rather than reimplemented without the effect, because every
        /// line of it is cosmetic. m_sailPosition, m_sailWasInPosition, m_sailBottomTransform and
        /// m_sailCloth are read nowhere else in the game: sail position drives the mesh and the
        /// cloth blend weight, while the force a sail actually produces comes from m_speed through
        /// GetSailForce. Nothing the server owes its clients is computed here.
        /// </summary>
        [HarmonyPatch(typeof(Ship), nameof(Ship.UpdateSailSize))]
        internal static class Ship_UpdateSailSize_Patch
        {
            private static bool Prefix()
            {
                return !Plugin.ServerActive || Player.m_localPlayerExists;
            }
        }

        /// <summary>
        /// Structural integrity walks m_bounds, which is only filled by the setup pass that objects
        /// created through the server's path can miss.
        /// </summary>
        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateSupport))]
        internal static class WearNTear_UpdateSupport_Patch
        {
            private static void Prefix(WearNTear __instance)
            {
                if (!Plugin.ServerActive)
                {
                    return;
                }

                if (__instance.m_colliders != null && __instance.m_bounds == null)
                {
                    __instance.SetupColliders();
                }
            }
        }

        /// <summary>
        /// An attack can survive on a humanoid without the character reference it needs, and then
        /// throws on every frame of that attack. Dropping it lets the creature pick a new one.
        /// The underlying cause is still unknown and this only stops the bleeding.
        /// </summary>
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UpdateAttack))]
        internal static class Humanoid_UpdateAttack_Patch
        {
            private static void Prefix(Humanoid __instance)
            {
                if (!Plugin.ServerActive)
                {
                    return;
                }

                if (__instance.m_currentAttack != null && __instance.m_currentAttack.m_character == null)
                {
                    __instance.m_currentAttack = null;
                }
            }
        }
    }
}
