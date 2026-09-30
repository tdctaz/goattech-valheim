using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Steamworks;
using UnityEngine;

namespace ServerAuthority
{
    internal static class NetworkStats
    {
        private const float SampleSeconds = 1f;
        private const float WarningCooldownSeconds = 60f;
        private const int MinimumTurnsForWarning = 5;

        private sealed class Counters
        {
            internal int Turns;
            internal int Flushes;
            internal int QueueFull;
            internal int Clipped;
            internal int Behind;
            internal long Objects;
            internal long Bytes;
            internal int BacklogMax;
            internal int WindowMax;
            internal long SendTicks;
            internal long SendTicksMax;
            internal int Samples;
            internal long PingTotal;
            internal int PingMax;
            internal double DeliveryTotal;
            internal int DeliverySamples;
            internal long QueueMicrosMax;
            internal double OutBytesTotal;
            internal int SendRate;

            internal void Clear()
            {
                Turns = 0;
                Flushes = 0;
                QueueFull = 0;
                Clipped = 0;
                Behind = 0;
                Objects = 0;
                Bytes = 0;
                BacklogMax = 0;
                WindowMax = 0;
                SendTicks = 0;
                SendTicksMax = 0;
                Samples = 0;
                PingTotal = 0;
                PingMax = 0;
                DeliveryTotal = 0;
                DeliverySamples = 0;
                QueueMicrosMax = 0;
                OutBytesTotal = 0;
                SendRate = 0;
            }
        }

        private sealed class Peer
        {
            internal string Name;
            internal bool Connected;
            internal int Ping = -1;
            internal int SendRate;
            internal readonly Counters Window = new Counters();
            internal readonly Counters Period = new Counters();
        }

        private static readonly Dictionary<long, Peer> Peers = new Dictionary<long, Peer>();
        private static readonly Dictionary<int, long> WindowPrefabBytes = new Dictionary<int, long>();
        private static readonly Dictionary<int, long> PeriodPrefabBytes = new Dictionary<int, long>();
        private static readonly List<long> Departed = new List<long>();

        private static float _nextSample;
        private static float _lastWarning = float.MinValue;
        private static long _windowSendTicks;

        internal static void Reset()
        {
            Peers.Clear();
            WindowPrefabBytes.Clear();
            PeriodPrefabBytes.Clear();
            _nextSample = 0f;
            _lastWarning = float.MinValue;
            _windowSendTicks = 0;
        }

        internal static bool TryGetLink(long uid, out int pingMs, out int sendRate)
        {
            if (Peers.TryGetValue(uid, out Peer peer) && peer.Ping >= 0 && peer.SendRate > 0)
            {
                pingMs = peer.Ping;
                sendRate = peer.SendRate;
                return true;
            }

            pingMs = 0;
            sendRate = 0;
            return false;
        }

        internal static void RecordQueueFull(ZNetPeer netPeer, int window)
        {
            Peer peer = Get(netPeer);
            Count(peer.Window, window);
            Count(peer.Period, window);
            peer.Window.QueueFull++;
            peer.Period.QueueFull++;
        }

        internal static void RecordTurn(ZNetPeer netPeer, int window, int objects, int bytes, int backlog, long ticks)
        {
            Peer peer = Get(netPeer);
            Turn(peer.Window, window, objects, bytes, backlog, ticks);
            Turn(peer.Period, window, objects, bytes, backlog, ticks);
            _windowSendTicks += ticks;
        }

        internal static void RecordObject(int prefab, int bytes)
        {
            WindowPrefabBytes.TryGetValue(prefab, out long window);
            WindowPrefabBytes[prefab] = window + bytes;
            PeriodPrefabBytes.TryGetValue(prefab, out long period);
            PeriodPrefabBytes[prefab] = period + bytes;
        }

