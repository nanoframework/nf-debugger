//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools;
using System;
using System.Text;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger
{
    public class RuntimeValue_String : RuntimeValue
    {
        internal string m_value;

        protected internal RuntimeValue_String(Engine eng, WireProtocol.Commands.Debugging_Value handle) : base(eng, handle)
        {
            byte[] buf = handle.m_builtinValue;

            if (handle.m_bytesInString >= buf.Length)
            {
                var result = m_eng.ReadMemory(m_handle.m_charsInString, m_handle.m_bytesInString);

                if (!result.Success)
                {
                    // Revert to the preview on failure
                    buf = handle.m_builtinValue;
                }
                else
                {
                    // copy return value back to handler value
                    Array.Copy(result.Buffer, 0, handle.m_builtinValue, 0, result.Buffer.Length);
                }
            }

            m_value = WireProtocol.Commands.GetZeroTerminatedString(buf, true);
        }

        public override bool IsReference { get { return false; } }
        public override bool IsNull { get { return false; } }
        public override bool IsPrimitive { get { return false; } }
        public override bool IsValueType { get { return false; } }
        public override bool IsArray { get { return false; } }
        public override bool IsReflection { get { return false; } }

        public override object Value
        {
            get
            {
                return m_value;
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        internal override void SetStringValue(string val)
        {
            byte[] buf = Encoding.UTF8.GetBytes(val);

            if (buf.Length != m_handle.m_bytesInString)
            {
                throw new ArgumentException("String must have same length");
            }

            var writeResult = m_eng.WriteMemory(m_handle.m_charsInString, buf);
            if (writeResult.Success == false)
            {
                throw new ArgumentException("Cannot write string");
            }

            m_value = val;
        }
    }
}
