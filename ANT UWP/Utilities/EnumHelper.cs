//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace NanoFramework.ANT
{
    public abstract class EnumHelper
    {
        public static List<T> ListOf<T>(List<T> exclude = null)
        {
            TypeInfo t = typeof(T).GetTypeInfo();
            if (t.IsEnum)
            {
                List<T> lst = Enum.GetValues(typeof(T)).Cast<T>().ToList();
                if (exclude != null)
                {
                    exclude.ForEach(e => lst.Remove(e));
                }
                return lst;
            }
            return null;
        }

    }
}
