//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    internal class RebootTime
    {
        public const int c_RECONNECT_RETRIES_DEFAULT = 5;
        public const int c_RECONNECT_HARD_TIMEOUT_DEFAULT_MS = 1000;         // one second
        public const int c_RECONNECT_SOFT_TIMEOUT_DEFAULT_MS = 1000;         // one second

        public const int c_MIN_RECONNECT_RETRIES = 1;
        public const int c_MAX_RECONNECT_RETRIES = 1000;
        public const int c_MIN_TIMEOUT_MS = 1 * 50;      // fifty milliseconds
        public const int c_MAX_TIMEOUT_MS = 60 * 1000;   // sixty seconds

        int m_retriesCount;
        int m_waitHardMs;
        int m_waitSoftMs;

        public RebootTime()
        {
            m_waitSoftMs = c_RECONNECT_SOFT_TIMEOUT_DEFAULT_MS;
            m_waitHardMs = c_RECONNECT_HARD_TIMEOUT_DEFAULT_MS;

            bool fOverride = false;
            string timingKey = @"\NonVersionSpecific\Timing\AnyDevice";

            //////////////////////////////////////////////////
            //////////////////////////////////////////////////
            // WinRT and UWP apps can't access the registry //
            //////////////////////////////////////////////////
            //////////////////////////////////////////////////


            //RegistryAccess.GetBoolValue(timingKey, "override", out fOverride, false);

            //if (RegistryAccess.GetIntValue(timingKey, "retries", out m_retriesCount, c_RECONNECT_RETRIES_DEFAULT))
            //{
            //    if (!fOverride)
            //    {
            //        if (m_retriesCount < c_MIN_RECONNECT_RETRIES)
            //            m_retriesCount = c_MIN_RECONNECT_RETRIES;

            //        if (m_retriesCount > c_MAX_RECONNECT_RETRIES)
            //            m_retriesCount = c_MAX_RECONNECT_RETRIES;
            //    }
            //}

            //if (RegistryAccess.GetIntValue(timingKey, "timeout", out m_waitHardMs, c_RECONNECT_HARD_TIMEOUT_DEFAULT_MS))
            //{
            //    if (!fOverride)
            //    {
            //        if (m_waitHardMs < c_MIN_TIMEOUT_MS)
            //            m_waitHardMs = c_MIN_TIMEOUT_MS;

            //        if (m_waitHardMs > c_MAX_TIMEOUT_MS)
            //            m_waitHardMs = c_MAX_TIMEOUT_MS;
            //    }
            //    m_waitSoftMs = m_waitHardMs;
            //}
        }

        public int Retries
        {
            get
            {
                return m_retriesCount;
            }
        }

        public int WaitMs(bool fSoftReboot)
        {
            return (fSoftReboot ? m_waitSoftMs : m_waitHardMs);
        }

    }
}
