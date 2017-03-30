﻿//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

namespace NanoFramework.Tools.Debugger
{
    public class ReflectionDefinition
    {
        public enum Kind : ushort
        {
            REFLECTION_INVALID = 0x00,
            REFLECTION_ASSEMBLY = 0x01,
            REFLECTION_TYPE = 0x02,
            REFLECTION_TYPE_DELAYED = 0x03,
            REFLECTION_CONSTRUCTOR = 0x04,
            REFLECTION_METHOD = 0x05,
            REFLECTION_FIELD = 0x06,
        };

        public ushort m_kind;
        public ushort m_levels;

        public uint m_raw;
    }
}
