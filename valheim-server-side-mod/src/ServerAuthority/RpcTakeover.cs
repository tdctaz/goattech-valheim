using System;
using System.Collections.Generic;
using UnityEngine;

namespace ServerAuthority
{
    internal static class RpcTakeover
    {
        private static readonly HashSet<string> Warned = new HashSet<string>();

        internal static void Reset()
        {
            Warned.Clear();
        }

        internal static void Replace<T>(Component owner, ZNetView view, string rpc, Action<long, T> handler)
        {
            if (Find(owner, view, rpc, out RoutedMethodBase existing))
            {
                CheckVanilla(owner, rpc, (existing as RoutedMethod<T>)?.m_action);
                view.Unregister(rpc);
                view.Register(rpc, handler);
            }
        }

        internal static void Replace<T, U>(Component owner, ZNetView view, string rpc, Action<long, T, U> handler)
        {
            if (Find(owner, view, rpc, out RoutedMethodBase existing))
            {
                CheckVanilla(owner, rpc, (existing as RoutedMethod<T, U>)?.m_action);
                view.Unregister(rpc);
                view.Register(rpc, handler);
            }
        }

        private static bool Find(Component owner, ZNetView view, string rpc, out RoutedMethodBase existing)
        {
            existing = null;
            if (view == null || view.GetZDO() == null)
            {
                return false;
            }

            if (view.m_functions.TryGetValue(rpc.GetStableHashCode(), out existing))
            {
                return true;
            }

            string type = owner.GetType().Name;
            if (Warned.Add("missing " + type + "." + rpc))
            {
                Plugin.Log.LogWarning(
                    $"{Utils.GetPrefabName(owner.gameObject)} has no {rpc} handler to take over, so vanilla's " +
                    $"{type}.{rpc} runs on the server unchanged. Re-read {type} after this game update.");
            }

            return false;
        }

        private static void CheckVanilla(Component owner, string rpc, Delegate current)
        {
            if (current != null && ReferenceEquals(current.Target, owner))
            {
                return;
            }

            string type = owner.GetType().Name;
            if (Warned.Add("foreign " + type + "." + rpc))
            {
                Plugin.Log.LogWarning(
                    $"{Utils.GetPrefabName(owner.gameObject)}'s {rpc} handler is not {type}'s own, probably another " +
                    "mod's, and is replaced anyway, so whatever it did is lost on the server.");
            }
        }
    }
}
