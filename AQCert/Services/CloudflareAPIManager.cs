using Newtonsoft.Json.Linq;
using Flurl;
using Flurl.Http;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

namespace AQCert.Services
{
    internal class CloudflareAPIManager
    {
        private static readonly Lazy<CloudflareAPIManager> lazy =
            new Lazy<CloudflareAPIManager>(() => new CloudflareAPIManager());

        public static CloudflareAPIManager Instance => lazy.Value;

        private const string BaseUrl = "https://api.cloudflare.com/client/v4";

        // 请确保 AppConfig.CloudflareKey 能正确获取到您的 API Token
        public static string APIKey = AppConfig.CloudflareKey;

        private CloudflareAPIManager()
        {
            FlurlHttp.Clients.WithDefaults(a =>
                a
                .WithHeader("Authorization", "Bearer " + APIKey)
                .WithHeader("Content-Type", "application/json")
            );
        }

        /// <summary>
        /// 根据域名查找最匹配的 Zone ID 和 Zone Name。
        /// 优先精确匹配，如果找不到，则查找包含该域名的最具体的 Zone。
        /// </summary>
        /// <param name="domain">需要查找的域名 (例如 "sub.example.com" 或 "example.com")</param>
        /// <returns>一个包含 Zone ID 和 Zone Name 的元组。如果找不到则返回空字符串。</returns>
        public async Task<(string ZoneId, string ZoneName)> GetZoneInfo(string domain)
        {
            try
            {
                // 1. 尝试通过 API 的 name 参数精确查找 Zone
                var exactMatchResponse = await BaseUrl
                    .AppendPathSegment("zones")
                    .SetQueryParam("name", domain)
                    .OnError(async a => { Debug.WriteLine($"Error finding exact zone: {await a.Response.GetStringAsync()}"); })
                    .GetAsync();

                if (exactMatchResponse.ResponseMessage.IsSuccessStatusCode)
                {
                    var exactMatchObject = JObject.Parse(await exactMatchResponse.GetStringAsync());
                    if (exactMatchObject.Value<bool>("success") && exactMatchObject["result"]?.Any() == true)
                    {
                        var zone = exactMatchObject["result"][0];
                        return (zone.Value<string>("id"), zone.Value<string>("name"));
                    }
                }

                // 2. 如果精确查找失败（例如传入的是子域名），则获取所有 Zone，找到最长匹配的那个
                var allZonesResponse = await BaseUrl.AppendPathSegment("zones").GetStringAsync();
                var allZonesObject = JObject.Parse(allZonesResponse);

                if (!allZonesObject.Value<bool>("success"))
                {
                    throw new Exception("Failed to list zones from Cloudflare API.");
                }

                var bestMatch = allZonesObject["result"]
                    .Where(z => domain.EndsWith(z.Value<string>("name"), StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(z => z.Value<string>("name").Length)
                    .FirstOrDefault();

                if (bestMatch != null)
                {
                    return (bestMatch.Value<string>("id"), bestMatch.Value<string>("name"));
                }

                return (string.Empty, string.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"An exception occurred in GetZoneInfo: {ex.Message}");
                return (string.Empty, string.Empty);
            }
        }

        /// <summary>
        /// 枚举并删除所有名称完全相同的 DNS 记录。
        /// </summary>
        /// <param name="domain">记录所在的域名 (例如 "example.com")。</param>
        /// <param name="recordName">要删除的记录的主机名部分 (例如 "www", "_acme-challenge", 或用 "@" 代表根域)。</param>
        /// <returns>如果删除成功或没有找到记录，则返回 true；否则返回 false。</returns>
        public async Task<bool> DeleteRecordsByName(string domain, string recordName)
        {
            var (zoneId, zoneName) = await GetZoneInfo(domain);
            if (string.IsNullOrWhiteSpace(zoneId))
            {
                Debug.WriteLine($"Could not find Zone for domain '{domain}'.");
                return false;
            }

            // 构建完整的记录名 (FQDN)
            string fqdn = (recordName == "@" || recordName.Equals(zoneName, StringComparison.OrdinalIgnoreCase))
                ? zoneName
                : $"{recordName}.{zoneName}";

            try
            {
                // 1. 使用 'name' 参数精确查找所有匹配的记录
                var listResponse = await BaseUrl
                    .AppendPathSegment($"zones/{zoneId}/dns_records")
                    .SetQueryParam("name", fqdn)
                    .OnError(async a => { Debug.WriteLine($"Error listing records: {await a.Response.GetStringAsync()}"); })
                    .GetAsync();

                if (!listResponse.ResponseMessage.IsSuccessStatusCode) return false;

                var listResponseObject = JObject.Parse(await listResponse.GetStringAsync());
                if (!listResponseObject.Value<bool>("success"))
                {
                    Debug.WriteLine($"API call to list records for '{fqdn}' failed: {listResponseObject["errors"]}");
                    return false;
                }

                var records = listResponseObject["result"]?.ToObject<JArray>();

                if (records == null || !records.Any())
                {
                    Debug.WriteLine($"No records found with name '{fqdn}'. Nothing to delete.");
                    return true;
                }

                // 2. 并发删除所有找到的记录
                Debug.WriteLine($"Found {records.Count} records with name '{fqdn}'. Deleting...");
                var deleteTasks = records.Select(record =>
                {
                    var recordId = record.Value<string>("id");
                    return DeleteRecord(zoneId, recordId);
                });

                var results = await Task.WhenAll(deleteTasks);

                bool allSucceeded = results.All(r => r);
                if (allSucceeded)
                {
                    Debug.WriteLine($"Successfully deleted {results.Length} records named '{fqdn}'.");
                }
                else
                {
                    Debug.WriteLine($"Failed to delete one or more records named '{fqdn}'.");
                }
                return allSucceeded;
            }
            catch (FlurlHttpException ex)
            {
                var error = await ex.GetResponseStringAsync();
                Debug.WriteLine($"HTTP exception while deleting records for '{fqdn}': {error}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Generic exception while deleting records for '{fqdn}': {ex.Message}");
                return false;
            }
        }


        /// <summary>
        /// 根据记录 ID 删除一条 DNS 记录。
        /// </summary>
        public async Task<bool> DeleteRecord(string zoneId, string recordId)
        {
            try
            {
                var response = await BaseUrl
                    .AppendPathSegment($"zones/{zoneId}/dns_records/{recordId}")
                    .OnError(async a => { Debug.WriteLine($"Failed to delete record {recordId}: {await a.Response.GetStringAsync()}"); })
                    .DeleteAsync();

                // 检查响应是否成功
                if (response.ResponseMessage.IsSuccessStatusCode)
                {
                    var responseObject = JObject.Parse(await response.GetStringAsync());
                    return responseObject.Value<bool>("success");
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception when deleting record {recordId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 添加或更新 TXT 记录。此方法会先删除所有同名的现有 TXT 记录，然后添加新的。
        /// </summary>
        public async Task<bool> AddOrUpdateTxtRecord(string domain, string recordName, string content)
        {
            var (zoneId, zoneName) = await GetZoneInfo(domain);
            if (string.IsNullOrWhiteSpace(zoneId))
            {
                Debug.WriteLine($"Could not find Zone for domain '{domain}' when updating TXT record.");
                return false;
            }

            // 先删除所有同名记录
            await DeleteRecordsByName(domain, recordName);

            var recordNameToCreate = (recordName == "@" || recordName.Equals(zoneName, StringComparison.OrdinalIgnoreCase))
                                    ? zoneName
                                    : recordName;

            var json = new JObject(
                new JProperty("type", "TXT"),
                new JProperty("name", recordNameToCreate), // API 创建时使用主机名或 FQDN
                new JProperty("content", content),
                new JProperty("ttl", 60)
            );

            // 添加新记录
            var response = await BaseUrl
                .AppendPathSegment($"zones/{zoneId}/dns_records")
                .OnError(async a => { Debug.WriteLine($"Error creating TXT record: {await a.Response.GetStringAsync()}"); })
                .PostStringAsync(json.ToString());

            if (!response.ResponseMessage.IsSuccessStatusCode)
            {
                Debug.WriteLine($"Failed to add TXT record for '{recordName}.{zoneName}'.");
                return false;
            }

            var resultObject = JObject.Parse(await response.GetStringAsync());
            return resultObject.Value<bool>("success");
        }

        /// <summary>
        /// 添加或更新A记录。
        /// </summary>
        public async Task<bool> AddOrUpdateARecord(string fullDomain, string ipAddress)
        {
            var (zoneId, zoneName) = await GetZoneInfo(fullDomain);
            if (string.IsNullOrWhiteSpace(zoneId))
            {
                Debug.WriteLine($"Could not find Zone for domain '{fullDomain}' when updating A record.");
                return false;
            }

            // 从完整域名中提取记录名 (主机名)
            string recordName;
            if (fullDomain.Equals(zoneName, StringComparison.OrdinalIgnoreCase))
            {
                recordName = "@";
            }
            else
            {
                recordName = fullDomain.Substring(0, fullDomain.Length - zoneName.Length - 1);
            }

            // 先删除所有同名记录
            await DeleteRecordsByName(fullDomain, recordName);

            var json = new JObject(
                new JProperty("type", "A"),
                new JProperty("name", recordName), // API 使用主机名或 @
                new JProperty("content", ipAddress),
                new JProperty("ttl", 1) // 1 表示自动
            );

            // 添加新记录
            var response = await BaseUrl
                .AppendPathSegment($"zones/{zoneId}/dns_records")
                .OnError(async a => { Debug.WriteLine($"Error creating A record: {await a.Response.GetStringAsync()}"); })
                .PostStringAsync(json.ToString());

            if (!response.ResponseMessage.IsSuccessStatusCode)
            {
                Debug.WriteLine($"Failed to add A record for '{fullDomain}'.");
                return false;
            }

            var resultObject = JObject.Parse(await response.GetStringAsync());
            return resultObject.Value<bool>("success");
        }
    }
}