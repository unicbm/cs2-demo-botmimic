using System.Runtime.InteropServices;

namespace DemoTracer;

internal static partial class BotControllerNative
{
    public static string LastLoadError { get; private set; } = string.Empty;

    public static int AbiVersion
    {
        get
        {
            try
            {
                return BotController_GetVersion();
            }
            catch
            {
                return -1;
            }
        }
    }

    public static BotControllerAbiInfo AbiInfo
        => TryGetAbiInfo(out var info) ? info : BotControllerAbiInfo.Unavailable;

    public static ulong Capabilities
    {
        get
        {
            try
            {
                return BotController_GetCapabilities();
            }
            catch
            {
                return 0;
            }
        }
    }

    public static string BuildId
    {
        get
        {
            try
            {
                var buildId = Marshal.PtrToStringAnsi(BotController_GetBuildId());
                return string.IsNullOrWhiteSpace(buildId) ? "unknown" : buildId;
            }
            catch
            {
                return "unavailable";
            }
        }
    }

    public static bool IsCompatible => AbiVersion == ExpectedAbiVersion;

    public static bool HasRequiredCapabilities
        => (Capabilities & RequiredCapabilityMask) == RequiredCapabilityMask;

    public static ulong MissingRequiredCapabilities
        => RequiredCapabilityMask & ~Capabilities;

    public static string RuntimeSummary
        => $"expected_abi={ExpectedAbiVersion} runtime_abi={AbiVersion} compatible={IsCompatible} " +
           $"caps=0x{Capabilities:X} missing=0x{MissingRequiredCapabilities:X} build={BuildId} " +
           $"dtr_reader={MinRecFormatVersion}..{RecFormatVersion} platform={RuntimePlatformName} " +
           $"api={DemoTracerApiVersion}";

