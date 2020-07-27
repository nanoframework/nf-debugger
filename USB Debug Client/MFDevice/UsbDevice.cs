//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the Microsoft .NET Micro Framework and is unsupported. 
// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use these files except in compliance with the License.
// You may obtain a copy of the License at:
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing
// permissions and limitations under the License.
// 
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using Microsoft.NetMicroFramework.Tools.UsbDebug;
using Microsoft.SPOT.Debugger;
using Microsoft.SPOT.Debugger.WireProtocol;
using System;
using System.Threading.Tasks;

namespace Microsoft.NetMicroFramework.Tools
{
    public class UsbDevice : MFDeviceBase, IMFDevice
    {
        /// <summary>
        /// .NETMF debug engine
        /// </summary>
        //public Engine<MFDevice> DebugEngine { get; protected set; }

        public UsbDeviceInformation DeviceInformation
        {
            get
            {
                return DeviceObject as UsbDeviceInformation;
            }

            set
            {
                DeviceObject = value;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public UsbDebugClient Parent { get; set; }

        public UsbDevice()
        {
            Transport = TransportType.Usb;
        }

        /// <summary>
        /// Connect to NETMF device
        /// </summary>
        /// <returns>True if operation is successful</returns>
        public Task<bool> ConnectAsync()
        {
            return Parent.ConnectDeviceAsync(this);
        }

        /// <summary>
        /// Disconnect NETMF device
        /// </summary>
        public void Disconnect()
        {
            Parent.DisconnectDevice(this);
        }
    }
}
