# FF3 Screen Reader - Claude Rules

## Critical
- **No code changes without approval** (direct commands = approval)
- **No per-frame patches** - use event methods: `SetCursor`, `SelectContent`, `OnSelect`
- **Git** - same as the other FFPR mods: commit when the user asks for it; push only when asked. (The old "no git commands" rule was lifted by the user on 2026-09-24.)
- **Update docs after fixes** - `plan.md` (features), `debug.md` (technical)
- **NEVER use PowerShell `Set-Content`, `Out-File`, or any PowerShell file-writing cmdlet on source files** - these destroy Unicode encoding (Japanese characters, arrows, special symbols). Always use the Edit tool for file modifications, which preserves encoding. This rule applies to ALL batch operations across multiple files. If a bulk change is needed, use the Edit tool on each file individually.
- **Game-specific translations** - Translations are game-specific; NEVER copy or look up translation strings from another FF mod (phrasing and presentation differ per game, and are not likely to repeat). When a string has no existing translation, translate it live yourself and add a self-contained entry to this mod's `translation.json`.

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
