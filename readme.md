# FF3-screen-reader

## Purpose

Adds NVDA output, pathfinding, sound queues and other accessibility aides to Final Fantasy III Pixel Remaster.

## Known Issues
There is about a 1 second delay (may be longer on lower spec pc) between the "Press any button" text and when the user games input. This is because the text is announced when the title screen finishes loading but there is another second or so of logo animation before the user is given control.
Shop menus are reading the first highlighted item on both entry and exit.
Secret passages, even when opened, do not show properly on the pathfinder. Can use wall bumps and estimation to find, usually near the opening mechanism.
H in battle announces statistics for all characters, not active character.
Level up gains are not fully implemented, only Hp is announced.

## Install

Create an account at store.steampowered.com, login, join steam.
Once account is created, install steam download app (should be prompted to do so after account creation.)
Log into desktop app.
to purchase games, the easiest way is to use the web interface. You can search for a game when logged into the browser, purchase it there and will be asked if you want to install your games, which opens the desktop app to finish installation.
Ensure you purchase Final Fantasy III, the page should mention being remastered in the description.
Install MelonLoader into game's installation directory. Ensure nightly builds are enabled.
https://melonloader.co/download.html
Copy NVDAControllerClient64.dll and tolk.dll into installation directory with game executable, usually c:\\Program Files (x86)\\Steam\\Steamapps\\common\\Final Fantasy III PR.
If you created a steam library on another drive, the path will be Drive Letter\\Path to steam library\\SteamLibrary\\steamapps\\common\\Final Fantasy III PR.
FFIII\_screenreader.dll   goes in MelonLoader/mods folder.

## Keys
Game:
WASD or arrow keys: movement
Enter: Confirm
Backspace: cancel
Q: On new game screen, random name for highlighted character. In shop, toggle between statistics and description view.
Mod:
J and L or \[ and ]: cycle destinations in pathfinder
Shift+J and L or - and =: change destination categories
K: Announce currently selected entity
\\ or p: get directions to selected destination
Shift+\\ or P: Toggle pathfinding filter so that not all destinations are visible, just ones with a valid path.
G: Announce current Gil
M: Announce current map.
H: In battle, announce character hp, mp, status effects.
I: In configuration  menu accessible from tab menu and jobs menu, read description of highlighted setting or job. In shop menus, reads description of highlighted item. In item menu with equipment highlighted, announces jobs that can equip.

When on a character's status screen:

up and down arrows read through statistics.
Shift plus arrows: jumps between groups, character info, vitals, statistics, combat statistics, progression.
control plus arrows: jump to beginning or end of statistics screen.
