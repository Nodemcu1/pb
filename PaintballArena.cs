using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("PaintballArena", "Nodemcu1", "0.1.0")]
    [Description("UI-driven paintball arena foundation and admin setup.")]
    public class PaintballArena : RustPlugin
    {
        private const string AdminPermission = "paintballarena.admin";
        private const string AdminUiName = "PaintballArena.AdminUI";

        private ConfigData config;

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
        }

        private class SpawnPoint
        {
            public Vector3 Position;
            public Vector3 Rotation;

            public SpawnPoint()
            {
            }

            public SpawnPoint(Vector3 position, Vector3 rotation)
            {
                Position = position;
                Rotation = rotation;
            }
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
            catch
            {
                PrintWarning("Failed to read config. Creating new config file.");
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
            }
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
            TrySetLobbySpawn(player);
        }

        [ChatCommand("pbspectator")]
        private void CmdSetSpectator(BasePlayer player, string command, string[] args)
        {
            TrySetSpectatorSpawn(player);
        }

        [ChatCommand("pbteama")]
        private void CmdAddTeamA(BasePlayer player, string command, string[] args)
        {
            TryAddTeamSpawn(player, config.TeamASpawns, "Team A");
        }

        [ChatCommand("pbteamb")]
        private void CmdAddTeamB(BasePlayer player, string command, string[] args)
        {
            TryAddTeamSpawn(player, config.TeamBSpawns, "Team B");
        }

        [ChatCommand("pbcleara")]
        private void CmdClearTeamA(BasePlayer player, string command, string[] args)
        {
            TryClearTeamSpawns(player, config.TeamASpawns, "Team A");
        }

        [ChatCommand("pbclearb")]
        private void CmdClearTeamB(BasePlayer player, string command, string[] args)
        {
            TryClearTeamSpawns(player, config.TeamBSpawns, "Team B");
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

        [ConsoleCommand("paintballarena.setlobby")]
        private void ConsoleSetLobby(ConsoleSystem.Arg arg)
        {
            TrySetLobbySpawn(arg.Player());
        }

        [ConsoleCommand("paintballarena.setspectator")]
        private void ConsoleSetSpectator(ConsoleSystem.Arg arg)
        {
            TrySetSpectatorSpawn(arg.Player());
        }

        [ConsoleCommand("paintballarena.addteama")]
        private void ConsoleAddTeamA(ConsoleSystem.Arg arg)
        {
            TryAddTeamSpawn(arg.Player(), config.TeamASpawns, "Team A");
        }

        [ConsoleCommand("paintballarena.addteamb")]
        private void ConsoleAddTeamB(ConsoleSystem.Arg arg)
        {
            TryAddTeamSpawn(arg.Player(), config.TeamBSpawns, "Team B");
        }

        [ConsoleCommand("paintballarena.clearteama")]
        private void ConsoleClearTeamA(ConsoleSystem.Arg arg)
        {
            TryClearTeamSpawns(arg.Player(), config.TeamASpawns, "Team A");
        }

        [ConsoleCommand("paintballarena.clearteamb")]
        private void ConsoleClearTeamB(ConsoleSystem.Arg arg)
        {
            TryClearTeamSpawns(arg.Player(), config.TeamBSpawns, "Team B");
        }

        private bool HasAdminPermission(BasePlayer player)
        {
            return player != null && permission.UserHasPermission(player.UserIDString, AdminPermission);
        }

        private void TrySetLobbySpawn(BasePlayer player)
        {
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            config.LobbySpawn = CreateSpawnPoint(player);
            SaveConfig();
            SendReply(player, "Lobby spawn set.");
            OpenAdminUi(player);
        }

        private void TrySetSpectatorSpawn(BasePlayer player)
        {
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            config.SpectatorSpawn = CreateSpawnPoint(player);
            SaveConfig();
            SendReply(player, "Spectator spawn set.");
            OpenAdminUi(player);
        }

        private void TryAddTeamSpawn(BasePlayer player, List<SpawnPoint> list, string label)
        {
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

            list.Add(CreateSpawnPoint(player));
            SaveConfig();
            SendReply(player, $"{label} spawn added. ({list.Count})");
            OpenAdminUi(player);
        }

        private void TryClearTeamSpawns(BasePlayer player, List<SpawnPoint> list, string label)
        {
            if (!EnsureAdminPlayer(player))
            {
                return;
            }

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

        private void OpenAdminUi(BasePlayer player)
        {
            DestroyAdminUi(player);

            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                Image = { Color = "0.08 0.08 0.08 0.92" },
                RectTransform = { AnchorMin = "0.2 0.2", AnchorMax = "0.8 0.8" },
                CursorEnabled = true
            }, "Overlay", AdminUiName);

            AddLabel(container, AdminUiName, "Paintball Arena Admin Setup", "0.25 0.85", "0.75 0.93", 20);
            AddLabel(container, AdminUiName, $"Lobby Spawn: {StatusLabel(config.LobbySpawn)}", "0.1 0.75", "0.4 0.8", 14);
            AddLabel(container, AdminUiName, $"Spectator Spawn: {StatusLabel(config.SpectatorSpawn)}", "0.6 0.75", "0.9 0.8", 14);
            AddLabel(container, AdminUiName, $"Team A Spawns: {config.TeamASpawns.Count}", "0.1 0.68", "0.4 0.73", 14);
            AddLabel(container, AdminUiName, $"Team B Spawns: {config.TeamBSpawns.Count}", "0.6 0.68", "0.9 0.73", 14);

            AddButton(container, AdminUiName, "Set Lobby Spawn", "paintballarena.setlobby", "0.1 0.55", "0.45 0.63");
            AddButton(container, AdminUiName, "Set Spectator Spawn", "paintballarena.setspectator", "0.55 0.55", "0.9 0.63");
            AddButton(container, AdminUiName, "Add Team A Spawn", "paintballarena.addteama", "0.1 0.42", "0.45 0.5");
            AddButton(container, AdminUiName, "Add Team B Spawn", "paintballarena.addteamb", "0.55 0.42", "0.9 0.5");
            AddButton(container, AdminUiName, "Clear Team A Spawns", "paintballarena.clearteama", "0.1 0.29", "0.45 0.37");
            AddButton(container, AdminUiName, "Clear Team B Spawns", "paintballarena.clearteamb", "0.55 0.29", "0.9 0.37");
            AddButton(container, AdminUiName, "Close", "paintballarena.closeadmin", "0.35 0.12", "0.65 0.2", "0.8 0.2 0.2 0.9");

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
