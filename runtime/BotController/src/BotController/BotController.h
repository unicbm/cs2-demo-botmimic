// MinHook install/remove for CCSBot Update/Upkeep/Jump.

#pragma once

#include <string>

#include <nlohmann/json.hpp>
#include "sig_scan.h"

namespace BotController
{
    namespace BotControllerHooks
    {
        // Resolve sigs and install detours.
        bool Install(const nlohmann::json &gd, const Sig::ModuleInfo &serverModule,
                     char *errorOut, size_t errorOutLen);

        // Disable + remove detours.
        void Remove();

        const char *Status();

        void *UpdateAddress();
        void *UpkeepAddress();
        void *JumpAddress();
        void *UpdateLookAnglesAddress();
    }
}
