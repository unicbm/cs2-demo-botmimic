// funchook for CS2 movement functions (ProcessMovement / PhysicsSimulate / FinishMove / PlayerRunCommand)

#pragma once

#include <cstdint>
#include <string>

#include <nlohmann/json.hpp>
#include "sig_scan.h"

namespace BotController
{
    namespace InputInjector
    {
        // Max bots we track per-slot state for.
        static constexpr int kMaxSlots = 64;

        // Resolve sigs and install the movement hooks.
        bool Install(const nlohmann::json &gd, const Sig::ModuleInfo &serverModule,
                     char *errorOut, size_t errorOutLen);

        // Disable + remove the hooks.
        void Remove();

        // Optional schema offset for CCSPlayerController::m_bControllingBot.
        // When available, replay stops immediately if a real player takes over a bot.
        bool SetControllerControllingBotOffset(int offset);

        // Short-lived, per-slot movement input lease. This is a low-level
        // usercmd/movedata primitive; policy lives in the caller. Only
        // movement button bits (WASD/duck/jump) are applied.
        bool SetUsercmdMovementIntent(int slot, uint64_t buttonsSet, uint64_t buttonsClear,
                                      float analogForward, float analogLeft,
                                      int durationMs, int flags);
        bool ClearUsercmdMovementIntent(int slot);
        void ClearAllUsercmdMovementIntents();

        const char *Status();

        // Whether replay should inject subtick pitch_delta/yaw_delta into
        // usercmd. Disabled by default because offline demo pawn snapshots do
        // not prove they are aligned to CBaseUserCmdPB base viewangles.
        void SetReplaySubtickViewDeltas(bool enabled);
        bool ReplaySubtickViewDeltas();

        // Last CCSPlayer_MovementServices* seen for this player slot.
        void *LiveMovementServices(int slot);

        // Resolved address of the hooked function.
        void *ProcessUsercmdAddress();

        // Diagnostics
        uint64_t HookCallCount();
        int LastResolvedSlot();
    }
}
