using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Aliyun.AutoCdnSsl.Utils
{
    public class MD5Utils
    {
        public static string GetMD5(string str)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] result = Encoding.Default.GetBytes(str);
                byte[] output = md5.ComputeHash(result);
                return BitConverter.ToString(output).Replace("-", "");
            }
        }

        public static string GetMD5(byte[] bytes)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] output = md5.ComputeHash(bytes);
                return BitConverter.ToString(output).Replace("-", "");
            }
        }

        public static string GetMD5(Stream stream)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] output = md5.ComputeHash(stream);
                return BitConverter.ToString(output).Replace("-", "");
            }
        }

        private static byte[] StreamToBytes(Stream stream)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                stream.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }
        }

    }
}
