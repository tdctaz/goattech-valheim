using System;
using System.Collections.Generic;

namespace ServerAuthority.Integrity
{
    internal enum NewCharacterPolicy
    {
        ResetToFresh,
        Accept,
        RequireNew,
    }

    internal enum InvalidUploadAction
    {
        Kick,
        LogOnly,
    }

    internal enum ProfileMessage
    {
        Unmanaged = 0,
        RequestInitial = 1,
        StoredCopy = 2,
        Accepted = 3,
        ResetToFresh = 4,
    }

    internal static class Protocol
    {
        internal const int Version = 2;

        internal const string Manifest = "ServerAuthority_Manifest";
        internal const string ServerInfo = "ServerAuthority_ServerInfo";
        internal const string Profile = "ServerAuthority_Profile";
        internal const string Upload = "ServerAuthority_Upload";
        internal const string Rejected = "ServerAuthority_Rejected";
        internal const string UploadAck = "ServerAuthority_UploadAck";

        internal const int UploadRoutine = 0;
        internal const int UploadFinal = 1;

        internal const int ChunkSize = 256 * 1024;
        internal const int MaxChunks = 128;

        internal static void SendChunked(ZRpc rpc, string method, int kind, int transferId, byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                ZPackage empty = new ZPackage();
                empty.Write(kind);
                empty.Write(transferId);
                empty.Write(0);
                empty.Write(0);
                empty.Write(Array.Empty<byte>());
                rpc.Invoke(method, empty);
                return;
            }

            int count = (data.Length + ChunkSize - 1) / ChunkSize;
            for (int i = 0; i < count; i++)
            {
                int offset = i * ChunkSize;
                int length = Math.Min(ChunkSize, data.Length - offset);
                byte[] part = new byte[length];
                Buffer.BlockCopy(data, offset, part, 0, length);

                ZPackage pkg = new ZPackage();
                pkg.Write(kind);
                pkg.Write(transferId);
                pkg.Write(i);
                pkg.Write(count);
                pkg.Write(part);
                rpc.Invoke(method, pkg);
            }
        }

        internal static List<string> ParseList(string value)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(value))
            {
                return result;
            }

            foreach (string raw in value.Split(','))
            {
                string item = raw.Trim();
                if (item.Length > 0)
                {
                    result.Add(item);
                }
            }

            return result;
        }
    }

    internal struct Chunk
    {
        internal int Kind;
        internal int TransferId;
        internal int Index;
        internal int Count;
        internal byte[] Data;

        internal static Chunk Read(ZPackage pkg)
        {
            return new Chunk
            {
                Kind = pkg.ReadInt(),
                TransferId = pkg.ReadInt(),
                Index = pkg.ReadInt(),
                Count = pkg.ReadInt(),
                Data = pkg.ReadByteArray(),
            };
        }
    }

    internal sealed class ChunkAssembler
    {
        private int _transferId = -1;
        private byte[][] _parts;
        private int _received;

        internal bool TryAdd(Chunk chunk, out byte[] complete, out string error)
        {
            complete = null;
            error = null;

            if (chunk.Count <= 0 || chunk.Count > Protocol.MaxChunks || chunk.Index < 0 || chunk.Index >= chunk.Count)
            {
                error = $"malformed chunk {chunk.Index}/{chunk.Count}";
                Reset();
                return false;
            }

            if (chunk.Data == null || chunk.Data.Length > Protocol.ChunkSize)
            {
                error = "oversized chunk";
                Reset();
                return false;
            }

            if (_parts == null || chunk.TransferId != _transferId || _parts.Length != chunk.Count)
            {
                _transferId = chunk.TransferId;
                _parts = new byte[chunk.Count][];
                _received = 0;
            }

            if (_parts[chunk.Index] == null)
            {
                _received++;
            }

            _parts[chunk.Index] = chunk.Data;

            if (_received < _parts.Length)
            {
                return false;
            }

            int total = 0;
            foreach (byte[] part in _parts)
            {
                total += part.Length;
            }

            complete = new byte[total];
            int offset = 0;
            foreach (byte[] part in _parts)
            {
                Buffer.BlockCopy(part, 0, complete, offset, part.Length);
                offset += part.Length;
            }

            Reset();
            return true;
        }

        internal void Reset()
        {
            _transferId = -1;
            _parts = null;
            _received = 0;
        }
    }
}
