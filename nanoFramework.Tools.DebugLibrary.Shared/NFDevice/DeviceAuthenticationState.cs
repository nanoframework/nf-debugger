// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.Debugger
{
    /// <summary>
    /// Authentication state of a device. Only network devices require authentication, all the others are always <see cref="Authenticated"/>.
    /// </summary>
    public enum DeviceAuthenticationState
    {
        /// <summary>
        /// The device accepted the token, or it doesn't require one.
        /// </summary>
        Authenticated = 0,

        /// <summary>
        /// The device requires a token and none is available.
        /// </summary>
        TokenMissing,

        /// <summary>
        /// The device rejected the token.
        /// </summary>
        TokenRejected
    }
}
