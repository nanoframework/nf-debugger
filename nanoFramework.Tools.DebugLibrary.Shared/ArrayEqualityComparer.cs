//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger
{
    public sealed class ArrayEqualityComparer<T> : IEqualityComparer<T[]>
    {
        public bool Equals(
            T[] x,
            T[] y)
        {
            if (x is null)
            {
                throw new ArgumentNullException(nameof(x));
            }

            if (y is null)
            {
                throw new ArgumentNullException(nameof(y));
            }

            var elementComparer = EqualityComparer<T>.Default;

            if (x == y)
            {
                return true;
            }
  
            if (x.Length != y.Length)
            {
                return false;
            }
            for (int i = 0; i < x.Length; i++)
            {
                if (!elementComparer.Equals(x[i], y[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public int GetHashCode(T[] obj)
        {
            var elementComparer = EqualityComparer<T>.Default;

            unchecked
            {
                if (obj == null)
                {
                    return 0;
                }
                int hash = 17;
                foreach (T element in obj)
                {
                    hash = hash * 31 + elementComparer.GetHashCode(element);
                }
                return hash;
            }
        }
    }
}
