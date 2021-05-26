//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

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
        ExceptionOccurred
    }
}
