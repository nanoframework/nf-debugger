//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger
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
