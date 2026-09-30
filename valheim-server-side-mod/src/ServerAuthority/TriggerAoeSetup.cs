using System;
using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority
{
    internal static class TriggerAoeSetup
    {
        private static readonly int Key = "ServerAuthority_AoeSetup".GetStableHashCode();
        private static readonly HashSet<string> Published = new HashSet<string>();
        private static readonly HashSet<string> Replayed = new HashSet<string>();

        internal static void Reset()
        {
            Published.Clear();
            Replayed.Clear();
        }

        internal static void Publish(Aoe aoe, Character owner, HitData hitData, ItemDrop.ItemData item)
        {
            ZNetView view = aoe.m_nview;
            if (!aoe.m_useTriggers || owner == null || owner.IsPlayer() || view == null || !view.IsValid() ||
                !view.IsOwner() || view.gameObject != aoe.gameObject)
            {
                return;
            }

            ZPackage package = new ZPackage();
            package.Write(owner.GetZDOID());
            package.Write(hitData != null);
            if (hitData != null)
            {
                hitData.Serialize(ref package);
            }

            package.Write(item != null);
            if (item != null)
            {
                package.Write(item.m_quality);
                package.Write(item.m_worldLevel);
            }

            view.GetZDO().Set(Key, package.GetArray());

            string name = Utils.GetPrefabName(aoe.gameObject);
            if (Published.Add(name))
            {
                Plugin.Log.LogInfo(
                    $"Trigger AoE {name} from {Utils.GetPrefabName(owner.gameObject)} carries its setup to clients: " +
                    $"{Describe(aoe, hitData)}.");
            }
        }

        internal static void Replay(GameObject instance, ZDO zdo)
        {
            if (instance == null || zdo == null || !zdo.GetByteArray(Key, out byte[] data))
            {
                return;
            }

            ZNet znet = ZNet.instance;
            if (znet == null || znet.IsServer() || zdo.GetOwner() != znet.GetServerPeer()?.m_uid)
            {
                return;
            }

            Aoe aoe = instance.GetComponent<Aoe>();
            if (aoe == null || aoe.m_nview == null || aoe.m_nview.GetZDO() != zdo)
            {
                return;
            }

            string name = Utils.GetPrefabName(instance);
            ZDOID ownerId;
            HitData hitData = null;
            bool hasItem;
            int quality = 0;
            int worldLevel = 0;
            try
            {
                ZPackage package = new ZPackage(data);
                ownerId = package.ReadZDOID();
                if (package.ReadBool())
                {
                    hitData = new HitData();
                    hitData.Deserialize(ref package);
                }

                hasItem = package.ReadBool();
                if (hasItem)
                {
                    quality = package.ReadInt();
                    worldLevel = package.ReadInt();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Trigger AoE {name} carried a setup this client cannot read: {e.Message}");
                return;
            }

            GameObject ownerObject = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(ownerId) : null;
            Character owner = ownerObject != null ? ownerObject.GetComponent<Character>() : null;

            aoe.Setup(owner, Vector3.zero, 0f, hitData, null, null);
            if (hasItem)
            {
                aoe.m_level = quality;
                aoe.m_worldLevel = worldLevel;
            }

            aoe.m_gaveSkill = true;
            if (hitData != null && aoe.m_useAttackSettings)
            {
                aoe.m_hitProps = false;
            }

            if (owner == null)
            {
                Plugin.Log.LogInfo(
                    $"Trigger AoE {name} names attacker {ownerId}, which does not exist on this client, so its hits here carry no attacker.");
            }

            if (Replayed.Add(name))
            {
                Plugin.Log.LogInfo(
                    $"Trigger AoE {name} took the server's setup: {Describe(aoe, hitData)}, " +
                    $"attacker {(owner != null ? Utils.GetPrefabName(owner.gameObject) : "missing")}.");
            }
        }

        private static string Describe(Aoe aoe, HitData hitData)
        {
            return hitData != null && aoe.m_useAttackSettings
                ? $"attacker and attack, {hitData.m_damage.GetTotalDamage():0.#} damage"
                : "attacker only, damage from the prefab";
        }
    }
}
