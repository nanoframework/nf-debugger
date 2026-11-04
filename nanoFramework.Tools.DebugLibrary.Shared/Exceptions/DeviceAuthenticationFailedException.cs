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

        /// <summary>
        /// Creates the exception for a device that rejected the token.
        /// </summary>
        /// <param name="deviceId">Id of the device.</param>
        public DeviceAuthenticationFailedException(string deviceId)
            : base($"Device {deviceId} rejected the authentication token.")
        {
            DeviceId = deviceId;
        }
    }
}
