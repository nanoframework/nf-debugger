//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger
{
    public class RuntimeValue_Generic : RuntimeValue
    {
        protected internal RuntimeValue_Generic(Engine eng, WireProtocol.Commands.Debugging_Value handle) : base(eng, handle)
        {
        }

        public override bool IsReference { get { return false; } }
        public override bool IsNull { get { return false; } }
        public override bool IsPrimitive { get { return false; } }
        public override bool IsValueType { get { return false; } }
        public override bool IsArray { get { return false; } }
        public override bool IsReflection { get { return false; } }
        public override bool IsGenericInst { get { return true; } }

        public override uint Type => m_handle.m_ts;

        public override uint NumOfFields
        {
            get
            {
                return m_handle.m_size - 1;
            }
        }

        public override RuntimeValue GetField(uint offset, uint fd)
        {
            return m_eng.GetFieldValue(this, offset, fd);
        }
    }
}
