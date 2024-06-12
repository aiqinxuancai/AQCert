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


        public static string APIKey = Environment.GetEnvironmentVariable("CFKEY");


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

        public async Task<string> GetZonesDnsRecordId(string zoneId, string recordName)
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

            var obj = responseObject["result"].FirstOrDefault(a => ((string)a["name"]).StartsWith($"{recordName}."));
            if (obj == null)
            {
                return string.Empty;
            }

            return (string)obj["id"];
        }


        public async Task<bool> AddOrUpdateTxtRecord(string domain, string recordName, string content)
        {
            //TODO 子域名有问题？
            var zoneId = await GetZoneId(domain);

            if (string.IsNullOrWhiteSpace(zoneId))
            { 
                return false;
            }

            var recordId = await GetZonesDnsRecordId(zoneId, recordName);

            var json = new JObject(
                new JProperty("type", "TXT"),
                new JProperty("name", recordName),
                new JProperty("content", content)
            );

            var jsonStr = json.ToString();

            if (string.IsNullOrEmpty(recordId))
            {
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
            }
            else
            {
                //Update 
                var response =
                    await BaseUrl
                        .AppendPathSegment($"zones/{zoneId}/dns_records/{recordId}")
                        .OnError(async a => { Debug.WriteLine(await a.Response.GetStringAsync()); })
                        .PatchStringAsync(jsonStr);

                if (response.StatusCode != 200)
                {
                    Console.WriteLine("Failed to update TXT record");
                    return false;
                }
            }


            return true;
        }
    }
}