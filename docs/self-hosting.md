# Self-Hosted QuickShareCloud Deployment Guide (#46)

Deploy the entire QuickShareCloud stack (ASP.NET Core 10 web/API + PostgreSQL 17 + MinIO S3-compatible object storage) on any Linux VPS or homelab with a single command:

```bash
docker compose -f docker-compose.selfhost.yml up -d
```

## Required Environment Variables

| Variable | Description |
|----------|-------------|
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string |
| `ObjectStore__Endpoint` | S3-compatible endpoint (MinIO, Cloudflare R2, AWS S3, Backblaze B2) |
| `ObjectStore__AccessKey` | S3 access key ID |
| `ObjectStore__SecretKey` | S3 secret access key |
| `ObjectStore__BucketName` | Primary bucket name for file objects |
| `Auth__PublicBaseUrl` | External HTTPS origin used for share links and OAuth callbacks |
