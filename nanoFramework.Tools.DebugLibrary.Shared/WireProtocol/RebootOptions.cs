//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.Debugger
{
    /// <summary>
    /// Reboot options for nanoFramework device.
    /// </summary>
    [Flags]
    public enum RebootOptions
    {
        /// <summary>
        /// Hard reboot CPU.
        /// </summary>
#pragma warning disable S2346 // Need this to be 0 because of native implementation
        NormalReboot = 0,
#pragma warning restore S2346 // Flags enumerations zero-value members should be named "None"

        /// <summary>
        /// Reboot and enter nanoBooter.
        /// </summary>
        EnterNanoBooter = 1,

        /// <summary>
        /// Reboot CLR only.
        /// </summary>
        ClrOnly = 2,

        /// <summary>
        /// Wait for debugger.
        /// </summary>
        WaitForDebugger = 4,

        /// <summary>
        /// Reboot and enter proprietary bootloader.
        /// </summary>
        EnterProprietaryBooter = 8,
    };
}
