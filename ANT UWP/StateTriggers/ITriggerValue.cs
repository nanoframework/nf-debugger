//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using System;
using System.Collections.Generic;
using System.Linq;

namespace NanoFramework.ANT.StateTriggers
{
    public interface ITriggerValue
    {
        /// <summary>  Gets a value indicating whether this trigger is active. </summary> 
 		/// <value><c>true</c> if this trigger is active; otherwise, <c>false</c>.</value> 
        bool IsActive { get; }

        /// <summary>  Occurs when the <see cref="IsActive"/> property has changed.  </summary> 
 		event EventHandler IsActiveChanged;
    }
}
