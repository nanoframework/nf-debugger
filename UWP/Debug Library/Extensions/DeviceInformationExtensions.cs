//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using Windows.Devices.Enumeration;

namespace NanoFramework.Tools.Debugger.Extensions
{
    public static class DeviceInformationExtensions
    {
        public static string GetSerialNumber(this DeviceInformation value)
        {
            // typical ID string is \\?\USB#VID_0483&PID_5740#NANO_3267335D#{86e0d1e0-8089-11d0-9ce4-08003e301f73}

            int startIndex = value.Id.IndexOf("USB");

            int endIndex = value.Id.LastIndexOf("#");

            // sanity check
            if(startIndex < 0 || endIndex < 0)
            {
                return null;
            }

            // get device ID portion
            var deviceIDCollection = value.Id.Substring(startIndex, endIndex - startIndex).Split('#');

            return deviceIDCollection?.GetValue(2) as string;
        }
    }
}
