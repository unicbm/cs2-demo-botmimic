using System.Runtime.InteropServices;

namespace DemoTracer;

internal static partial class BotControllerNative
{
    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_Lock(int slot, int kind, int arg);

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_Unlock(int slot, int kind);

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_GetVersion();

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_GetAbiInfo(out BotControllerAbiInfo info, int size);

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong BotController_GetCapabilities();

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr BotController_GetBuildId();

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_SetControllerControllingBotOffset(int offset);

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_SetReplayPovMask(ulong mask);

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_LoadReplay(
        int slot,
        [In] NativeReplayTick[] ticks,
        int tickCount,
        [In] NativeSubtickMove[] subs,
        int subCount);

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_StartReplay(int slot, int loop);

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_StartReplayAt(int slot, int loop, int startIndex);

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_StartReplayUntil(
        int slot,
        int loop,
        int startIndex,
        int holdBeforeIndex);

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_StopReplay(int slot);

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_GetReplayCursor(int slot);

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_GetReplayTotal(int slot);

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_GetReplaySlotState(
        int slot,
        out NativeReplaySlotState state);

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_GetReplayTick(int slot, out NativeReplayTick tick);

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_SwitchBotWeapon(int slot, int defIndex);

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_GetBotActiveWeaponDef(int slot);

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_SetBuyPlan(
        int slot,
        [MarshalAs(UnmanagedType.LPStr)] string aliases);

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_SetBuySkip(int slot);

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_ClearBuyPlan(int slot);

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_ClearAllBuyPlans();

    [DllImport("BotController", CallingConvention = CallingConvention.Cdecl)]
    private static extern int BotController_GetBuyPlanItemCount(int slot);
}
