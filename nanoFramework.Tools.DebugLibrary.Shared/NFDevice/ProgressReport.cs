//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger
{
    public class ProgressReport
    {
        public long Total { get; internal set; }

        public string Status { get; internal set; }

        public long Value { get; internal set; }


        public ProgressReport(long value, long total, string status)
        {
            Value = value;
            Total = total;
            Status = status;
        }
    }
}
