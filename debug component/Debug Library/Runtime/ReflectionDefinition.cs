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

namespace Microsoft.SPOT.Debugger
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
