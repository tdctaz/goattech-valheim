using UnityEngine;

namespace ServerAuthority
{
    internal static class ItemStacks
    {
        private static int _itemMask;

        internal static void BeforeAutoStack(ItemDrop keeper)
        {
            if (keeper.m_haveAutoStacked || !Owned(keeper) || keeper.m_itemData.m_shared.m_maxStackSize <= 1)
            {
                return;
            }

            Refresh(keeper, "before auto-stacking");
            if (keeper.m_itemData.m_stack >= keeper.m_itemData.m_shared.m_maxStackSize)
            {
                return;
            }

            if (_itemMask == 0)
            {
                _itemMask = LayerMask.GetMask("item");
            }

            string name = keeper.m_itemData.m_shared.m_name;
            foreach (Collider collider in Physics.OverlapSphere(keeper.transform.position, 4f, _itemMask))
            {
                ItemDrop other = collider.attachedRigidbody != null ? collider.attachedRigidbody.GetComponent<ItemDrop>() : null;
                if (other != null && other != keeper && Owned(other) && other.m_itemData.m_shared.m_name == name)
                {
                    Refresh(other, $"before auto-stacking into {keeper.m_nview.GetZDO().m_uid}");
                }
            }
        }

        internal static void BeforeRemoveOne(ItemDrop item)
        {
            if (Owned(item))
            {
                Refresh(item, "before a creature ate from it");
            }
        }

        private static bool Owned(ItemDrop item)
        {
            return item.m_nview != null && item.m_nview.IsValid() && item.m_nview.IsOwner();
        }

        private static void Refresh(ItemDrop item, string when)
        {
            int counted = item.m_itemData.m_stack;
            item.Load();
            int saved = item.m_itemData.m_stack;
            if (saved == counted)
            {
                return;
            }

            ZDO zdo = item.m_nview.GetZDO();
            Vector3 position = item.transform.position;
            string error = saved > counted ? $"{saved - counted} too few" : $"{counted - saved} too many";
            Plugin.Log.LogInfo(
                $"Item stack refreshed {when}: {Utils.GetPrefabName(item.gameObject)} {zdo.m_uid} at " +
                $"({position.x:0}, {position.z:0}) was counted as {counted} but {saved} are saved, {error}.");
        }
    }
}
