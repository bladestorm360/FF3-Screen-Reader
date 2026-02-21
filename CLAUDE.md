# FF3 Screen Reader - Claude Rules

## Critical
- **No code changes without approval** (direct commands = approval)
- **No per-frame patches** - use event methods: `SetCursor`, `SelectContent`, `OnSelect`
- **No git commands** - user manages git
- **Update docs after fixes** - `plan.md` (features), `debug.md` (technical)
- **NEVER use PowerShell `Set-Content`, `Out-File`, or any PowerShell file-writing cmdlet on source files** - these destroy Unicode encoding (Japanese characters, arrows, special symbols). Always use the Edit tool for file modifications, which preserves encoding. This rule applies to ALL batch operations across multiple files. If a bulk change is needed, use the Edit tool on each file individually.

## Build
```
powershell -Command "& {cd 'D:\Games\Dev\Unity\FFPR\ff3\ff3-screen-reader'; .\build_and_deploy.bat}"
```

## IL2CPP Rules (see debug.md)
- Manual Harmony patches only (attributes crash)
- No string/enum params in patched methods
- `AccessTools.Method()` required (not `Type.GetMethod()`)
- `TryCast<T>()` for type casts (not .NET reflection)

## Files
- **Logs:** `D:\Games\SteamLibrary\steamapps\common\Final Fantasy III PR\MelonLoader\Latest.log`
- **Game dump:** `dump.cs` - class/method signatures
- Large files: Grep first, then Read with offset/limit
