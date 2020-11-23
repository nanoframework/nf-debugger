//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Text;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public class TargetInfo : IConverter
    {
        private const int c_sizeOfVersion = 8;
        private const int c_sizeOfInfo = 128;
        private const int c_sizeOfTargetName = 32;
        private const int c_sizeOfPlatformName = 32;
        private const int c_sizeOfPlatformInfo = 128;

        // these constants below hold the size of the current version 
        // they are used to init the byte array with raw data and trying to check if there is enough data for PlatformInfo
        private const int TotalSizeOfRaw = c_sizeOfVersion + c_sizeOfVersion + c_sizeOfInfo + c_sizeOfTargetName + c_sizeOfPlatformName + c_sizeOfPlatformInfo;

        private readonly VersionStruct _booterVersion;
        private readonly VersionStruct _clrVersion;
        private byte[] _rawInfo;

        public TargetInfo(int dataLength = TotalSizeOfRaw)
        {
            _booterVersion = new VersionStruct();
            _clrVersion = new VersionStruct();

            InitFields(dataLength);
        }

        private void InitFields(int dataLength)
        {
            if (dataLength == TotalSizeOfRaw)
            {
                // need to subtract the size of both version fields
                _rawInfo = new byte[TotalSizeOfRaw - c_sizeOfVersion - c_sizeOfVersion];
            }
            else
            {
                // shouldn't be here
                throw new ArgumentOutOfRangeException($"{nameof(dataLength)} has an invalid value");
            }
        }

        public void PrepareForDeserialize(int size, byte[] data, Converter converter)
        {
            // need to subtract the size of both version fields
            _rawInfo =  new byte[data.Length - c_sizeOfVersion - c_sizeOfVersion];
        }

        public Version BooterVersion => _booterVersion.Version;

        public Version ClrVersion => _clrVersion.Version;

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
                if (_rawInfo.Length == TotalSizeOfRaw - c_sizeOfVersion - c_sizeOfVersion)
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

        public override string ToString()
        {
            try
            {
                StringBuilder output = new StringBuilder();

                output.AppendLine($"HAL build info: {Info?.ToString()}");
                output.AppendLine($"  Target:     {TargetName?.ToString()}");
                output.AppendLine($"  Platform:   {PlatformName?.ToString()}");
                output.AppendLine($"  nanoBooter: v{BooterVersion}");
                output.AppendLine($"  nanoCLR:    v{ClrVersion}");
                output.AppendLine($"  Type:       {PlatformInfo}");
                output.AppendLine();

                return output.ToString();
            }
            catch
            {
                // OK to fail. Most likely because of a formatting issue.
            }

            return "ReleaseInfo is not valid!";
        }
    }
}
