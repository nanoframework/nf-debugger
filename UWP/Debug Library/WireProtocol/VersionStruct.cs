//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;

namespace NanoFramework.Tools.Debugger.WireProtocol
{
    public class VersionStruct
    {
        public ushort major;
        public ushort minor;
        public ushort build;
        public ushort revision;

        public Version Version
        {
            get { return new Version(major, minor, build, revision); }
        }
    }
}