    public static bool SetControllerControllingBotOffset(int offset)
    {
        try
        {
            return BotController_SetControllerControllingBotOffset(offset) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool SetReplayPovMask(ulong mask)
    {
        try
        {
            return BotController_SetReplayPovMask(mask) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool LoadReplayFromFile(int slot, string path)
        => LoadReplayFromFile(slot, path, out _);

    public static bool LoadReplayFromFile(int slot, string path, out ReplayFileMetadata metadata)
    {
        metadata = ReplayFileMetadata.Empty;
        if (!ValidSlot(slot))
        {
            LastLoadError = $"slot {slot} out of range 0..{MaxSlots - 1}";
            return false;
        }

        try
        {
            EnsureNativeLayout();
            var replay = DtrReplayReader.Read(path);
            metadata = ReplayNativeMapper.BuildMetadata(replay);
            if (replay.Ticks.Length == 0)
            {
                LastLoadError = "replay has no ticks";
                return false;
            }

            var subticks = replay.Subticks.Length == 0
                ? [new NativeSubtickMove()]
                : replay.Subticks;
            if (replay.Version >= 7)
            {
                if (!IsCompatible)
                {
                    LastLoadError = $"v7 replay requires BotController ABI {ExpectedAbiVersion}; {RuntimeSummary}";
                    return false;
                }
                if ((Capabilities & CapabilityExtendedReplay) == 0)
                {
                    LastLoadError = $"v7 replay requires extended replay capability; {RuntimeSummary}";
                    return false;
                }

                var commandFrames = replay.CommandFrames.Length == 0
                    ? [new NativeReplayCommandFrame()]
                    : replay.CommandFrames;
                var movementExtras = replay.MovementExtras.Length == 0
                    ? [new NativeReplayMovementExtra()]
                    : replay.MovementExtras;
                var extendedOk = BotController_LoadReplayExtended(
                    slot,
                    replay.Ticks,
                    replay.Ticks.Length,
                    subticks,
                    replay.Subticks.Length,
                    commandFrames,
                    replay.CommandFrames.Length,
                    movementExtras,
                    replay.MovementExtras.Length) == 0;
                LastLoadError = extendedOk ? string.Empty : "BotController_LoadReplayExtended failed";
                return extendedOk;
            }

            var ok = BotController_LoadReplay(
                slot,
                replay.Ticks,
                replay.Ticks.Length,
                subticks,
                replay.Subticks.Length) == 0;
            LastLoadError = ok ? string.Empty : "BotController_LoadReplay failed";
            return ok;
        }
        catch (Exception ex)
        {
            LastLoadError = ex.Message;
            return false;
        }
    }

    public static bool TryReadReplayMetadata(string path, out ReplayFileMetadata metadata)
    {
        try
        {
            var replay = DtrReplayReader.Read(path);
            metadata = ReplayNativeMapper.BuildMetadata(replay);
            return true;
        }
        catch
        {
            metadata = ReplayFileMetadata.Empty;
            return false;
        }
    }

    public static bool UnloadReplay(int slot)
    {
        if (!ValidSlot(slot))
            return false;
        StopReplay(slot);
        LastLoadError = string.Empty;
        return true;
    }

    public static bool StartReplay(int slot, bool loop)
        => StartReplayAt(slot, loop, 0);

    public static bool StartReplayAt(int slot, bool loop, uint startIndex)
    {
        if (!ValidSlot(slot))
            return false;
        if (BotController_Lock(slot, LockKindAll, 0) != 0)
            return false;

        var ok = startIndex == 0
            ? BotController_StartReplay(slot, loop ? 1 : 0) == 0
            : BotController_StartReplayAt(slot, loop ? 1 : 0, checked((int)startIndex)) == 0;
        if (!ok)
            BotController_Unlock(slot, LockKindAll);
        return ok;
    }

    public static bool StartReplayUntil(
        int slot,
        bool loop,
        uint startIndex,
        uint holdBeforeIndex)
    {
        if (!ValidSlot(slot))
            return false;
        if (holdBeforeIndex <= startIndex)
            return false;
        if (BotController_Lock(slot, LockKindAll, 0) != 0)
            return false;

        var ok = BotController_StartReplayUntil(
            slot,
            loop ? 1 : 0,
            checked((int)startIndex),
            checked((int)holdBeforeIndex)) == 0;
        if (!ok)
            BotController_Unlock(slot, LockKindAll);
        return ok;
    }

    public static bool StopReplay(int slot)
    {
        if (!ValidSlot(slot))
            return false;
        var ok = BotController_StopReplay(slot) == 0;
        BotController_Unlock(slot, LockKindAll);
        return ok;
    }

    public static ReplayState GetReplayState(int slot)
    {
        if (!ValidSlot(slot))
            return ReplayState.Empty;

        try
        {
            if (BotController_GetReplaySlotState(slot, out var state) == 0)
            {
                return new ReplayState(
                    state.Cursor,
                    state.Total,
                    state.Playing != 0,
                    state.CurrentTickIndex,
                    state.WeaponDefIndex,
                    state.NumSubtick);
            }
        }
        catch
        {
        }

        var cursor = BotController_GetReplayCursor(slot);
        var total = BotController_GetReplayTotal(slot);
        return new ReplayState(cursor, total, cursor >= 0, -1, -1, 0);
    }

    public static bool TryGetReplayTick(int slot, out NativeReplayTick tick)
    {
        tick = default;
        return ValidSlot(slot) && BotController_GetReplayTick(slot, out tick) == 0;
    }

    public static bool SwitchBotWeapon(int slot, int defIndex)
        => ValidSlot(slot) && BotController_SwitchBotWeapon(slot, defIndex) == 0;

    public static int BotActiveWeaponDef(int slot)
        => ValidSlot(slot) ? BotController_GetBotActiveWeaponDef(slot) : -1;

    public static bool SetBuyPlan(int slot, string aliases)
    {
        if (!ValidSlot(slot))
            return false;
        try
        {
            return BotController_SetBuyPlan(slot, aliases ?? string.Empty) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool SetBuySkip(int slot)
    {
        if (!ValidSlot(slot))
            return false;
        try
        {
            return BotController_SetBuySkip(slot) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool ClearBuyPlan(int slot)
    {
        if (!ValidSlot(slot))
            return false;
        try
        {
            return BotController_ClearBuyPlan(slot) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool ClearAllBuyPlans()
    {
        try
        {
            return BotController_ClearAllBuyPlans() == 0;
        }
        catch
        {
            return false;
        }
    }

    public static int BuyPlanItemCount(int slot)
    {
        if (!ValidSlot(slot))
            return -1;
        try
        {
            return BotController_GetBuyPlanItemCount(slot);
        }
        catch
        {
            return -1;
        }
    }

    public static bool LockWeaponSlot(int slot, int target)
        => ValidSlot(slot) && target is >= 1 and <= 5 && BotController_Lock(slot, LockKindWeapon, target) == 0;

    public static bool UnlockWeaponSlot(int slot)
        => ValidSlot(slot) && BotController_Unlock(slot, LockKindWeapon) == 0;

    public static void UnlockReplayControl(int slot)
    {
        if (!ValidSlot(slot))
            return;
        BotController_Unlock(slot, LockKindAll);
        BotController_Unlock(slot, LockKindAim);
        BotController_Unlock(slot, LockKindJump);
    }

    private static bool ValidSlot(int slot)
        => slot is >= 0 and < MaxSlots;

    private static bool TryGetAbiInfo(out BotControllerAbiInfo info)
    {
        info = default;
        try
        {
            return BotController_GetAbiInfo(out info, BotControllerAbiInfo.ByteSize) == 0;
        }
        catch
        {
            info = BotControllerAbiInfo.Unavailable;
            return false;
        }
    }
}
