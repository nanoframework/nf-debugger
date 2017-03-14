//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Threading;

namespace NanoFramework.Tools.Debugger.Extensions
{
    static class CancellationTokenExtensions
    {
        /// <summary>
        /// Links cancellation token source with a set timeout cancellation token.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <param name="timeout">Timeout to link with cancelation token.</param>
        /// <returns>A linked cancellation token.</returns>
        public static CancellationToken AddTimeout(this CancellationToken cancellationToken, TimeSpan timeout)
        {
            var timeoutCancellatioToken = new CancellationTokenSource(timeout).Token;
            return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellatioToken).Token;
        }
    }
}