        internal static void Tick(float now)
        {
            if (now < _nextSample)
            {
                return;
            }

            _nextSample = now + SampleSeconds;

            ZNet znet = ZNet.instance;
            if (znet == null)
            {
                return;
            }

            foreach (Peer peer in Peers.Values)
            {
                peer.Connected = false;
            }

            List<ZNetPeer> peers = znet.m_peers;
            for (int i = 0; i < peers.Count; i++)
            {
                ZNetPeer netPeer = peers[i];
                if (netPeer == null || !netPeer.IsReady())
                {
                    continue;
                }

                Peer peer = Get(netPeer);
                peer.Connected = true;

                if (netPeer.m_socket is ZSteamSocket steam && steam.IsConnected())
                {
                    Sample(peer, steam);
                }
            }
        }

        internal static string SendWork(float seconds)
        {
            double ms = _windowSendTicks * 1000.0 / Stopwatch.Frequency;
            return $"Sending object updates took {ms / Math.Max(seconds, 0.001f):F1} ms per second.";
        }

        internal static void CloseWindow(float now, float seconds)
        {
            int threshold = ModConfig.NetworkSaturationWarning.Value;
            if (threshold > 0 && now - _lastWarning >= WarningCooldownSeconds)
            {
                StringBuilder text = null;
                foreach (Peer peer in Peers.Values)
                {
                    Counters c = peer.Window;
                    if (c.Turns < MinimumTurnsForWarning || (c.Behind + c.QueueFull) * 100 < threshold * c.Turns)
                    {
                        continue;
                    }

                    if (text == null)
                    {
                        text = new StringBuilder($"Object updates could not keep up over the last {seconds:F0}s.");
                    }

                    text.Append(' ').Append(Describe(peer, c, seconds));
                }

                if (text != null)
                {
                    _lastWarning = now;
                    text.Append(" Most sent: ").Append(TopPrefabs(WindowPrefabBytes, 6)).Append('.');
                    Plugin.Log.LogWarning(text.ToString());
                }
            }

            foreach (Peer peer in Peers.Values)
            {
                peer.Window.Clear();
            }

            WindowPrefabBytes.Clear();
            _windowSendTicks = 0;

            if (ModConfig.PerformanceReportSeconds.Value <= 0f)
            {
                ClosePeriod(0f);
            }
        }

        internal static string ClosePeriod(float seconds)
        {
            StringBuilder text = null;
            if (seconds > 0f)
            {
                foreach (Peer peer in Peers.Values.OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (peer.Period.Turns == 0 && peer.Period.Samples == 0)
                    {
                        continue;
                    }

                    if (text == null)
                    {
                        text = new StringBuilder($"Server network over {seconds:F0}s, per player:");
                    }

                    text.Append("\n  ").Append(Describe(peer, peer.Period, seconds));
                }

                if (text != null && PeriodPrefabBytes.Count > 0)
                {
                    text.Append("\n  Most sent: ").Append(TopPrefabs(PeriodPrefabBytes, 10)).Append('.');
                }
            }

            Departed.Clear();
            foreach (KeyValuePair<long, Peer> entry in Peers)
            {
                entry.Value.Period.Clear();
                if (!entry.Value.Connected)
                {
                    Departed.Add(entry.Key);
                }
            }

            for (int i = 0; i < Departed.Count; i++)
            {
                Peers.Remove(Departed[i]);
            }

            PeriodPrefabBytes.Clear();
            return text?.ToString();
        }

        private static Peer Get(ZNetPeer netPeer)
        {
            if (!Peers.TryGetValue(netPeer.m_uid, out Peer peer))
            {
                peer = new Peer { Connected = true };
                Peers[netPeer.m_uid] = peer;
            }

            if (!string.IsNullOrEmpty(netPeer.m_playerName))
            {
                peer.Name = netPeer.m_playerName;
            }
            else if (peer.Name == null)
            {
                peer.Name = netPeer.m_uid.ToString();
            }

            return peer;
        }

        private static void Count(Counters c, int window)
        {
            c.Turns++;
            c.WindowMax = Math.Max(c.WindowMax, window);
        }

        private static void Turn(Counters c, int window, int objects, int bytes, int backlog, long ticks)
        {
            Count(c, window);
            if (objects > 0 || bytes > 0)
            {
                c.Flushes++;
            }

            if (backlog > 0)
            {
                c.Clipped++;
            }

            if (backlog > objects)
            {
                c.Behind++;
            }

            c.Objects += objects;
            c.Bytes += bytes;
            c.BacklogMax = Math.Max(c.BacklogMax, backlog);
            c.SendTicks += ticks;
            c.SendTicksMax = Math.Max(c.SendTicksMax, ticks);
        }

