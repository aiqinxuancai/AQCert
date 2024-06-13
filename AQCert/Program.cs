
using System.Runtime.ConstrainedExecution;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using AQCert.Services;
using AQCert.Models;


namespace AQCert
{
    internal class Program
    {
        private static Dictionary<string, DateTime> _certTimes = new Dictionary<string, DateTime>();

        private const string kCertTimePath = "/config/certtimes.json";

        static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }

        static async Task MainAsync(string[] args)
        {
            Console.WriteLine("AQCert");

            var cfKey = Environment.GetEnvironmentVariable("CFKEY");
            var acmeMail = Environment.GetEnvironmentVariable("ACME_MAIL");
            var domains = Environment.GetEnvironmentVariable("DOMAINS");

            if (string.IsNullOrWhiteSpace(cfKey)) 
            {
                Console.WriteLine("缺少CFKEY");
                return;
            }

            if (string.IsNullOrWhiteSpace(acmeMail))
            {
                Console.WriteLine("缺少ACME_MAIL");
                return;
            }
            if (string.IsNullOrWhiteSpace(domains))
            {
                Console.WriteLine("缺少DOMAINS");
                return;
            }

            var domainArray = domains.Split(',');


            Console.WriteLine($"为{domains}申请证书...");
            Console.WriteLine(acmeMail);

            //指定为
            var caModel = new CAModel()
            {
                Name = "CA_LETSENCRYPT_V2",
                Url = "https://acme-v02.api.letsencrypt.org/directory", //"https://acme.zerossl.com/v2/DV90",
                EabId = null,
                EabKey = null
            };

            if (!Directory.Exists("/config"))
            {
                Directory.CreateDirectory("/config");
            }
            if (!Directory.Exists("/cert"))
            {
                Directory.CreateDirectory("/cert");
            }

            if (File.Exists(kCertTimePath))
            {
                _certTimes = JsonConvert.DeserializeObject<Dictionary<string, DateTime>>(File.ReadAllText(kCertTimePath));
            }
           

            while (true)
            {
                Console.WriteLine($"开始一次检查...");

                foreach (var domain in domainArray)
                {
                    if (_certTimes.ContainsKey(domain))
                    {
                        if ((DateTime.Now - _certTimes[domain]).TotalDays < 7)
                        {
                            var lastSuccessTime = _certTimes[domain].ToString("yyyy-MM-dd HH:mm:ss");
                            Console.WriteLine($"[{domain}]上次成功时间[{lastSuccessTime}]不足7天，不重新申请");
                            continue;
                        }
                    }

                    for (int i = 0; i < 3; i++)
                    {
                        Console.WriteLine($"[{domain}]开始第{i+1}次申请");
                        try
                        {
                            var cert = await AcmeManager.Instance.Order(domain, caModel);
                            if (!string.IsNullOrEmpty(cert.pem))
                            {
                                Console.WriteLine($"[{domain}]申请证书成功");
                                _certTimes[domain] = DateTime.Now;

                                //证书写出到/cert

                                File.WriteAllText($"/cert/{domain.Replace("*.", "")}.pem", cert.pem);
                                File.WriteAllText($"/cert/{domain.Replace("*.", "")}.key", cert.privateKey);

                                SaveCertTime();
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[{domain}]申请失败");
                            Console.WriteLine(ex);
                        }
                    }
                }
                Console.WriteLine($"等待下次检查...");
                //每小时检查一次
                await Task.Delay(1000 * 60 * 60);
            }
        }

        private static void SaveCertTime()
        {
            File.WriteAllText(kCertTimePath, JsonConvert.SerializeObject(_certTimes, Formatting.None));
        }
    }
}
