using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
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
        private sealed record DohEndpoint(string Name, string Url);

        private sealed record DohQueryResult(IReadOnlyList<string> Records, string? Error = null);

        private sealed record DnsQueryAttempt(
            string Endpoint,
            IReadOnlyList<string> Records,
            string? Error = null);

        private sealed record TxtValidationResult(
            bool IsMatch,
            string? MatchedEndpoint,
            IReadOnlyList<DnsQueryAttempt> Attempts);

        private static readonly DohEndpoint[] DohEndpoints =
        {
            new("Cloudflare DoH", "https://cloudflare-dns.com/dns-query"),
            new("AliDNS DoH", "https://dns.alidns.com/resolve"),
            new("DNSPod DoH", "https://doh.pub/resolve"),
            new("Google DoH", "https://dns.google/resolve")
        };

        private static readonly HttpClient httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

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
            var endpoint = new DohEndpoint(GetDohEndpointName(dohServer), dohServer);
            var result = await QueryViaDoH(domain, "TXT", 16, endpoint, CancellationToken.None);

            if (!string.IsNullOrEmpty(result.Error))
            {
                Console.WriteLine($"{endpoint.Name}查询DNS TXT记录失败: {domain} - {result.Error}");
                return new List<string>();
            }

            foreach (var txtValue in result.Records)
            {
                Console.WriteLine($"{endpoint.Name}查询记录：{txtValue}");
            }

            return result.Records.ToList();
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
                    var txt = string.Concat(record.Text);
                    if (!string.IsNullOrEmpty(txt))
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
                var txts = result.Answers.TxtRecords()
                    .Select(record => string.Concat(record.Text))
                    .Where(txt => !string.IsNullOrEmpty(txt))
                    .ToList();
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
            if (method == DnsQueryMethod.DoH)
            {
                var preferredEndpoint = new DohEndpoint(GetDohEndpointName(dnsServer), dnsServer);
                var endpoints = new[] { preferredEndpoint }
                    .Concat(DohEndpoints.Where(endpoint =>
                        !endpoint.Url.Equals(dnsServer, StringComparison.OrdinalIgnoreCase)));
                var dohResult = await ValidateTxtRecordsViaDoH(
                    domain,
                    authTxt,
                    retryWithFallback ? endpoints : new[] { preferredEndpoint },
                    CancellationToken.None);
                return dohResult.IsMatch;
            }

            var list = await GetTxtRecordsByMethod(domain, dnsServer, method);

            if (ContainsExpectedRecord(list, authTxt))
            {
                Console.WriteLine($"✓ 验证成功：找到匹配的TXT记录 (方式: {method})");
                return true;
            }

            if (!retryWithFallback)
            {
                Console.WriteLine("✗ 验证失败：未找到匹配的TXT记录");
                return false;
            }

            var failureReason = list.Count == 0 ? "未返回TXT记录" : "返回的TXT记录与期望值不匹配";
            Console.WriteLine($"使用{method}方式验证失败（{failureReason}），尝试其他查询方式...");

            if (method == DnsQueryMethod.UDP)
            {
                Console.WriteLine("尝试TCP查询...");
                list = await GetTxtRecordsByMethod(domain, dnsServer, DnsQueryMethod.TCP);
                if (ContainsExpectedRecord(list, authTxt))
                {
                    Console.WriteLine("✓ 验证成功：找到匹配的TXT记录 (回退方式: TCP)");
                    return true;
                }
            }

            Console.WriteLine("尝试多个DoH端点查询...");
            var fallbackResult = await ValidateTxtRecordsViaDoH(
                domain,
                authTxt,
                DohEndpoints,
                CancellationToken.None);

            return fallbackResult.IsMatch;
        }

        /// <summary>
        /// 等待TXT记录传播，超过指定时间后抛出包含查询详情的超时异常。
        /// </summary>
        public static async Task WaitForTxtRecordAsync(
            string domain,
            string authTxt,
            TimeSpan timeout,
            TimeSpan retryInterval)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(domain);
            ArgumentException.ThrowIfNullOrWhiteSpace(authTxt);
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }
            if (retryInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(retryInterval));
            }

            using var timeoutCancellation = new CancellationTokenSource(timeout);
            var stopwatch = Stopwatch.StartNew();
            var authoritativeNameServers = new List<string>();
            var authoritativeLookupEndpoint = "未检测到";
            TxtValidationResult? lastResult = null;
            var observedRecords = new HashSet<string>(StringComparer.Ordinal);
            var latestAttempts = new Dictionary<string, DnsQueryAttempt>(StringComparer.Ordinal);

            try
            {
                using var authoritativeLookupCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(timeoutCancellation.Token);
                authoritativeLookupCancellation.CancelAfter(TimeSpan.FromSeconds(20));

                try
                {
                    (authoritativeNameServers, authoritativeLookupEndpoint) =
                        await GetAuthoritativeNameServers(domain, authoritativeLookupCancellation.Token);
                }
                catch (OperationCanceledException) when (!timeoutCancellation.IsCancellationRequested)
                {
                    Console.WriteLine("权威NS探测超过20秒，继续进行TXT验证");
                }

                var nsText = authoritativeNameServers.Count == 0
                    ? "未检测到"
                    : string.Join(", ", authoritativeNameServers);
                Console.WriteLine($"检测到的权威NS（查询端点: {authoritativeLookupEndpoint}）：{nsText}");

                while (!timeoutCancellation.IsCancellationRequested)
                {
                    Console.WriteLine(
                        $"正在通过多个DoH端点验证DNS TXT记录（已等待 {stopwatch.Elapsed:mm\\:ss}/{timeout:mm\\:ss}）...");

                    lastResult = await ValidateTxtRecordsViaDoH(
                        domain,
                        authTxt,
                        DohEndpoints,
                        timeoutCancellation.Token);

                    foreach (var attempt in lastResult.Attempts)
                    {
                        foreach (var record in attempt.Records)
                        {
                            observedRecords.Add(record);
                        }

                        if (attempt.Error != "达到总超时时间" || !latestAttempts.ContainsKey(attempt.Endpoint))
                        {
                            latestAttempts[attempt.Endpoint] = attempt;
                        }
                    }

                    if (lastResult.IsMatch)
                    {
                        Console.WriteLine($"本地DNS验证成功，匹配端点：{lastResult.MatchedEndpoint}");
                        return;
                    }

                    var remaining = timeout - stopwatch.Elapsed;
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    var delay = remaining < retryInterval ? remaining : retryInterval;
                    Console.WriteLine($"本轮未找到匹配记录，{delay.TotalSeconds:0}秒后重试...");
                    await Task.Delay(delay, timeoutCancellation.Token);
                }
            }
            catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
            {
                // 统一在下面生成包含DNS诊断信息的超时异常。
            }

            var actualText = observedRecords.Count == 0 ? "<无>" : string.Join(", ", observedRecords);
            var nsDetails = authoritativeNameServers.Count == 0
                ? "<未检测到>"
                : string.Join(", ", authoritativeNameServers);
            var endpointDetails = FormatAttemptDetails(latestAttempts.Values.ToArray());

            throw new TimeoutException(
                $"等待DNS TXT记录传播超时（{timeout.TotalMinutes:0.##}分钟）。" +
                $"域名: {domain}; 期望值: {authTxt}; 实际值: {actualText}; " +
                $"权威NS: {nsDetails}（检测端点: {authoritativeLookupEndpoint}）; " +
                $"DoH端点明细: {endpointDetails}");
        }

        private static async Task<TxtValidationResult> ValidateTxtRecordsViaDoH(
            string domain,
            string authTxt,
            IEnumerable<DohEndpoint> endpoints,
            CancellationToken cancellationToken)
        {
            var attempts = new List<DnsQueryAttempt>();

            foreach (var endpoint in endpoints)
            {
                DohQueryResult result;
                try
                {
                    result = await QueryViaDoH(domain, "TXT", 16, endpoint, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    attempts.Add(new DnsQueryAttempt(
                        endpoint.Name,
                        Array.Empty<string>(),
                        "达到总超时时间"));
                    break;
                }
                attempts.Add(new DnsQueryAttempt(endpoint.Name, result.Records, result.Error));

                if (!string.IsNullOrEmpty(result.Error))
                {
                    Console.WriteLine($"{endpoint.Name}查询失败：{result.Error}");
                    continue;
                }

                if (result.Records.Count == 0)
                {
                    Console.WriteLine($"{endpoint.Name}未返回TXT记录");
                    continue;
                }

                Console.WriteLine($"{endpoint.Name}查询记录：{string.Join(", ", result.Records)}");
                if (ContainsExpectedRecord(result.Records, authTxt))
                {
                    Console.WriteLine($"✓ 验证成功：找到匹配的TXT记录 (DoH端点: {endpoint.Name})");
                    return new TxtValidationResult(true, endpoint.Name, attempts);
                }

                Console.WriteLine($"{endpoint.Name}返回的TXT记录与期望值不匹配，继续尝试其他端点...");
            }

            Console.WriteLine("✗ 验证失败：所有DoH端点均未找到匹配的TXT记录");
            return new TxtValidationResult(false, null, attempts);
        }

        private static async Task<(List<string> NameServers, string Endpoint)>
            GetAuthoritativeNameServers(string domain, CancellationToken cancellationToken)
        {
            var candidates = GetDnsHierarchy(domain).ToArray();

            foreach (var endpoint in DohEndpoints)
            {
                foreach (var candidate in candidates)
                {
                    var result = await QueryViaDoH(candidate, "NS", 2, endpoint, cancellationToken);
                    if (result.Records.Count > 0)
                    {
                        return (result.Records.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), endpoint.Name);
                    }
                }
            }

            return (new List<string>(), "未检测到");
        }

        private static IEnumerable<string> GetDnsHierarchy(string domain)
        {
            var labels = domain.Trim('.').Split('.', StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < labels.Length - 1; index++)
            {
                yield return string.Join('.', labels.Skip(index));
            }
        }

        private static async Task<DohQueryResult> QueryViaDoH(
            string domain,
            string recordType,
            int expectedAnswerType,
            DohEndpoint endpoint,
            CancellationToken cancellationToken)
        {
            try
            {
                var separator = endpoint.Url.Contains('?') ? "&" : "?";
                var url = $"{endpoint.Url}{separator}name={Uri.EscapeDataString(domain)}&type={recordType}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.ParseAdd("application/dns-json");
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();

                await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var jsonDoc = await JsonDocument.ParseAsync(
                    responseStream,
                    cancellationToken: cancellationToken);

                if (jsonDoc.RootElement.TryGetProperty("Status", out var statusElement) &&
                    statusElement.GetInt32() != 0)
                {
                    return new DohQueryResult(Array.Empty<string>(), $"DNS响应状态码 {statusElement.GetInt32()}");
                }

                var records = new List<string>();
                if (jsonDoc.RootElement.TryGetProperty("Answer", out var answers))
                {
                    foreach (var answer in answers.EnumerateArray())
                    {
                        if (!answer.TryGetProperty("type", out var typeElement) ||
                            typeElement.GetInt32() != expectedAnswerType ||
                            !answer.TryGetProperty("data", out var dataElement))
                        {
                            continue;
                        }

                        var value = dataElement.GetString();
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            continue;
                        }

                        records.Add(recordType == "TXT"
                            ? NormalizeTxtRecord(value)
                            : value.Trim().TrimEnd('.'));
                    }
                }

                return new DohQueryResult(records);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new DohQueryResult(Array.Empty<string>(), "请求超时");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new DohQueryResult(Array.Empty<string>(), ex.Message);
            }
        }

        private static bool ContainsExpectedRecord(IEnumerable<string> records, string authTxt)
        {
            return records.Any(txt => string.Equals(txt, authTxt, StringComparison.Ordinal));
        }

        private static string NormalizeTxtRecord(string value)
        {
            return value.Trim().Trim('"').Replace("\" \"", string.Empty, StringComparison.Ordinal);
        }

        private static string GetDohEndpointName(string url)
        {
            return DohEndpoints.FirstOrDefault(endpoint =>
                endpoint.Url.Equals(url, StringComparison.OrdinalIgnoreCase))?.Name ?? "自定义DoH";
        }

        private static string FormatAttemptDetails(IReadOnlyList<DnsQueryAttempt>? attempts)
        {
            if (attempts == null || attempts.Count == 0)
            {
                return "<无查询结果>";
            }

            return string.Join("; ", attempts.Select(attempt =>
            {
                if (!string.IsNullOrEmpty(attempt.Error))
                {
                    return $"{attempt.Endpoint}=失败({attempt.Error})";
                }

                var records = attempt.Records.Count == 0
                    ? "<无记录>"
                    : string.Join(", ", attempt.Records);
                return $"{attempt.Endpoint}={records}";
            }));
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
