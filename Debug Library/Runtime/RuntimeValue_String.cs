//
// Copyright (c) .NET Foundation and Contributors
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
                var task = m_eng.ReadMemoryAsync(m_handle.m_charsInString, m_handle.m_bytesInString);
                task.Start();
                if(task.Wait(5000))
                {
                    if (task.Result.Item2 == false)
                    {
                        // Revert to the preview on failure
                        buf = handle.m_builtinValue;
                    }
                    else
                    {
                        // copy return value back to handler value
                        Array.Copy(task.Result.Item1, 0, handle.m_builtinValue, 0, task.Result.Item1.Length);
                    }
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

        internal override async Task SetStringValueAsync(string val)
        {
            byte[] buf = Encoding.UTF8.GetBytes(val);

            if (buf.Length != m_handle.m_bytesInString)
            {
                throw new ArgumentException("String must have same length");
            }

            if (await m_eng.WriteMemoryAsync(m_handle.m_charsInString, buf) == false)
            {
                throw new ArgumentException("Cannot write string");
            }

            m_value = val;
        }
    }
}
