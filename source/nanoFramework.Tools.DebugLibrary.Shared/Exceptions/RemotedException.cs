//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.Debugger
{
    public class RemotedException
    {
        public string m_message;
        public RemotedException(Exception payload)
        {
            m_message = payload.Message;
        }

        public void Raise()
        {
            throw new Exception("Remote exception" + m_message);
            //throw new System.Runtime.Remoting.RemotingException(m_message);
        }
    }
}
