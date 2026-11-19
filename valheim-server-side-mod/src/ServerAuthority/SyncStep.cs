using System;
using System.Collections.Generic;

namespace ServerAuthority
{
    internal static class SyncStep
    {
        private static readonly List<IMonoUpdater> Others = new List<IMonoUpdater>();
        private static readonly List<IMonoUpdater> Unowned = new List<IMonoUpdater>();
        private static readonly List<IMonoUpdater> Work = new List<IMonoUpdater>();

        private static int _configured;
        private static int _override = -1;
        private static int _every;
        private static int _steps;

        internal static bool Engaged => Plugin.ServerActive && _every > 0;

        internal static void Configure(int every)
        {
            _configured = Math.Max(0, every);
            _every = _override >= 0 ? _override : _configured;
            _steps = 0;
            Others.Clear();
            Unowned.Clear();
        }

        internal static void SetOverride(int every)
        {
            _override = every;
            _every = _override >= 0 ? _override : _configured;
            _steps = 0;
            Others.Clear();
            Unowned.Clear();
            Plugin.Log.LogInfo(_every > 0
                ? $"Sync step: synced objects the server owns are skipped and unowned ones visited once every {_every} physics steps, ownership re-sorted as often."
                : "Sync step: every synced object is visited each physics step, as in vanilla.");
        }

        internal static void Run(List<IMonoUpdater> source, float deltaTime)
        {
            if (_steps % _every == 0)
            {
                Sort(source);
            }

            int slot = _steps % _every;
            _steps++;
            Work.AddRange(Others);
            for (int i = slot; i < Unowned.Count; i += _every)
            {
                Work.Add(Unowned[i]);
            }

            foreach (IMonoUpdater item in Work)
            {
                ZSyncTransform sync = item as ZSyncTransform;
                if (sync != null && sync.isActiveAndEnabled)
                {
                    sync.CustomFixedUpdate(deltaTime);
                }
            }

            Work.Clear();
        }

        private static void Sort(List<IMonoUpdater> source)
        {
            Others.Clear();
            Unowned.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                ZSyncTransform sync = source[i] as ZSyncTransform;
                if (sync == null)
                {
                    continue;
                }

                ZNetView view = sync.m_nview;
                if (view == null || !view.IsValid())
                {
                    continue;
                }

                ZDO zdo = view.GetZDO();
                if (zdo.IsOwner())
                {
                    continue;
                }

                if (zdo.HasOwner())
                {
                    Others.Add(sync);
                }
                else
                {
                    Unowned.Add(sync);
                }
            }
        }
    }
}
