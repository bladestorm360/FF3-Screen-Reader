# Claude Code Rules - FF3 Screen Reader

## Critical Rules

### User Approval Required
- **NEVER** implement code changes without explicit user approval
- Present a plan and wait for approval before writing/editing code
- Research and file reading do NOT require approval
- Direct commands ("fix X", "implement X") = approval granted

### No Polling or Deduplication
- **NEVER** patch per-frame methods (called 60x/second)
- **NEVER** add deduplication workarounds (lastAnnouncedText, debouncing, timers)
- If you need deduplication, you're patching the wrong method
- Find methods called once per event: `SetCursor(int)`, `SelectContent(...)`, `OnSelect`

### Minimal Changes
- **NEVER** refactor systems without explicit permission
- Keep changes focused on the specific issue
- Don't change unrelated code when fixing bugs

### Git Off Limits
- **NEVER** run git commands - git is tracked and modified only by the user
- No git status, git log, git diff, git add, git commit, etc.

### Workspace Boundaries
- **NEVER** search or glob parent directories outside the permitted workspace
- Only search within: current working directory and additional working directories listed in env
- If a file location is unknown, ask the user - do not speculatively search upward

### Documentation Required
- **ALWAYS** document completed fixes, solutions, and features to their respective docs:
  - `docs/plan.md` - Feature status, project phases, hotkeys
  - `docs/debug.md` - Technical solutions, offsets, working patterns
  - `docs/PerformanceIssues.md` - Performance fixes (if file exists)
- Update documentation **immediately** after completing work
- Mark items as COMPLETED with date when done

---

## Shell & Build

**Build:** `powershell -Command "& {cd 'D:\Games\Dev\Unity\FFPR\ff3\ff3-screen-reader'; .\build_and_deploy.bat}"`

**CMD only:** Use `dir`, `copy`, `del`, `type`, `findstr`, `set` (not Unix/PowerShell equivalents)

---

## File Rules

- **NEVER** load files >500 lines fully - use Grep first, then Read with offset/limit
- **Logs:** `D:\Games\SteamLibrary\steamapps\common\Final Fantasy III PR\MelonLoader\Latest.log`
- **Game dump:** `dump.cs` - search for class/method signatures

---

## FF3 Constraints

See `docs/debug.md` for full details:
- Use manual Harmony patching (not attributes) - see "Critical Constraints"
- Methods with string/enum params crash - use bool/int params only
- Use `TryCast<T>()` not .NET reflection - see "IL2CPP Rules"
- **ALWAYS** use `AccessTools.Method()` for patching - `Type.GetMethod()` fails on private IL2CPP methods
- Memory offsets and state machine values in debug.md tables

---

## References

| Document | Contents |
|----------|----------|
| `docs/plan.md` | Project phases, feature status, hotkeys |
| `docs/debug.md` | Architecture, offsets, state machines, working solutions |
| `docs/PerformanceIssues.md` | Performance audit, consolidation opportunities |
| `dump.cs` | Game class headers for IL2CPP type discovery |
