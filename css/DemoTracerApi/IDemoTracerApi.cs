using System.Collections.Generic;

namespace DemoTracerApi;

public interface IDemoTracerApi
{
    int ApiVersion { get; }

    bool TryLoadNadeManifest(
        string manifestPath,
        out DemoTracerNadeManifest manifest,
        out string error);

    bool TryRunNadeClip(
        string manifestPath,
        string clipId,
        int slot,
        bool loop,
        out DemoTracerNadeRunResult result);

    bool TryRunNadeClipDirect(
        string clipBasePath,
        DemoTracerNadeClip clip,
        int slot,
        bool loop,
        out DemoTracerNadeRunResult result);

    bool IsSlotBusy(int slot);

    bool IsDemoTracerBot(int slot);
}

public sealed class DemoTracerNadeManifest
{
    public int FormatVersion { get; set; }

    public string Map { get; set; } = string.Empty;

    public string CoordinateMode { get; set; } = string.Empty;

    public float TickRate { get; set; }

    public List<DemoTracerNadeClip> Clips { get; set; } = new();
}

public sealed class DemoTracerNadeClip
{
    public string ClipId { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string GrenadeType { get; set; } = string.Empty;

    public int WeaponDefIndex { get; set; }

    public int FirstWeaponDefIndex { get; set; }

    public string Phase { get; set; } = string.Empty;

    public int Round { get; set; }

    public string Side { get; set; } = string.Empty;

    public ulong SteamId { get; set; }

    public string PlayerName { get; set; } = string.Empty;

    public int ThrowTick { get; set; }

    public DemoTracerVector3 StartOrigin { get; set; } = new();

    public float StartYaw { get; set; }

    public DemoTracerVector3 ProjectileInitialVelocity { get; set; } = new();

    public DemoTracerVector3 ProjectileDetonationPosition { get; set; } = new();

    public float DurationSeconds { get; set; }
}

public sealed class DemoTracerNadeRunResult
{
    public bool Queued { get; set; }

    public int Slot { get; set; }

    public string ClipId { get; set; } = string.Empty;

    public float DurationSeconds { get; set; }

    public string Message { get; set; } = string.Empty;
}

public sealed class DemoTracerVector3
{
    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }
}
