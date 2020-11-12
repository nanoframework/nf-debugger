//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger
{
    /// <summary>
    /// Reboot options for nanoFramework device.
    /// </summary>
    public enum RebootOptions
    {
        /// <summary>
        /// Hard reboot CPU.
        /// </summary>
        NormalReboot = 0,

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
        EnterProprietaryBooter = 5,
    };
}
