# Books

Books 项目当前处于需求定义与审阅阶段，尚未进入实现。

需求草案通过 Pull Request 提交审阅；文档发布不代表需求已经批准。

## Development

Requirements: .NET 10 SDK, Docker (for local PostgreSQL and Testcontainers).

```bash
docker compose up -d                                  # local PostgreSQL
dotnet tool restore
dotnet ef database update --project src/Books.Infrastructure   # needs ConnectionStrings__Books
dotnet test --solution Books.slnx                     # integration tests start their own PostgreSQL
```

Without Docker, set `BOOKS_TEST_POSTGRES` to an admin connection string; the tests create a fresh database on that server.
