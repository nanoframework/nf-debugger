//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System.Collections.Generic;

namespace nanoFramework.Tools.Debugger.Extensions
{
    public static class DeviceConfigurationExtensions
    {
        public static bool ValidateIndex<T>(this List<T> collection, uint blockIndex)
        {
            // if collection has items, check if requested index is in range
            if (collection.Count > 0)
            {
                return (blockIndex < collection.Count);
            }
            else if (collection.Count == 0)
            {
                // if collection is empty and requested index is 0
                return (blockIndex == 0);
            }
            else
            {
                return false;
            }
        }
    }
}
