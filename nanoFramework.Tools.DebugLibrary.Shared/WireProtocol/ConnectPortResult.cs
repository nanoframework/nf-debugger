// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.Debugger
{
    public enum ConnectPortResult
    {
        /// <summary>
        /// No result set for the connect port.
        /// </summary>
        None,

        /// <summary>
        /// Port connected successfully.
        /// </summary>
        Connected,

        /// <summary>
        /// Couldn't connect to the specified port.
        /// </summary>
        NotConnected,

        /// <summary>
        /// Connection to the specified port is not authorized
        /// </summary>
        Unauthorized,

        /// <summary>
        /// Exception occurred when attempting to connect to the specified port.
        /// </summary>
        ExceptionOccurred,

        /// <summary>
        /// Exclusive access to the device was not granted
        /// </summary>
        NoExclusiveAccess
    }
}
