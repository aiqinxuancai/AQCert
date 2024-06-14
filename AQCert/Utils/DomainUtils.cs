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

            // 如果匹配成功，返回匹配结果
            if (match.Success)
            {
                return match.Value;
            }

            // 否则判断是否已经是主域名
            var mainDomainPattern = @"^[^\.]+\.[^\.]+$";
            var mainDomainMatch = Regex.Match(domain, mainDomainPattern);

            return mainDomainMatch.Success ? domain : null;
        }

        public static async Task<List<string>> GetTxtRecords(string domain, string dnsServer)
        {
            try
            {
                var lookup = new LookupClient(IPAddress.Parse(dnsServer));
                var result = await lookup.QueryAsync(domain, QueryType.TXT);
                var record = result.Answers.TxtRecords().FirstOrDefault();
                var txts = new List<string>(record?.Text);
                foreach (var txt in txts)
                {
                    Console.WriteLine($"本地记录：{txt}");
                }
                return txts;
            }
            catch (Exception ex)
            {

            }
            return new List<string>();
        }

        public static async Task<bool> AuthTxtRecords(string domain, string dnsServer, string authTxt)
        {
            var list = await GetTxtRecords(domain, dnsServer);
            foreach (var txt in list)
            {
                if (txt == authTxt)
                {
                    return true;
                }

            }

            return false;
        }
    }
}
