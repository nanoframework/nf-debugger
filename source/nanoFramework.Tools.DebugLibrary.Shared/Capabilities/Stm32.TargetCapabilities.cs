﻿//
// Copyright (c) 2019 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger
{
    /////////////////////////////////////////////////////////////////////////////////////////////////////////
    // !!! KEEP IN SYNC WITH platform_target_capabilities.h (in nf-interpreter inside the platform folder) //
    /////////////////////////////////////////////////////////////////////////////////////////////////////////

    public partial class Stm32
    {
        public enum TargetCapabilities : byte
        {
            JtagUpdate  = 0,

            DfuUpdate   = 1,
        }
    }
}