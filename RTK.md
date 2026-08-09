# RTK - Token Compression Proxy

**Use `rtk` prefix for ALL shell commands.** RTK compresses bash output up to 90% before it reaches LLM context.

## Rules

- Always prefix shell commands with `rtk`. `git status` -> `rtk git status`. `dotnet build` -> `rtk dotnet build`.
- Built-in tools (`read`, `grep`, `glob`) don't go through bash hook. For large outputs that would benefit from compression, use `rtk read`, `rtk grep`, or `rtk find` instead.
- Verification: `rtk --version`, `rtk gain` (savings dashboard).

## Supported Commands (partial)

| Category | Commands |
|---|---|
| Files | `ls`, `cat`/`read`, `find`, `grep`, `diff` |
| Git | `git status`, `git diff`, `git log`, `git add/commit/push` |
| .NET | `dotnet build`, `dotnet test`, `dotnet run` |
| Tests | `cargo test`, `npm test`, `pytest`, `go test` |
| Docker | `docker ps`, `docker images` |
