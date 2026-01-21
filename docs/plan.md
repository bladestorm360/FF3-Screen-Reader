# FF3 Screen Reader - Project Plan

Screen reader accessibility mod for Final Fantasy III Pixel Remaster. TTS announcements for menus, navigation, dialogue, and battle.

**Reference:** `ff5-screen-reader` (similar architecture)

---

## Status

| Component | Status |
|-----------|--------|
| Core & Dialogue | DONE |
| Title/Config/Status Menu | DONE |
| Item/Magic/Job Menu | DONE |
| Save/Load & New Game Naming | DONE |
| Battle System & Pause Menu | DONE |
| Field Navigation & Entity Scanner | DONE |
| Shops & Vehicles | DONE |
| Popups & Map Transitions | DONE |
| NPC Event Item Selection | DONE |

---

## FF3-Specific Features

- **Job Menu:** Job name, level, "Equipped" indicator
- **Magic:** Per-level MP (8 levels), format: "Spell: MP: X/Y. Description"
- **Vehicles:** `GetOn()`/`GetOff()` patches, landing detection via `ShowLandingGuide(bool)`
- **Entity Scanner:** VehicleTypeMap from `Transportation.ModelList`, event-driven refresh

---

## Hotkeys

| Key | Action |
|-----|--------|
| J / [ | Previous entity |
| L / ] | Next entity |
| K | Repeat current entity |
| P / \ | Pathfind to entity |
| Shift+J/L | Cycle category |
| M | Map name |
| G | Gil |
| I | Details (tooltips, stats, job requirements) |
| V | Vehicle/movement mode |
| Up/Down | Navigate stats (status screen) |
| Shift+Up/Down | Jump stat group |
| Ctrl+Up/Down | First/last stat |
| R | Repeat stat |

---

## Known Issues

| Issue | Description |
|-------|-------------|
| Title Screen Timing | "Press any button" speaks ~1 second before input available |

---

## Dependencies

- MelonLoader (net6.0)
- Tolk.dll + screen reader (NVDA recommended)
- FF3 assemblies: `D:\Games\SteamLibrary\steamapps\common\Final Fantasy III PR`

---

## References

| Document | Contents |
|----------|----------|
| `debug.md` | Technical implementation, memory offsets, resolved issues |
| `dump.cs` | Game class/method signatures |
