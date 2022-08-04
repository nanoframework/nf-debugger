//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;
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
                if ((value.Flags
                    & BlockRegionAttributes_MASK
                    & BlockRegionAttribute_ProgramWidthIs64bits) == BlockRegionAttribute_ProgramWidthIs64bits)
                {
                    // programming width is 64bits => 8 bytes
                    programmingAlignment = 8;
                }
                if ((value.Flags
                    & BlockRegionAttributes_MASK
                    & BlockRegionAttribute_ProgramWidthIs128bits) == BlockRegionAttribute_ProgramWidthIs128bits)
                {
                    // programming width is 128bits => 16 bytes
                    programmingAlignment = 16;
                }
                if ((value.Flags
                    & BlockRegionAttributes_MASK
                    & BlockRegionAttribute_ProgramWidthIs256bits) == BlockRegionAttribute_ProgramWidthIs256bits)
                {
                    // programming width is 256bits => 32 bytes
                    programmingAlignment = 32;
                }

                blocks.Add(new DeploymentBlock(
                    (int)value.StartAddress + (i * (int)value.BytesPerBlock),
                    (int)value.BytesPerBlock,
                    programmingAlignment));
            }

            return new DeploymentSector(blocks);
        }
    }
}
