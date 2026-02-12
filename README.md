# Paintball Arena (Phase 2)

Foundation plugin for a Rust paintball arena minigame. Phase 1 covers configuration and admin setup. Phase 2 adds the HUD, lobby UI, and team theme cycling.

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
