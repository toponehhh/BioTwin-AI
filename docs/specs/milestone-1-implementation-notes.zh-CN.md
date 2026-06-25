# Milestone 1 实施说明

## 本地运行

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-local-m1.ps1
```

- API: `http://localhost:5014`
- Blazor WASM: `http://localhost:5193`
- 日志: `artifacts/devserver`

## Cloudflare Pages 构建

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-cloudflare-pages.ps1
```

发布输出位于 `artifacts/cloudflare-pages/wwwroot`，可作为 Cloudflare Pages 或 Workers Static Assets 的静态资源输入。M1 后端仍是 ASP.NET Core API，后续 milestone 再迁移到 Cloudflare Containers 或 Workers API。
