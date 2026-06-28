using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DemoTracer;

public sealed partial class DemoTracerPlugin
{
    private const string RuntimeConfigFileName = "demotracer.config.json";

    private static readonly JsonSerializerOptions RuntimeConfigJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    [ConsoleCommand("dtr_config_reload", "dtr_config_reload")]
    public void ConfigReloadCommand(CCSPlayerController? player, CommandInfo command)
    {
        LoadRuntimeConfig(command.ReplyToCommand, announceMissing: true);
    }

    [ConsoleCommand("dtr_config_status", "dtr_config_status")]
    public void ConfigStatusCommand(CCSPlayerController? player, CommandInfo command)
    {
        var path = RuntimeConfigPath();
        command.ReplyToCommand(
            $"[DTR OK] config path=\"{path}\" exists={File.Exists(path)}");
        ReplyRuntimeSettings(command.ReplyToCommand, "[DTR OK] config effective");
    }

    private string RuntimeConfigPath()
    {
        var directory = Path.GetDirectoryName(GetType().Assembly.Location);
        return Path.Combine(string.IsNullOrWhiteSpace(directory) ? "." : directory, RuntimeConfigFileName);
    }

    private void LoadRuntimeConfig(Action<string> reply, bool announceMissing)
    {
        var path = RuntimeConfigPath();
        if (!File.Exists(path))
        {
            if (announceMissing)
                reply($"[DTR OK] config not found; using built-in defaults. path=\"{path}\"");
            ApplyRuntimeConfigSideEffects();
            return;
        }

        DemoTracerRuntimeConfig? config;
        try
        {
            var json = File.ReadAllText(path);
            config = JsonSerializer.Deserialize<DemoTracerRuntimeConfig>(json, RuntimeConfigJsonOptions);
        }
        catch (Exception ex)
        {
            reply($"[DTR ERR] failed to read config path=\"{path}\": {ex.Message}");
            ApplyRuntimeConfigSideEffects();
            return;
        }

        if (config == null)
        {
            reply($"[DTR ERR] config path=\"{path}\" was empty or invalid JSON");
            ApplyRuntimeConfigSideEffects();
            return;
        }

        ApplyRuntimeConfig(config, reply);
        reply($"[DTR OK] loaded config path=\"{path}\"");
    }

    private void ApplyRuntimeConfig(DemoTracerRuntimeConfig config, Action<string> reply)
    {
        if (!string.IsNullOrWhiteSpace(config.Identity))
        {
            if (TryParseReplayIdentityMode(config.Identity, out var identityMode))
                _replayIdentityMode = identityMode;
            else
                reply($"[DTR WARN] ignored config identity=\"{config.Identity}\"; expected off, name, or full");
        }

        if (config.AllowPartial.HasValue)
            _partialReplayEnabled = config.AllowPartial.Value;

        ApplyRuntimeAlignConfig(config.Align, reply);
        ApplyRuntimeHandoffConfig(config.Handoff, reply);
        ApplyRuntimeConfigSideEffects();
    }

    private void ApplyRuntimeAlignConfig(DemoTracerAlignConfig? align, Action<string> reply)
    {
        if (align == null)
            return;

        if (align.Weapons.HasValue)
            SetWeaponAlignEnabled(align.Weapons.Value);
        if (align.Projectiles.HasValue)
            SetProjectileAlignEnabled(align.Projectiles.Value);
        if (align.Cosmetics.HasValue)
            SetCosmeticAlignEnabled(align.Cosmetics.Value);
        if (align.Stickers.HasValue)
            SetStickerAlignEnabled(align.Stickers.Value);
        if (align.Charms.HasValue)
            SetCharmAlignEnabled(align.Charms.Value);
        if (align.Crosshair.HasValue)
            SetCrosshairAlignEnabled(align.Crosshair.Value);
        if (align.LeftHandDesired.HasValue)
        {
            _leftHandDesiredEnabled = align.LeftHandDesired.Value;
            if (!_leftHandDesiredEnabled)
                reply(LeftHandDesiredFidelityNotice);
        }
        if (align.Scoreboard.HasValue)
            SetScoreboardAlignEnabled(align.Scoreboard.Value);
    }

