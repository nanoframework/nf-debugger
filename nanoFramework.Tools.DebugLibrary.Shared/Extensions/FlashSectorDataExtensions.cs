//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;
using System;
using System.Collections.Generic;
using static nanoFramework.Tools.Debugger.WireProtocol.Commands.Monitor_FlashSectorMap;

namespace nanoFramework.Tools.Debugger.Extensions
{
    public static class FlashSectorDataExtensions
    {
        /// <summary>
        /// Convert a <see cref="FlashSectorData"/> into a <see cref="DeploymentSector"/>
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static DeploymentSector ToDeploymentSector(this FlashSectorData value)
        {
            // build a DeploymentSector from a FlashSectorData

            List<DeploymentBlock> blocks = new List<DeploymentBlock>();

            for (int i = 0; i < value.NumBlocks; i++)
            {
                int programmingAlignment = 0;

                // check alignment requirements
                uint blockRegionFlashProgrammingWidth = value.Flags & BlockRegionFlashProgrammingWidth_MASK;
                uint blockRegionBitsSet = blockRegionFlashProgrammingWidth;

                // Zero, or 1 bit only allowed to be set for programming width
                uint countOfBitsSet = 0;
                while (blockRegionBitsSet > 0)
                {
                    countOfBitsSet += blockRegionBitsSet & 1;
                    blockRegionBitsSet >>= 1;
                }
                if( countOfBitsSet > 1)
                {
                    throw new NotSupportedException("Multiple selections for Flash Program Width found, only one supported per block");
                }

                switch (blockRegionFlashProgrammingWidth)
                {
                    case BlockRegionAttribute_ProgramWidthIs8bits:
                        // when not specified, default to minimum flash word size
                        programmingAlignment = 0;
                        break;

                    case BlockRegionAttribute_ProgramWidthIs64bits:
                        programmingAlignment = 64 / 8;
                        break;

                    case BlockRegionAttribute_ProgramWidthIs128bits:
                        programmingAlignment = 128 / 8;
                        break;

                    case BlockRegionAttribute_ProgramWidthIs256bits:
                        programmingAlignment = 256 / 8;
                        break;

                    case BlockRegionAttribute_ProgramWidthIs512bits:
                        programmingAlignment = 512 / 8;
                        break;

                    case BlockRegionAttribute_ProgramWidthIs1024bits:
                        programmingAlignment = 1024 / 8;
                        break;

                    case BlockRegionAttribute_ProgramWidthIs2048bits:
                        programmingAlignment = 2048 / 8;
                        break;

                    default:
                        throw new NotSupportedException($"The specified Flash Program Width '{blockRegionFlashProgrammingWidth}' is not supported. Please check the native implementation and/or that you have the .NET nanoFramework Visual Studio extension update.");
                }

                Console.WriteLine($"The value is {programmingAlignment}");

                blocks.Add(new DeploymentBlock(
                    (int)value.StartAddress + (i * (int)value.BytesPerBlock),
                    (int)value.BytesPerBlock,
                    programmingAlignment));
            }

            return new DeploymentSector(blocks);
        }
    }
}
