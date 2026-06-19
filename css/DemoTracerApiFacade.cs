using DemoTracerApi;

namespace DemoTracer;

public sealed partial class DemoTracerPlugin
{
    private sealed class DemoTracerApiFacade : IDemoTracerApi
    {
        private readonly DemoTracerPlugin _plugin;

        public DemoTracerApiFacade(DemoTracerPlugin plugin)
        {
            _plugin = plugin;
        }

        public int ApiVersion => 2;

        public bool TryLoadNadeManifest(
            string manifestPath,
            out DemoTracerNadeManifest manifest,
            out string error)
        {
            manifest = new DemoTracerNadeManifest();
            if (!TryReadNadeManifest(manifestPath, out var internalManifest, out error))
                return false;

            manifest = ToApiManifest(internalManifest);
            return true;
        }

        public bool TryRunNadeClip(
            string manifestPath,
            string clipId,
            int slot,
            bool loop,
            out DemoTracerNadeRunResult result)
        {
            var run = _plugin.RunNadeClip(manifestPath, clipId, slot, loop, quiet: true);
            result = new DemoTracerNadeRunResult
            {
                Queued = run.Ok,
                Slot = slot,
                ClipId = clipId,
                Message = run.Message
            };
            return run.Ok;
        }

        public bool TryRunNadeClipDirect(
            string clipBasePath,
            DemoTracerNadeClip clip,
            int slot,
            bool loop,
            out DemoTracerNadeRunResult result)
        {
            var internalClip = FromApiClip(clip);
            var baseManifestPath = Path.Combine(
                string.IsNullOrWhiteSpace(clipBasePath) ? "." : clipBasePath,
                "__direct_nade_clip__.json");
            var run = _plugin.RunNadeClip(baseManifestPath, internalClip, slot, loop, quiet: true);
            result = new DemoTracerNadeRunResult
            {
                Queued = run.Ok,
                Slot = slot,
                ClipId = clip.ClipId,
                DurationSeconds = clip.DurationSeconds,
                Message = run.Message
            };
            return run.Ok;
        }

        public bool IsSlotBusy(int slot)
            => _plugin.IsReplaySlotBusy(slot);

        private static DemoTracerNadeManifest ToApiManifest(NadeManifest manifest)
        {
            var clips = new List<DemoTracerNadeClip>(manifest.Clips.Count);
            foreach (var clip in manifest.Clips)
                clips.Add(ToApiClip(clip));

            return new DemoTracerNadeManifest
            {
                FormatVersion = manifest.FormatVersion,
                Map = manifest.Map,
                CoordinateMode = manifest.CoordinateMode,
                TickRate = manifest.TickRate,
                Clips = clips
            };
        }

        private static DemoTracerNadeClip ToApiClip(NadeClip clip)
            => new()
            {
                ClipId = clip.ClipId,
                Path = clip.Path,
                Kind = clip.Kind,
                GrenadeType = clip.GrenadeType,
                WeaponDefIndex = clip.WeaponDefIndex,
                FirstWeaponDefIndex = clip.FirstWeaponDefIndex,
                Phase = clip.Phase,
                Round = clip.Round,
                Side = clip.Side,
                SteamId = clip.SteamId,
                PlayerName = clip.PlayerName,
                ThrowTick = clip.ThrowTick,
                StartOrigin = ToApiVector(clip.StartOrigin),
                StartYaw = clip.StartYaw,
                ProjectileInitialVelocity = ToApiVector(clip.ProjectileInitialVelocity),
                ProjectileDetonationPosition = ToApiVector(clip.ProjectileDetonationPosition),
                DurationSeconds = clip.DurationSeconds
            };

        private static NadeClip FromApiClip(DemoTracerNadeClip clip)
            => new()
            {
                ClipId = clip.ClipId,
                Path = clip.Path,
                Kind = clip.Kind,
                GrenadeType = clip.GrenadeType,
                WeaponDefIndex = clip.WeaponDefIndex,
                FirstWeaponDefIndex = clip.FirstWeaponDefIndex != 0 ? clip.FirstWeaponDefIndex : clip.WeaponDefIndex,
                Phase = clip.Phase,
                Round = clip.Round,
                Side = clip.Side,
                SteamId = clip.SteamId,
                PlayerName = clip.PlayerName,
                ThrowTick = clip.ThrowTick,
                StartOrigin = FromApiVector(clip.StartOrigin),
                StartYaw = clip.StartYaw,
                ProjectileInitialVelocity = FromApiVector(clip.ProjectileInitialVelocity),
                ProjectileDetonationPosition = FromApiVector(clip.ProjectileDetonationPosition),
                DurationSeconds = clip.DurationSeconds,
                PreloadWeaponDefIndices = clip.WeaponDefIndex != 0 ? [clip.WeaponDefIndex] : null
            };

        private static DemoTracerVector3 ToApiVector(float[]? values)
        {
            if (values == null || values.Length < 3)
                return new DemoTracerVector3();
            return new DemoTracerVector3
            {
                X = values[0],
                Y = values[1],
                Z = values[2]
            };
        }

        private static float[] FromApiVector(DemoTracerVector3? value)
            => value == null ? [0f, 0f, 0f] : [value.X, value.Y, value.Z];
    }
}
