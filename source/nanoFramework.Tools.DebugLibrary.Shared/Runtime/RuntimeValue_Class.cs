//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger
{
    public class RuntimeValue_Class : RuntimeValue
    {
        protected internal RuntimeValue_Class(Engine eng, WireProtocol.Commands.Debugging_Value handle) : base(eng, handle)
        {
        }

        public override bool IsReference { get { return false; } }
        public override bool IsNull { get { return false; } }
        public override bool IsPrimitive { get { return false; } }
        public override bool IsValueType { get { return false; } }
        public override bool IsArray { get { return false; } }
        public override bool IsReflection { get { return false; } }

        public override uint NumOfFields
        {
            get
            {
                return m_handle.m_size - 1;
            }
        }

        public override async Task<RuntimeValue> GetFieldAsync(uint offset, uint fd)
        {
            return await m_eng.GetFieldValueAsync(this, offset, fd);
        }
    }
}
