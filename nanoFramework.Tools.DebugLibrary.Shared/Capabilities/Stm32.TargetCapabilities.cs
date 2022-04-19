//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger
{
    /////////////////////////////////////////////////////////////////////////////////////////////////////////
    // !!! KEEP IN SYNC WITH platform_target_capabilities.h (in nf-interpreter inside the platform folder) //
    /////////////////////////////////////////////////////////////////////////////////////////////////////////

    public partial class Stm32
    {
        public enum TargetCapabilities : ulong
        {
            JtagUpdate  = CLRCapabilities.Capability.TargetCapability_0,

            DfuUpdate   = CLRCapabilities.Capability.TargetCapability_1,
        }
    }
}