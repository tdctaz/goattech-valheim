using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

namespace ServerAuthority.Integrity
{
    internal static class IntegrityClient
    {
        private const float FinalSaveTimeout = 10f;

        private enum FinalSave
        {
            None,
            Waiting,
            Done,
        }

        private static FinalSave _final;
        private static int _finalTransfer;
        private static int _lastUpload = -1;
        private static bool _finalUploadNext;
        private static ZRpc _server;
        private static bool _managed;
        private static bool _ready;
        private static bool _heldIntro;
        private static bool _failed;
        private static float _timeout;
        private static float _waitingSince;
        private static int _nextUpload;
        private static string _restoredNotice;
        private static float _noticeAt;
        private static readonly ChunkAssembler Incoming = new ChunkAssembler();

        internal static string RejectReason;

        internal static bool HoldSpawn => _managed && !_ready;

        internal static void Reset()
        {
            _server = null;
            _managed = false;
            _ready = false;
            _heldIntro = false;
            _failed = false;
            _restoredNotice = null;
            _noticeAt = 0f;
            _final = FinalSave.None;
            _lastUpload = -1;
            Incoming.Reset();
        }

        internal static bool SuppressLocalSave => _final != FinalSave.None && _managed;

        internal static void BeforeShutdown(bool save)
        {
            Game game = Game.instance;
            if (!save || game == null || game.m_shuttingDown || _final != FinalSave.None || !BeginFinalSave())
            {
                return;
            }

            Stopwatch clock = Stopwatch.StartNew();
            float last = 0f;
            while (_final == FinalSave.Waiting && clock.Elapsed.TotalSeconds < FinalSaveTimeout && _server.IsConnected())
            {
                float now = (float)clock.Elapsed.TotalSeconds;
                float dt = now - last;
                last = now;

                ZSteamSocket.UpdateAllSockets(dt);
                ZPlayFabSocket.UpdateAllSockets(dt);
                ZPlayFabSocket.LateUpdateAllSocket();
                _server.GetSocket().Flush();
                _server.Update(dt);
                Thread.Sleep(10);
            }

            if (_final == FinalSave.Waiting)
            {
                Plugin.Log.LogWarning(
                    $"The server did not confirm the character sent at logout within {FinalSaveTimeout:0} seconds. Leaving anyway.");
            }
            else
            {
                Plugin.Log.LogInfo($"The server confirmed the character in {clock.ElapsedMilliseconds} ms.");
            }

            _final = FinalSave.Done;
        }

