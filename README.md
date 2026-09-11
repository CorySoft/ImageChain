# ImageChain

OpenAI 兼容风格的图像生成服务，具备**多厂商 / 多模型链式自动容错**（fallback）能力。

- 文本生图（Text-to-Image）与图像生图（Image-to-Image）
- 在同一优先级链上自动重试并切换候选模型，直到生成成功
- 内置 Web 配置页面：厂商管理、模型发现、拖拽 / 右键排序、模型设置、实时日志
- 运行时配置热加载，无需重启

## 特性

- OpenAI 兼容请求 / 响应：`model`（`GenImg` / `EditImg`）与 `prompt` 等字段
- 统一 API Key 认证（`Authorization: Bearer`）+ 管理后台密码登录（无状态会话，登录一次不设过期）
- 模型两级管理：厂商（共享 API Key）→ 模型（独立启用 / 禁用、重试次数、能力优先级、默认参数、请求模板、响应图片路径）
- 实时日志面板：展示请求接口、正在调用的具体模型、失败 / 重试 / 切换到下一个模型
- 配置存储版本化，支持回滚与导入导出

## 目录结构

```
src/
  ImageChain.Core  核心库：配置、发现、链式容错流水线、HTTP 厂商适配
  ImageChain.Web   Web 服务：/v1 API + 配置管理页面 + 实时日志
  ImageChain.Cli   命令行示例
```

## 快速开始

```bash
dotnet run --project src/ImageChain.Web
```

服务默认监听 `0.0.0.0:5000`，访问 `http://localhost:5000` 进入配置页面。

> 首次启动会依据 `src/ImageChain.Web/appsettings.json` 生成运行时配置。请替换其中的占位密钥。

## API

所有 `/v1/*` 接口需携带 API Key：

```
Authorization: Bearer <api-key>
```

| 接口 | 说明 |
| --- | --- |
| `POST /v1/genimage` · `/v1/images/generations` | 文本生图（`GenImg`） |
| `POST /v1/editimage` · `/v1/images/transform` | 图像生图（`EditImg`，需 `image` 字段） |
| `GET /v1/models` | 列出可用模型 |

请求示例：

```json
{
  "model": "GenImg",
  "prompt": "a beautiful landscape, mountains and a lake",
  "size": "1024x1024",
  "n": 1
}
```

支持 `b64_json` 响应格式；JSON / form / query 均可传参，空请求体也会有默认兜底。

管理接口（需登录 Cookie）：

- `POST /api/admin/login` 密码登录；`GET /api/admin/me` 会话探测
- `/api/config/*` 厂商、模型、发现、备份 / 回滚 / 导入导出
- `/api/log/poll` · `/api/log/clear` 实时日志轮询与清空

## 配置

- 初始种子：`src/ImageChain.Web/appsettings.json`
- 运行时配置：`imagechain-config.json`（版本化，位于应用目录），修改即时生效

## 许可证

[MIT](./LICENSE)