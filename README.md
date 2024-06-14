# AQCert
全自动申请https证书（letsencrypt），并保存到文件，仅接入了Cloudflare的API，使用Cloudflare的DNS来验证域名所有权。

我主要用于frpc的https证书更新，程序运行后会每小时检测，如果距离上次申请成功时间大于7天，则执行一次申请流程。

## 如何使用

### 直接运行
编译后执行参数，例子：
```
AQCert --CLOUDFLARE_KEY=你的CFKEY --ACME_MAIL=你的ACME_MAIL --DOMAINS=aaa.xxx.com,*.hahaha.com
```

### Docker运行
```
docker run -e CLOUDFLARE_KEY=你的CLOUDFLARE_KEY -e ACME_MAIL=你的ACME_MAIL -e DOMAINS=你要申请的域名 -v /你的cert保存目录:/cert aiqinxuancai/aqcert:latest
```
* 环境变量
```
CLOUDFLARE_KEY
ACME_MAIL
DOMAINS
```
* 路径映射
```
/cert
```
