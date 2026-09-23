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
}
