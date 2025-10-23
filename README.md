# AQCert

全自动申请 HTTPS 证书工具，基于 Let's Encrypt 服务，支持通过 Cloudflare DNS 验证域名所有权，自动申请和更新 SSL/TLS 证书。

## 功能特点

- 🔒 自动申请 Let's Encrypt 免费证书
- 🌐 支持通过 Cloudflare DNS API 验证域名
- 🔄 自动定时检测和更新证书（每小时检测一次）
- 📦 支持多域名和通配符域名申请
- 🐳 提供 Docker 容器化部署
- 💾 证书自动保存到本地文件

## 工作原理

程序运行后会每小时自动检测证书状态，当距离上次申请成功时间超过 7 天时，自动执行证书申请流程。适用于需要长期维护证书的场景，如 frpc 等服务的 HTTPS 证书自动更新。

## 环境要求

- Cloudflare 账号及 API Key

## 使用方法

### 方式一：直接运行

编译后执行程序并传入参数：

```bash
AQCert --CLOUDFLARE_KEY=你的CF_API_KEY --ACME_MAIL=你的邮箱 --DOMAINS=example.com,*.example.com
```

### 方式二：Docker 运行

```bash
docker run -d \
  --name aqcert \
  --restart unless-stopped \
  -e CLOUDFLARE_KEY=你的CLOUDFLARE_API_KEY \
  -e ACME_MAIL=your-email@example.com \
  -e DOMAINS=example.com,*.example.com,subdomain.example.com \
  -v /opt/cert:/cert \
  -v /opt/cert/config:/config \
  -v /opt/cert/account:/account \
  aiqinxuancai/aqcert:latest
```

### 方式三：Docker Compose 部署

创建 `docker-compose.yml` 文件：

```yaml
version: '3.8'

services:
  aqcert:
    image: aiqinxuancai/aqcert:latest
    container_name: aqcert
    restart: unless-stopped
    environment:
      - CLOUDFLARE_KEY=你的CLOUDFLARE_API_KEY
      - ACME_MAIL=your-email@example.com
      - DOMAINS=example.com,*.example.com
    volumes:
      - ./cert:/cert
      - ./config:/config
      - ./account:/account
```

启动服务：

```bash
docker-compose up -d
```

查看日志：

```bash
docker-compose logs -f aqcert
```

停止服务：

```bash
docker-compose down
```

配合 `.env` 文件使用：

```env
CLOUDFLARE_KEY=你的CLOUDFLARE_API_KEY
ACME_MAIL=your-email@example.com
```

## 配置说明

### 环境变量

| 变量名 | 必填 | 说明 | 示例 |
|--------|------|------|------|
| `CLOUDFLARE_KEY` | 是 | Cloudflare API Key | `your_api_key_here` |
| `ACME_MAIL` | 是 | Let's Encrypt 注册邮箱 | `admin@example.com` |
| `DOMAINS` | 是 | 要申请证书的域名，多个域名用逗号分隔 | `example.com,*.example.com` |

### 数据卷映射

| 容器路径 | 说明 | 建议映射 |
|----------|------|----------|
| `/cert` | 证书文件存储目录 | 必须映射 |
| `/config` | 配置文件目录 | 必须映射 |
| `/account` | ACME 账户信息 | 必须映射，否则每次启动都申请新账号 |

### 获取 Cloudflare API Key

1. 登录 [Cloudflare Dashboard](https://dash.cloudflare.com/)
2. 进入 "My Profile" → "API Tokens"
3. 创建 Token 或使用 Global API Key
4. 确保 Token 具有 DNS 编辑权限

## 证书文件位置

证书申请成功后，文件会保存在映射的 `/cert` 目录下：

- `example.com.pem` - 完整证书链
- `example.com.key` - 私钥文件

## 常见问题

### 1. 证书多久更新一次？

程序会每小时检测一次，当距离上次成功申请超过 10 天时会自动更新证书。

### 2. 支持哪些域名格式？

- 单域名：`example.com`
- 通配符域名：`*.example.com`
- 多域名组合：`example.com,*.example.com,sub.example.com`

### 3. 是否支持其他 DNS 提供商？

目前仅支持 Cloudflare DNS API 验证。

## 许可证

本项目使用开源许可证，详见 LICENSE 文件。

## 贡献

欢迎提交 Issue 和 Pull Request！
