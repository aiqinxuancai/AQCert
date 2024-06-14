using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AQCert.Services
{
    internal class AppConfig
    {
        public AppConfig() { }

        public static string CloudflareKey {  get; set; }

        public static string AcmeMail { get; set; }

        public static string Domains { get; set; }
    }
}
