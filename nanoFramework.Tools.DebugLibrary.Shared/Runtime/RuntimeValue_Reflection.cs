//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.Debugger
{
    public class RuntimeValue_Reflection : RuntimeValue
    {
        private readonly ReflectionDefinition m_rd;

        protected internal RuntimeValue_Reflection(Engine eng, WireProtocol.Commands.Debugging_Value handle) : base(eng, handle)
        {
            m_rd = (ReflectionDefinition)Activator.CreateInstance((typeof(ReflectionDefinition)));

            m_eng.CreateConverter().Deserialize(m_rd, handle.m_builtinValue);
        }

        public override bool IsReference { get { return false; } }
        public override bool IsNull { get { return false; } }
        public override bool IsPrimitive { get { return false; } }
        public override bool IsValueType { get { return false; } }
        public override bool IsArray { get { return false; } }
        public override bool IsReflection { get { return true; } }

        public ReflectionDefinition.Kind ReflectionType
        {
            get
            {
                return (ReflectionDefinition.Kind)m_rd.m_kind;
            }
        }

        public ushort ArrayDepth
        {
            get
            {
                return m_rd.m_levels;
            }
        }

        public uint ReflectionIndex
        {
            get
            {
                return m_rd.m_raw;
            }
        }
    }
}
