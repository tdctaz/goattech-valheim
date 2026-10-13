using HarmonyLib;

namespace ServerAuthority.Patches
{
    // The container RPC patches that used to live here were removed after they crashed a live
    // server. Valheim invokes RPC methods through Delegate.DynamicInvoke, and Mono failed to invoke
    // the Harmony-rewritten Container.RPC_RequestOpen with "Method has zero rva", then aborted
    // inside StackTrace.ToString while formatting that very exception, taking the process with it.
    //
    // They only existed to cover the ~100ms window between the server handing a container to a
    // client and that client writing its InUse flag back. OwnershipPolicy now covers the same
    // window with a grace period on any ZDO a connected peer holds, which needs no RPC patching.

    [HarmonyPatch(typeof(Container), nameof(Container.SetInUse))]
    internal static class Container_SetInUse_Patch
    {
        private static void Prefix(Container __instance, bool inUse)
        {
            ZNetView view = __instance.m_nview;
            if (view == null || !view.IsValid())
            {
                return;
            }

            if (!view.IsOwner())
            {
                if (!inUse && __instance.m_inUse)
                {
                    __instance.m_inUse = false;
                    Plugin.Log.LogInfo(
                        $"{Utils.GetPrefabName(__instance.gameObject)} {view.GetZDO().m_uid} was closed after this client " +
                        "stopped owning it, so its in-use mark was cleared here.");
                }

                return;
            }

            if (inUse && __instance.m_inUse && view.GetZDO().GetInt(ZDOVars.s_inUse) == 0)
            {
                view.GetZDO().Set(ZDOVars.s_inUse, 1);
                Plugin.Log.LogInfo(
                    $"{Utils.GetPrefabName(__instance.gameObject)} {view.GetZDO().m_uid} is open here but was not marked " +
                    "in use, so the mark was written again.");
            }
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.CheckForChanges))]
    internal static class Container_CheckForChanges_Patch
    {
        private static void Prefix(Container __instance)
        {
            if (!__instance.m_inUse)
            {
                return;
            }

            ZNetView view = __instance.m_nview;
            if (view == null || !view.IsValid())
            {
                return;
            }

            if (view.IsOwner())
            {
                InventoryGui gui = InventoryGui.instance;
                if (gui != null && gui.m_currentContainer == __instance)
                {
                    return;
                }

                __instance.m_inUse = false;
                view.GetZDO().Set(ZDOVars.s_inUse, 0);
                Plugin.Log.LogInfo(
                    $"{Utils.GetPrefabName(__instance.gameObject)} {view.GetZDO().m_uid} was marked in use here but is not " +
                    "open in the inventory, so it was closed and other players can open it again.");
                return;
            }

            __instance.m_inUse = false;
            Plugin.Log.LogInfo(
                $"{Utils.GetPrefabName(__instance.gameObject)} {view.GetZDO().m_uid} was open here when this client stopped " +
                "owning it, so its in-use mark was cleared and its contents load from the owner again.");
        }
    }
}
