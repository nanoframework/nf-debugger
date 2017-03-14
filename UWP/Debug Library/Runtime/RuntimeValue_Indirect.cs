//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using NanoFramework.Tools;
using System;
using System.Threading.Tasks;

namespace NanoFramework.Tools.Debugger
{
    public class RuntimeValue_Indirect : RuntimeValue
    {
        protected RuntimeValue m_value;

        protected internal RuntimeValue_Indirect(Engine eng, WireProtocol.Commands.Debugging_Value[] array, int pos) : base(eng, array[pos])
        {
            if (++pos < array.Length)
            {
                m_value = Convert(eng, array, pos);
            }
        }

        public override uint ReferenceId
        {
            get
            {
                return m_value == null ? 0 : m_value.ReferenceId;
            }
        }

        public override CorElementType CorElementType
        {
            get
            {
                return m_value == null ? CorElementTypeDirect : m_value.CorElementType;
            }
        }

        public override bool IsBoxed
        {
            get
            {
                return base.IsBoxed || (m_value != null && m_value.IsBoxed);
            }
        }

        public override RuntimeDataType DataType { get { return (m_value == null) ? RuntimeDataType.DATATYPE_FIRST_INVALID : m_value.DataType; } }

        public override bool IsReference { get { return false; } }
        public override bool IsNull { get { return m_value == null; } }
        public override bool IsPrimitive { get { return (m_value != null && m_value.IsPrimitive); } }
        public override bool IsValueType { get { return (m_value != null && m_value.IsValueType); } }
        public override bool IsArray { get { return (m_value != null && m_value.IsArray); } }
        public override bool IsReflection { get { return false; } }

        public override object Value
        {
            get
            {
                if (m_value == null) return null;

                return m_value.Value;
            }

            set
            {
                if (m_value == null) return;

                m_value.Value = value;
            }
        }

        public override uint NumOfFields { get { return (m_value == null) ? 0 : m_value.NumOfFields; } }
        public override uint Length { get { return (m_value == null) ? 0 : m_value.Length; } }
        public override uint Depth { get { return (m_value == null) ? 0 : m_value.Depth; } }
        public override uint Type { get { return (m_value == null) ? 0 : m_value.Type; } }

        internal override async Task SetStringValueAsync(string val)
        {
            if (m_value == null) throw new NotImplementedException();

            await m_value.SetStringValueAsync(val).ConfigureAwait(false);
        }

        public override async Task<RuntimeValue> GetFieldAsync(uint offset, uint fd)
        {
            return (m_value == null) ? null : await m_value.GetFieldAsync(offset, fd).ConfigureAwait(false);
        }

        public override async Task<RuntimeValue> GetElementAsync(uint index)
        {
            return (m_value == null) ? null : await m_value.GetElementAsync(index).ConfigureAwait(false);
        }
    }
}
