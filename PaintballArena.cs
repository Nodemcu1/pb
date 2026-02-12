using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("PaintballArena", "Nodemcu1", "0.1.0")]
    [Description("UI-driven paintball arena foundation and admin setup.")]
    public class PaintballArena : RustPlugin
    {
        [PluginReference]
        private Plugin ImageLibrary;

        private const string AdminPermission = "paintballarena.admin";
        private const string AdminUiName = "PaintballArena.AdminUI";
        private const string HudUiName = "PaintballArena.HUD";
        private const string HudScoreUiName = "PaintballArena.HUD.Scoreboard";
        private const string HudLobbyUiName = "PaintballArena.HUD.LobbyButton";
        private const string LobbyUiName = "PaintballArena.LobbyUI";
        private const string HudScoreAnchorMin = "0.3 0.93";
        private const string HudScoreAnchorMax = "0.7 0.99";
        private const string HudScoreImageName = "PaintballArena.HUD.Scoreboard.Background";
        private const string HudLobbyImageName = "PaintballArena.HUD.Lobby.Background";
        private const string LobbyBackgroundImageName = "PaintballArena.Lobby.Background";
        private const string AdminBackgroundImageName = "PaintballArena.Admin.Background";
        private const int MaxPlayersPerSide = 6;
        private const int PaintballAmmoAmount = 200;
        private const int MatchCountdownSeconds = 10;
        private const int ScoreLimit = 5;
        private const int RoundResetDelaySeconds = 3;
        private const string PaintballGunShortname = "paintballgun";
        private const string PaintballAmmoShortname = "ammo.paintball";

        private ConfigData config;
        private readonly Dictionary<ulong, TeamSide> playerSides = new Dictionary<ulong, TeamSide>();
        private readonly List<ulong> queueSideA = new List<ulong>();
        private readonly List<ulong> queueSideB = new List<ulong>();
        private readonly Dictionary<ulong, InventorySnapshot> savedInventories = new Dictionary<ulong, InventorySnapshot>();
        private readonly List<TeamTheme> teamThemes = new List<TeamTheme>
        {
            new TeamTheme("Orange", "1 0.5 0 0.9"),
            new TeamTheme("Green", "0.2 0.8 0.2 0.9"),
            new TeamTheme("Yellow", "0.9 0.85 0.1 0.9"),
            new TeamTheme("Blue", "0.2 0.5 0.9 0.9"),
            new TeamTheme("Purple", "0.6 0.3 0.8 0.9")
        };
        private int themeIndex = 0;
        private int scoreA;
        private int scoreB;
        private MatchState matchState = MatchState.Lobby;
        private Timer countdownTimer;
        private int countdownRemaining;
        private bool roundInProgress;

        private enum MatchState
        {
            Lobby,
            Countdown,
            Live
        }

        private enum TeamSide
        {
            None,
            A,
            B
        }

        private class ConfigData
        {
            [JsonProperty("Lobby Spawn")]
            public SpawnPoint LobbySpawn;

            [JsonProperty("Spectator Spawn")]
            public SpawnPoint SpectatorSpawn;

            [JsonProperty("Team A Spawns")]
            public List<SpawnPoint> TeamASpawns = new List<SpawnPoint>();

            [JsonProperty("Team B Spawns")]
            public List<SpawnPoint> TeamBSpawns = new List<SpawnPoint>();

            [JsonProperty("Auto Start Enabled")]
            public bool AutoStartEnabled;

            [JsonProperty("HUD Scoreboard Background URL")]
            public string HudScoreboardBackgroundUrl;

            [JsonProperty("HUD Lobby Button Background URL")]
            public string HudLobbyButtonBackgroundUrl;

            [JsonProperty("Lobby Background URL")]
            public string LobbyBackgroundUrl;

            [JsonProperty("Admin Background URL")]
            public string AdminBackgroundUrl;
        }

        private class SpawnPoint
        {
            public Vector3 Position;
            public Vector3 Rotation;

            [JsonConstructor]
            public SpawnPoint()
            {
            }

            public SpawnPoint(Vector3 position, Vector3 rotation)
            {
                Position = position;
                Rotation = rotation;
            }
        }

        private class TeamTheme
        {
            public string Name { get; }
            public string Color { get; }

            public TeamTheme(string name, string color)
            {
                Name = name;
                Color = color;
            }
        }

        private class InventorySnapshot
        {
            public List<ItemSnapshot> Main = new List<ItemSnapshot>();
            public List<ItemSnapshot> Belt = new List<ItemSnapshot>();
            public List<ItemSnapshot> Wear = new List<ItemSnapshot>();
        }

        private class ItemSnapshot
        {
            public string Shortname;
            public int Amount;
            public ulong Skin;
            public float Condition;
            public int Slot;
            public int Ammo;
            public string AmmoType;
            public List<ItemSnapshot> Contents = new List<ItemSnapshot>();
        }

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config == null)
                {
                    throw new JsonException("Config is empty.");
                }
            }
            catch (System.Exception ex)
            {
                PrintWarning($"Failed to read config ({ex.Message}). Creating new config file.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void Init()
        {
            permission.RegisterPermission(AdminPermission, this);
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyAdminUi(player);
                DestroyHud(player);
                DestroyLobbyUi(player);
            }
        }

        private void OnServerInitialized()
        {
            LoadImages();
            RefreshHudForAll();
        }

        private void OnPlayerInit(BasePlayer player)
        {
            ShowHud(player);
            RestoreInventoryIfNeeded(player);
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            DestroyAdminUi(player);
            DestroyHud(player);
            DestroyLobbyUi(player);
            RemovePlayerFromSideAndQueue(player);
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (player == null || matchState != MatchState.Live)
            {
                return;
            }

            var side = GetPlayerSide(player.userID);
            if (side == TeamSide.None)
            {
                return;
            }

            timer.Once(0.1f, () =>
            {
                if (player == null || !player.IsConnected)
                {
                    return;
                }

                EquipPaintballKit(player);
                TeleportToSideSpawn(player, side);
            });
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (matchState != MatchState.Live || !roundInProgress)
            {
                return null;
            }

            var victim = entity as BasePlayer;
            if (victim == null || info == null)
            {
                return null;
            }

            var attacker = info.Initiator as BasePlayer;
            if (attacker == null || attacker == victim)
            {
                return null;
            }

            var attackerSide = GetPlayerSide(attacker.userID);
            var victimSide = GetPlayerSide(victim.userID);
            if (attackerSide == TeamSide.None || victimSide == TeamSide.None || attackerSide == victimSide)
            {
                return null;
            }

            if (!IsPaintballHit(info))
            {
                return null;
            }

            info.damageTypes?.ScaleAll(0f);
            info.HitMaterial = 0;
            info.DoHitEffects = false;
            HandlePaintballHit(attackerSide, attacker.displayName, victim.displayName);
            return true;
        }

        [ChatCommand("pbadmin")]
        private void CmdOpenAdmin(BasePlayer player, string command, string[] args)
        {
            if (!HasAdminPermission(player))
            {
                SendReply(player, "You do not have permission to use this command.");
                return;
            }

            OpenAdminUi(player);
        }

        [ChatCommand("pblobby")]
        private void CmdSetLobby(BasePlayer player, string command, string[] args)
        {
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            SetLobbySpawn(player);
        }

        [ChatCommand("pbspectator")]
        private void CmdSetSpectator(BasePlayer player, string command, string[] args)
        {
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            SetSpectatorSpawn(player);
        }

        [ChatCommand("pbteama")]
        private void CmdAddTeamA(BasePlayer player, string command, string[] args)
        {
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            AddTeamSpawn(player, config.TeamASpawns, "Team A");
        }

        [ChatCommand("pbteamb")]
        private void CmdAddTeamB(BasePlayer player, string command, string[] args)
        {
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            AddTeamSpawn(player, config.TeamBSpawns, "Team B");
        }

        [ChatCommand("pbcleara")]
        private void CmdClearTeamA(BasePlayer player, string command, string[] args)
        {
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            ClearTeamSpawns(player, config.TeamASpawns, "Team A");
        }

        [ChatCommand("pbclearb")]
        private void CmdClearTeamB(BasePlayer player, string command, string[] args)
        {
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            ClearTeamSpawns(player, config.TeamBSpawns, "Team B");
        }

        [ChatCommand("pblobbyui")]
        private void CmdOpenLobby(BasePlayer player, string command, string[] args)
        {
            OpenLobbyUi(player);
        }

        [ChatCommand("pbcycle")]
        private void CmdCycleThemes(BasePlayer player, string command, string[] args)
        {
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            CycleTeamThemes();
            SendReply(player, $"Next match themes: {CurrentThemeA().Name} vs {CurrentThemeB().Name}");
        }

        [ChatCommand("pbstart")]
        private void CmdStartMatch(BasePlayer player, string command, string[] args)
        {
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            StartMatchCountdown(player, false);
        }

        [ChatCommand("pbforcestart")]
        private void CmdForceStart(BasePlayer player, string command, string[] args)
        {
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            StartMatchCountdown(player, true);
        }

        [ChatCommand("pbautostart")]
        private void CmdToggleAutoStart(BasePlayer player, string command, string[] args)
        {
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            config.AutoStartEnabled = !config.AutoStartEnabled;
            SaveConfig();
            SendReply(player, $"Auto start is now {(config.AutoStartEnabled ? "enabled" : "disabled")}.");
        }

        [ChatCommand("pbend")]
        private void CmdEndMatch(BasePlayer player, string command, string[] args)
        {
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            EndMatch(ParseWinner(args), player);
        }

        [ConsoleCommand("paintballarena.openadmin")]
        private void ConsoleOpenAdmin(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
            {
                return;
            }

            OpenAdminUi(player);
        }

        [ConsoleCommand("paintballarena.closeadmin")]
        private void ConsoleCloseAdmin(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                return;
            }

            DestroyAdminUi(player);
        }

        [ConsoleCommand("paintballarena.openlobby")]
        private void ConsoleOpenLobby(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                PrintWarning("paintballarena.openlobby can only be used by a player.");
                return;
            }

            OpenLobbyUi(player);
        }

        [ConsoleCommand("paintballarena.closelobby")]
        private void ConsoleCloseLobby(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                PrintWarning("paintballarena.closelobby can only be used by a player.");
                return;
            }

            DestroyLobbyUi(player);
        }

        [ConsoleCommand("paintballarena.joina")]
        private void ConsoleJoinA(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                PrintWarning("paintballarena.joina can only be used by a player.");
                return;
            }

            SetPlayerSide(player, TeamSide.A);
        }

        [ConsoleCommand("paintballarena.joinb")]
        private void ConsoleJoinB(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                PrintWarning("paintballarena.joinb can only be used by a player.");
                return;
            }

            SetPlayerSide(player, TeamSide.B);
        }

        [ConsoleCommand("paintballarena.leave")]
        private void ConsoleLeave(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                PrintWarning("paintballarena.leave can only be used by a player.");
                return;
            }

            SetPlayerSide(player, TeamSide.None);
        }

        [ConsoleCommand("paintballarena.cyclethemes")]
        private void ConsoleCycleThemes(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && !EnsureAdminPlayer(player))
            {
                return;
            }

            CycleTeamThemes();
        }

        [ConsoleCommand("paintballarena.start")]
        private void ConsoleStartMatch(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && !EnsureAdminPlayer(player))
            {
                return;
            }

            StartMatchCountdown(player, false);
        }

        [ConsoleCommand("paintballarena.forcestart")]
        private void ConsoleForceStart(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && !EnsureAdminPlayer(player))
            {
                return;
            }

            StartMatchCountdown(player, true);
        }

        [ConsoleCommand("paintballarena.autostart")]
        private void ConsoleToggleAutoStart(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && !EnsureAdminPlayer(player))
            {
                return;
            }

            config.AutoStartEnabled = !config.AutoStartEnabled;
            SaveConfig();
            Reply(player, $"Auto start is now {(config.AutoStartEnabled ? "enabled" : "disabled")}.");
        }

        [ConsoleCommand("paintballarena.end")]
        private void ConsoleEndMatch(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && !EnsureAdminPlayer(player))
            {
                return;
            }

            EndMatch(ParseWinner(arg.Args), player);
        }

        [ConsoleCommand("paintballarena.setlobby")]
        private void ConsoleSetLobby(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            SetLobbySpawn(player);
        }

        [ConsoleCommand("paintballarena.setspectator")]
        private void ConsoleSetSpectator(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            SetSpectatorSpawn(player);
        }

        [ConsoleCommand("paintballarena.addteama")]
        private void ConsoleAddTeamA(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            AddTeamSpawn(player, config.TeamASpawns, "Team A");
        }

        [ConsoleCommand("paintballarena.addteamb")]
        private void ConsoleAddTeamB(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            AddTeamSpawn(player, config.TeamBSpawns, "Team B");
        }

        [ConsoleCommand("paintballarena.clearteama")]
        private void ConsoleClearTeamA(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            ClearTeamSpawns(player, config.TeamASpawns, "Team A");
        }

        [ConsoleCommand("paintballarena.clearteamb")]
        private void ConsoleClearTeamB(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            ClearTeamSpawns(player, config.TeamBSpawns, "Team B");
        }

        private bool HasAdminPermission(BasePlayer player)
        {
            return player != null && permission.UserHasPermission(player.UserIDString, AdminPermission);
        }

        private void SetLobbySpawn(BasePlayer player)
        {
            config.LobbySpawn = CreateSpawnPoint(player);
            SaveConfig();
            SendReply(player, "Lobby spawn set.");
            OpenAdminUi(player);
        }

        private void SetSpectatorSpawn(BasePlayer player)
        {
            config.SpectatorSpawn = CreateSpawnPoint(player);
            SaveConfig();
            SendReply(player, "Spectator spawn set.");
            OpenAdminUi(player);
        }

        private void AddTeamSpawn(BasePlayer player, List<SpawnPoint> list, string label)
        {
            list.Add(CreateSpawnPoint(player));
            SaveConfig();
            SendReply(player, $"{label} spawn added. ({list.Count})");
            OpenAdminUi(player);
        }

        private void ClearTeamSpawns(BasePlayer player, List<SpawnPoint> list, string label)
        {
            list.Clear();
            SaveConfig();
            SendReply(player, $"{label} spawns cleared.");
            OpenAdminUi(player);
        }

        private bool EnsureAdminPlayer(BasePlayer player)
        {
            if (player == null)
            {
                return false;
            }

            if (!HasAdminPermission(player))
            {
                SendReply(player, "You do not have permission to use this command.");
                return false;
            }

            return true;
        }

        private SpawnPoint CreateSpawnPoint(BasePlayer player)
        {
            var position = player.transform.position;
            var rotation = player.transform.rotation.eulerAngles;
            return new SpawnPoint(position, rotation);
        }

        private void CycleTeamThemes()
        {
            if (teamThemes.Count < 2)
            {
                PrintWarning("At least two team themes are required to cycle.");
                return;
            }

            themeIndex = (themeIndex + 1) % teamThemes.Count;
            scoreA = 0;
            scoreB = 0;
            RefreshHudForAll();
        }

        private void RefreshHudForAll()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                ShowHud(player);
            }
        }

        private TeamTheme CurrentThemeA()
        {
            return teamThemes[themeIndex];
        }

        private TeamTheme CurrentThemeB()
        {
            return teamThemes[(themeIndex + 1) % teamThemes.Count];
        }

        private string ScoreboardText()
        {
            var themeA = CurrentThemeA();
            var themeB = CurrentThemeB();
            var status = MatchStatusLabel();
            var counts = $"A {GetSideCount(TeamSide.A)}/{MaxPlayersPerSide} | B {GetSideCount(TeamSide.B)}/{MaxPlayersPerSide}";
            return string.Join("\n", new[]
            {
                $"{themeA.Name} {scoreA} - {scoreB} {themeB.Name}",
                $"{MatchRuleLabel()} {status}",
                counts
            });
        }

        private string LobbyInfoText()
        {
            var themeA = CurrentThemeA();
            var themeB = CurrentThemeB();
            var status = MatchStatusLabel();
            var autoStart = config.AutoStartEnabled ? "On" : "Off";
            var sideA = $"{GetSideCount(TeamSide.A)}/{MaxPlayersPerSide} (Queued {queueSideA.Count})";
            var sideB = $"{GetSideCount(TeamSide.B)}/{MaxPlayersPerSide} (Queued {queueSideB.Count})";
            return string.Join("\n", new[]
            {
                $"{themeA.Name} vs {themeB.Name}",
                $"Score: {scoreA} - {scoreB} ({MatchRuleLabel()})",
                $"State: {status} | Auto Start: {autoStart}",
                $"Side A: {sideA}",
                $"Side B: {sideB}"
            });
        }

        private string MatchStatusLabel()
        {
            if (matchState == MatchState.Countdown)
            {
                return $"Countdown {countdownRemaining}s";
            }

            return matchState == MatchState.Live ? "Live" : "Lobby";
        }

        private void ShowHud(BasePlayer player)
        {
            if (player == null || teamThemes.Count < 2)
            {
                return;
            }

            DestroyHud(player);

            var container = new CuiElementContainer();
            var scoreboardImage = GetImage(HudScoreImageName);
            var scoreboardHasImage = !string.IsNullOrEmpty(scoreboardImage);
            var scoreboard = container.Add(new CuiPanel
            {
                Image = { Color = scoreboardHasImage ? "0 0 0 0" : "0.08 0.08 0.08 0.75" },
                RectTransform = { AnchorMin = HudScoreAnchorMin, AnchorMax = HudScoreAnchorMax }
            }, "Hud", HudScoreUiName);

            if (scoreboardHasImage)
            {
                AddBackgroundImage(container, scoreboard, scoreboardImage);
            }

            AddLabel(container, scoreboard, ScoreboardText(), "0 0", "1 1", 12);

            var lobbyButtonImage = GetImage(HudLobbyImageName);
            var lobbyButtonHasImage = !string.IsNullOrEmpty(lobbyButtonImage);
            var lobbyButtonPanel = container.Add(new CuiPanel
            {
                Image = { Color = lobbyButtonHasImage ? "0 0 0 0" : "0.2 0.2 0.2 0.85" },
                RectTransform = { AnchorMin = "0.9 0.94", AnchorMax = "0.98 0.985" }
            }, "Hud", HudLobbyUiName);

            if (lobbyButtonHasImage)
            {
                AddBackgroundImage(container, lobbyButtonPanel, lobbyButtonImage);
            }

            container.Add(new CuiButton
            {
                Button = { Color = "0 0 0 0", Command = "paintballarena.openlobby" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Text = { Text = "Lobby", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 0.95" }
            }, lobbyButtonPanel);

            CuiHelper.AddUi(player, container);
        }

        private void DestroyHud(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            CuiHelper.DestroyUi(player, HudScoreUiName);
            CuiHelper.DestroyUi(player, HudLobbyUiName);
            CuiHelper.DestroyUi(player, HudUiName);
        }

        private void OpenLobbyUi(BasePlayer player)
        {
            if (player == null || teamThemes.Count < 2)
            {
                return;
            }

            DestroyLobbyUi(player);

            var themeA = CurrentThemeA();
            var themeB = CurrentThemeB();

            var container = new CuiElementContainer();
            var lobbyImage = GetImage(LobbyBackgroundImageName);
            var lobbyHasImage = !string.IsNullOrEmpty(lobbyImage);
            var lobbyPanel = container.Add(new CuiPanel
            {
                Image = { Color = lobbyHasImage ? "0 0 0 0.75" : "0 0 0 0.85" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", LobbyUiName);

            if (lobbyHasImage)
            {
                AddBackgroundImage(container, lobbyPanel, lobbyImage);
            }

            AddLabel(container, lobbyPanel, "Paintball Lobby", "0.3 0.86", "0.7 0.93", 24);
            AddLabel(container, lobbyPanel, LobbyInfoText(), "0.2 0.68", "0.8 0.85", 14);

            AddButton(container, lobbyPanel, $"Join Side A ({themeA.Name})", "paintballarena.joina", "0.35 0.52", "0.65 0.6", themeA.Color);
            AddButton(container, lobbyPanel, $"Join Side B ({themeB.Name})", "paintballarena.joinb", "0.35 0.43", "0.65 0.51", themeB.Color);
            AddButton(container, lobbyPanel, "Leave Match", "paintballarena.leave", "0.35 0.34", "0.65 0.42", "0.8 0.2 0.2 0.9");
            AddButton(container, lobbyPanel, "Close", "paintballarena.closelobby", "0.35 0.25", "0.65 0.33", "0.2 0.2 0.2 0.9");

            CuiHelper.AddUi(player, container);
        }

        private void DestroyLobbyUi(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            CuiHelper.DestroyUi(player, LobbyUiName);
        }

        private void SetPlayerSide(BasePlayer player, TeamSide side)
        {
            if (player == null)
            {
                return;
            }

            if (side == TeamSide.None)
            {
                RemovePlayerFromSideAndQueue(player);
                SendReply(player, "You have left the match queue.");
                RestoreInventoryIfNeeded(player);
                DestroyLobbyUi(player);
                return;
            }

            if (GetPlayerSide(player.userID) == side)
            {
                SendReply(player, $"You are already on Side {side}.");
                DestroyLobbyUi(player);
                return;
            }

            if (IsQueuedForSide(player.userID, side))
            {
                SendReply(player, $"You are already waiting for Side {side}.");
                DestroyLobbyUi(player);
                return;
            }

            RemovePlayerFromSideAndQueue(player);
            SaveInventoryIfNeeded(player);

            if (IsSideFull(side))
            {
                QueuePlayer(player, side);
                DestroyLobbyUi(player);
                return;
            }

            playerSides[player.userID] = side;
            var theme = side == TeamSide.A ? CurrentThemeA() : CurrentThemeB();
            SendReply(player, $"You joined Side {side} ({theme.Name}).");
            DestroyLobbyUi(player);
            TryAutoStart();
        }

        private TeamSide GetPlayerSide(ulong userId)
        {
            return playerSides.TryGetValue(userId, out var side) ? side : TeamSide.None;
        }

        private void RemovePlayerFromSideAndQueue(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            var previousSide = RemovePlayerFromSide(player.userID);
            RemoveFromQueues(player.userID);
            TryPromoteQueuedPlayers(previousSide);
        }

        private TeamSide RemovePlayerFromSide(ulong userId)
        {
            if (playerSides.TryGetValue(userId, out var side))
            {
                playerSides.Remove(userId);
                return side;
            }

            return TeamSide.None;
        }

        private void RemoveFromQueues(ulong userId)
        {
            queueSideA.Remove(userId);
            queueSideB.Remove(userId);
        }

        private bool IsSideFull(TeamSide side)
        {
            return GetSideCount(side) >= MaxPlayersPerSide;
        }

        private int GetSideCount(TeamSide side)
        {
            return playerSides.Count(entry => entry.Value == side);
        }

        private void QueuePlayer(BasePlayer player, TeamSide side)
        {
            if (player == null)
            {
                return;
            }

            RemoveFromQueues(player.userID);
            var queue = GetQueue(side);
            if (queue == null)
            {
                return;
            }
            queue.Add(player.userID);
            var queuePosition = queue.Count;
            SendReply(player, $"Side {side} is full. You are position #{queuePosition} in the waiting queue.");
        }

        private bool IsQueuedForSide(ulong userId, TeamSide side)
        {
            var queue = GetQueue(side);
            return queue != null && queue.Contains(userId);
        }

        private List<ulong> GetQueue(TeamSide side)
        {
            if (side == TeamSide.A)
            {
                return queueSideA;
            }

            if (side == TeamSide.B)
            {
                return queueSideB;
            }

            return null;
        }

        private void TryPromoteQueuedPlayers(TeamSide side)
        {
            if (side == TeamSide.None)
            {
                return;
            }

            var queue = GetQueue(side);
            if (queue == null)
            {
                return;
            }
            while (queue.Count > 0 && !IsSideFull(side))
            {
                var userId = queue[0];
                queue.RemoveAt(0);
                var player = BasePlayer.FindByID(userId);
                if (player == null)
                {
                    continue;
                }

                playerSides[userId] = side;
                var theme = side == TeamSide.A ? CurrentThemeA() : CurrentThemeB();
                SendReply(player, $"A slot opened. You joined Side {side} ({theme.Name}).");
                ShowHud(player);
            }

            TryAutoStart();
        }

        private void SaveInventoryIfNeeded(BasePlayer player)
        {
            if (player == null || savedInventories.ContainsKey(player.userID))
            {
                return;
            }

            savedInventories[player.userID] = new InventorySnapshot
            {
                Main = CaptureContainer(player.inventory.containerMain),
                Belt = CaptureContainer(player.inventory.containerBelt),
                Wear = CaptureContainer(player.inventory.containerWear)
            };
        }

        private void RestoreInventoryIfNeeded(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            if (!savedInventories.TryGetValue(player.userID, out var snapshot))
            {
                return;
            }

            savedInventories.Remove(player.userID);
            player.inventory.Strip();
            RestoreContainer(player.inventory.containerWear, snapshot.Wear);
            RestoreContainer(player.inventory.containerBelt, snapshot.Belt);
            RestoreContainer(player.inventory.containerMain, snapshot.Main);
            player.SendNetworkUpdateImmediate();
        }

        private List<ItemSnapshot> CaptureContainer(ItemContainer container)
        {
            var list = new List<ItemSnapshot>();
            if (container == null)
            {
                return list;
            }

            foreach (var item in container.itemList)
            {
                list.Add(CaptureItem(item));
            }

            return list;
        }

        private ItemSnapshot CaptureItem(Item item)
        {
            var snapshot = new ItemSnapshot
            {
                Shortname = item.info.shortname,
                Amount = item.amount,
                Skin = item.skin,
                Condition = item.condition,
                Slot = item.position
            };

            if (item.contents != null && item.contents.itemList != null)
            {
                foreach (var child in item.contents.itemList)
                {
                    snapshot.Contents.Add(CaptureItem(child));
                }
            }

            var projectile = item.GetHeldEntity() as BaseProjectile;
            if (projectile != null && projectile.primaryMagazine != null)
            {
                snapshot.Ammo = projectile.primaryMagazine.contents;
                snapshot.AmmoType = projectile.primaryMagazine.ammoType?.shortname;
            }

            return snapshot;
        }

        private void RestoreContainer(ItemContainer container, List<ItemSnapshot> snapshots)
        {
            if (container == null || snapshots == null)
            {
                return;
            }

            foreach (var snapshot in snapshots)
            {
                RestoreItem(container, snapshot);
            }
        }

        private void RestoreItem(ItemContainer container, ItemSnapshot snapshot)
        {
            var item = ItemManager.CreateByName(snapshot.Shortname, snapshot.Amount, snapshot.Skin);
            if (item == null)
            {
                return;
            }

            if (item.hasCondition)
            {
                item.condition = snapshot.Condition;
            }

            item.MoveToContainer(container, snapshot.Slot);

            if (item.contents != null && snapshot.Contents != null)
            {
                RestoreContainer(item.contents, snapshot.Contents);
            }

            if (snapshot.Ammo > 0)
            {
                var projectile = item.GetHeldEntity() as BaseProjectile;
                if (projectile != null && projectile.primaryMagazine != null)
                {
                    var ammoDefinition = string.IsNullOrEmpty(snapshot.AmmoType)
                        ? projectile.primaryMagazine.ammoType
                        : ItemManager.FindItemDefinition(snapshot.AmmoType);
                    if (ammoDefinition != null)
                    {
                        projectile.primaryMagazine.ammoType = ammoDefinition;
                    }

                    projectile.primaryMagazine.contents = snapshot.Ammo;
                }
            }
        }

        private void StartMatchCountdown(BasePlayer starter, bool forceStart)
        {
            if (matchState != MatchState.Lobby)
            {
                Reply(starter, "A match is already in progress.");
                return;
            }

            if (config.TeamASpawns.Count == 0 || config.TeamBSpawns.Count == 0)
            {
                Reply(starter, "Match spawns are not configured for both teams.");
                return;
            }

            var totalPlayers = GetSideCount(TeamSide.A) + GetSideCount(TeamSide.B);
            if (totalPlayers == 0)
            {
                Reply(starter, "At least one player must join a team to start.");
                return;
            }

            if (!forceStart && (GetSideCount(TeamSide.A) == 0 || GetSideCount(TeamSide.B) == 0))
            {
                Reply(starter, "Both teams need at least one player to start.");
                return;
            }

            matchState = MatchState.Countdown;
            countdownRemaining = MatchCountdownSeconds;
            Broadcast($"Paintball match starts in {countdownRemaining} seconds!");
            RefreshHudForAll();
            countdownTimer?.Destroy();
            countdownTimer = timer.Repeat(1f, MatchCountdownSeconds, () =>
            {
                countdownRemaining--;
                if (countdownRemaining <= 0)
                {
                    countdownTimer?.Destroy();
                    countdownTimer = null;
                    BeginMatch();
                    return;
                }

                Broadcast($"Match starts in {countdownRemaining}...");
                RefreshHudForAll();
            });
        }

        private void TryAutoStart()
        {
            if (!config.AutoStartEnabled || matchState != MatchState.Lobby)
            {
                return;
            }

            if (GetSideCount(TeamSide.A) == 0 || GetSideCount(TeamSide.B) == 0)
            {
                return;
            }

            StartMatchCountdown(null, false);
        }

        private void BeginMatch()
        {
            matchState = MatchState.Live;
            Broadcast($"Paintball match is live! {MatchRuleLabel()}");
            StartRound();
            TeleportQueuedPlayersToSpectator();
            RefreshHudForAll();
        }

        private void EndMatch(TeamSide winner, BasePlayer caller = null)
        {
            if (matchState == MatchState.Lobby)
            {
                Reply(caller, "No match is currently running.");
                return;
            }

            matchState = MatchState.Lobby;
            roundInProgress = false;
            countdownTimer?.Destroy();
            countdownTimer = null;

            var message = winner == TeamSide.None
                ? "Match ended in a draw."
                : $"Match ended. {SideLabel(winner)} wins!";
            Broadcast(message);

            TeleportAllToLobby();
            RestoreAllInventories();
            playerSides.Clear();
            queueSideA.Clear();
            queueSideB.Clear();
            CycleTeamThemes();
            RefreshHudForAll();
        }

        private void StartRound()
        {
            if (matchState != MatchState.Live)
            {
                return;
            }

            roundInProgress = true;
            TeleportSidePlayers(TeamSide.A);
            TeleportSidePlayers(TeamSide.B);
        }

        private void HandlePaintballHit(TeamSide scoringSide, string attackerName, string victimName)
        {
            if (!roundInProgress)
            {
                return;
            }

            roundInProgress = false;
            if (scoringSide == TeamSide.A)
            {
                scoreA++;
            }
            else if (scoringSide == TeamSide.B)
            {
                scoreB++;
            }

            RefreshHudForAll();
            Broadcast($"{attackerName} hit {victimName}. {SideLabel(scoringSide)} scores!");

            if (scoreA >= ScoreLimit || scoreB >= ScoreLimit)
            {
                EndMatch(scoreA >= ScoreLimit ? TeamSide.A : TeamSide.B);
                return;
            }

            Broadcast($"Next round in {FormatSeconds(RoundResetDelaySeconds)}.");
            timer.Once(RoundResetDelaySeconds, StartRound);
        }

        private bool IsPaintballHit(HitInfo info)
        {
            var weaponItem = info.Weapon?.GetItem();
            if (weaponItem == null)
            {
                return false;
            }

            var projectile = weaponItem.GetHeldEntity() as BaseProjectile;
            var ammoType = projectile?.primaryMagazine?.ammoType?.shortname;
            if (string.IsNullOrEmpty(ammoType))
            {
                return false;
            }

            return weaponItem.info.shortname == PaintballGunShortname && ammoType == PaintballAmmoShortname;
        }

        private TeamSide ParseWinner(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return TeamSide.None;
            }

            var value = args[0].ToLowerInvariant();
            if (value == "a" || value == "sidea")
            {
                return TeamSide.A;
            }

            if (value == "b" || value == "sideb")
            {
                return TeamSide.B;
            }

            return TeamSide.None;
        }

        private void TeleportSidePlayers(TeamSide side)
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (GetPlayerSide(player.userID) != side)
                {
                    continue;
                }

                SaveInventoryIfNeeded(player);
                EquipPaintballKit(player);
                TeleportToSideSpawn(player, side);
            }
        }

        private void TeleportToSideSpawn(BasePlayer player, TeamSide side)
        {
            var spawn = GetRandomSpawn(side == TeamSide.A ? config.TeamASpawns : config.TeamBSpawns);
            if (spawn == null)
            {
                return;
            }

            TeleportToSpawn(player, spawn);
        }

        private void TeleportQueuedPlayersToSpectator()
        {
            if (config.SpectatorSpawn == null)
            {
                return;
            }

            foreach (var userId in queueSideA.Concat(queueSideB))
            {
                var player = BasePlayer.FindByID(userId);
                if (player == null)
                {
                    continue;
                }

                TeleportToSpawn(player, config.SpectatorSpawn);
            }
        }

        private void TeleportAllToLobby()
        {
            if (config.LobbySpawn == null)
            {
                return;
            }

            var userIds = new HashSet<ulong>(playerSides.Keys);
            foreach (var userId in queueSideA)
            {
                userIds.Add(userId);
            }

            foreach (var userId in queueSideB)
            {
                userIds.Add(userId);
            }

            foreach (var userId in userIds)
            {
                var player = BasePlayer.FindByID(userId);
                if (player == null)
                {
                    continue;
                }

                TeleportToSpawn(player, config.LobbySpawn);
            }
        }

        private void RestoreAllInventories()
        {
            var userIds = savedInventories.Keys.ToList();
            foreach (var userId in userIds)
            {
                var player = BasePlayer.FindByID(userId);
                if (player == null)
                {
                    continue;
                }

                RestoreInventoryIfNeeded(player);
            }
        }

        private void EquipPaintballKit(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            player.inventory.Strip();
            GiveItem(player, "paintballoveralls.suit", 1, player.inventory.containerWear);
            GiveItem(player, PaintballGunShortname, 1, player.inventory.containerBelt);
            GiveItem(player, PaintballAmmoShortname, PaintballAmmoAmount, player.inventory.containerMain);
            var maxHealth = player.MaxHealth();
            player.SetHealth(maxHealth);
            player.SendNetworkUpdateImmediate();
        }

        private void GiveItem(BasePlayer player, string shortname, int amount, ItemContainer container)
        {
            var definition = ItemManager.FindItemDefinition(shortname);
            if (definition == null)
            {
                PrintWarning($"Item definition not found: {shortname}");
                return;
            }

            var item = ItemManager.Create(definition, amount);
            if (item == null)
            {
                return;
            }

            if (container != null && item.MoveToContainer(container))
            {
                return;
            }

            player.GiveItem(item);
        }

        private SpawnPoint GetRandomSpawn(List<SpawnPoint> spawns)
        {
            if (spawns == null || spawns.Count == 0)
            {
                return null;
            }

            return spawns[UnityEngine.Random.Range(0, spawns.Count)];
        }

        private string SideLabel(TeamSide side)
        {
            return side == TeamSide.A ? "Side A" : "Side B";
        }

        private string MatchRuleLabel()
        {
            return $"First to {ScoreLimit}.";
        }

        private string FormatSeconds(int seconds)
        {
            var suffix = seconds == 1 ? "second" : "seconds";
            return $"{seconds} {suffix}";
        }

        private void TeleportToSpawn(BasePlayer player, SpawnPoint spawn)
        {
            if (player == null || spawn == null)
            {
                return;
            }

            player.Teleport(spawn.Position);
            player.transform.rotation = Quaternion.Euler(spawn.Rotation);
            player.SendNetworkUpdateImmediate();
        }

        private void Broadcast(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                PrintToChat(message);
            }
        }

        private void Reply(BasePlayer player, string message)
        {
            if (player == null)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    PrintWarning(message);
                }

                return;
            }

            SendReply(player, message);
        }

        private void LoadImages()
        {
            if (ImageLibrary == null)
            {
                return;
            }

            AddImageIfSet(config.HudScoreboardBackgroundUrl, HudScoreImageName);
            AddImageIfSet(config.HudLobbyButtonBackgroundUrl, HudLobbyImageName);
            AddImageIfSet(config.LobbyBackgroundUrl, LobbyBackgroundImageName);
            AddImageIfSet(config.AdminBackgroundUrl, AdminBackgroundImageName);
        }

        private void AddImageIfSet(string url, string name)
        {
            if (ImageLibrary == null || string.IsNullOrEmpty(url))
            {
                return;
            }

            ImageLibrary.Call("AddImage", url, name);
        }

        private string GetImage(string name)
        {
            return ImageLibrary?.Call<string>("GetImage", name) ?? string.Empty;
        }

        private void AddBackgroundImage(CuiElementContainer container, string parent, string image)
        {
            if (string.IsNullOrEmpty(image))
            {
                return;
            }

            container.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiRawImageComponent { Png = image, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });
        }

        private void OpenAdminUi(BasePlayer player)
        {
            DestroyAdminUi(player);

            var container = new CuiElementContainer();
            var adminImage = GetImage(AdminBackgroundImageName);
            var adminHasImage = !string.IsNullOrEmpty(adminImage);
            var adminPanel = container.Add(new CuiPanel
            {
                Image = { Color = adminHasImage ? "0 0 0 0.85" : "0.08 0.08 0.08 0.92" },
                RectTransform = { AnchorMin = "0.2 0.2", AnchorMax = "0.8 0.8" },
                CursorEnabled = true
            }, "Overlay", AdminUiName);

            if (adminHasImage)
            {
                AddBackgroundImage(container, adminPanel, adminImage);
            }

            AddLabel(container, adminPanel, "Paintball Arena Admin Setup", "0.25 0.85", "0.75 0.93", 20);
            AddLabel(container, adminPanel, $"Lobby Spawn: {StatusLabel(config.LobbySpawn)}", "0.1 0.75", "0.4 0.8", 14);
            AddLabel(container, adminPanel, $"Spectator Spawn: {StatusLabel(config.SpectatorSpawn)}", "0.6 0.75", "0.9 0.8", 14);
            AddLabel(container, adminPanel, $"Team A Spawns: {config.TeamASpawns.Count}", "0.1 0.68", "0.4 0.73", 14);
            AddLabel(container, adminPanel, $"Team B Spawns: {config.TeamBSpawns.Count}", "0.6 0.68", "0.9 0.73", 14);

            AddButton(container, adminPanel, "Set Lobby Spawn", "paintballarena.setlobby", "0.1 0.55", "0.45 0.63");
            AddButton(container, adminPanel, "Set Spectator Spawn", "paintballarena.setspectator", "0.55 0.55", "0.9 0.63");
            AddButton(container, adminPanel, "Add Team A Spawn", "paintballarena.addteama", "0.1 0.42", "0.45 0.5");
            AddButton(container, adminPanel, "Add Team B Spawn", "paintballarena.addteamb", "0.55 0.42", "0.9 0.5");
            AddButton(container, adminPanel, "Clear Team A Spawns", "paintballarena.clearteama", "0.1 0.29", "0.45 0.37");
            AddButton(container, adminPanel, "Clear Team B Spawns", "paintballarena.clearteamb", "0.55 0.29", "0.9 0.37");
            AddButton(container, adminPanel, "Close", "paintballarena.closeadmin", "0.35 0.12", "0.65 0.2", "0.8 0.2 0.2 0.9");

            CuiHelper.AddUi(player, container);
        }

        private void DestroyAdminUi(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, AdminUiName);
        }

        private string StatusLabel(SpawnPoint spawn)
        {
            return spawn == null ? "Not Set" : "Set";
        }

        private void AddLabel(CuiElementContainer container, string parent, string text, string anchorMin, string anchorMax, int fontSize)
        {
            container.Add(new CuiLabel
            {
                Text = { Text = text, FontSize = fontSize, Align = TextAnchor.MiddleCenter, Color = "1 1 1 0.95" },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent);
        }

        private void AddButton(CuiElementContainer container, string parent, string text, string command, string anchorMin, string anchorMax, string color = "0.2 0.5 0.8 0.9")
        {
            container.Add(new CuiButton
            {
                Button = { Color = color, Command = command },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                Text = { Text = text, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 0.95" }
            }, parent);
        }
    }
}
