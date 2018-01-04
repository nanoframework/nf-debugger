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

        private VersionStruct _version;
        private byte[] _info;

        public ReleaseInfo()
        {
            _version = new VersionStruct();
            _info = new byte[c_sizeOfInfo - c_sizeOfVersion];
        }

        public void PrepareForDeserialize(int size, byte[] data, Converter converter)
        {
            _info = new byte[c_sizeOfInfo - c_sizeOfVersion];
        }

        public Version Version => _version.Version;

        public string Info => Encoding.UTF8.GetString(_info, 0, _info.Length).TrimEnd('\0');
    }
}
