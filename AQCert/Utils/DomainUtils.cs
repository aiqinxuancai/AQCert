using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DnsClient;

namespace Aliyun.AutoCdnSsl.Utils
{
    internal class DomainUtils
    {
        public static string ExtractTopLevelDomain(string domain)
        {
            var topLevelDomainPattern = @"(?<=\.)([^\.]+\.[^\.]+)$";

            var match = Regex.Match(domain, topLevelDomainPattern);

            return match.Success ? match.Value : null;
        }

        public static async Task<string> GetTxtRecords(string domain, string dnsServer)
        {
            try
            {
                var lookup = new LookupClient(IPAddress.Parse(dnsServer));
                var result = await lookup.QueryAsync(domain, QueryType.TXT);
                var record = result.Answers.TxtRecords().FirstOrDefault();
                var ip = record?.Text;
                return ip.First();
            }
            catch (Exception ex)
            {

            }
            return "";
        }
    }
}
