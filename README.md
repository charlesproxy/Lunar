![Lunar banner](assets/lunar-banner.png)

# Lunar 1.58

Install Lunar with Lunar-Setup-1.58.msi or run Lunar-1.58.exe directly on Windows with .NET Framework 4.8. The executable includes the Lunar mark, rank and agent icons, and map backgrounds.

Use Tracker to search by Name#TAG. The mouse Back button returns to the previous Lunar page or closes an open match scoreboard. Lunar reserves local request capacity for manual searches. If account lookup hits the provider limit, Tracker shows a countdown and retries once. HenrikDev supplies ranks and up to ten recent competitive matches. For a new user installation, set the LUNAR_HENRIK_KEY user environment variable before opening Lunar. Lunar also accepts its existing encrypted local API configuration. At the bottom of the match list in Overview or Matches, select Load all matches to fetch the archived records available through HenrikDev for the selected mode. Competitive is selected by default. The combined list stays ordered by date and keeps the map, act, and date filters. Older scoreboards load when opened and may be unavailable from the provider. The daily win–loss record uses your Windows local date and the loaded competitive matches. An asterisk indicates incomplete coverage.

Daily Store reads the Riot Client session automatically while the page is open. Try the store with Riot Client signed in and VALORANT closed. Lunar displays a saved rotation when it belongs to the verified active account, or as explicitly saved data when Riot Client is closed. An unverified login cannot display a rotation from a previous account. Saved offers are marked expired after the reset time. Open Daily Store once with a working Riot session to save the rotation. The saved view excludes balances and stores no login credentials. Skin and bundle artwork comes from Valorant-API.

Run build.ps1 in Windows PowerShell to compile the app. Build the installer with build-installer.ps1 and a WiX Toolset 3.14 bin path.

## Live Match and Discord

Use VALORANT in Windowed Fullscreen or Windowed mode to see the agent-select panel. The panel opens automatically in agent select and shows the starting side when reported. Alt+T opens the interactive overlay when VALORANT is in the foreground during agent select or an ongoing match. It dims the game, uses the Windows pointer, and takes mouse focus while open. Press Alt+T again to dismiss it and return focus to VALORANT. The automatic agent-select panel is passive until Alt+T is pressed. The panel starts centered on the game window, shows HS% from recent competitive matches and visible account levels on agent portraits, and can be moved by dragging its header. It returns to the center whenever it opens. Use Windowed Fullscreen for the panel to stay visible above the game. Closing the window keeps Lunar available in the Windows notification area. Choose **Exit Lunar** from its tray icon to quit.

**Friends** lists friends from your Riot chat account. Open VALORANT if Riot chat has not loaded. Friend avatars load automatically. Select a friend to view their account banner. Live Match shows peak competitive rank and recent results when available. Colored stripes identify parties supplied by Riot. A dimmer stripe suggests players who were in the same party in their latest known competitive match.

Discord Presence connects automatically to Discord desktop using Lunar's application. Use the sidebar or tray switch to change the preference. The activity displays Play Lunar while the game is closed and switches to map, agent, and score once the game reports those details. Discord's own Activity Sharing setting controls who can see it.

Discord symbol from [Discord Brand Assets](https://discord.com/branding?previewMode=true).


## Crosshair library

Browse 59 player presets, filter them by player category or color, and select a card to preview the static primary crosshair. Use **Copy code**, then import it in VALORANT under **Settings → Crosshair → Import Profile Code**. The clipboard keeps the full profile, including additional scopes when included in the source code. The preview displays the primary crosshair at rest and at actual size.

Player presets are published codes from [VCRDB](https://www.vcrdb.net/) and [ProSettings](https://prosettings.net/). The Pro and Creator categories are independent of the publishing site. Their individual sources are recorded in `assets/crosshairs.json`.

Use **Save** to keep a preset, or **Add crosshair** to paste your own named code. **My Crosshairs** is saved locally on Windows and can be opened offline.
