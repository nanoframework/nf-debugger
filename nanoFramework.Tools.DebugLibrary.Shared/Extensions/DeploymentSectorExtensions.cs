//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;
using System.Collections.Generic;

namespace nanoFramework.Tools.Debugger.Extensions
{
    public static class DeploymentSectorExtensions
    {
        public static List<DeploymentBlock> ToDeploymentBlockList(this List<DeploymentSector> value)
        {
            List<DeploymentBlock> blocks = new List<DeploymentBlock>();

            foreach(DeploymentSector sector in value)
            {
                blocks.AddRange(sector.Blocks);
            }

            return blocks;
        }
    }
}
