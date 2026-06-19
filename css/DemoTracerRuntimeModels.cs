using CounterStrikeSharp.API.Core;

namespace DemoTracer;

public sealed partial class DemoTracerPlugin
{
    private enum ReplayStartAnchor
    {
        Live,
        RecordingStart,
        FreezePreroll,
    }

    private enum ReplayIdentityMode
    {
        Off,
        Name,
        Full,
    }

    private sealed class TickPlayerSnapshot
    {
        private readonly Dictionary<int, CCSPlayerController> _bySlot = new();

        public TickPlayerSnapshot(
            IReadOnlyList<CCSPlayerController> controllers,
            IReadOnlyList<CCSPlayerController> teamPlayers)
        {
            Controllers = controllers;
            TeamPlayers = teamPlayers;

            foreach (var controller in controllers)
            {
                if (controller is not { IsValid: true } || controller.Slot < 0)
                    continue;
                _bySlot.TryAdd(controller.Slot, controller);
            }
        }

        public IReadOnlyList<CCSPlayerController> Controllers { get; }
        public IReadOnlyList<CCSPlayerController> TeamPlayers { get; }

        public bool TryGetSlot(int slot, out CCSPlayerController player)
        {
            if (_bySlot.TryGetValue(slot, out var value))
            {
                player = value;
                return true;
            }

            player = null!;
            return false;
        }
    }

    private readonly record struct LoadRoundResult(bool Ok, string Message)
    {
        public static LoadRoundResult Success(string message) => new(true, message);
        public static LoadRoundResult Fail(string message) => new(false, message);
    }

    private readonly record struct LoadedReplay(
        string Path,
        string PlayerName,
        ulong SteamId,
        int FirstWeaponDefIndex,
        int[] PreloadWeaponDefIndices,
        bool HasLoadout,
        ReplayLoadoutSnapshot Loadout,
        ReplayProjectileEvent[] Projectiles,
        bool UtilityOnly,
        int UtilityWeaponDefIndex,
        float TickRate,
        uint PlayStartTickIndex);

    private readonly record struct ReplayAssignment(ManifestFile File, CCSPlayerController Bot);

    private readonly record struct PendingWeaponAlign(int WeaponDefIndex, bool ForceSwitch);

    private readonly record struct PendingBulletHit(int AttackerSlot, float Time);

    private readonly record struct PendingBulletDamage(int AttackerSlot, int Damage, float Time);

    private readonly record struct PendingThreat360(int EnemySlot, float FirstSeenAt);

    private readonly record struct TeamEconomySnapshot(uint EquipmentValue, string Class);

    private sealed class NadeCycleState(
        int token,
        string manifestPath,
        List<NadeClip> clips,
        int slot,
        string kindFilter,
        string sideFilter,
        string phaseFilter,
        float gapSeconds)
    {
        public int Token { get; } = token;
        public string ManifestPath { get; } = manifestPath;
        public List<NadeClip> Clips { get; } = clips;
        public int Slot { get; } = slot;
        public string KindFilter { get; } = kindFilter;
        public string SideFilter { get; } = sideFilter;
        public string PhaseFilter { get; } = phaseFilter;
        public float GapSeconds { get; } = gapSeconds;
        public int Index { get; set; }
        public bool Waiting { get; set; }
    }

    private sealed class PendingProjectileAlign(
        uint index,
        IntPtr handle,
        ReplayProjectileKind kind,
        int weaponDefIndex)
    {
        public uint Index { get; } = index;
        public IntPtr Handle { get; } = handle;
        public ReplayProjectileKind Kind { get; } = kind;
        public int WeaponDefIndex { get; } = weaponDefIndex;
        public ReplayProjectileEvent Align { get; set; }
        public int Slot { get; set; } = -1;
        public int EventIndex { get; set; } = -1;
        public int MatchAttemptsRemaining { get; set; }
        public int WritesRemaining { get; set; }
        public bool Matched { get; set; }
    }

    private readonly record struct TraceVector(float? X, float? Y, float? Z)
    {
        public static TraceVector Empty => new(null, null, null);
    }

    private sealed class UtilityProjectileTrace(uint index, IntPtr handle, string designerName)
    {
        private bool _hasLastPosition;
        private TraceVector _lastPosition = TraceVector.Empty;
        private float _lastTime;

        public uint Index { get; } = index;
        public IntPtr Handle { get; } = handle;
        public string DesignerName { get; } = designerName;

        public TraceVector EstimateVelocity(TraceVector position, float time)
        {
            if (!_hasLastPosition ||
                !position.X.HasValue ||
                !position.Y.HasValue ||
                !position.Z.HasValue ||
                !_lastPosition.X.HasValue ||
                !_lastPosition.Y.HasValue ||
                !_lastPosition.Z.HasValue)
            {
                return TraceVector.Empty;
            }

            var dt = time - _lastTime;
            if (dt <= 0.0f)
                return TraceVector.Empty;

            return new TraceVector(
                (position.X.Value - _lastPosition.X.Value) / dt,
                (position.Y.Value - _lastPosition.Y.Value) / dt,
                (position.Z.Value - _lastPosition.Z.Value) / dt);
        }

        public void Update(TraceVector position, float time)
        {
            _lastPosition = position;
            _lastTime = time;
            _hasLastPosition = position.X.HasValue && position.Y.HasValue && position.Z.HasValue;
        }
    }

    private enum ReplayWeaponSlot
    {
        Other,
        Primary,
        Secondary,
        Utility,
        C4,
        Taser,
        Knife
    }

    private enum HandoffMode
    {
        Off,
        Death,
        Contact,
        DeathOrContact
    }
}
