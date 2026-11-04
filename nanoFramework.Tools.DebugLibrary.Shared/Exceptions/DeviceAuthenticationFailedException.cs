//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//
using System;

namespace nanoFramework.Tools.Debugger
{
    /// <summary>
    /// Thrown when a network device rejects the authentication token.
    /// </summary>
    public class DeviceAuthenticationFailedException : Exception
    {
        /// <summary>
        /// Id of the device that refused the authentication.
        /// </summary>
        public string DeviceId { get; }

        public DeviceAuthenticationFailedException(string deviceId)
            : base($"Device {deviceId} rejected the authentication token.")
        {
            DeviceId = deviceId;
        }
    }
}