    private void ApplyRuntimeHandoffConfig(DemoTracerHandoffConfig? handoff, Action<string> reply)
    {
        if (handoff == null)
            return;

        if (!string.IsNullOrWhiteSpace(handoff.Mode))
        {
            if (TryParseHandoffMode(handoff.Mode, out var mode))
                _handoffMode = mode;
            else
                reply($"[DTR WARN] ignored config handoff.mode=\"{handoff.Mode}\"");
        }

        if (!string.IsNullOrWhiteSpace(handoff.Scope))
        {
            if (handoff.Scope.Equals("slot", StringComparison.OrdinalIgnoreCase))
                _handoffAllSlots = false;
            else if (handoff.Scope.Equals("all", StringComparison.OrdinalIgnoreCase))
                _handoffAllSlots = true;
            else
                reply($"[DTR WARN] ignored config handoff.scope=\"{handoff.Scope}\"; expected slot or all");
        }

        if (handoff.Threat360.HasValue)
        {
            _handoffThreat360Enabled = handoff.Threat360.Value;
            if (!_handoffThreat360Enabled)
                _pendingThreat360.Clear();
        }

        if (handoff.Threat360Range.HasValue)
        {
            _handoffThreat360Range = Math.Clamp(
                handoff.Threat360Range.Value,
                HandoffThreat360MinRange,
                HandoffThreat360MaxRange);
            _pendingThreat360.Clear();
        }

        if (handoff.Threat360Los.HasValue)
        {
            _handoffThreat360LosEnabled = handoff.Threat360Los.Value;
            _pendingThreat360.Clear();
        }
    }

    private void ApplyRuntimeConfigSideEffects()
    {
        BotControllerNative.WriteLeftHandDesired = _leftHandDesiredEnabled;
    }

    private void ReplyRuntimeSettings(Action<string> reply, string prefix)
    {
        reply(
            $"{prefix} identity={ReplayIdentityModeName()} weapons={FormatOnOff(_weaponAlignEnabled)} projectiles={FormatOnOff(_projectileAlignEnabled)} cosmetics={FormatOnOff(_cosmeticAlignEnabled)} stickers={FormatOnOff(_stickerAlignEnabled)} charms={FormatOnOff(_charmAlignEnabled)} crosshair={FormatOnOff(_crosshairAlignEnabled)} left_hand_desired={FormatOnOff(_leftHandDesiredEnabled)} scoreboard={FormatOnOff(_scoreboardAlignEnabled)} handoff={FormatHandoffMode(_handoffMode)}:{(_handoffAllSlots ? "all" : "slot")} handoff_360={FormatOnOff(_handoffThreat360Enabled)} range={_handoffThreat360Range.ToString("F0", CultureInfo.InvariantCulture)} los={FormatOnOff(_handoffThreat360LosEnabled)} allow_partial={FormatOnOff(_partialReplayEnabled)}");
    }

    private static bool TryParseReplayIdentityMode(string value, out ReplayIdentityMode mode)
    {
        mode = value.Trim().ToLowerInvariant() switch
        {
            "off" or "0" or "false" => ReplayIdentityMode.Off,
            "name" => ReplayIdentityMode.Name,
            "full" or "1" or "on" or "true" => ReplayIdentityMode.Full,
            _ => ReplayIdentityMode.Off,
        };
        return value.Trim().ToLowerInvariant() is
            "off" or "0" or "false" or
            "name" or
            "full" or "1" or "on" or "true";
    }

    public sealed class DemoTracerRuntimeConfig
    {
        [JsonPropertyName("identity")]
        public string? Identity { get; set; }

        [JsonPropertyName("allow_partial")]
        public bool? AllowPartial { get; set; }

        [JsonPropertyName("handoff")]
        public DemoTracerHandoffConfig? Handoff { get; set; }

        [JsonPropertyName("align")]
        public DemoTracerAlignConfig? Align { get; set; }
    }

    public sealed class DemoTracerHandoffConfig
    {
        [JsonPropertyName("mode")]
        public string? Mode { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("threat_360")]
        public bool? Threat360 { get; set; }

        [JsonPropertyName("threat_360_range")]
        public float? Threat360Range { get; set; }

        [JsonPropertyName("threat_360_los")]
        public bool? Threat360Los { get; set; }
    }

    public sealed class DemoTracerAlignConfig
    {
        [JsonPropertyName("weapons")]
        public bool? Weapons { get; set; }

        [JsonPropertyName("projectiles")]
        public bool? Projectiles { get; set; }

        [JsonPropertyName("crosshair")]
        public bool? Crosshair { get; set; }

        [JsonPropertyName("left_hand_desired")]
        public bool? LeftHandDesired { get; set; }

        [JsonPropertyName("cosmetics")]
        public bool? Cosmetics { get; set; }

        [JsonPropertyName("stickers")]
        public bool? Stickers { get; set; }

        [JsonPropertyName("charms")]
        public bool? Charms { get; set; }

        [JsonPropertyName("scoreboard")]
        public bool? Scoreboard { get; set; }
    }
}
