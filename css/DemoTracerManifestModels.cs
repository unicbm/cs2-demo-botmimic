using System.Text.Json.Serialization;

namespace DemoTracer;

public sealed partial class DemoTracerPlugin
{
    private sealed class ConversionManifest
    {
        [JsonPropertyName("format_version")]
        public int FormatVersion { get; set; }

        [JsonPropertyName("dtr_format_version")]
        public int DtrFormatVersion { get; set; }

        [JsonPropertyName("abi")]
        public int Abi { get; set; }

        [JsonPropertyName("map")]
        public string Map { get; set; } = string.Empty;

        [JsonPropertyName("files")]
        public List<ManifestFile> Files { get; set; } = new();

        public int EffectiveDtrFormatVersion => DtrFormatVersion != 0 ? DtrFormatVersion : FormatVersion;
    }

    private sealed class NadeManifest
    {
        [JsonPropertyName("format_version")]
        public int FormatVersion { get; set; }

        [JsonPropertyName("map")]
        public string Map { get; set; } = string.Empty;

        [JsonPropertyName("coordinate_mode")]
        public string CoordinateMode { get; set; } = string.Empty;

        [JsonPropertyName("tickrate")]
        public float TickRate { get; set; }

        [JsonPropertyName("tick_rate")]
        public float TickRateAlt
        {
            get => TickRate;
            set => TickRate = value;
        }

        [JsonPropertyName("clips")]
        public List<NadeClip> Clips { get; set; } = new();
    }

    private sealed record CachedNadeManifest(
        NadeManifest Manifest,
        Dictionary<string, NadeClip> ClipsById,
        DateTime LastWriteUtc,
        long Length);

    private sealed class NadeClip
    {
        [JsonPropertyName("clip_id")]
        public string ClipId { get; set; } = string.Empty;

        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = string.Empty;

        [JsonPropertyName("grenade_type")]
        public string GrenadeType { get; set; } = string.Empty;

        [JsonPropertyName("weapon_def_index")]
        public int WeaponDefIndex { get; set; }

        [JsonPropertyName("phase")]
        public string Phase { get; set; } = string.Empty;

        [JsonPropertyName("round")]
        public int Round { get; set; }

        [JsonPropertyName("side")]
        public string Side { get; set; } = string.Empty;

        [JsonPropertyName("start_origin")]
        public float[]? StartOrigin { get; set; }

        [JsonPropertyName("start_yaw")]
        public float StartYaw { get; set; }

        [JsonPropertyName("projectile_initial_velocity")]
        public float[]? ProjectileInitialVelocity { get; set; }

        [JsonPropertyName("projectile_detonation_position")]
        public float[]? ProjectileDetonationPosition { get; set; }

        [JsonPropertyName("duration_seconds")]
        public float DurationSeconds { get; set; }

        [JsonPropertyName("steam_id")]
        public ulong SteamId { get; set; }

        [JsonPropertyName("player_name")]
        public string PlayerName { get; set; } = string.Empty;

        [JsonPropertyName("throw_tick")]
        public int ThrowTick { get; set; }

        [JsonPropertyName("first_weapon_def_index")]
        public int FirstWeaponDefIndex { get; set; }

        [JsonPropertyName("preload_weapon_def_indices")]
        public int[]? PreloadWeaponDefIndices { get; set; }

        [JsonPropertyName("loadout")]
        public ReplayLoadoutSnapshot? Loadout { get; set; }
    }

    private sealed class RoundPoolManifest
    {
        [JsonPropertyName("format_version")]
        public int FormatVersion { get; set; }

        [JsonPropertyName("abi")]
        public int Abi { get; set; }

        [JsonPropertyName("map")]
        public string Map { get; set; } = string.Empty;

        [JsonPropertyName("candidates")]
        public List<RoundPoolCandidate> Candidates { get; set; } = new();
    }

    private sealed class RoundPoolCandidate
    {
        [JsonPropertyName("manifest")]
        public string Manifest { get; set; } = string.Empty;

        [JsonPropertyName("demo_stem")]
        public string DemoStem { get; set; } = string.Empty;

        [JsonPropertyName("demo_path")]
        public string DemoPath { get; set; } = string.Empty;

        [JsonPropertyName("source_round")]
        public int SourceRound { get; set; }

        [JsonPropertyName("pistol_round")]
        public bool PistolRound { get; set; }

        [JsonPropertyName("t_economy")]
        public PoolTeamEconomy TEconomy { get; set; } = new();

        [JsonPropertyName("ct_economy")]
        public PoolTeamEconomy CtEconomy { get; set; } = new();

        [JsonPropertyName("duration_seconds")]
        public float DurationSeconds { get; set; }

        [JsonPropertyName("cut_reason")]
        public string? CutReason { get; set; }

        [JsonPropertyName("files")]
        public int Files { get; set; }
    }

    private sealed class PoolTeamEconomy
    {
        [JsonPropertyName("side")]
        public string Side { get; set; } = string.Empty;

        [JsonPropertyName("players")]
        public int Players { get; set; }

        [JsonPropertyName("round_start_equipment_value")]
        public uint RoundStartEquipmentValue { get; set; }

        [JsonPropertyName("equipment_value_total")]
        public uint EquipmentValueTotal { get; set; }

        [JsonPropertyName("money_saved_total")]
        public uint MoneySavedTotal { get; set; }

        [JsonPropertyName("cash_spent_this_round")]
        public uint CashSpentThisRound { get; set; }

        [JsonPropertyName("class")]
        public string Class { get; set; } = "unknown";

        public uint BestEquipmentValue => Math.Max(RoundStartEquipmentValue, EquipmentValueTotal);
    }

    private sealed class ManifestFile
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("round")]
        public int Round { get; set; }

        [JsonPropertyName("side")]
        public string Side { get; set; } = string.Empty;

        [JsonPropertyName("steam_id")]
        public ulong SteamId { get; set; }

        [JsonPropertyName("player_name")]
        public string PlayerName { get; set; } = string.Empty;

        [JsonPropertyName("first_weapon_def_index")]
        public int? FirstWeaponDefIndex { get; set; }

        [JsonPropertyName("preload_weapon_def_indices")]
        public int[]? PreloadWeaponDefIndices { get; set; }

        [JsonPropertyName("loadout")]
        public ReplayLoadoutSnapshot? Loadout { get; set; }
    }

    private sealed class ReplayLoadoutSnapshot
    {
        [JsonPropertyName("weapon_def_indices")]
        public int[]? WeaponDefIndices { get; set; }

        [JsonPropertyName("armor_value")]
        public uint ArmorValue { get; set; }

        [JsonPropertyName("has_helmet")]
        public bool HasHelmet { get; set; }

        [JsonPropertyName("has_defuser")]
        public bool HasDefuser { get; set; }
    }
}
