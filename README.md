# Paintball Arena (Phase 4)

Foundation plugin for a Rust paintball arena minigame. Phase 1 covers configuration and admin setup. Phase 2 adds the HUD, lobby UI, and team theme cycling. Phase 3 adds team caps and waiting queues. Phase 4 adds match flow, kit loadouts, and inventory restore.

## Admin Setup

Grant admins the `paintballarena.admin` permission, then use `/pbadmin` to open the setup UI.

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
- `/pbend [a|b]` - End the match and declare a winner (admin only, omit for draw).
