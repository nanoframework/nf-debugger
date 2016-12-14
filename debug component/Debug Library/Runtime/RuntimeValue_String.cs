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
using System;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.SPOT.Debugger
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
