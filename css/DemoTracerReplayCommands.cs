using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace DemoTracer;

public sealed partial class DemoTracerPlugin
{
    [ConsoleCommand("dtr_load", "dtr_load <round|slot> ...")]
    public void LoadCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CheckAbi(command))
            return;
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand("usage: dtr_load round <manifest.json> <source_round> | dtr_load slot <slot> <path.dtr>");
            return;
        }

        var mode = command.GetArg(1).ToLowerInvariant();
        if (mode == "round")
        {
            if (!TryParseRoundArgs(command, "dtr_load round", out var manifestPath, out var round, argOffset: 2))
                return;

            var result = LoadRound(manifestPath, round);
            command.ReplyToCommand(result.Message);
            return;
        }

        var slotArg = mode == "slot" ? 2 : 1;
        if (!TryParseSlotAt(command, slotArg, out var slot) || command.ArgCount <= slotArg + 1)
        {
            command.ReplyToCommand("usage: dtr_load slot <slot> <path.dtr>");
            command.ReplyToCommand("legacy usage: dtr_load <slot> <path.dtr>");
            return;
        }

        var path = command.GetArg(slotArg + 1);
        var ok = BotControllerNative.LoadReplayFromFile(slot, path, out var replayMetadata);
        if (ok)
        {
            RememberLoadedSlot(slot);
            TrackLoadedReplay(slot, path, $"slot{slot}", replayMetadata: replayMetadata);
        }

        command.ReplyToCommand(ok
            ? $"dtr: loaded slot {slot}: {path}"
            : $"dtr: failed to load slot {slot}: {path} ({BotControllerNative.LastLoadError})");
    }

    [ConsoleCommand("dtr_load_round", "dtr_load_round <manifest.json> <source_round>")]
    public void LoadRoundCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CheckAbi(command))
            return;
        if (!TryParseRoundArgs(command, "dtr_load_round", out var manifestPath, out var round))
            return;

        var result = LoadRound(manifestPath, round);
        command.ReplyToCommand(result.Message);
    }

    [ConsoleCommand("dtr_play_loaded", "dtr_play_loaded [loop:0|1]")]
    public void PlayLoadedCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CheckAbi(command))
            return;
        var loop = command.ArgCount >= 2 && command.GetArg(1) != "0";
        command.ReplyToCommand(PlayLoaded(loop));
    }

    [ConsoleCommand("dtr_play", "dtr_play <loaded|slot> ...")]
    public void PlayCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CheckAbi(command))
            return;
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand("usage: dtr_play loaded [loop:0|1] | dtr_play slot <slot> [loop:0|1]");
            command.ReplyToCommand("legacy usage: dtr_play <slot> [loop:0|1]");
            return;
        }

        var mode = command.GetArg(1).ToLowerInvariant();
        if (mode == "loaded")
        {
            var loopLoaded = command.ArgCount >= 3 && command.GetArg(2) != "0";
            command.ReplyToCommand("[DTR WARN] dtr_play loaded is manual/debug playback; it bypasses round_start/round_freeze_end lifecycle alignment.");
            command.ReplyToCommand(PlayLoaded(loopLoaded));
            return;
        }

        var slotArg = mode == "slot" ? 2 : 1;
        if (!TryParseSlotAt(command, slotArg, out var slot))
            return;
        var loop = command.ArgCount > slotArg + 1 && command.GetArg(slotArg + 1) != "0";
        if (_loadedReplays.TryGetValue(slot, out var replay))
            PreloadReplayWeaponsForSlot(slot, replay);
        _lastEnsuredWeaponDef.Remove(slot);

        if (!IsReplaySlotStillSafe(slot))
        {
            command.ReplyToCommand($"dtr: refused to play slot {slot}: not a safe bot target");
            return;
        }

        var ok = StartReplayForSlot(slot, loop);
        if (ok)
        {
            MarkReplayStarted(slot);
        }
        var state = ok ? default : BotControllerNative.GetReplayState(slot);
        command.ReplyToCommand(ok
            ? $"dtr: playing slot {slot}, loop={loop}"
            : $"dtr: failed to play slot {slot} (cursor={state.Cursor}, total={state.Total})");
    }

    [ConsoleCommand("dtr_stop", "dtr_stop <sequence|pool|replay|slot|all> ...")]
    public void StopCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CheckAbi(command))
            return;
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand("usage: dtr_stop sequence|pool|replay|slot <slot>|all");
            command.ReplyToCommand("legacy usage: dtr_stop <slot>");
            return;
        }

        switch (command.GetArg(1).ToLowerInvariant())
        {
            case "sequence":
            case "seq":
                StopSequenceState();
                command.ReplyToCommand("[DTR OK] sequence scheduling stopped");
                return;
            case "pool":
                StopPoolState();
                command.ReplyToCommand("[DTR OK] pool scheduling stopped");
                return;
            case "replay":
            case "loaded":
                StopLoadedReplaySlots("manual_stop_replay");
                command.ReplyToCommand("[DTR OK] current loaded/running replay slots stopped");
                return;
            case "all":
                StopAllState("manual_stop_all");
                command.ReplyToCommand("[DTR OK] all DemoTracer replay state stopped");
                return;
            case "slot":
                if (!TryParseSlotAt(command, 2, out var namedSlot))
                    return;
                StopOneSlot(command, namedSlot, "manual_stop");
                return;
            default:
                if (!TryParseSlotAt(command, 1, out var legacySlot))
                    return;
                StopOneSlot(command, legacySlot, "manual_stop");
                return;
        }
    }

    [ConsoleCommand("dtr_stop_all", "dtr_stop_all")]
    public void StopAllCommand(CCSPlayerController? player, CommandInfo command)
    {
        StopAllState("manual_stop_all");
        command.ReplyToCommand("[DTR OK] all DemoTracer replay state stopped");
    }

    [ConsoleCommand("dtr_unload", "dtr_unload <slot>")]
    public void UnloadCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CheckAbi(command) || !TryParseSlot(command, out var slot))
            return;
        var ok = BotControllerNative.UnloadReplay(slot);
        if (ok)
        {
            if (IsNadeCycleSlot(slot))
                StopNadeCycle("manual_unload", stopCurrent: false);
            _loadedSlots.Remove(slot);
            _loadedReplays.Remove(slot);
            _lastEnsuredWeaponDef.Remove(slot);
            _lastReplayWeaponDef.Remove(slot);
            _lastLockedWeaponTarget.Remove(slot);
            _pendingWeaponAlign.Remove(slot);
            _rebuiltInventorySlots.Remove(slot);
            _pendingBulletHits.Remove(slot);
            _pendingBulletDamages.Remove(slot);
            ReleaseReplaySlot(slot, "unload");
        }

        command.ReplyToCommand(ok
            ? $"dtr: unloaded slot {slot}"
            : $"dtr: failed to unload slot {slot}");
    }
}
