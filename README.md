# AQCert

基于 Let's Encrypt + Cloudflare DNS 的全自动 SSL 证书申请 & 续期工具。每小时检测一次，证书申请成功 10 天后自动触发续期。

## 快速开始

**直接运行**

```bash
AQCert --CLOUDFLARE_KEY=<API_KEY> --ACME_MAIL=<EMAIL> --DOMAINS=example.com,*.example.com
```

**Docker**

```bash
docker run -d \
  --name aqcert \
  --restart unless-stopped \
  -e CLOUDFLARE_KEY=<API_KEY> \
  -e ACME_MAIL=<EMAIL> \
  -e DOMAINS=example.com,*.example.com \
  -v /opt/cert:/cert \
  -v /opt/cert/config:/config \
  aiqinxuancai/aqcert:latest
```

**Docker Compose**

```yaml
services:
  aqcert:
    image: aiqinxuancai/aqcert:latest
    container_name: aqcert
    restart: unless-stopped
    environment:
      - CLOUDFLARE_KEY=<API_KEY>
      - ACME_MAIL=<EMAIL>
      - DOMAINS=example.com,*.example.com
    volumes:
      - ./cert:/cert
      - ./config:/config
```

## 配置

### 环境变量

| 变量 | 必填 | 说明 |
|------|:----:|------|
| `CLOUDFLARE_KEY` | ✅ | Cloudflare API Key（需含 DNS 编辑权限） |
| `ACME_MAIL` | ✅ | 注册 ACME 账户的邮箱 |
| `DOMAINS` | ✅ | 域名列表，逗号分隔，支持通配符 |
| `AQCERT_CERT_PATH` | ➖ | 证书输出目录（默认 `/cert`） |
| `AQCERT_CONFIG_PATH` | ➖ | 配置目录（默认 `/config`） |
| `AQCERT_ACCOUNT_PATH` | ➖ | ACME 账户目录（默认 `/config/account`） |

### 数据卷

| 路径 | 说明 |
|------|------|
| `/cert` | 证书输出（`domain.pem` + `domain.key`） |
| `/config` | 配置文件和 ACME 账户信息（默认 `/config/account`），务必持久化 |

> **获取 Cloudflare API Key**：[Cloudflare Dashboard](https://dash.cloudflare.com/) → My Profile → API Tokens，创建具有 DNS 编辑权限的 Token。

## 许可证

MIT · 欢迎提交 Issue / PR

