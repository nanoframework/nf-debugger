//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    internal class FifoBuffer
    {
        byte[] m_buffer;
        int m_offset;
        int m_count;
        ManualResetEvent m_ready;

        // use a timer to trigger the event m_ready only after a specified value is elapsed
        // this prevents showing partial messages which would happen if the event handler was called on every spurious char received
        Timer timerToTriggerEvent;

        // time (in ms) to wait before actually setting the ready event
        const int triggerDelay = 500;

        public FifoBuffer()
        {
            m_buffer = new byte[1024];
            m_offset = 0;
            m_count = 0;
            m_ready = new ManualResetEvent(false);
            timerToTriggerEvent = new Timer((state) => 
                {
                    m_ready.Set();
                }, 
                null, Timeout.Infinite, Timeout.Infinite);
        }

        public WaitHandle WaitHandle
        {
            get { return m_ready; }
        }

        public int Read(byte[] buf, int offset, int count)
        {
            int countRequested = count;

            int len = m_buffer.Length;

            while (m_count > 0 && count > 0)
            {
                int avail = m_count; if (avail + m_offset > len) avail = len - m_offset;

                if (avail > count) avail = count;

                Array.Copy(m_buffer, m_offset, buf, offset, avail);

                m_offset += avail; if (m_offset == len) m_offset = 0;
                offset += avail;

                m_count -= avail;
                count -= avail;
            }

            if (m_count == 0)
            {
                //
                // No pending data, resync to the beginning of the buffer.
                //
                m_offset = 0;

                m_ready.Reset();
            }

            return countRequested - count;
        }

        public void Write(byte[] buf, int offset, int count)
        {
            // stop timer is running?
            timerToTriggerEvent.Change(Timeout.Infinite, Timeout.Infinite);

            while (count > 0)
            {
                int len = m_buffer.Length;
                int avail = len - m_count;

                if (avail == 0) // Buffer full. Expand it.
                {
                    byte[] buffer = new byte[len * 2];

                    //
                    // Double the buffer and copy all the data to the left side.
                    //
                    Array.Copy(m_buffer, m_offset, buffer, 0, len - m_offset);
                    Array.Copy(m_buffer, 0, buffer, len - m_offset, m_offset);

                    m_buffer = buffer;
                    m_offset = 0;
                    len *= 2;
                    avail = len;
                }

                int offsetWrite = m_offset + m_count; if (offsetWrite >= len) offsetWrite -= len;

                if (avail + offsetWrite > len) avail = len - offsetWrite;

                if (avail > count) avail = count;

                Array.Copy(buf, offset, m_buffer, offsetWrite, avail);

                offset += avail;
                m_count += avail;
                count -= avail;
            }

            // start timer to set reset event
            timerToTriggerEvent.Change(500, Timeout.Infinite);
        }

        public int Available
        {
            get
            {
                return m_count;
            }
        }
    }
}