        private static bool BeginFinalSave()
        {
            Game game = Game.instance;
            if (!_managed || !_ready || game == null || _server == null || !_server.IsConnected() ||
                ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
            {
                return false;
            }

            _lastUpload = -1;
            _finalUploadNext = true;
            try
            {
                game.SavePlayerProfile(setLogoutPoint: true);
            }
            finally
            {
                _finalUploadNext = false;
            }

            if (_lastUpload < 0)
            {
                return false;
            }

            _final = FinalSave.Waiting;
            _finalTransfer = _lastUpload;
            Plugin.Log.LogInfo("Sending the character to the server before leaving.");
            return true;
        }

        private static void RPC_UploadAck(ZRpc rpc, int transferId, bool stored)
        {
            if (_final != FinalSave.Waiting || transferId != _finalTransfer)
            {
                return;
            }

            if (!stored)
            {
                Plugin.Log.LogWarning("The server did not store the character sent at logout.");
            }

            _final = FinalSave.Done;
        }

        internal static void OnNewConnection(ZNetPeer peer)
        {
            Reset();
            RejectReason = null;
            _server = peer.m_rpc;

            peer.m_rpc.Register<ZPackage>(Protocol.ServerInfo, RPC_ServerInfo);
            peer.m_rpc.Register<ZPackage>(Protocol.Profile, RPC_Profile);
            peer.m_rpc.Register<string>(Protocol.Rejected, RPC_Rejected);
            peer.m_rpc.Register<int, bool>(Protocol.UploadAck, RPC_UploadAck);

            byte[] local = ProfileCodec.ReadSaved(Game.instance?.GetPlayerProfile());
            peer.m_rpc.Invoke(Protocol.Manifest, ModManifest.BuildLocal(local).Write());
        }

        private static void RPC_ServerInfo(ZRpc rpc, ZPackage pkg)
        {
            try
            {
                int protocol = pkg.ReadInt();
                pkg.ReadBool();
                bool characters = pkg.ReadBool();
                _timeout = pkg.ReadSingle();

                if (protocol != Protocol.Version)
                {
                    Plugin.Log.LogWarning(
                        $"The server runs Server Authority protocol {protocol}, this game has {Protocol.Version}.");
                    return;
                }

                _managed = characters;
                _waitingSince = Time.time;
                if (characters)
                {
                    Plugin.Log.LogInfo("This server keeps characters server side. Waiting for ours.");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Unreadable server info: {e.Message}");
            }
        }

        private static void RPC_Rejected(ZRpc rpc, string reason)
        {
            RejectReason = reason;
            Plugin.Log.LogWarning($"The server rejected this game: {reason}");
        }

        private static void RPC_Profile(ZRpc rpc, ZPackage pkg)
        {
            try
            {
                Chunk chunk = Chunk.Read(pkg);
                switch ((ProfileMessage)chunk.Kind)
                {
                    case ProfileMessage.Unmanaged:
                        _managed = false;
                        _ready = true;
                        break;

                    case ProfileMessage.RequestInitial:
                        Plugin.Log.LogInfo("The server has no copy of this character yet. Sending it.");
                        Upload(Game.instance.GetPlayerProfile());
                        break;

                    case ProfileMessage.Accepted:
                        Plugin.Log.LogInfo("The server accepted this character.");
                        Proceed();
                        break;

                    case ProfileMessage.StoredCopy:
                        if (Incoming.TryAdd(chunk, out byte[] data, out string error))
                        {
                            ApplyServerCopy(data);
                        }
                        else if (error != null)
                        {
                            Fail($"The server sent a damaged character ({error}).");
                        }

                        break;

                    case ProfileMessage.ResetToFresh:
                        if (Incoming.TryAdd(chunk, out byte[] fresh, out string freshError))
                        {
                            ResetToFresh(fresh);
                        }
                        else if (freshError != null)
                        {
                            Fail($"The server sent a damaged character ({freshError}).");
                        }

                        break;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Handling the server's character failed: {e}");
                Fail("Could not load your character from the server.");
            }
        }

        private static void ApplyServerCopy(byte[] data)
        {
            Game game = Game.instance;
            PlayerProfile local = game.GetPlayerProfile();
            PlayerProfile server = ProfileCodec.Decode(data, local.m_filename, local.m_fileSource);
            if (server == null)
            {
                Fail("The server's copy of your character could not be read.");
                return;
            }

            byte[] localData = ProfileCodec.ReadSaved(local);
            if (ModManifest.HashBytes(localData) != ModManifest.HashBytes(data))
            {
                Backup(local, localData);
                _restoredNotice = "Your character was restored from the server's copy.";
                Plugin.Log.LogInfo(
                    "The local character differed from the server's copy and was replaced by it. The " +
                    "local file was backed up first.");
            }

            Install(server);
            Proceed();
        }

        private static void ResetToFresh(byte[] playerData)
        {
            Game game = Game.instance;
            PlayerProfile local = game.GetPlayerProfile();
            Backup(local, ProfileCodec.ReadSaved(local));

            PlayerProfile fresh = new PlayerProfile(local.m_filename, local.m_fileSource);
            fresh.SetName(local.GetName());
            fresh.m_playerID = local.GetPlayerID();
            fresh.m_dateCreated = local.m_dateCreated;
            fresh.m_playerData = playerData;
            fresh.m_firstSpawn = true;

            Install(fresh);
            fresh.Save();

            _restoredNotice = "This server starts every new character fresh. Your previous character was backed up.";
            Plugin.Log.LogInfo("The server reset this character to a fresh start. Sending the reset copy back.");
            Upload(fresh);
        }

        private static void Install(PlayerProfile profile)
        {
            Game game = Game.instance;
            game.m_playerProfile = profile;
            if (!profile.m_firstSpawn)
            {
                game.m_queuedIntro = false;
                _heldIntro = false;
            }

            Minimap map = Minimap.instance;
            if (map != null && map.m_hasGenerated)
            {
                if (profile.GetMapData() != null)
                {
                    map.LoadMapData();
                }
                else
                {
                    map.Reset();
                    map.ClearPins();
                }
            }
        }

        private static void Proceed()
        {
            _ready = true;
            Game game = Game.instance;
            if (game != null && _heldIntro)
            {
                game.m_queuedIntro = game.GetPlayerProfile().m_firstSpawn;
            }

            _heldIntro = false;
        }

        private static void Backup(PlayerProfile profile, byte[] data)
        {
            try
            {
                if (data == null || data.Length == 0)
                {
                    return;
                }

                string folder = SaveSystem.GetCharacterFolderPath(FileHelpers.FileSource.Local);
                Directory.CreateDirectory(folder);
                string backup = Path.Combine(
                    folder,
                    $"{profile.m_filename}_backup_serverauthority-{DateTime.Now:yyyyMMdd-HHmmss}.fch");
                CharacterStore.WriteFramed(backup, data);
                SaveSystem.InvalidateCache(SaveDataType.Character);
                Plugin.Log.LogInfo($"Backed up the local character to {backup}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not back up the local character: {e.Message}");
            }
        }

        internal static void OnSaved()
        {
            if (!_managed || !_ready || _final != FinalSave.None || _server == null || !_server.IsConnected())
            {
                return;
            }

            Upload(Game.instance.GetPlayerProfile(), _finalUploadNext ? Protocol.UploadFinal : Protocol.UploadRoutine);
        }

        private static void Upload(PlayerProfile profile, int kind = Protocol.UploadRoutine)
        {
            byte[] data = ProfileCodec.ReadSaved(profile);
            if (data == null)
            {
                Plugin.Log.LogWarning("There is no saved character to send to the server.");
                return;
            }

            if (_server == null || !_server.IsConnected())
            {
                return;
            }

            _lastUpload = _nextUpload++;
            Protocol.SendChunked(_server, Protocol.Upload, kind, _lastUpload, data);
            _server.GetSocket().Flush();
        }

        internal static void Tick()
        {
            Game game = Game.instance;
            if (game == null)
            {
                return;
            }

            if (HoldSpawn)
            {
                if (game.m_queuedIntro)
                {
                    game.m_queuedIntro = false;
                    _heldIntro = true;
                }

                if (Time.time - _waitingSince > _timeout)
                {
                    Fail("The server did not send your character in time.");
                }

                return;
            }

            if (_restoredNotice == null || Player.m_localPlayer == null || Player.m_localPlayer.InIntro() || Chat.instance == null)
            {
                return;
            }

            if (_noticeAt <= 0f)
            {
                _noticeAt = Time.realtimeSinceStartup + 2f;
                return;
            }

            if (Time.realtimeSinceStartup < _noticeAt)
            {
                return;
            }

            Chat.instance.m_hideTimer = 0f;
            Chat.instance.AddString("Server", _restoredNotice, Talker.Type.Normal);
            _restoredNotice = null;
        }

        private static void Fail(string reason)
        {
            if (!_failed)
            {
                _failed = true;
                RejectReason = reason;
                Plugin.Log.LogError(reason);
            }

            ZNet.m_connectionStatus = ZNet.ConnectionStatus.ErrorDisconnected;
        }
    }
}
