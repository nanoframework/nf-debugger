//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

//using System.Runtime.Remoting.Messaging;

namespace nanoFramework.Tools.Debugger
{
    internal class MessageCall
    {
        public readonly string Name;
        public readonly object[] Args;

        public MessageCall(string name, object[] args)
        {
            Name = name;
            Args = args;
        }

        //public static MessageCall CreateFromIMethodMessage(IMethodMessage message)
        //{
        //    return new MessageCall(message.MethodName, message.Args);
        //}

        public object CreateMessagePayload()
        {
            return new object[] { Name, Args };
        }

        public static MessageCall CreateFromMessagePayload(object payload)
        {
            object[] data = (object[])payload;
            string name = (string)data[0];
            object[] args = (object[])data[1];

            return new MessageCall(name, args);
        }
    }
}
