using System;

namespace ServerAuthority
{
    internal static class AllocationMeter
    {
        internal static long Now()
        {
            return GC.GetTotalMemory(false);
        }

        internal static long Growth(long since)
        {
            long grew = GC.GetTotalMemory(false) - since;
            return grew > 0 ? grew : 0;
        }
    }
}
