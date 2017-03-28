//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using NanoFramework.Tools.Debugger.WireProtocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NanoFramework.Tools.Debugger.Extensions
{
    public static class OutputExtensions
    {
        /// <summary>
        /// Prints a nicely formated output of a MemoryMap array.
        /// </summary>
        /// <param name="range"></param>
        /// <returns></returns>
        public static string ToStringForOutput(this List<Commands.Monitor_MemoryMap.Range> range)
        {
            StringBuilder output = new StringBuilder();

            try
            {
                if (range != null && range.Count > 0)
                {
                    output.AppendLine("Type     Start       Size");
                    output.AppendLine("--------------------------------");

                    foreach (Commands.Monitor_MemoryMap.Range item in range)
                    {
                        string mem = "";
                        switch (item.m_flags)
                        {
                            case Commands.Monitor_MemoryMap.c_FLASH:
                                mem = "FLASH";
                                break;
                            case Commands.Monitor_MemoryMap.c_RAM:
                                mem = "RAM";
                                break;
                        }

                        output.AppendLine(string.Format("{0,-6} 0x{1:x08}  0x{2:x08}", mem, item.m_address, item.m_length));
                    }
                    return output.ToString();
                }
            }
            catch { }

            return "Invalid or empty map data.";
        }

        public static string ToStringForOutput(this List<Commands.Monitor_FlashSectorMap.FlashSectorData> range)
        {
            StringBuilder output = new StringBuilder();

            try
            {
                if (range != null && range.Count > 0)
                {
                    output.AppendLine("  Region     Start      Blocks   Bytes/Block    Usage");
                    output.AppendLine("-----------------------------------------------------------");

                    int i = 0;

                    foreach (Commands.Monitor_FlashSectorMap.FlashSectorData item in range)
                    {
                        string usage = "";
                        switch (item.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK)
                        {
                            case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_BOOTSTRAP:
                                usage = "nanoBooter";
                                break;
                            case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CODE:
                                usage = "nanoCLR";
                                break;
                            case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CONFIG:
                                usage = "Configuration";
                                break;
                            case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_DEPLOYMENT:
                                usage = "Deployment";
                                break;
                            case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_UPDATE:
                                usage = "Update Storage";
                                break;
                            case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_FS:
                                usage = "File System";
                                break;
                            case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_SIMPLE_A:
                                usage = "Simple Storage (A)";
                                break;
                            case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_SIMPLE_B:
                                usage = "Simple Storage (B)";
                                break;
                            case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_STORAGE_A:
                                usage = "EWR Storage (A)";
                                break;
                            case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_STORAGE_B:
                                usage = "EWR Storage (B)";
                                break;
                        }

                        output.AppendLine($"{string.Format("{0,7}", i++)}    {string.Format("0x{0:x08}", item.m_StartAddress)}   {string.Format("{0,5}", item.m_NumBlocks)}      {string.Format("0x{0:x06}", item.m_BytesPerBlock)}     {usage}");
                    }

                    return output.ToString();
                }
            }
            catch { }

            return "Invalid or empty map data.";
        }

        public static string ToStringForOutput(this List<Commands.Monitor_DeploymentMap.DeploymentData> range)
        {
            StringBuilder output = new StringBuilder();

            try
            {
                if (range != null && range.Count > 0)
                {
                    int i = 0;

                    foreach (Commands.Monitor_DeploymentMap.DeploymentData item in range)
                    {
                        output.AppendLine("Assembly " + i++);
                        output.AppendLine("  Address: " + item.m_address.ToString());
                        output.AppendLine("  Size   : " + item.m_size.ToString());
                        output.AppendLine("  CRC    : " + item.m_CRC.ToString());
                    }

                    return output.ToString();
                }
                else
                {
                    return "No deployed assemblies";
                }
            }
            catch { }

            return "Invalid or empty map data.";
        }
    }
}
