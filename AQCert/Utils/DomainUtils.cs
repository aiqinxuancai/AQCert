using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;

namespace Aliyun.AutoCdnSsl.Utils
{
    public enum DnsQueryMethod
    {
        UDP,        // 默认UDP查询
        TCP,        // TCP查询
        DoH         // DNS-over-HTTPS
    }

    internal class DomainUtils
    {
        private static readonly HttpClient httpClient = new HttpClient();

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

        /// <summary>
        /// 通过DNS-over-HTTPS查询TXT记录
        /// </summary>
        public static async Task<List<string>> GetTxtRecordsViaDoH(string domain, string dohServer = "https://dns.google/resolve")
        {
            try
            {
                var url = $"{dohServer}?name={domain}&type=TXT";
                var response = await httpClient.GetStringAsync(url);
                var jsonDoc = JsonDocument.Parse(response);
                var txts = new List<string>();

                if (jsonDoc.RootElement.TryGetProperty("Answer", out var answers))
                {
                    foreach (var answer in answers.EnumerateArray())
                    {
                        if (answer.TryGetProperty("data", out var data))
                        {
                            var txtValue = data.GetString()?.Trim('"');
                            if (!string.IsNullOrEmpty(txtValue))
                            {
                                txts.Add(txtValue);
                                Console.WriteLine($"DoH查询记录：{txtValue}");
                            }
                        }
                    }
                }

                return txts;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DoH查询DNS TXT记录失败: {domain} - {ex.Message}");
            }
            return new List<string>();
        }

        /// <summary>
        /// 通过TCP查询TXT记录
        /// </summary>
        public static async Task<List<string>> GetTxtRecordsViaTcp(string domain, string dnsServer)
        {
            try
            {
                var lookup = new LookupClient(new LookupClientOptions(IPAddress.Parse(dnsServer))
                {
                    UseTcpOnly = true,
                    UseCache = false
                });
                var result = await lookup.QueryAsync(domain, QueryType.TXT);
                var txts = new List<string>();

                foreach (var record in result.Answers.TxtRecords())
                {
                    foreach (var txt in record.Text)
                    {
                        txts.Add(txt);
                        Console.WriteLine($"TCP查询记录：{txt}");
                    }
                }

                return txts;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TCP查询DNS TXT记录失败: {domain} - {ex.Message}");
            }
            return new List<string>();
        }

        /// <summary>
        /// 通过UDP查询TXT记录（原有方法）
        /// </summary>
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
                    Console.WriteLine($"UDP查询记录：{txt}");
                }
                return txts;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"查询DNS TXT记录失败: {domain} - {ex.Message}");
            }
            return new List<string>();
        }


        /// <summary>
        /// 验证TXT记录，支持多种查询方式
        /// </summary>
        /// <param name="domain">域名</param>
        /// <param name="dnsServer">DNS服务器地址（UDP/TCP模式）或DoH服务器URL（DoH模式）</param>
        /// <param name="authTxt">期望的TXT记录值</param>
        /// <param name="method">查询方式：UDP、TCP或DoH</param>
        /// <param name="retryWithFallback">是否在失败时尝试其他查询方式</param>
        /// <returns></returns>
        public static async Task<bool> AuthTxtRecords(
            string domain,
            string dnsServer,
            string authTxt,
            DnsQueryMethod method = DnsQueryMethod.UDP,
            bool retryWithFallback = true)
        {
            var list = await GetTxtRecordsByMethod(domain, dnsServer, method);

            // 检查是否找到匹配的记录
            foreach (var txt in list)
            {
                if (txt == authTxt)
                {
                    Console.WriteLine($"✓ 验证成功：找到匹配的TXT记录 (方式: {method})");
                    return true;
                }
            }

            // 如果启用了回退机制且当前方式失败，尝试其他方式
            if (retryWithFallback && list.Count == 0)
            {
                Console.WriteLine($"使用{method}方式查询失败，尝试其他查询方式...");

                if (method != DnsQueryMethod.TCP)
                {
                    Console.WriteLine("尝试TCP查询...");
                    list = await GetTxtRecordsByMethod(domain, dnsServer, DnsQueryMethod.TCP);
                    foreach (var txt in list)
                    {
                        if (txt == authTxt)
                        {
                            Console.WriteLine($"✓ 验证成功：找到匹配的TXT记录 (回退方式: TCP)");
                            return true;
                        }
                    }
                }

                if (method != DnsQueryMethod.DoH)
                {
                    Console.WriteLine("尝试DoH查询...");
                    list = await GetTxtRecordsByMethod(domain, "https://dns.google/resolve", DnsQueryMethod.DoH);
                    foreach (var txt in list)
                    {
                        if (txt == authTxt)
                        {
                            Console.WriteLine($"✓ 验证成功：找到匹配的TXT记录 (回退方式: DoH)");
                            return true;
                        }
                    }
                }
            }

            Console.WriteLine($"✗ 验证失败：未找到匹配的TXT记录");
            return false;
        }

        /// <summary>
        /// 根据指定的方法获取TXT记录
        /// </summary>
        private static async Task<List<string>> GetTxtRecordsByMethod(string domain, string server, DnsQueryMethod method)
        {
            return method switch
            {
                DnsQueryMethod.TCP => await GetTxtRecordsViaTcp(domain, server),
                DnsQueryMethod.DoH => await GetTxtRecordsViaDoH(domain, server),
                _ => await GetTxtRecords(domain, server) // 默认UDP
            };
        }

        /// <summary>
        /// 保持原有方法的向后兼容（默认使用UDP）
        /// </summary>
        public static async Task<bool> AuthTxtRecords(string domain, string dnsServer, string authTxt)
        {
            return await AuthTxtRecords(domain, dnsServer, authTxt, DnsQueryMethod.UDP, true);
        }
    }
}
