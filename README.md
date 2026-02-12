# Paintball Arena (Phase 4)

Foundation plugin for a Rust paintball arena minigame. Phase 1 covers configuration and admin setup. Phase 2 adds the HUD, lobby UI, and team theme cycling. Phase 3 adds team caps and waiting queues. Phase 4 adds match flow, kit loadouts, and inventory restore.

## Match Rules

- **First hit ends the round.** The scoring team gains 1 point.
- **First to 5** points wins the match.
- After each round, active players are teleported back to their team spawns for the next round.

## Admin Setup

Grant admins the `paintballarena.admin` permission, then use `/pbadmin` to open the setup UI.

### Setting Spawns

Use the admin UI or chat commands while standing on the desired location:

- **UI:** `/pbadmin` → click **Set Lobby Spawn**, **Set Spectator Spawn**, **Add Team A Spawn**, **Add Team B Spawn**.
- **Chat:** `/pblobby`, `/pbspectator`, `/pbteama`, `/pbteamb`.

Use `/pbcleara` or `/pbclearb` to reset team spawns.

### Choosing Sides

Open the lobby UI and click the side buttons:

- **HUD button:** Click **Lobby** in the top HUD.
- **Chat:** `/pblobbyui`.

Then choose **Join Side A**, **Join Side B**, or **Leave Match** from the lobby screen.

### Chat Commands

- `/pbadmin` - Open the admin setup UI.
- `/pblobby` - Set the lobby spawn to your current position.
- `/pbspectator` - Set the spectator spawn to your current position.
- `/pbteama` - Add a Team A spawn at your current position.
- `/pbteamb` - Add a Team B spawn at your current position.
- `/pbcleara` - Clear all Team A spawns.
- `/pbclearb` - Clear all Team B spawns.
- `/pblobbyui` - Open the lobby UI.
- `/pbcycle` - Cycle the active team themes (admin only).
- `/pbstart` - Start a match countdown (admin only).
- `/pbforcestart` - Force start the match countdown even if only one side has players (admin only).
- `/pbautostart` - Toggle auto-start when both sides have players (admin only).
- `/pbend [a|b]` - End the match and declare a winner (admin only, omit for draw).
