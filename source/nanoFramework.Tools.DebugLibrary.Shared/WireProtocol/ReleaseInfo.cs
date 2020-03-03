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
        private const int c_sizeOfPlatformInfo = 128;

        // these constants below hold the size of the old V0 format and the new one
        // they are used to init the byte array with raw data and trying to check if there is enough data for PlatformInfo
        private const int TotalSizeOfRawV0 = c_sizeOfVersion + c_sizeOfInfo + c_sizeOfTargetName + c_sizeOfPlatformName;
        private const int TotalSizeOfRaw =   c_sizeOfVersion + c_sizeOfInfo + c_sizeOfTargetName + c_sizeOfPlatformName + c_sizeOfPlatformInfo;

        private readonly VersionStruct _version;
        private byte[] _rawInfo;

        public ReleaseInfo()
        {
            _version = new VersionStruct();

            InitFields(TotalSizeOfRawV0);
        }

        public ReleaseInfo(int dataLength)
        {
            _version = new VersionStruct();

            InitFields(dataLength);
        }

        private void InitFields(int dataLength)
        {
            if (dataLength == TotalSizeOfRaw)
            {
                // need to subtract the size of the _version field
                _rawInfo = new byte[TotalSizeOfRaw - c_sizeOfVersion];
            }
            else
            {
                // need to subtract the size of the _version field
                _rawInfo = new byte[TotalSizeOfRawV0 - c_sizeOfVersion];
            }
        }

        public void PrepareForDeserialize(int size, byte[] data, Converter converter)
        {
            // need to subtract the size of the _version field
            _rawInfo =  new byte[data.Length - c_sizeOfVersion];
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

        public string PlatformInfo
        {
            get
            {
                if (_rawInfo.Length == TotalSizeOfRaw - c_sizeOfVersion)
                {
                    var myString = Encoding.UTF8.GetString(_rawInfo, c_sizeOfInfo + c_sizeOfTargetName + c_sizeOfPlatformName, c_sizeOfPlatformInfo);
                    return myString.Substring(0, myString.IndexOf('\0'));
                }
                else
                {
                    // old version format, no PlatformInfo
                    return "";
                }
            }
        }
    }
}
