using Newtonsoft.Json.Linq;
using Flurl;
using Flurl.Http;
using System.Diagnostics;
using Aliyun.AutoCdnSsl.Utils;

namespace AQCert.Services
{
    internal class CloudflareAPIManager
    {
        private static readonly Lazy<CloudflareAPIManager> lazy =
            new Lazy<CloudflareAPIManager>(() => new CloudflareAPIManager());

        public static CloudflareAPIManager Instance => lazy.Value;

        private const string BaseUrl = "https://api.cloudflare.com/client/v4";


        public static string APIKey = AppConfig.CloudflareKey;


        private CloudflareAPIManager()
        {
            FlurlHttp.Clients.WithDefaults(a =>
                a
                .WithHeader("Authorization", "Bearer " + APIKey)
                //.WithHeader("X-Auth-Email", APIEmail)
                .WithHeader("Content-Type", "application/json")
            );
        }

        public async Task<string> GetZoneId(string domain)
        {
            var responseContent =
                await BaseUrl
                .AppendPathSegment("zones")
                //.AppendQueryParam("name", domain)
                .GetStringAsync();

            var responseObject = JObject.Parse(responseContent);

            if (!bool.Parse(responseObject["success"].ToString()))
            {
                throw new Exception("Failed to get zone ID");
            }


            foreach (var item in responseObject["result"])
            {
                //TODO 需要更严格的匹配
                if (domain.EndsWith((string)item["name"]))
                {
                    return (string)item["id"];
                }
            }


            return string.Empty;
        }

        public async Task<List<string>> GetZonesDnsRecordId(string zoneId, string recordName)
        {
            var responseContent =
                await BaseUrl
                .AppendPathSegment("zones")
                .AppendPathSegment(zoneId)
                .AppendPathSegment("dns_records")
                .GetStringAsync();

            var responseObject = JObject.Parse(responseContent);

            if (!bool.Parse(responseObject["success"].ToString()))
            {
                throw new Exception("Failed to get zone ID");
            }

            
            var results = new List<string>();
            foreach (var item in responseObject["result"])
            {
                string zoneName = (string)item["zone_name"];

                if (((string)item["name"]).Replace($".{zoneName}", "") == recordName)
                {
                    results.Add((string)item["id"]);
                }
            }

            return results;
        }

        public async Task<bool> DeleteRecord(string zoneId, string recordId)
        {

            var response = await BaseUrl
                        .AppendPathSegment($"zones/{zoneId}/dns_records/{recordId}")
                        .OnError(async a => { Debug.WriteLine(await a.Response.GetStringAsync()); })
                        .DeleteAsync();

            var result = await response.GetStringAsync();

            return true;
        }



        public async Task<bool> AddOrUpdateTxtRecord(string domain, string recordName, string content)
        {
            //TODO 子域名有问题？
            var zoneId = await GetZoneId(domain);

            if (string.IsNullOrWhiteSpace(zoneId))
            { 
                return false;
            }

            var recordIds = await GetZonesDnsRecordId(zoneId, recordName);

            var json = new JObject(
                new JProperty("type", "TXT"),
                new JProperty("name", recordName),
                new JProperty("content", content),
                new JProperty("ttl", 60)
            );

            var jsonStr = json.ToString();

            foreach (var item in recordIds)
            {
                await DeleteRecord(zoneId, item);
            }


            //Add
            var response = await BaseUrl
                                .AppendPathSegment($"zones/{zoneId}/dns_records")
                                .OnError(async a => { Debug.WriteLine(await a.Response.GetStringAsync()); })
                                .PostStringAsync(jsonStr);

            var result = await response.GetStringAsync();


            if (response.StatusCode != 200)
            {
                Console.WriteLine("Failed to add TXT record");
                return false;
            }


            return true;
        }
    }
}