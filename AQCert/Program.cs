
using System.Runtime.ConstrainedExecution;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using AQCert.Services;
using AQCert.Models;
using static Org.BouncyCastle.Math.EC.ECCurve;


namespace AQCert
{
    internal class Program
    {
        private static Dictionary<string, DateTime> _certTimes = new Dictionary<string, DateTime>();

        private static string kCertTimeFile = Path.Combine(Directory.GetCurrentDirectory(), "config", "certtimes.json");

        private static string kConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "config");

#if DEBUG
        private static string kCertPath = Path.Combine(Directory.GetCurrentDirectory(), "cert");
#else
        private static string kCertPath = "/cert";
#endif
        private static string kAccountPath = Path.Combine(Directory.GetCurrentDirectory(), "account");

        static void Main(string[] args)
        {
            if (!Directory.Exists(kConfigPath))
            {
                Directory.CreateDirectory(kConfigPath);
            }
            if (!Directory.Exists(kCertPath))
            {
                Directory.CreateDirectory(kCertPath);
            }
            if (!Directory.Exists(kAccountPath))
            {
                Directory.CreateDirectory(kAccountPath);
            }

            MainAsync(args).GetAwaiter().GetResult();
        }

        static async Task MainAsync(string[] args)
        {
            Console.WriteLine("AQCert");
#if DEBUG
            var cfKey = File.ReadAllText("CLOUDFLARE_KEY.txt");
            var acmeMail = File.ReadAllText("ACME_MAIL.txt");
            var domains = File.ReadAllText("DOMAINS.txt");
#else
            var cfKey = Environment.GetEnvironmentVariable("CLOUDFLARE_KEY");
            var acmeMail = Environment.GetEnvironmentVariable("ACME_MAIL");
            var domains = Environment.GetEnvironmentVariable("DOMAINS");
#endif


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

            AppConfig.CloudflareKey = cfKey;
            AppConfig.Domains = domains;
            AppConfig.AcmeMail = acmeMail;


            var domainArray = domains.Split(',');


            Console.WriteLine($"为{domains}申请证书...");
            Console.WriteLine(acmeMail);


#if DEBUG
            //指定为letsencrypt
            var caModel = new CAModel()
            {
                Name = "CA_LETSENCRYPT_V2_TEST",
                Url = "https://acme-staging-v02.api.letsencrypt.org/directory",
                EabId = null,
                EabKey = null
            };
#else
    
            //指定为letsencrypt
            var caModel = new CAModel()
            {
                Name = "CA_LETSENCRYPT_V2",
                Url = "https://acme-v02.api.letsencrypt.org/directory", //"https://acme.zerossl.com/v2/DV90",
                EabId = null,
                EabKey = null
            };
#endif


            if (File.Exists(kCertTimeFile))
            {
                _certTimes = JsonConvert.DeserializeObject<Dictionary<string, DateTime>>(File.ReadAllText(kCertTimeFile));
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

                                var domainName = domain.Replace("*.", "");

                                File.WriteAllText(Path.Combine(kCertPath, $"{domainName}.pem")  , cert.pem);
                                File.WriteAllText(Path.Combine(kCertPath, $"{domainName}.key"), cert.privateKey);

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
            File.WriteAllText(kCertTimeFile, JsonConvert.SerializeObject(_certTimes, Formatting.None));
        }
    }
}
