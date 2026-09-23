using System;
using HarmonyLib;
using ServerAuthority.Integrity;
using UnityEngine;

namespace ServerAuthority.Patches
{
    internal static class IntegrityPatches
    {
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
        internal static class ZNet_OnNewConnection_Patch
        {
            private static void Postfix(ZNet __instance, ZNetPeer peer)
            {
                try
                {
                    if (__instance.IsServer())
                    {
                        IntegrityServer.OnNewConnection(peer);
                    }
                    else
                    {
                        IntegrityClient.OnNewConnection(peer);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Setting up a new connection failed: {e}");
                }
            }
        }

        [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.AddPeer))]
        internal static class ZRoutedRpc_AddPeer_Patch
        {
            private static void Postfix(ZNetPeer peer)
            {
                try
                {
                    if (ZNet.instance != null && ZNet.instance.IsServer())
                    {
                        IntegrityServer.OnPeerAdmitted(peer);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Admitting a peer failed: {e}");
                }
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Update))]
        internal static class ZNet_Update_Patch
        {
            private static void Postfix(ZNet __instance)
            {
                try
                {
                    if (__instance.IsServer())
                    {
                        IntegrityServer.DropClosedSessions();
                        IntegrityServer.Tick();
                    }
                    else
                    {
                        IntegrityClient.Tick();
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Integrity update failed: {e}");
                }
            }
        }

        [HarmonyPatch(typeof(Game), nameof(Game.Start))]
        internal static class Game_Start_Patch
        {
            private static void Postfix()
            {
                if (ZNet.instance != null && ZNet.instance.IsServer() &&
                    ModConfig.CharactersEnabled.Value &&
                    ModConfig.NewCharacters.Value == NewCharacterPolicy.ResetToFresh &&
                    !string.IsNullOrWhiteSpace(ModConfig.StartingKit.Value))
                {
                    FreshCharacter.ResolveKit(ModConfig.StartingKit.Value);
                }
            }
        }

        [HarmonyPatch(typeof(Game), nameof(Game.FindSpawnPoint))]
        internal static class Game_FindSpawnPoint_Patch
        {
            private static bool Prefix(ref bool __result, ref Vector3 point, ref bool usedLogoutPoint)
            {
                if (!IntegrityClient.HoldSpawn)
                {
                    return true;
                }

                point = Vector3.zero;
                usedLogoutPoint = false;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Game), nameof(Game.Shutdown))]
        internal static class Game_Shutdown_Patch
        {
            private static void Prefix(bool saveWorld)
            {
                try
                {
                    if (ZNet.instance == null)
                    {
                        return;
                    }

                    if (ZNet.instance.IsServer())
                    {
                        IntegrityServer.BeforeShutdown(saveWorld);
                    }
                    else
                    {
                        IntegrityClient.BeforeShutdown(saveWorld);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Collecting final character saves before shutdown failed: {e}");
                }
            }
        }

        [HarmonyPatch(typeof(Game), nameof(Game.SavePlayerProfile))]
        internal static class Game_SavePlayerProfile_Patch
        {
            private static bool Prefix()
            {
                return !IntegrityClient.SuppressLocalSave;
            }

            private static void Postfix()
            {
                try
                {
                    if (ZNet.instance != null && !ZNet.instance.IsServer())
                    {
                        IntegrityClient.OnSaved();
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Sending the character to the server failed: {e}");
                }
            }
        }

        [HarmonyPatch(typeof(PlayerProfile), nameof(PlayerProfile.LoadPlayerDataFromDisk))]
        internal static class PlayerProfile_LoadPlayerDataFromDisk_Patch
        {
            private static bool Prefix(ref ZPackage __result)
            {
                if (!ProfileCodec.TryTakePending(out ZPackage pkg))
                {
                    return true;
                }

                __result = pkg;
                return false;
            }
        }

        [HarmonyPatch(typeof(ZPackage), nameof(ZPackage.ReadByteArray), new Type[0])]
        internal static class ZPackage_ReadByteArray_Patch
        {
            private static bool Prefix(ZPackage __instance, ref byte[] __result)
            {
                if (!ProfileCodec.Decoding)
                {
                    return true;
                }

                int count = __instance.m_reader.ReadInt32();
                long remaining = __instance.m_stream.Length - __instance.m_stream.Position;
                if (count < 0 || count > remaining)
                {
                    throw new System.IO.InvalidDataException($"byte array claims {count} bytes, {remaining} remain");
                }

                __result = __instance.m_reader.ReadBytes(count);
                return false;
            }
        }

        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.ShowConnectError))]
        internal static class FejdStartup_ShowConnectError_Patch
        {
            private static void Postfix(FejdStartup __instance)
            {
                string reason = IntegrityClient.RejectReason;
                if (reason == null || !__instance.m_connectionFailedPanel.activeSelf)
                {
                    return;
                }

                __instance.m_connectionFailedError.text = reason;
                IntegrityClient.RejectReason = null;
            }
        }
    }
}
