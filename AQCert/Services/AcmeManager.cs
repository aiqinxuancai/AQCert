
using Aliyun.AutoCdnSsl.Utils;
using Certify.ACME.Anvil;
using Certify.ACME.Anvil.Acme;
using Certify.ACME.Anvil.Acme.Resource;
using AQCert.Models;

namespace AQCert.Services
{
    internal class AcmeManager
    {

        private const string kAccountPath = @"account";

        private string _email = AppConfig.AcmeMail;
        private AcmeContext _acme = null;
        private IAccountContext _account = null;
        private static readonly Lazy<AcmeManager> lazy = new Lazy<AcmeManager>(() => new AcmeManager());

        public static AcmeManager Instance => lazy.Value;

        private AcmeManager()
        {
            if (!System.IO.Directory.Exists(kAccountPath))
            {
                System.IO.Directory.CreateDirectory(kAccountPath);
            }
        }

        private async Task Init(CAModel model)
        {
            if (string.IsNullOrWhiteSpace(_email))
            {
                throw new ArgumentNullException();
            }


            try
            {
                var acmeUri = new Uri(model.Url);

                if (!string.IsNullOrEmpty(model.Url))
                {
                    acmeUri = new Uri(model.Url);
                }
                var accountHash = MD5Utils.GetMD5(acmeUri.ToString() + "_" + _email);
                var pemPath = Path.Combine(kAccountPath, $"{accountHash}.pem");

                if (File.Exists(pemPath))
                {
                    Console.WriteLine("从本地读取账号...");
                    var pem = File.ReadAllText(pemPath);
                    var accountKey = KeyFactory.FromPem(pem);
                    var acme = new AcmeContext(acmeUri, accountKey);
                    var account = await acme.Account();

                    _acme = acme;
                    _account = account;
                }
                else
                {
                    Console.WriteLine($"申请新的账号 {acmeUri} {_email}...");
                    var acme = new AcmeContext(acmeUri);
                    var account = await acme.NewAccount(_email, true, model.EabId, model.EabKey);
                    var pemKey = acme.AccountKey.ToPem();
                    Console.WriteLine($"申请完成，写出到{pemPath}...");
                    File.WriteAllText(pemPath, pemKey);

                    _acme = acme;
                    _account = account;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                throw new Exception($"Init Error#1 {ex}");
            }

            if (_account == null)
            {
                throw new ArgumentNullException("Init Error#2");
            }
        }

        private IOrderContext LoadOrder(string domain)
        {
            var orderPath = Path.Combine(kAccountPath, MD5Utils.GetMD5(domain) + ".order");
            if (File.Exists(orderPath))
            {
                //存在
                return _acme.Order(new Uri(File.ReadAllText(orderPath)));
            }
            return null;
        }


        /// <summary>
        /// Start order
        /// </summary> 
        /// <param name="domains">"*.your.domain.name"</param>
        public async Task<(string privateKey, string pem)> Order(string domain, CAModel model)
        {
            if (_account == null)
            {
                await Init(model);
            }

            //TODO 检查本地配置是否已经有这个Order，然后检查其状态
            //var localOrder = LoadOrder(domain);
            //if (localOrder != null)
            //{
            //    var r = await localOrder.Resource();
            //}

            var orderContext = await _acme.NewOrder(new List<string> { domain });
            //orderContext.Location
            var authz = (await orderContext.Authorizations()).First();

            Challenge validateResult = new Challenge();
            var dnsChallenge = await authz.Dns();
            var dnsTxt = _acme.AccountKey.DnsTxt(dnsChallenge.Token);
            Console.WriteLine($"正在添加{domain}记录...");


            var mainDomain = DomainUtils.ExtractTopLevelDomain(domain);
            var txtDomain = "_acme-challenge." + domain.Replace("*.", "").Replace($".{mainDomain}", "");



            var deleteSuccess = await CloudflareAPIManager.Instance.DeleteRecordsByName(domain.Replace("*.", ""), txtDomain);

            var addRecordResult = await CloudflareAPIManager.Instance.AddOrUpdateTxtRecord(domain.Replace("*.", ""), txtDomain, dnsTxt);
            
            
            if (!addRecordResult)
            {
                throw new Exception("添加Cloudflare解析失败！");
            }
            Console.WriteLine($"Cloudflare记录添加完成，等待20秒开始验证");
            await Task.Delay(10 * 1000); //TTL

            do
            {
                await Task.Delay(10 * 1000);
                Console.WriteLine($"正在本地验证DNSTXT记录...");
            } while (!await DomainUtils.AuthTxtRecords($"{txtDomain}.{mainDomain}", "8.8.8.8", dnsTxt));
            
            Console.WriteLine($"本地验证完成");
            await Task.Delay(5 * 1000);
            Console.WriteLine($"本地验证DNS成功，提交CA验证...");
            var v = await dnsChallenge.Validate();
            Console.WriteLine(v.Status.ToString());

            if (v.Status != ChallengeStatus.Valid)
            {
                //CA未直接验证成功
                await Task.Delay(10 * 1000);

                var errorCount = 0;
                do
                {
                    try
                    {
                        validateResult = await dnsChallenge.Resource();
                        Console.WriteLine("结果：" + validateResult.Status.ToString());
                        if (validateResult.Status == ChallengeStatus.Valid)
                        {
                            break;
                        }
                        else if (validateResult.Status == ChallengeStatus.Invalid)
                        {
                            errorCount++;
                            if (errorCount > 20)
                            {
                                errorCount = 0;
                                //重新提交一次验证
                                Console.WriteLine("重新提交验证...");
                                var reValidate = await dnsChallenge.Validate();
                                Console.WriteLine(reValidate.Status.ToString());
                            }

                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }

                    await Task.Delay(1000 * 10);

                } while (validateResult.Status != ChallengeStatus.Valid);
            }

            


            Console.WriteLine($"CA验证成功");
            await Task.Delay(3000);

            var privateKey = KeyFactory.NewKey(KeyAlgorithm.RS256);

            var order = await orderContext.Finalize(new CsrInfo
            {
                CommonName = domain,
            }, privateKey);

            Console.WriteLine($"等待签发证书...");
            if (order.Status == OrderStatus.Processing)
            {
                var attempts = 10;
                while (attempts > 0 && order.Status == OrderStatus.Processing)
                {
                    var waitMS = 3000;
                    if (orderContext.RetryAfter > 0 && orderContext.RetryAfter < 60)
                    {
                        waitMS = orderContext.RetryAfter * 1000;
                    }

                    await Task.Delay(waitMS);
                    order = await orderContext.Resource();
                    attempts--;
                }
            }

            if (order.Status != OrderStatus.Valid)
            {
                Console.WriteLine($"错误");
                throw new Exception("Error#1");
            }

            Console.WriteLine($"已签发证书");
            CertificateChain certificateChain = await orderContext.Download();

            var privateKeyPem = privateKey.ToPem();
            var certPem = certificateChain.ToPem();
            return (privateKeyPem, certPem);
        }


    }
}
