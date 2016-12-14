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

using System;
using System.Reflection;

namespace Microsoft.SPOT.Debugger
{
    static class TypeExtensions
    {
        public static Type BaseType(this Type t)
        {
            return t.GetTypeInfo().BaseType;
        }
        /// <summary>
        /// Gets the TypeCode value for a type.
        /// </summary>
        /// <param name="t">Type object.</param>
        /// <returns></returns>
        public static TypeCode GetTypeCode(Type t)
        {
            TypeCode result = TypeCode.Empty;
            if (t.Equals(typeof(bool)))
                result = TypeCode.Boolean;
            else if (t.Equals(typeof(string)))
                result = TypeCode.String;
            else if (t.Equals(typeof(byte)))
                result = TypeCode.Byte;
            else if (t.Equals(typeof(char)))
                result = TypeCode.Char;
            else if (t.Equals(typeof(DateTime)))
                result = TypeCode.DateTime;
            else if (t.Equals(typeof(decimal)))
                result = TypeCode.Decimal;
            else if (t.Equals(typeof(double)))
                result = TypeCode.Double;
            else if (t.Equals(typeof(Int16)))
                result = TypeCode.Int16;
            else if (t.Equals(typeof(Int32)))
                result = TypeCode.Int32;
            else if (t.Equals(typeof(Int64)))
                result = TypeCode.Int64;
            else if (t.Equals(typeof(UInt16)))
                result = TypeCode.UInt16;
            else if (t.Equals(typeof(UInt32)))
                result = TypeCode.UInt32;
            else if (t.Equals(typeof(UInt64)))
                result = TypeCode.UInt64;
            else if (t.Equals(typeof(sbyte)))
                result = TypeCode.SByte;
            else if (t.Equals(typeof(Single)))
                result = TypeCode.Single;
            else if (t.Equals(typeof(UInt64)))
                result = TypeCode.UInt64;
            else if (t.Equals(typeof(object)))
                result = TypeCode.Object;
            return result;
        }

        /// <summary>
        /// Type codes
        /// </summary>
        public enum TypeCode
        {
            /// <summary>
            /// Empty
            /// </summary>
            Empty,
            /// <summary>
            /// Object
            /// </summary>
            Object,
            /// <summary>
            /// DBNull
            /// </summary>
            DBNull,
            /// <summary>
            /// Boolean
            /// </summary>
            Boolean,
            /// <summary>
            /// Char
            /// </summary>
            Char,
            /// <summary>
            /// SByte
            /// </summary>
            SByte,
            /// <summary>
            /// Byte
            /// </summary>
            Byte,
            /// <summary>
            /// Int16
            /// </summary>
            Int16,
            /// <summary>
            /// UInt16
            /// </summary>
            UInt16,
            /// <summary>
            /// Int32
            /// </summary>
            Int32,
            /// <summary>
            /// UInt32
            /// </summary>
            UInt32,
            /// <summary>
            /// Int64
            /// </summary>
            Int64,
            /// <summary>
            /// UInt64
            /// </summary>
            UInt64,
            /// <summary>
            /// Single
            /// </summary>
            Single,
            /// <summary>
            /// Double
            /// </summary>
            Double,
            /// <summary>
            /// Decimal
            /// </summary>
            Decimal,
            /// <summary>
            /// DateTime
            /// </summary>
            DateTime,
            /// <summary>
            /// String
            /// </summary>
            String
        }
    }
}
