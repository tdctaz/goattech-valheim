using System;
using UnityEngine;

namespace ServerAuthority
{
    internal static class PetFaces
    {
        private static bool _logged;

        internal static void Reset()
        {
            _logged = false;
        }

        internal static bool IsServerOwned(Pet pet)
        {
            ZNetView view = pet.m_nview;
            ZNet znet = ZNet.instance;
            return view != null && view.IsValid() && znet != null && !znet.IsServer() &&
                view.GetZDO().GetOwner() == znet.GetServerPeer()?.m_uid;
        }

        internal static void UpdateLocally(Pet pet)
        {
            MaterialVariation variation = pet.m_materialVariation;
            int material = variation.GetMaterial();
            if (pet.m_randomSpeak != null)
            {
                pet.m_randomSpeak.enabled = material != 5 && material != 6;
            }

            if (pet.m_renderer.isVisible)
            {
                return;
            }

            float closest = 99999f;
            float threshold = material == 5 ? 0.07f : 0.5f;
            foreach (Player player in Player.GetAllPlayers())
            {
                float distance = Utils.DistanceXZ(player.transform.position, pet.transform.position);
                if (closest > distance)
                {
                    closest = distance;
                }

                if (distance > 10f)
                {
                    continue;
                }

                SEMan seman = player.GetSEMan();
                if (seman == null)
                {
                    continue;
                }

                if (seman.HaveStatusEffect(SEMan.s_statusEffectSoftDeath) && UnityEngine.Random.value > threshold)
                {
                    SetFace(pet, 1);
                    return;
                }

                if ((seman.HaveStatusEffect(SEMan.s_statusEffectRested) || seman.HaveStatusEffect(SEMan.s_statusEffectCampFire) ||
                        pet.m_procreation.GetLovePoints() > 2) && UnityEngine.Random.value > threshold)
                {
                    SetFace(pet, 0);
                    return;
                }

                if (seman.HaveStatusEffect(SEMan.s_statusEffectBurning) || seman.HaveStatusEffect(SEMan.s_statusEffectFreezing) ||
                    (seman.HaveStatusEffect(SEMan.s_statusEffectPoison) && UnityEngine.Random.value > threshold))
                {
                    SetFace(pet, 3);
                    return;
                }

                if (seman.HaveStatusEffect(SEMan.s_statusEffectEncumbered) && UnityEngine.Random.value > threshold)
                {
                    SetFace(pet, 7);
                    return;
                }

                if (seman.HaveStatusEffect(SEMan.s_statusEffectSmoked) && UnityEngine.Random.value > threshold)
                {
                    SetFace(pet, 5);
                    return;
                }

                if (DateTime.Now - TimeSpan.FromSeconds(pet.m_UpdateRate) >= Player.LastEmoteTime)
                {
                    continue;
                }

                string emote = Player.LastEmote;
                if (emote == "cry" && UnityEngine.Random.value > threshold)
                {
                    SetFace(pet, UnityEngine.Random.value > 0.5f ? 3 : 1);
                    return;
                }

                if ((emote == "cheer" || emote == "toast" || emote == "flex" || emote == "laugh") &&
                    UnityEngine.Random.value > threshold)
                {
                    SetFace(pet, UnityEngine.Random.value > 0.5f ? 0 : 4);
                    return;
                }

                if ((emote == "blowkiss" || emote == "dance" || emote == "shrug" || emote == "roar") &&
                    UnityEngine.Random.value > threshold)
                {
                    SetFace(pet, UnityEngine.Random.value > 0.5f ? 5 : 7);
                    return;
                }

                if ((emote == "kneel" || emote == "bow" || emote == "sit") && UnityEngine.Random.value > threshold)
                {
                    SetFace(pet, UnityEngine.Random.value > 0.5f ? 4 : 2);
                    return;
                }
            }

            if (UnityEngine.Random.value < 0.1f && (Player.GetAllPlayers().Count == 1 || closest > 20f))
            {
                SetFace(pet, UnityEngine.Random.Range(0, variation.m_materials.Count));
            }
        }

        private static void SetFace(Pet pet, int index)
        {
            MaterialVariation variation = pet.m_materialVariation;
            variation.m_variation = index;
            variation.UpdateMaterial();
            if (pet.m_randomSpeak != null)
            {
                pet.m_randomSpeak.enabled = variation.GetMaterial() != 7;
            }

            if (!_logged)
            {
                _logged = true;
                Plugin.Log.LogInfo(
                    "A server-owned pet rock changed its face on this client, from this client's own player. " +
                    "Later changes are not logged.");
            }
        }
    }
}
