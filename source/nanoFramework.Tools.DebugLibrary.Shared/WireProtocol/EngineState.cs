//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.Debugger
{
    internal class EngineState
    {
        public enum Value
        {
            NotStarted,
            Starting,
            Started,
            Stopping,
            Resume,
            Stopped,
            Disposing,
            Disposed
        }

        private Value _value;
        public object SyncObject { get; private set; }

        public EngineState(object syncObject)
        {
            _value = Value.NotStarted;
            SyncObject = syncObject;
        }

        public Value GetValue()
        {
            return _value;
        }

        public bool SetValue(Value value)
        {
            return SetValue(value, false);
        }

        public bool SetValue(Value value, bool fThrow)
        {
            lock (SyncObject)
            {
                if (_value == Value.Stopping && value == Value.Resume)
                {
                    _value = Value.Started;
                    return true;
                }
                else if (_value < value)
                {
                    _value = value;
                    return true;
                }
                else
                {
                    if (fThrow)
                    {
                        throw new Exception(string.Format("Cannot set State to {0}", value));
                    }

                    return false;
                }
            }
        }

        public bool IsRunning
        {
            get
            {
                Value val = _value;

                return val == Value.Starting || val == Value.Started;
            }
        }
    }
}
