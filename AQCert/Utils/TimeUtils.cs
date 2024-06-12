using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Aliyun.Counter.Utils
{
    internal class TimeUtils
    {
        internal static DateTimeOffset GetBeijingTimeNow()
        {
            TimeZoneInfo cst;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                cst = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                     RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                cst = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
            }
            else
            {
                throw new NotSupportedException("Unknown platform");
            }

            DateTimeOffset dateTimeNow = DateTimeOffset.UtcNow;
            return TimeZoneInfo.ConvertTime(dateTimeNow, cst);
        }
    }
}
