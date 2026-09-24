# FF3-screen-reader

## Purpose

Adds NVDA output, pathfinding, sound queues and other accessibility aides to Final Fantasy III Pixel Remaster.

## Known Issues

Secret passages, even when opened, do not show properly on the pathfinder. Can use wall bumps and estimation to find, usually near the opening mechanism.

Auto Detail (descriptions read automatically when you move onto an item, spell or piece of equipment) is now on by default for new installs. If you used an earlier version, your saved setting is kept; press F7 or use the mod menu to turn it on.

## Install

Create an account at store.steampowered.com, login, join steam.

Once account is created, install steam download app (should be prompted to do so after account creation.)

Log into desktop app.

to purchase games, the easiest way is to use the web interface. You can search for a game when logged into the browser, purchase it there and will be asked if you want to install your games, which opens the desktop app to finish installation.

Ensure you purchase Final Fantasy III, the page should mention being remastered in the description.

Install MelonLoader into game's installation directory. Ensure nightly builds are enabled.
https://github.com/LavaGang/MelonLoader/releases

Copy NVDAControllerClient64.dll, tolk.dll and SDL3.dll into installation directory with game executable, usually c:\\Program Files (x86)\\Steam\\Steamapps\\common\\Final Fantasy III PR.

If you created a steam library on another drive, the path will be Drive Letter\\Path to steam library\\SteamLibrary\\steamapps\\common\\Final Fantasy III PR.

FFIII\_screenreader.dll   goes in MelonLoader/mods folder.

## Keys

Game:

WASD or arrow keys: movement

Enter: Confirm

Backspace: cancel

Q: On new game screen, random name for highlighted character. In shop, toggle between statistics and description view.

F1: toggle walk and run. F3: toggle random encounters. The mod announces the new state, whichever way it was changed.

Mod, on the field:

J and L or \[ and ]: cycle destinations in pathfinder

Shift+J and L, Shift+\[ and ], or - and =: change destination categories

Shift+K: back to the All category

K: announce the selected destination's name

\\ or P: get directions to the selected destination (with audio beacons on, restarts the beacon instead)

Shift+\\ or Shift+P: toggle pathfinding filter, so only destinations with a valid path are listed

Ctrl+\\ or Ctrl+P: toggle layer filter (hide stairs and ladders between floors)

Shift+M: toggle map exit filter (exits to the same map merged into the closest one)

Backtick (\`): rescan the map for destinations

Comma and period: previous and next waypoint. Shift+comma and Shift+period: waypoint category.

Slash: directions to the selected waypoint. Shift+slash: add a waypoint here. Ctrl+period: rename it. Ctrl+slash: delete it. Ctrl+Shift+slash: clear all waypoints on this map.

Ctrl+arrow keys: teleport one tile beside the selected destination

Semicolon: toggle wall tones. Apostrophe: toggle footsteps. F6 or 9: toggle audio beacons.

V: announce movement mode (walking, ship, airship and so on)

Mod, everywhere:

G: announce current Gil

M: announce current map

H: in battle, announce the HP and status effects of the character whose turn it is

R: repeat the last dialogue page

I: read the description of the highlighted item, spell, equipment, job, setting or shop item

Shift+I: read the controls shown on screen

U: on equipment, announce which unlocked jobs can equip it

F5: in battle, change how enemy HP is read (numbers, percentage, hidden)

F7: toggle Auto Detail

F8: open the mod menu (field only). Up and down to move, left and right to change a value, I for a description of the highlighted option, Escape or F8 to close.

0: save the untranslated entity names on this map to a file, for translation

When on a character's status screen or a bestiary entry:

Up and down arrows (or W and S) read through statistics.
Shift plus arrows: jump between groups.
Control plus arrows: jump to beginning or end of the list.
R: repeat the current statistic (status screen).

### Game controller

Button names are shown Xbox (PlayStation). Nintendo Pro / Joy-Con labels are also recognized — the controller type is auto-detected.

- Left stick: movement and menu navigation.
- D-pad: menu navigation. On the field, the D-pad is used by the mod for waypoints (see below).
- A / B / X / Y and the shoulder buttons: the game's own functions. A face button also stops the current speech.

### Mod controller

On the field:

- D-pad up and down: previous and next waypoint. D-pad left and right: waypoint category.
- Right stick up and down: previous and next destination. Right stick left and right: destination category.
- LT (L2): directions to the last selected destination or waypoint (with audio beacons on, restarts the beacon).
- R3: toggle pathfinding filter. L3: toggle audio beacons. With Stick Click Normalization on in the mod menu, R3 and L3 go to the game instead and these move to mod mode.
- Start: open the mod menu. D-pad or left stick to move and change values, A to toggle, B or Start to close.

In game menus:

- Right stick up: read the description of the highlighted entry (same as I).
- Right stick down: read the controls shown on screen (same as Shift+I).
- Right stick left: which unlocked jobs can equip the highlighted equipment (same as U).
- On a status screen, bestiary entry or controls list: D-pad or left stick up and down read through the entries.

Mod mode: press Back (Select / View) to enter it, then one of:

- In dialogue: X repeats the last dialogue page.
- In battle: X announces the active character's HP. Right stick down reads these controls.
- On the field: X announces Gil, Y the current map, right stick teleports next to the selected destination.

Press Back again to leave mod mode without doing anything.
