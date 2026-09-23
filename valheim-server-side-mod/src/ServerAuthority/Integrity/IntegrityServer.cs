using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace ServerAuthority.Integrity
{
    internal static class IntegrityServer
    {
        private sealed class Session
        {
            internal ModManifest Manifest;
            internal bool Admitted;
            internal string StorePath;
            internal string Label;
            internal Baseline Baseline;
            internal bool AwaitingInitial;
            internal float AwaitingSince;
            internal bool Synced;
            internal float LastSaveRequest;
            internal bool Rejected;
            internal byte[] FreshPlayerData;
            internal long FreshPlayerId;
            internal readonly ChunkAssembler Incoming = new ChunkAssembler();
            internal int NextTransferId;
            internal int UploadsReceived;
            internal int UploadsStored;
        }

        private static readonly Dictionary<ZNetPeer, Session> Sessions = new Dictionary<ZNetPeer, Session>();
        private static readonly List<ZNetPeer> Closed = new List<ZNetPeer>();

        private static bool AnyFeature =>
            ModConfig.ModValidationEnabled.Value || ModConfig.CharactersEnabled.Value;

        internal static void Reset()
        {
            Sessions.Clear();
        }

        internal static void OnNewConnection(ZNetPeer peer)
        {
            Sessions[peer] = new Session();
            peer.m_rpc.Register<ZPackage>(Protocol.Manifest, RPC_Manifest);
            peer.m_rpc.Register<ZPackage>(Protocol.Upload, RPC_Upload);
        }

        /// <summary>
        /// Forgets the sessions of peers that are no longer connected, by reconciling against
        /// ZNet's own peer list once per frame.
        ///
        /// This used to be a Harmony prefix on ZNet.Disconnect, which is the obvious place for it
        /// and which killed the server. Mono raised
        /// <c>BadImageFormatException: Method has zero rva</c> building the dynamic replacement for
        /// Disconnect, every frame, forever: ZNet.UpdatePeers calls it for a closed socket, the
        /// call throws, the peer is therefore never removed from m_peers, and the next frame tries
        /// again. One player quitting to the desktop left the server spinning on it 5000 times.
        ///
        /// The likely reason is what Disconnect reaches. It calls peer.Dispose, which calls
        /// ISocket.Dispose and so lands in the Steam native socket, and a P/Invoke has no IL body
        /// for Harmony to work from. That is the same hazard the README records for RPC methods,
        /// and the same conclusion applies: do not patch a method when something else will do.
        ///
        /// Nothing here needs to happen at the instant of the disconnect. The session is
        /// bookkeeping, so noticing within a frame is enough, and reconciling cannot be left half
        /// done by an exception the way a prefix can.
        /// </summary>
        internal static void DropClosedSessions()
        {
            if (Sessions.Count == 0)
            {
                return;
            }

            ZNet znet = ZNet.instance;
            if (znet == null)
            {
                Sessions.Clear();
                return;
            }

            Closed.Clear();
            foreach (KeyValuePair<ZNetPeer, Session> entry in Sessions)
            {
                if (!znet.m_peers.Contains(entry.Key))
                {
                    Closed.Add(entry.Key);
                }
            }

            for (int i = 0; i < Closed.Count; i++)
            {
                Sessions.Remove(Closed[i]);
                Plugin.Log.LogInfo(
                    $"Forgetting the session of disconnected peer {Closed[i].m_uid}. " +
                    $"{Sessions.Count} session(s) remain.");
            }

            Closed.Clear();
        }

        private static void RPC_Manifest(ZRpc rpc, ZPackage pkg)
        {
            try
            {
                ZNetPeer peer = ZNet.instance?.GetPeer(rpc);
                if (peer == null || !Sessions.TryGetValue(peer, out Session session))
                {
                    return;
                }

                session.Manifest = ModManifest.Read(pkg);

                ZPackage info = new ZPackage();
                info.Write(Protocol.Version);
                info.Write(ModConfig.ModValidationEnabled.Value);
                info.Write(ModConfig.CharactersEnabled.Value);
                info.Write(ModConfig.CharacterSyncTimeoutSeconds.Value);
                rpc.Invoke(Protocol.ServerInfo, info);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Unreadable mod manifest from a client: {e.Message}");
            }
        }

        internal static void OnPeerAdmitted(ZNetPeer peer)
        {
            if (!Sessions.TryGetValue(peer, out Session session))
            {
                session = new Session();
                Sessions[peer] = session;
            }

            session.Label = $"{peer.m_playerName} ({peer.m_socket.GetHostName()})";

            if (!AnyFeature)
            {
                return;
            }

            if (session.Manifest == null)
            {
                Reject(peer, session,
                    "This server requires the Server Authority mod on your game. Install the same " +
                    "version the server runs.");
                return;
            }

            List<string> problems = session.Manifest.Problems();
            if (problems.Count > 0)
            {
                Plugin.Log.LogInfo($"{session.Label} mods: {session.Manifest.Describe()}");
                Reject(peer, session, "Your mods do not match this server:\n" + string.Join("\n", Limit(problems, 8)));
                return;
            }

            session.Admitted = true;

            if (!ModConfig.CharactersEnabled.Value)
            {
                SendMessage(peer, session, ProfileMessage.Unmanaged, null);
                return;
            }

            StartCharacter(peer, session);
        }

        private static void StartCharacter(ZNetPeer peer, Session session)
        {
            string world = ZNet.instance.GetWorldName();
            session.StorePath = CharacterStore.PathFor(world, peer.m_socket.GetHostName(), peer.m_playerName);

            byte[] stored;
            try
            {
                stored = CharacterStore.Load(session.StorePath);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not read {session.StorePath}: {e.Message}");
                Reject(peer, session, "The server could not read your saved character. Ask the server admin.");
                return;
            }

            if (stored == null)
            {
                Plugin.Log.LogInfo($"{session.Label} is new to this world, asking for their character.");
                session.AwaitingInitial = true;
                session.AwaitingSince = Time.time;
                SendMessage(peer, session, ProfileMessage.RequestInitial, null);
                return;
            }

            PlayerProfile profile = ProfileCodec.Decode(stored, "serverauthority", FileHelpers.FileSource.Local);
            if (profile == null)
            {
                Plugin.Log.LogError($"{session.StorePath} does not decode as a character.");
                Reject(peer, session, "The server's copy of your character is damaged. Ask the server admin.");
                return;
            }

            session.Baseline = CharacterValidator.BaselineOf(profile, PlayerDataSummary.Read(profile.m_playerData, out _));

            string storedHash = ModManifest.HashBytes(stored);
            if (!string.IsNullOrEmpty(session.Manifest.ProfileHash) && session.Manifest.ProfileHash != storedHash)
            {
                Plugin.Log.LogInfo(
                    $"{session.Label} arrived with a local character that differs from the server's copy. " +
                    "Restoring the server's copy. This is expected after playing the character elsewhere " +
                    "or after a lost connection, and is also what an edited character looks like.");
            }

            SendMessage(peer, session, ProfileMessage.StoredCopy, stored);
            session.Synced = true;
            session.LastSaveRequest = Time.time;
        }

        private static void RPC_Upload(ZRpc rpc, ZPackage pkg)
        {
            try
            {
                ZNetPeer peer = ZNet.instance?.GetPeer(rpc);
                if (peer == null || !Sessions.TryGetValue(peer, out Session session))
                {
                    return;
                }

                if (!session.Admitted || session.Rejected || session.StorePath == null)
                {
                    return;
                }

                Chunk chunk = Chunk.Read(pkg);
                if (!session.Incoming.TryAdd(chunk, out byte[] data, out string error))
                {
                    if (error != null)
                    {
                        Plugin.Log.LogWarning($"{session.Label} sent a bad character upload: {error}");
                    }

                    return;
                }

                session.UploadsReceived++;
                bool stored = HandleUpload(peer, session, data);
                if (stored)
                {
                    session.UploadsStored++;
                }

                if (chunk.Kind == Protocol.UploadFinal)
                {
                    Plugin.Log.LogInfo(
                        stored ? $"Stored {session.Label}'s character at logout." : $"Did not store {session.Label}'s character at logout.");
                }

                if (!session.Rejected)
                {
                    peer.m_rpc.Invoke(Protocol.UploadAck, chunk.TransferId, stored);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Character upload failed: {e}");
            }
        }

        private static bool HandleUpload(ZNetPeer peer, Session session, byte[] data)
        {
            if (session.FreshPlayerData != null)
            {
                return HandleResetUpload(peer, session, data);
            }

            bool isNew = session.AwaitingInitial;

            if (isNew && ModConfig.NewCharacters.Value == NewCharacterPolicy.ResetToFresh)
            {
                StartReset(peer, session, data);
                return false;
            }

            List<string> problems = new List<string>();
            PlayerProfile profile = ProfileCodec.Decode(data, "serverauthority", FileHelpers.FileSource.Local);
            PlayerDataSummary summary = null;

            if (profile == null)
            {
                problems.Add("the upload is not a readable character");
            }
            else
            {
                summary = PlayerDataSummary.Read(profile.m_playerData, out string error);
                if (summary == null)
                {
                    int version = PlayerDataSummary.ReadVersion(profile.m_playerData);
                    if (version > PlayerDataSummary.SupportedPlayerDataVersion)
                    {
                        Plugin.Log.LogWarning(
                            $"Cannot check the items and skills in {session.Label}'s character ({error}). " +
                            "Checking its name, id and cheat flag only. A game update probably changed the save format.");
                    }
                    else if (version >= 0 && version < PlayerDataSummary.SupportedPlayerDataVersion)
                    {
                        problems.Add(
                            $"the character was saved by an older version of the game ({error}), load it once " +
                            "in single player so the game saves it in the current format");
                    }
                    else
                    {
                        problems.Add($"the character's contents are not readable ({error})");
                    }
                }

                problems.AddRange(CharacterValidator.Validate(
                    profile, summary, peer.m_playerName, session.Baseline, isNew));
            }

            if (problems.Count > 0)
            {
                string list = string.Join("; ", problems);
                Plugin.Log.LogWarning($"Rejected a character save from {session.Label}: {list}");

                if (isNew)
                {
                    Reject(peer, session, "This server did not accept your character:\n" + string.Join("\n", Limit(problems, 8)));
                }
                else if (ModConfig.OnInvalidUpload.Value == InvalidUploadAction.Kick)
                {
                    Reject(peer, session,
                        "Your character failed the server's checks and was not saved. Reconnect to " +
                        "continue from the server's last good copy.\n" + string.Join("\n", Limit(problems, 8)));
                }

                return false;
            }

            try
            {
                CharacterStore.Save(session.StorePath, data);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not store {session.Label}'s character at {session.StorePath}: {e.Message}");
                if (isNew)
                {
                    Reject(peer, session, "The server could not store your character. Ask the server admin.");
                }

                return false;
            }

            session.Baseline = CharacterValidator.BaselineOf(profile, summary);

            if (isNew)
            {
                Plugin.Log.LogInfo($"Stored {session.Label}'s character for the first time.");
                session.AwaitingInitial = false;
                session.Synced = true;
                session.LastSaveRequest = Time.time;
                SendMessage(peer, session, ProfileMessage.Accepted, null);
            }

            return true;
        }

        private static void StartReset(ZNetPeer peer, Session session, byte[] data)
        {
            PlayerProfile profile = ProfileCodec.Decode(data, "serverauthority", FileHelpers.FileSource.Local);
            PlayerDataSummary summary = profile != null ? PlayerDataSummary.Read(profile.m_playerData, out _) : null;
            if (summary == null)
            {
                Reject(peer, session, "The server could not read your character to reset it.");
                return;
            }

            if (!string.Equals(profile.GetName(), peer.m_playerName, StringComparison.OrdinalIgnoreCase))
            {
                Reject(peer, session, "The character you sent is not the one you connected with.");
                return;
            }

            session.FreshPlayerData = FreshCharacter.BuildPlayerData(summary);
            session.FreshPlayerId = profile.GetPlayerID();
            session.AwaitingSince = Time.time;
            Plugin.Log.LogInfo($"{session.Label} is new to this world. Resetting them to a fresh character.");
            SendMessage(peer, session, ProfileMessage.ResetToFresh, session.FreshPlayerData);
        }

        private static bool HandleResetUpload(ZNetPeer peer, Session session, byte[] data)
        {
            PlayerProfile profile = ProfileCodec.Decode(data, "serverauthority", FileHelpers.FileSource.Local);

            string problem = null;
            if (profile == null)
            {
                problem = "the reset character is not readable";
            }
            else if (!string.Equals(profile.GetName(), peer.m_playerName, StringComparison.OrdinalIgnoreCase))
            {
                problem = "the reset character has a different name";
            }
            else if (profile.GetPlayerID() != session.FreshPlayerId)
            {
                problem = "the reset character has a different id";
            }
            else if (!profile.m_firstSpawn || !BytesEqual(profile.m_playerData, session.FreshPlayerData))
            {
                problem = "the character was not reset to what the server sent";
            }

            if (problem != null)
            {
                Plugin.Log.LogWarning($"{session.Label}: {problem}.");
                Reject(peer, session, "Your character could not be reset for this server: " + problem + ".");
                return false;
            }

            try
            {
                CharacterStore.Save(session.StorePath, data);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not store {session.Label}'s character at {session.StorePath}: {e.Message}");
                Reject(peer, session, "The server could not store your character. Ask the server admin.");
                return false;
            }

            Plugin.Log.LogInfo($"Stored {session.Label}'s fresh character for the first time.");
            session.Baseline = CharacterValidator.BaselineOf(profile, PlayerDataSummary.Read(profile.m_playerData, out _));
            session.FreshPlayerData = null;
            session.AwaitingInitial = false;
            session.Synced = true;
            session.LastSaveRequest = Time.time;
            SendMessage(peer, session, ProfileMessage.Accepted, null);
            return true;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        internal static void Tick()
        {
            if (!ModConfig.CharactersEnabled.Value || Sessions.Count == 0)
            {
                return;
            }

            float now = Time.time;
            float interval = ModConfig.CharacterSaveIntervalSeconds.Value;
            float timeout = ModConfig.CharacterSyncTimeoutSeconds.Value;

            List<KeyValuePair<ZNetPeer, Session>> snapshot = new List<KeyValuePair<ZNetPeer, Session>>(Sessions);
            foreach (KeyValuePair<ZNetPeer, Session> entry in snapshot)
            {
                ZNetPeer peer = entry.Key;
                Session session = entry.Value;
                if (session.Rejected || peer.m_rpc == null)
                {
                    continue;
                }

                if (session.AwaitingInitial && now - session.AwaitingSince > timeout)
                {
                    Reject(peer, session, "Your game did not send your character to the server in time.");
                    continue;
                }

                if (session.Synced && interval > 0f && now - session.LastSaveRequest > interval)
                {
                    session.LastSaveRequest = now;
                    peer.m_rpc.Invoke("SavePlayerProfile");
                }
            }
        }

        internal static void BeforeShutdown(bool save)
        {
            Game game = Game.instance;
            float timeout = ModConfig.CharacterShutdownSaveSeconds.Value;
            if (!save || !ModConfig.CharactersEnabled.Value || timeout <= 0f || game == null || game.m_shuttingDown ||
                Sessions.Count == 0)
            {
                return;
            }

            List<ZNetPeer> peers = new List<ZNetPeer>();
            List<Session> sessions = new List<Session>();
            List<int> marks = new List<int>();
            List<int> storedMarks = new List<int>();
            foreach (KeyValuePair<ZNetPeer, Session> entry in Sessions)
            {
                ZNetPeer peer = entry.Key;
                Session session = entry.Value;
                if (!session.Synced || session.Rejected || peer.m_rpc == null || !peer.m_rpc.IsConnected())
                {
                    continue;
                }

                peers.Add(peer);
                sessions.Add(session);
                marks.Add(session.UploadsReceived);
                storedMarks.Add(session.UploadsStored);
                peer.m_rpc.Invoke("SavePlayerProfile");
                peer.m_socket.Flush();
            }

            if (peers.Count == 0)
            {
                return;
            }

            Plugin.Log.LogInfo(
                $"Shutting down. Asking {peers.Count} player(s) for a final character save, waiting up to {timeout:0} seconds.");

            long[] arrived = new long[peers.Count];
            for (int i = 0; i < arrived.Length; i++)
            {
                arrived[i] = -1;
            }

            Stopwatch clock = Stopwatch.StartNew();
            float last = 0f;
            while (clock.Elapsed.TotalSeconds < timeout)
            {
                float now = (float)clock.Elapsed.TotalSeconds;
                float dt = now - last;
                last = now;

                ZSteamSocket.UpdateAllSockets(dt);
                ZPlayFabSocket.UpdateAllSockets(dt);
                ZPlayFabSocket.LateUpdateAllSocket();

                bool pending = false;
                for (int i = 0; i < peers.Count; i++)
                {
                    if (arrived[i] >= 0 || sessions[i].Rejected || !peers[i].m_rpc.IsConnected())
                    {
                        continue;
                    }

                    peers[i].m_socket.Flush();
                    peers[i].m_rpc.Update(dt);

                    if (sessions[i].UploadsReceived > marks[i])
                    {
                        arrived[i] = clock.ElapsedMilliseconds;
                    }
                    else
                    {
                        pending = true;
                    }
                }

                if (!pending)
                {
                    break;
                }

                Thread.Sleep(10);
            }

            int saved = 0;
            for (int i = 0; i < peers.Count; i++)
            {
                Session session = sessions[i];
                if (arrived[i] >= 0 && session.UploadsStored > storedMarks[i])
                {
                    saved++;
                    Plugin.Log.LogInfo($"Stored {session.Label}'s final save before shutdown, after {arrived[i]} ms.");
                }
                else if (arrived[i] >= 0 || session.Rejected)
                {
                    Plugin.Log.LogWarning(
                        $"{session.Label}'s final save before shutdown was not stored. They keep their last stored copy.");
                }
                else if (!peers[i].m_rpc.IsConnected())
                {
                    Plugin.Log.LogWarning(
                        $"{session.Label} disconnected before their final save arrived. They keep their last stored copy.");
                }
                else
                {
                    Plugin.Log.LogWarning(
                        $"{session.Label}'s final save did not arrive within {timeout:0} seconds. They keep their last stored copy.");
                }
            }

            Plugin.Log.LogInfo(
                $"Final saves before shutdown: {saved} of {peers.Count} stored in {clock.ElapsedMilliseconds} ms.");
        }

        private static void SendMessage(ZNetPeer peer, Session session, ProfileMessage message, byte[] data)
        {
            Protocol.SendChunked(peer.m_rpc, Protocol.Profile, (int)message, session.NextTransferId++, data);
        }

        private static void Reject(ZNetPeer peer, Session session, string reason)
        {
            session.Rejected = true;
            session.Synced = false;
            session.AwaitingInitial = false;
            Plugin.Log.LogWarning($"Kicking {session.Label ?? peer.m_socket.GetEndPointString()}: {reason.Replace('\n', ' ')}");

            if (session.Manifest != null)
            {
                peer.m_rpc.Invoke(Protocol.Rejected, reason);
            }

            ZNet.instance.InternalKick(peer);
        }

        private static IEnumerable<string> Limit(List<string> items, int max)
        {
            for (int i = 0; i < items.Count && i < max; i++)
            {
                yield return items[i];
            }

            if (items.Count > max)
            {
                yield return $"...and {items.Count - max} more.";
            }
        }
    }
}
