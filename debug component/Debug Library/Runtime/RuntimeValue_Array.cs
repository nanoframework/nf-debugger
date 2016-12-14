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

using Microsoft.NetMicroFramework.Tools;
using System.Threading.Tasks;

namespace Microsoft.SPOT.Debugger
{
    public class RuntimeValue_Array : RuntimeValue
    {
        protected internal RuntimeValue_Array(Engine eng, WireProtocol.Commands.Debugging_Value handle) : base(eng, handle)
        {
        }

        public override bool IsReference { get { return false; } }
        public override bool IsNull { get { return false; } }
        public override bool IsPrimitive { get { return false; } }
        public override bool IsValueType { get { return false; } }
        public override bool IsArray { get { return true; } }
        public override bool IsReflection { get { return false; } }

        public override async Task<RuntimeValue> GetElementAsync(uint index)
        {
            return await m_eng.GetArrayElementAsync(m_handle.m_referenceID, index).ConfigureAwait(false);
        }

        public override uint Length { get { return m_handle.m_array_numOfElements; } }
        public override uint Depth { get { return m_handle.m_array_depth; } }
        public override uint Type { get { return m_handle.m_array_typeIndex; } }

    }
}
