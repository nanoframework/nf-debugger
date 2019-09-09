//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Text;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public class ReleaseInfo : IConverter
    {
        // these constants reflect the size of the struct NFReleaseInfo in native code @ nanoHAL_ReleaseInfo.h
        private const int c_sizeOfVersion = 8;
        private const int c_sizeOfInfo = 128;
        private const int c_sizeOfTargetName = 32;
        private const int c_sizeOfPlatformName = 32;

        private VersionStruct _version;
        private byte[] _rawInfo;

        public ReleaseInfo()
        {
            _version = new VersionStruct();
            _rawInfo = new byte[c_sizeOfInfo + c_sizeOfTargetName + c_sizeOfPlatformName];
        }

        public void PrepareForDeserialize(int size, byte[] data, Converter converter)
        {
            _rawInfo = new byte[c_sizeOfInfo + c_sizeOfTargetName + c_sizeOfPlatformName];
        }

        public Version Version => _version.Version;

        public string Info
        {
            get
            {
                var myString = Encoding.UTF8.GetString(_rawInfo, 0, c_sizeOfInfo);
                return myString.Substring(0, myString.IndexOf('\0'));
            }
        }

        public string TargetName
        {
            get
            {
                var myString = Encoding.UTF8.GetString(_rawInfo, c_sizeOfInfo, c_sizeOfTargetName);
                return myString.Substring(0, myString.IndexOf('\0'));
            }
        }

        public string PlatformName
        {
            get
            {
                var myString = Encoding.UTF8.GetString(_rawInfo, c_sizeOfInfo + c_sizeOfTargetName, c_sizeOfPlatformName);
                return myString.Substring(0, myString.IndexOf('\0'));
            }
        }
    }
}
