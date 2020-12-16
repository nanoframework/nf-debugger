//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;

namespace nanoFramework.Tools.Debugger.Extensions
{
    public static class MonitorFlashSectorMapExtensions
    {
        public static string UsageAsString(this Commands.Monitor_FlashSectorMap.FlashSectorData value)
        {
            switch (value.Flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK)
            {
                case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_BOOTSTRAP:
                    return "nanoBooter";
                case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CODE:
                    return "nanoCLR";
                case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CONFIG:
                    return "Configuration";
                case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_DEPLOYMENT:
                    return "Deployment";
                case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_UPDATE:
                    return "Update Storage";
                case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_FS:
                    return "File System";
                case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_SIMPLE_A:
                    return "Simple Storage (A)";
                case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_SIMPLE_B:
                    return "Simple Storage (B)";
                case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_STORAGE_A:
                    return "EWR Storage (A)";
                case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_STORAGE_B:
                    return "EWR Storage (B)";
                default:
                    return "";
            }
        }
    }
}
