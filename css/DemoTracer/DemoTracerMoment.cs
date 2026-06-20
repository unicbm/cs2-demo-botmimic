using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API;
using System.Globalization;

namespace DemoTracer;

public sealed partial class DemoTracerPlugin
{
    [ConsoleCommand("dtr_moment", "dtr_moment <manifest.json> <source_round> <bomb|seconds|bomb+seconds> <player_name|steamid> [human_slot] [loop:0|1]")]
    public void MomentCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CheckAbi(command))
            return;
        if (command.ArgCount < 5)
        {
            command.ReplyToCommand("usage: dtr_moment <manifest.json> <source_round> <bomb|seconds|bomb+seconds> <player_name|steamid> [human_slot] [loop:0|1]");
            return;
        }

        var manifestPath = command.GetArg(1);
        if (!int.TryParse(command.GetArg(2), out var round) || round < 0)
        {
            command.ReplyToCommand("dtr: source_round must be a non-negative integer");
            return;
        }

        var anchor = command.GetArg(3);
        var selector = command.GetArg(4);
        var humanSlotText = player is { IsValid: true } ? null : command.ArgCount > 5 ? command.GetArg(5) : null;
        if (!TryResolveMomentHuman(player, humanSlotText, out var human, out var humanError))
        {
            command.ReplyToCommand(humanError);
            return;
        }

        var loopArgIndex = player is { IsValid: true } ? 5 : 6;
        var loop = command.ArgCount > loopArgIndex && ParseLoopArgument(command.GetArg(loopArgIndex));
        RunMoment(manifestPath, round, anchor, selector, human, loop, message => command.ReplyToCommand(message));
    }

    private void RunMomentChatCommand(CCSPlayerController? player, IReadOnlyList<string> tokens)
    {
        void Reply(string message) => ReplyToReplayChat(player, message);

        if (tokens.Count == 1 || tokens[1].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            Reply("usage: .moment \"<manifest.json>\" <source_round> <player_name|steamid> [bomb|seconds|bomb+seconds] [loop:0|1]");
            Reply("usage: .moment stop");
            return;
        }

        if (tokens[1].Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            StopAllState("chat_moment_stop");
            Reply("[DTR OK] moment stopped");
            return;
        }

        if (tokens.Count < 4)
        {
            Reply("usage: .moment \"<manifest.json>\" <source_round> <player_name|steamid> [bomb|seconds|bomb+seconds] [loop:0|1]");
            return;
        }

        if (player is not { IsValid: true })
        {
            Reply("[DTR ERR] .moment must be run by an in-game player.");
            return;
        }

        var manifestPath = tokens[1];
        if (!int.TryParse(tokens[2], out var round) || round < 0)
        {
            Reply("dtr: source_round must be a non-negative integer");
            return;
        }

        var selector = tokens[3];
        var anchor = tokens.Count > 4 ? tokens[4] : "bomb";
        var loop = tokens.Count > 5 && ParseLoopArgument(tokens[5]);
        RunMoment(manifestPath, round, anchor, selector, player, loop, Reply);
    }

    private void RunMoment(
        string manifestPath,
        int round,
        string anchor,
        string selector,
        CCSPlayerController human,
        bool loop,
        Action<string> reply)
    {
        if (!BotControllerNative.IsCompatible)
        {
            reply($"dtr: ABI mismatch, runtime={BotControllerNative.AbiVersion}, expected={BotControllerNative.ExpectedAbiVersion}");
            return;
        }

        if (!TryReadManifest(manifestPath, out var manifest, out var readError))
        {
            reply($"[DTR ERR] failed to read manifest: {readError}");
            return;
        }
        if (!CurrentMapMatchesManifest(manifest.Map, out var currentMap))
        {
            reply($"[DTR ERR] map mismatch: server=\"{currentMap}\" manifest=\"{manifest.Map}\" path=\"{manifestPath}\"");
            return;
        }
        if (!TryResolveReplayStartAnchor(anchor, reply, "dtr_moment", manifest, round, out var secondsAfterLive))
            return;

        if (!TryResolveMomentTargetFile(manifest, round, selector, out var target, out var targetError))
        {
            reply(targetError);
            return;
        }

        var anchorSnapshot = ResolveMomentAnchorSnapshot(manifest, round, secondsAfterLive);
        var targetSnapshot = anchorSnapshot?.Players.FirstOrDefault(player => player.SteamId == target.SteamId);
        if (anchorSnapshot != null && targetSnapshot is not { IsAlive: true })
        {
            reply($"[DTR ERR] target {target.PlayerName} is not alive at the anchor tick.");
            return;
        }

        var targetTeam = ManifestSideToTeam(target.Side);
        if (targetTeam is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            reply($"[DTR ERR] target side \"{target.Side}\" is not playable");
            return;
        }

        StopAllState("moment_plan");
        EnsureHumanMomentTeam(human, targetTeam);

        var previousFreezeTime = TryReadFreezeTimeConVar(out var freezeTime, out _)
            ? freezeTime
            : (float?)null;
        var remainingSeconds = EstimateMomentRemainingSeconds(manifest, round, secondsAfterLive);
        var token = ++_momentChallengeToken;
        _momentChallenge = new MomentChallengeState(
            token,
            manifestPath,
            round,
            anchor,
            selector,
            human.Slot,
            loop,
            secondsAfterLive,
            target.PlayerName,
            targetTeam,
            anchorSnapshot != null,
            remainingSeconds,
            previousFreezeTime);

        ConfigureMomentServerRules(_momentChallenge);
        Server.ExecuteCommand("mp_restartgame 1");
        reply($"[DTR OK] moment planned player={target.PlayerName} round={round} start=+{F(secondsAfterLive)}s prep={F(MomentFreezeSeconds)}s restart=now");
        reply("[DTR OK] moment will spawn only alive snapshot players, replace the target with you, and reset on your death.");
        if (anchorSnapshot == null)
            reply("[DTR WARN] manifest has no anchor snapshot; moment uses legacy round loadout/100HP fallback.");
    }

    private void PrepareMomentChallengeRound(string reason)
    {
        var state = _momentChallenge;
        if (state == null)
            return;

        state.Prepared = false;
        state.Started = false;
        state.ResetQueued = false;

        void Reply(string message) => Server.PrintToConsole(message);
        if (!TryReadManifest(state.ManifestPath, out var manifest, out var readError))
        {
            StopMomentChallenge("moment_manifest_error", restoreFreezeTime: true);
            Reply($"[DTR ERR] failed to read moment manifest: {readError}");
            return;
        }
        if (!CurrentMapMatchesManifest(manifest.Map, out var currentMap))
        {
            StopMomentChallenge("moment_map_mismatch", restoreFreezeTime: true);
            Reply($"[DTR ERR] map mismatch: server=\"{currentMap}\" manifest=\"{manifest.Map}\" path=\"{state.ManifestPath}\"");
            return;
        }
        if (!TryResolveMomentTargetFile(manifest, state.Round, state.Selector, out var target, out var targetError))
        {
            StopMomentChallenge("moment_target_error", restoreFreezeTime: true);
            Reply(targetError);
            return;
        }

        var secondsAfterLive = state.SecondsAfterLive;
        var anchorSnapshot = ResolveMomentAnchorSnapshot(manifest, state.Round, secondsAfterLive);
        var snapshotsBySteamId = anchorSnapshot?.Players
            .GroupBy(player => player.SteamId)
            .ToDictionary(group => group.Key, group => group.First());
        RoundPlayerSnapshot? targetSnapshot = null;
        snapshotsBySteamId?.TryGetValue(target.SteamId, out targetSnapshot);
        if (anchorSnapshot != null && targetSnapshot is not { IsAlive: true })
        {
            StopMomentChallenge("moment_target_dead", restoreFreezeTime: true);
            Reply($"[DTR ERR] target {target.PlayerName} is not alive at the anchor tick.");
            return;
        }

        var manifestDir = Path.GetDirectoryName(Path.GetFullPath(state.ManifestPath)) ?? ".";
        var roundFiles = manifest.Files
            .Where(file => file.Round == state.Round)
            .OrderBy(file => file.Side, StringComparer.Ordinal)
            .ThenBy(file => file.SteamId)
            .ToList();
        if (roundFiles.Count == 0)
        {
            StopMomentChallenge("moment_round_empty", restoreFreezeTime: true);
            Reply($"[DTR ERR] manifest has no files for source_round={state.Round}");
            return;
        }

        if (!TryBuildMomentCandidate(manifestDir, target, secondsAfterLive, out var targetCandidate, out var buildTargetError))
        {
            StopMomentChallenge("moment_target_unplayable", restoreFreezeTime: true);
            Reply($"[DTR ERR] target {target.PlayerName} is not alive/playable at +{F(secondsAfterLive)}s: {buildTargetError}");
            return;
        }
        if (targetSnapshot != null)
            targetCandidate = targetCandidate with { Snapshot = targetSnapshot };

        var otherFiles = roundFiles.Where(file => !ReferenceEquals(file, target)).ToList();
        var candidates = new List<MomentCandidate>();
        foreach (var file in otherFiles)
        {
            RoundPlayerSnapshot? snapshot = null;
            snapshotsBySteamId?.TryGetValue(file.SteamId, out snapshot);
            if (anchorSnapshot != null && snapshot is not { IsAlive: true })
                continue;
            if (TryBuildMomentCandidate(manifestDir, file, secondsAfterLive, out var candidate, out _))
            {
                if (snapshot != null)
                    candidate = candidate with { Snapshot = snapshot };
                candidates.Add(candidate);
            }
        }

        var human = Utilities.GetPlayerFromSlot(state.HumanSlot);
        if (human is not { IsValid: true, IsBot: false })
        {
            StopMomentChallenge("moment_human_missing", restoreFreezeTime: true);
            Reply($"[DTR ERR] moment human slot {state.HumanSlot} is no longer valid.");
            return;
        }

        EnsureHumanMomentTeam(human, state.TargetTeam);
        var targets = FindReplayTargets()
            .Where(bot => bot.Slot != human.Slot)
            .ToList();
        var tBots = targets.Where(bot => bot.Team == CsTeam.Terrorist).OrderBy(bot => bot.Slot).ToList();
        var ctBots = targets.Where(bot => bot.Team == CsTeam.CounterTerrorist).OrderBy(bot => bot.Slot).ToList();
        var tFiles = candidates
            .Where(candidate => candidate.File.Side.Equals("t", StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.File)
            .ToList();
        var ctFiles = candidates
            .Where(candidate => candidate.File.Side.Equals("ct", StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.File)
            .ToList();

        var tAssignments = BuildReplayAssignments(tFiles, tBots);
        var ctAssignments = BuildReplayAssignments(ctFiles, ctBots);
        var skippedT = tFiles.Count - tAssignments.Count;
        var skippedCt = ctFiles.Count - ctAssignments.Count;
        var assignments = tAssignments.Concat(ctAssignments).ToList();

        StopAndUnloadLoaded();
        var loaded = new List<string>();
        if (!LoadSide(tAssignments, manifestDir, loaded, out var loadError) ||
            !LoadSide(ctAssignments, manifestDir, loaded, out loadError))
        {
            StopAndUnloadLoaded();
            StopMomentChallenge("moment_load_error", restoreFreezeTime: true);
            Reply($"[DTR ERR] failed to load moment bots: {loadError}");
            return;
        }

        Server.NextFrame(() =>
        {
            if (human is { IsValid: true } && !human.PawnIsAlive)
                human.Respawn();
            Server.NextFrame(() =>
            {
                ApplyMomentToHuman(human, target, targetCandidate.Tick.Pre, targetCandidate.Tick.WeaponDefIndex, targetCandidate.Snapshot);
                PreloadLoadedReplays(ReplayStartAnchor.Live, null, secondsAfterLive);
                ApplyMomentSnapshotsToBots(assignments, snapshotsBySteamId);
                RemoveUnusedMomentBots(targets, assignments);
                if (anchorSnapshot != null && !TryEnsureMomentBomb(anchorSnapshot, out var bombMessage))
                    Reply($"[DTR WARN] {bombMessage}");
                state.Prepared = true;
                state.Started = false;
                state.LoadedCount = loaded.Count;
                Reply($"[DTR OK] moment prepared on {reason}: player={target.PlayerName} round={state.Round} start=+{F(secondsAfterLive)}s bots={loaded.Count} skipped T={skippedT}/CT={skippedCt}");
                if (anchorSnapshot == null)
                    Reply("[DTR WARN] manifest has no anchor snapshot; moment uses legacy round loadout/100HP fallback.");
            });
        });
    }

    private void StartPreparedMomentChallengeRound()
    {
        var state = _momentChallenge;
        if (state == null)
            return;
        if (!state.Prepared)
        {
            Server.PrintToConsole($"[DTR WARN] moment is waiting for round_start preparation: player={state.PlayerName}");
            return;
        }

        state.Prepared = false;
        state.Started = true;
        state.ResetQueued = false;
        var start = StartLoaded(state.Loop, ReplayStartAnchor.Live, null, state.SecondsAfterLive);
        Server.PrintToConsole($"[DTR OK] moment started player={state.PlayerName}: {start}");
    }

    private bool TryHandleMomentPlayerDeath(EventPlayerDeath @event)
    {
        var state = _momentChallenge;
        if (state == null || !state.Started || state.ResetQueued)
            return false;

        if (@event.Userid is not { IsValid: true } victim || victim.Slot != state.HumanSlot)
            return false;

        QueueMomentRestart(state, "human_death");
        return true;
    }

    private void QueueMomentRestart(MomentChallengeState state, string reason)
    {
        state.ResetQueued = true;
        state.Prepared = false;
        state.Started = false;
        StopLoadedReplaySlots($"moment_reset_{reason}");

        var token = state.Token;
        AddTimer(0.35f, () =>
        {
            if (_momentChallenge is not { } current || current.Token != token)
                return;
            current.ResetQueued = false;
            ConfigureMomentServerRules(current);
            Server.ExecuteCommand("mp_restartgame 1");
            Server.PrintToConsole($"[DTR OK] moment reset reason={reason} player={current.PlayerName}");
        });
    }

    private void StopMomentChallenge(string reason, bool restoreFreezeTime)
    {
        var state = _momentChallenge;
        if (state == null)
            return;

        _momentChallenge = null;
        _momentChallengeToken++;
        if (restoreFreezeTime && state.PreviousFreezeTime.HasValue)
        {
            Server.ExecuteCommand(
                $"mp_freezetime {state.PreviousFreezeTime.Value.ToString("0.###", CultureInfo.InvariantCulture)}");
        }
        Server.PrintToConsole($"dtr: moment stopped reason={reason}");
    }

    private static bool TryResolveMomentHuman(
        CCSPlayerController? commandPlayer,
        string? slotText,
        out CCSPlayerController human,
        out string error)
    {
        human = null!;
        error = string.Empty;
        if (commandPlayer is { IsValid: true, IsBot: false })
        {
            human = commandPlayer;
            return true;
        }

        if (string.IsNullOrWhiteSpace(slotText))
        {
            error = "[DTR ERR] server console usage requires human_slot after player selector.";
            return false;
        }

        if (!int.TryParse(slotText, out var slot) || slot < 0 || slot >= MaxPlayerSlots)
        {
            error = $"[DTR ERR] human_slot must be 0..{MaxPlayerSlots - 1}";
            return false;
        }

        var player = Utilities.GetPlayerFromSlot(slot);
        if (player is not { IsValid: true } || player.IsBot)
        {
            error = $"[DTR ERR] slot {slot} is not a valid human player";
            return false;
        }

        human = player;
        return true;
    }

    private static bool MomentPlayerMatches(ManifestFile file, string selector)
    {
        selector = selector.Trim();
        if (ulong.TryParse(selector, out var steamId) && steamId == file.SteamId)
            return true;

        return file.PlayerName.Equals(selector, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveMomentTargetFile(
        ConversionManifest manifest,
        int round,
        string selector,
        out ManifestFile target,
        out string error)
    {
        target = null!;
        var roundFiles = manifest.Files
            .Where(file => file.Round == round)
            .ToList();
        if (roundFiles.Count == 0)
        {
            error = $"[DTR ERR] manifest has no files for source_round={round}";
            return false;
        }

        var matches = roundFiles.Where(file => MomentPlayerMatches(file, selector)).ToList();
        if (matches.Count == 1)
        {
            target = matches[0];
            error = string.Empty;
            return true;
        }

        error = matches.Count == 0
            ? $"[DTR ERR] no player matched \"{selector}\" in source_round={round}"
            : $"[DTR ERR] player selector \"{selector}\" is ambiguous: {string.Join(", ", matches.Select(file => file.PlayerName))}";
        return false;
    }

    private static CsTeam ManifestSideToTeam(string side)
        => side.Equals("t", StringComparison.OrdinalIgnoreCase)
            ? CsTeam.Terrorist
            : side.Equals("ct", StringComparison.OrdinalIgnoreCase)
                ? CsTeam.CounterTerrorist
                : CsTeam.None;

    private static bool TryBuildMomentCandidate(
        string manifestDir,
        ManifestFile file,
        float secondsAfterLive,
        out MomentCandidate candidate,
        out string error)
    {
        candidate = default;
        if (!TryResolveChildPathUnderRoot(manifestDir, file.Path, out var recPath, out error))
            return false;

        if (!BotControllerNative.TryReadReplayTickAt(
                recPath,
                secondsAfterLive,
                out var tick,
                out _,
                out _,
                out error))
            return false;

        candidate = new MomentCandidate(file, tick, null);
        return true;
    }

    private static RoundAnchorSnapshot? ResolveMomentAnchorSnapshot(
        ConversionManifest manifest,
        int round,
        float secondsAfterLive)
    {
        var snapshot = manifest.Rounds
            .FirstOrDefault(item => item.Round == round)
            ?.BombPlantedSnapshot;
        if (snapshot == null)
            return null;

        return Math.Abs(snapshot.SecondsAfterLive - secondsAfterLive) <= 0.05f
            ? snapshot
            : null;
    }

    private static float EstimateMomentRemainingSeconds(
        ConversionManifest manifest,
        int round,
        float secondsAfterLive)
    {
        var duration = manifest.Rounds
            .FirstOrDefault(item => item.Round == round)
            ?.DurationSeconds ?? 0.0f;
        if (!float.IsFinite(duration) || duration <= secondsAfterLive)
            return MomentDefaultC4TimerSeconds;

        return Math.Clamp(duration - secondsAfterLive, 15.0f, 600.0f);
    }

    private static void ConfigureMomentServerRules(MomentChallengeState state)
    {
        Server.ExecuteCommand($"mp_freezetime {MomentFreezeSeconds.ToString("0.###", CultureInfo.InvariantCulture)}");

        var roundMinutes = Math.Clamp(
            MathF.Ceiling(Math.Max(state.RemainingSeconds, 15.0f) / 60.0f),
            1.0f,
            60.0f);
        var roundTime = roundMinutes.ToString("0.###", CultureInfo.InvariantCulture);
        Server.ExecuteCommand($"mp_roundtime {roundTime}");
        Server.ExecuteCommand($"mp_roundtime_defuse {roundTime}");
        if (state.BombMoment)
            Server.ExecuteCommand($"mp_c4timer {MomentDefaultC4TimerSeconds.ToString("0.###", CultureInfo.InvariantCulture)}");
    }

    private static void EnsureHumanMomentTeam(CCSPlayerController human, CsTeam targetTeam)
    {
        if (human.Team != targetTeam)
            human.SwitchTeam(targetTeam);
    }

    private void ApplyMomentToHuman(
        CCSPlayerController human,
        ManifestFile file,
        NativeMovementSnapshot snapshot,
        int activeWeaponDefIndex,
        RoundPlayerSnapshot? anchorSnapshot)
    {
        if (human is not { IsValid: true })
            return;
        if (!human.PawnIsAlive)
            human.Respawn();
        if (human.PlayerPawn is not { IsValid: true, Value.IsValid: true })
            return;
        var pawn = human.PlayerPawn.Value;

        var loadout = NormalizeReplayLoadout(anchorSnapshot?.Loadout ?? file.Loadout ?? new ReplayLoadoutSnapshot());
        var activeDef = anchorSnapshot?.ActiveWeaponDefIndex ?? activeWeaponDefIndex;
        ApplyMomentLoadout(human, pawn, loadout, activeDef);
        ApplyMomentHealth(pawn, anchorSnapshot?.Health ?? ReplayStartHealth);

        var origin = new Vector(snapshot.OriginX, snapshot.OriginY, snapshot.OriginZ);
        var angles = new QAngle(snapshot.Pitch, snapshot.Yaw, snapshot.Roll);
        var velocity = new Vector(snapshot.VelX, snapshot.VelY, snapshot.VelZ);
        pawn.Teleport(origin, angles, velocity);
    }

    private void ApplyMomentSnapshotsToBots(
        IReadOnlyList<ReplayAssignment> assignments,
        IReadOnlyDictionary<ulong, RoundPlayerSnapshot>? snapshotsBySteamId)
    {
        if (snapshotsBySteamId == null)
            return;

        foreach (var assignment in assignments)
        {
            if (!snapshotsBySteamId.TryGetValue(assignment.File.SteamId, out var snapshot) ||
                !snapshot.IsAlive)
                continue;

            var bot = assignment.Bot;
            if (bot is not { IsValid: true, PawnIsAlive: true } ||
                bot.PlayerPawn is not { IsValid: true, Value.IsValid: true })
                continue;

            var pawn = bot.PlayerPawn.Value;
            var loadout = NormalizeReplayLoadout(snapshot.Loadout ?? assignment.File.Loadout ?? new ReplayLoadoutSnapshot());
            ApplyMomentLoadout(bot, pawn, loadout, snapshot.ActiveWeaponDefIndex);
            ApplyMomentHealth(pawn, snapshot.Health);
        }
    }

    private static void RemoveUnusedMomentBots(
        IReadOnlyList<CCSPlayerController> targets,
        IReadOnlyList<ReplayAssignment> assignments)
    {
        var aliveSlots = assignments
            .Select(assignment => assignment.Bot.Slot)
            .ToHashSet();
        foreach (var target in targets)
        {
            if (aliveSlots.Contains(target.Slot) || target is not { IsValid: true })
                continue;
            try
            {
                if (target.UserId.HasValue)
                {
                    Server.ExecuteCommand($"kickid {target.UserId.Value}");
                    continue;
                }
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"dtr: moment failed to kick unused bot slot={target.Slot}: {ex.Message}");
            }

            if (!target.PawnIsAlive)
                continue;
            try
            {
                target.CommitSuicide(false, true);
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"dtr: moment failed to remove unused bot slot={target.Slot}: {ex.Message}");
            }
        }
    }

    private static void ApplyMomentHealth(CCSPlayerPawn pawn, int health)
    {
        pawn.Health = Math.Clamp(health, 1, ReplayStartHealth);
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
    }

    private static bool TryEnsureMomentBomb(RoundAnchorSnapshot snapshot, out string message)
    {
        message = string.Empty;
        if (snapshot.Bomb?.Position == null || !TryReadVector(snapshot.Bomb.Position, out var origin))
        {
            message = "anchor snapshot has no planted C4 position";
            return false;
        }

        try
        {
            foreach (var existing in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("planted_c4"))
            {
                if (existing is { IsValid: true })
                    existing.AcceptInput("Kill");
            }

            var c4 = Utilities.CreateEntityByName<CBaseEntity>("planted_c4");
            if (c4 is not { IsValid: true })
            {
                message = "failed to create planted_c4 entity";
                return false;
            }

            c4.DispatchSpawn(new CEntityKeyValues());
            c4.Teleport(origin, new QAngle(0.0f, 0.0f, 0.0f), new Vector(0.0f, 0.0f, 0.0f));
            return true;
        }
        catch (Exception ex)
        {
            message = $"failed to create planted C4: {ex.Message}";
            return false;
        }
    }

    private static bool TryReadVector(float[]? values, out Vector vector)
    {
        vector = new Vector(0.0f, 0.0f, 0.0f);
        if (values is not { Length: >= 3 } ||
            !float.IsFinite(values[0]) ||
            !float.IsFinite(values[1]) ||
            !float.IsFinite(values[2]))
            return false;

        vector = new Vector(values[0], values[1], values[2]);
        return true;
    }

    private static void ApplyMomentLoadout(
        CCSPlayerController human,
        CCSPlayerPawn pawn,
        ReplayLoadoutSnapshot loadout,
        int activeWeaponDefIndex)
    {
        ApplyReplayArmorAndKit(human, pawn, loadout);
        try
        {
            human.RemoveWeapons();
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"dtr: moment failed to remove weapons slot={human.Slot}: {ex.Message}");
        }

        TryGiveNamedItem(human, "weapon_knife");
        foreach (var def in loadout.WeaponDefIndices ?? [])
        {
            if (!TryGetWeaponClassByDefIndex(def, out var className))
                continue;
            if (GetReplayWeaponSlot(className) is ReplayWeaponSlot.Knife or ReplayWeaponSlot.C4 or ReplayWeaponSlot.Other)
                continue;
            TryGiveNamedItem(human, className);
        }

        var active = NormalizeWeaponDefIndex(activeWeaponDefIndex);
        if (!TryGetWeaponClassByDefIndex(active, out var activeClass))
            return;
        if (!HasReplayWeapon(pawn, activeClass) &&
            GetReplayWeaponSlot(activeClass) is not (ReplayWeaponSlot.Knife or ReplayWeaponSlot.C4 or ReplayWeaponSlot.Other))
            TryGiveNamedItem(human, activeClass);

        if (human.UserId != null)
            NativeAPI.IssueClientCommand(human.UserId.Value, $"use {activeClass}");
    }

    private readonly record struct MomentCandidate(
        ManifestFile File,
        NativeReplayTick Tick,
        RoundPlayerSnapshot? Snapshot);

    private sealed class MomentChallengeState(
        int token,
        string manifestPath,
        int round,
        string anchor,
        string selector,
        int humanSlot,
        bool loop,
        float secondsAfterLive,
        string playerName,
        CsTeam targetTeam,
        bool bombMoment,
        float remainingSeconds,
        float? previousFreezeTime)
    {
        public int Token { get; } = token;
        public string ManifestPath { get; } = manifestPath;
        public int Round { get; } = round;
        public string Anchor { get; } = anchor;
        public string Selector { get; } = selector;
        public int HumanSlot { get; } = humanSlot;
        public bool Loop { get; } = loop;
        public float SecondsAfterLive { get; } = secondsAfterLive;
        public string PlayerName { get; } = playerName;
        public CsTeam TargetTeam { get; } = targetTeam;
        public bool BombMoment { get; } = bombMoment;
        public float RemainingSeconds { get; } = remainingSeconds;
        public float? PreviousFreezeTime { get; } = previousFreezeTime;
        public bool Prepared { get; set; }
        public bool Started { get; set; }
        public bool ResetQueued { get; set; }
        public int LoadedCount { get; set; }
    }
}
