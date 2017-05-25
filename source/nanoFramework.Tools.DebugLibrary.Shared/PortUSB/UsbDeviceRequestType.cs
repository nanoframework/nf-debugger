//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger.Usb
{
    public enum UsbDeviceRequestType
    {
        GetStatus = 0,
        ClearFeature = 1,
        _Reserved0 = 2,
        SetFeature = 3,
        _Reserved1 = 4,
        SetAddress = 5,
        GetDescriptor = 6,
        SetDescriptor = 7,
        GetConfiguration = 8,
        SetConfiguration = 9,
        GetInterface = 10,
        SetInterface = 11,
        SyncFrame = 12
    }
}
