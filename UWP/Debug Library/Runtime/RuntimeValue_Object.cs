﻿//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using NanoFramework.Tools;

namespace NanoFramework.Tools.Debugger
{
    public class RuntimeValue_Object : RuntimeValue_Indirect
    {
        protected internal RuntimeValue_Object(Engine eng, WireProtocol.Commands.Debugging_Value[] array, int pos) : base(eng, array, pos)
        {
        }
    }
}