        private static void Sample(Peer peer, ZSteamSocket steam)
        {
            SteamNetConnectionRealTimeStatus_t status = default;
            SteamNetConnectionRealTimeLaneStatus_t lanes = default;
            if (SteamGameServerNetworkingSockets.GetConnectionRealTimeStatus(steam.m_con, ref status, 0, ref lanes) != EResult.k_EResultOK)
            {
                return;
            }

            peer.Ping = status.m_nPing;
            peer.SendRate = status.m_nSendRateBytesPerSecond;
            Sample(peer.Window, status);
            Sample(peer.Period, status);
        }

        private static void Sample(Counters c, SteamNetConnectionRealTimeStatus_t status)
        {
            c.Samples++;
            c.PingTotal += status.m_nPing;
            c.PingMax = Math.Max(c.PingMax, status.m_nPing);
            if (status.m_flConnectionQualityLocal >= 0f)
            {
                c.DeliveryTotal += status.m_flConnectionQualityLocal;
                c.DeliverySamples++;
            }

            c.QueueMicrosMax = Math.Max(c.QueueMicrosMax, status.m_usecQueueTime.m_SteamNetworkingMicroseconds);
            c.OutBytesTotal += status.m_flOutBytesPerSec;
            c.SendRate = status.m_nSendRateBytesPerSecond;
        }

        private static string Describe(Peer peer, Counters c, float seconds)
        {
            StringBuilder text = new StringBuilder();
            text.Append(peer.Name).Append(": ");
            text.Append($"{c.Turns / seconds:F1} sends/s ({c.Flushes / seconds:F1} with data), ");
            text.Append($"{c.Objects / seconds:F0} objects/s, {c.Bytes / 1024f / seconds:F1} KiB/s, ");
            text.Append($"window up to {c.WindowMax / 1024f:F0} KiB; ");
            text.Append($"{Percent(c.Clipped, c.Flushes)} of sends full, ");
            text.Append($"{Percent(c.Behind, c.Flushes)} fell behind, ");
            text.Append($"{Percent(c.QueueFull, c.Turns)} held back by a full queue, ");
            text.Append($"backlog up to {c.BacklogMax} object(s)");

            if (c.Samples > 0)
            {
                text.Append($"; ping {c.PingTotal / c.Samples} ms (worst {c.PingMax})");
                if (c.DeliverySamples > 0)
                {
                    text.Append($", delivery {c.DeliveryTotal / c.DeliverySamples * 100.0:F1}%");
                }

                text.Append($", Steam queue up to {c.QueueMicrosMax / 1000} ms, ");
                text.Append($"Steam rate {c.SendRate / 1024} KiB/s, ");
                text.Append($"Steam out {c.OutBytesTotal / c.Samples / 1024.0:F1} KiB/s");
            }

            if (c.Turns > 0)
            {
                double average = c.SendTicks * 1000.0 / Stopwatch.Frequency / c.Turns;
                double worst = c.SendTicksMax * 1000.0 / Stopwatch.Frequency;
                text.Append($"; send work {average:F2} ms per send (worst {worst:F1} ms)");
            }

            text.Append('.');
            return text.ToString();
        }

        private static string Percent(int part, int whole)
        {
            return whole > 0 ? $"{part * 100 / whole}%" : "0%";
        }

        private static string TopPrefabs(Dictionary<int, long> bytes, int take)
        {
            long total = bytes.Values.Sum();
            if (total <= 0)
            {
                return "nothing";
            }

            return string.Join(", ", bytes
                .OrderByDescending(e => e.Value)
                .Take(take)
                .Select(e => $"{e.Value * 100 / total}% {PrefabName(e.Key)}"));
        }

        private static string PrefabName(int hash)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(hash) : null;
            return prefab != null ? prefab.name : hash.ToString();
        }
    }
}
