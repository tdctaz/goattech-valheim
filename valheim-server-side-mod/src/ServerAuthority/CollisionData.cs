using UnityEngine;

namespace ServerAuthority
{
    internal static class CollisionData
    {
        private const int CachedSizes = 33;

        private static readonly ContactPoint[][] BySize = new ContactPoint[CachedSizes][];
        private static bool _changed;
        private static bool _original;

        internal static bool Active;

        internal static void Apply(bool server)
        {
            Active = server && ModConfig.ReuseCollisionData.Value;
            if (Active)
            {
                if (!_changed)
                {
                    _original = Physics.reuseCollisionCallbacks;
                    _changed = true;
                }

                Physics.reuseCollisionCallbacks = true;
            }
            else if (_changed)
            {
                Physics.reuseCollisionCallbacks = _original;
                _changed = false;
            }
        }

        internal static ContactPoint[] Contacts(Collision collision)
        {
            if (!Active)
            {
                return collision.contacts;
            }

            int count = collision.contactCount;
            if (count >= CachedSizes)
            {
                return collision.contacts;
            }

            ContactPoint[] exact = BySize[count];
            if (exact == null)
            {
                exact = new ContactPoint[count];
                BySize[count] = exact;
            }

            if (count > 0)
            {
                collision.GetContacts(exact);
            }

            return exact;
        }
    }
}
