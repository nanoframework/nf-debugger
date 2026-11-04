//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Text;

namespace nanoFramework.Tools.Debugger
{
    /// <summary>
    /// Wire Protocol configuration block: the authentication token of the network channel.
    /// KEEP IN SYNC WITH native HAL_Configuration_WPAuthToken in nanoHAL_Network.h.
    /// </summary>
    public class WPAuthTokenConfigurationBase
    {
        /// <summary>
        /// This is the marker placeholder for this configuration block
        /// 4 bytes length.
        /// </summary>
        public byte[] Marker;

        /// <summary>
        /// Length of the token, 0 for no authentication.
        /// </summary>
        public byte TokenLength;

        /// <summary>
        /// Authentication token (UTF-8, not null terminated), padded with zeros.
        /// </summary>
        public byte[] Token;

        public WPAuthTokenConfigurationBase()
        {
            // need to init these here to match the expected size on the struct to be sent to the device
            Marker = new byte[4];
            Token = new byte[DeviceConfiguration.WPAuthTokenMaxTokenLength];
        }

        /// <summary>
        /// Creates the block for a token.
        /// </summary>
        /// <param name="token">The token. Empty turns the authentication off.</param>
        /// <exception cref="ArgumentNullException"><paramref name="token"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">The UTF-8 encoding of <paramref name="token"/> is too long.</exception>
        public static WPAuthTokenConfigurationBase Create(string token)
        {
            if (token is null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            byte[] bytes = Encoding.UTF8.GetBytes(token);

            if (bytes.Length > DeviceConfiguration.WPAuthTokenMaxTokenLength)
            {
                throw new ArgumentException(
                    $"The token is {bytes.Length} bytes long (UTF-8), the maximum is {DeviceConfiguration.WPAuthTokenMaxTokenLength}.",
                    nameof(token));
            }

            var block = new WPAuthTokenConfigurationBase
            {
                Marker = Encoding.UTF8.GetBytes(DeviceConfiguration.MarkerConfigurationWPAuthToken_v1),
                TokenLength = (byte)bytes.Length
            };

            Array.Copy(bytes, block.Token, bytes.Length);

            return block;
        }
    }
}
