﻿using EvoS.Framework.Constants.Enums;
using EvoS.Framework.Game;
using EvoS.Framework.Logging;
using EvoS.Framework.Network.NetworkMessages;
using EvoS.Framework.Network.Static;
using EvoS.LobbyServer.LobbyQueue;
using EvoS.LobbyServer.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EvoS.LobbyServer
{
    class LobbyQueueManager
    {
        private static LobbyQueueManager Instance = null;
        
        private Dictionary<GameType, LobbyQueue.LobbyQueue> Queues = new Dictionary<GameType, LobbyQueue.LobbyQueue>();
        private List<PendingGame> PendingGames = new List<PendingGame>();
        private Timer UpdateInterval;

        private static long currentMatchId = 0;

        protected class PendingGame {
            public LobbyGameInfo GameInfo;
            public LobbyTeamInfo TeamInfo;
            public List<long> PlayerSessionTokens;
            public String Name => GameInfo.GameConfig.RoomName;
            public GameStatus GameStatus
            {
                get { return GameInfo.GameStatus; }
                set { GameInfo.GameStatus = value; }
            }
            public GameType GameType => GameInfo.GameConfig.GameType;

            public PendingGame(LobbyGameInfo gameInfo, LobbyTeamInfo teamInfo)
            {
                GameInfo = gameInfo;
                TeamInfo = teamInfo;
                PlayerSessionTokens = new List<long>();
                foreach(LobbyPlayerInfo playerInfo in TeamInfo.TeamPlayerInfo){
                    if (!playerInfo.IsNPCBot) {
                        LobbyServerConnection player = LobbyServer.GetPlayerByAccountId(playerInfo.AccountId);
                        PlayerSessionTokens.Add(player.SessionToken);
                    }
                }
            }

            public void SendNotification()
            {
                foreach (LobbyPlayerInfo playerInfo in TeamInfo.TeamPlayerInfo)
                {
                    if (!playerInfo.IsNPCBot)
                    {
                        LobbyServerConnection player = LobbyServer.GetPlayerByAccountId(playerInfo.AccountId);
                        LobbyTeamInfo teamInfoClone = TeamInfo.Clone();
                        foreach (LobbyPlayerInfo pi in teamInfoClone.TeamPlayerInfo) {
                            if(pi.PlayerId == playerInfo.PlayerId){
                                pi.ControllingPlayerId = 0;
                            }
                        }
                        LobbyPlayerInfo playerInfoClone = playerInfo.Clone();
                        playerInfoClone.ControllingPlayerId = 0;
                        //Log.Print(LogType.Debug, $"Sending notification to {Players[i]}");
                        GameInfoNotification gameInfoNotification = new GameInfoNotification()
                        {
                            GameInfo = GameInfo,
                            PlayerInfo = playerInfoClone,
                            TeamInfo = teamInfoClone
                        };
                        _ = player.SendMessage(gameInfoNotification);
                    }
                }
            }
        }

        private LobbyQueueManager() {
            CreatePracticeQueue();
            CreateCoopQueue();
            CreatePvPQueue();
            CreateRankedQueue();
            CreateCustomQueue();

            UpdateInterval = new Timer( new TimerCallback(Update), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }
        

        public static LobbyQueueManager GetInstance() {
            if (Instance == null) {
                Instance = new LobbyQueueManager();
            }
            return Instance;
        }

        private LobbyQueue.LobbyQueue FindQueue(GameType gameType)
        {
            try
            {
                return Queues[gameType];
            }
            catch (KeyNotFoundException)
            {
                throw new LobbyQueueExceptions.LobbyQueueNotFoundException();
            }
            
        }

        public static void AddPlayerToQueue(LobbyServerConnection client)
        {
            //LobbyQueue.LobbyQueue queue = LobbyQueueManager.GetInstance().FindQueue(client.PlayerInfo.GetGameType());
            //queue.AddPlayer(client);
            /*
            if (client.SelectedGameType == GameType.Practice)
            {
                StartPracticeGame(client);
            }
            else
            {
                if (client.SelectedGameType == GameType.Coop && client.SelectedSubTypeMask == GameTypesUtils.SubTypeMasks.SoloVsBots)
                {
                    Log.Print(LogType.Debug, "AI TEAMMATES");
                }
                AddPlayerToQueue(client);

                QueuedPlayers[client.SelectedGameType] += 1;
                Instance.Queue.Add(client);

                MatchmakingQueueAssignmentNotification assignmentNotification = new MatchmakingQueueAssignmentNotification()
                {
                    Reason = "",
                    MatchmakingQueueInfo = new LobbyMatchmakingQueueInfo()
                    {
                        ShowQueueSize = true,
                        QueuedPlayers = QueuedPlayers[client.SelectedGameType],
                        PlayersPerMinute = 1,
                        GameConfig = new LobbyGameConfig(),
                        QueueStatus = QueueStatus.Success,
                        AverageWaitTime = TimeSpan.FromSeconds(100),
                        GameType = client.SelectedGameType
                    }
                };

                client.SendMessage(assignmentNotification).Wait();

                ProcessMatchmaking();
            }
            /*
            */
        }

        public static void RemovePlayerFromQueue(LobbyServerConnection client)
        {
            //LobbyQueue.LobbyQueue queue = LobbyQueueManager.GetInstance().FindQueue(client.PlayerInfo.GetGameType());
            //queue.RemovePlayer(client);
        }

        private void StartGame(PendingGame game)
        {
            game.GameInfo.GameServerProcessCode = "LobbyQueueManager.StartGame";
            foreach(LobbyPlayerInfo playerInfo in game.TeamInfo.TeamPlayerInfo) {
                if (!playerInfo.IsNPCBot) {
                    LobbyServerConnection player = LobbyServer.GetPlayerByAccountId(playerInfo.AccountId);
                    GameAssignmentNotification assNotification = new GameAssignmentNotification
                    {
                        GameInfo = game.GameInfo,
                        GameResult = GameResult.NoResult,
                        GameplayOverrides = DummyLobbyData.CreateLobbyGameplayOverrides(),
                        PlayerInfo = player.GetLobbyPlayerInfo().Clone(),
                        Reconnection = false,
                        Observer = false
                    };
                    assNotification.PlayerInfo.ControllingPlayerId = 0;
                    player.SendMessage(assNotification); ;
                }
            }
            game.GameStatus = GameStatus.Launching; // Put in wait state until game server starts
            game.SendNotification();

            new Task(() => {
                //GameManagerHolder.CreateGameManager(game.GameInfo, game.TeamInfo, game.PlayerSessionTokens); // Launch new game
                game.GameStatus = GameStatus.Launched; // Put in wait state until game server starts
                game.SendNotification();

                PendingGames.Remove(game);

            }).Start();
        }

        private void Update(object state)
        {
            foreach (PendingGame game in PendingGames)
            {
                switch (game.GameStatus)
                {
                    case GameStatus.Assembling:
                        if (game.GameType == GameType.Practice)
                        {
                            StartGame(game);
                        }
                        else
                        {
                            // TODO: maybe this is FreelancerSelecting
                            game.GameStatus = GameStatus.LoadoutSelecting;
                            game.SendNotification();

                            new Task(() => { 
                                Thread.Sleep(game.GameInfo.LoadoutSelectTimeout);
                                StartGame(game);
                            })
                            .Start();
                        }
                        break;
                }
            }
        }

        
        /// <summary>
        /// Creates a new game and puts all players in the pending games queue
        /// </summary>
        /// <param name="gameInfo"></param>
        /// <param name="teamInfo"></param>
        public static void CreateGame(LobbyGameInfo gameInfo, LobbyTeamInfo teamInfo)
        {
            Log.Print(LogType.Debug, "Creating Game for playes:");
            foreach (var a in teamInfo.TeamPlayerInfo) {
                Log.Print(LogType.Debug, $"   player {a.Handle} in team {a.TeamId.ToString()}");
            }
            

            gameInfo.GameStatus = GameStatus.Assembling;
            LobbyQueueManager.GetInstance().PendingGames.Add(new PendingGame(gameInfo, teamInfo));
        }

        private void CreatePracticeQueue()
        {
            LobbyGameConfig gameConfig = new LobbyGameConfig()
            {
                GameType = GameType.Practice,
                IsActive = false,
                GameOptionFlags = GameOptionFlag.EnableTeamAIOutput | GameOptionFlag.NoInputIdleDisconnect,
                Spectators = 0,
                TeamAPlayers = 1,
                TeamABots = 0,
                TeamBPlayers = 0,
                TeamBBots = 2,
                ResolveTimeoutLimit = 160,
                RoomName = "default",
                Map = Maps.Skyway_Deathmatch,
                SubTypes = new List<GameSubType>
                {
                    new GameSubType
                    {
                        DuplicationRule = FreelancerDuplicationRuleTypes.noneInTeam,
                        GameMapConfigs = new List<GameMapConfig> { new GameMapConfig(Maps.Skyway_Deathmatch) },
                        InstructionsToDisplay = GameSubType.GameLoadScreenInstructions.Default,
                        LocalizedName = "GenericPractice@SubTypes",
                        PersistedStatBucket = PersistedStatBucket.Deathmatch_Unranked,
                        RewardBucket = GameBalanceVars.GameRewardBucketType.NoRewards,
                        RoleBalancingRule = FreelancerRoleBalancingRuleTypes.none,
                        TeamAPlayers = 1,
                        TeamABots = 0,
                        TeamBPlayers = 0,
                        TeamBBots = 2,
                        Mods = new List<GameSubType.SubTypeMods>
                        {
                            GameSubType.SubTypeMods.AllowPlayingLockedCharacters,
                            GameSubType.SubTypeMods.HumansHaveFirstSlots,
                            GameSubType.SubTypeMods.NotAllowedForGroups
                        },
                        TeamComposition = new TeamCompositionRules
                        {
                            Rules = new Dictionary<TeamCompositionRules.SlotTypes, FreelancerSet>
                            {
                                {
                                    TeamCompositionRules.SlotTypes.A1,
                                    FreelancerSet.AllRoles
                                },
                                {
                                    TeamCompositionRules.SlotTypes.B1,
                                    new FreelancerSet{ Types = new List<CharacterType> {CharacterType.PunchingDummy} }
                                },
                                {
                                    TeamCompositionRules.SlotTypes.B2,
                                    new FreelancerSet{ Types = new List<CharacterType> {CharacterType.PunchingDummy} }
                                }
                            }
                        }
                    }
                }
                
            };
            LobbyQueue.LobbyQueue queue = new LobbyQueue.LobbyQueue(GameType.Practice, gameConfig);
            Queues.Add(GameType.Practice, queue);
        }

        private void CreateCoopQueue()
        {
            LobbyGameConfig gameConfig = new LobbyGameConfig()
            {
                GameType = GameType.Coop,
                IsActive = false,
                GameOptionFlags = GameOptionFlag.EnableTeamAIOutput | GameOptionFlag.ReplaceHumansWithBots,
                Spectators = 0,
                TeamAPlayers = 4,
                TeamABots = 0,
                TeamBPlayers = 4,
                TeamBBots = 0,
                ResolveTimeoutLimit = 160,
                RoomName = "default",
                Map = String.Empty,
                SubTypes = new List<GameSubType>
                {
                    new GameSubType
                    {
                        DuplicationRule = FreelancerDuplicationRuleTypes.noneInTeam,
                        GameMapConfigs = GameMapConfig.GetDeatmatchMaps(),
                        InstructionsToDisplay = GameSubType.GameLoadScreenInstructions.Default,
                        LocalizedName = "GenericPvE@SubTypes",
                        PersistedStatBucket = PersistedStatBucket.Deathmatch_Unranked,
                        RewardBucket = GameBalanceVars.GameRewardBucketType.HumanVsBotsRewards,
                        RoleBalancingRule = FreelancerRoleBalancingRuleTypes.balanceBothTeams,
                        TeamAPlayers = 4,
                        TeamABots = 3,
                        TeamBPlayers = 0,
                        TeamBBots = 4,
                        Mods = new List<GameSubType.SubTypeMods>
                        {
                            GameSubType.SubTypeMods.HumansHaveFirstSlots,
                            GameSubType.SubTypeMods.ShowWithAITeammates
                        },
                        TeamComposition = new TeamCompositionRules
                        {
                            Rules = new Dictionary<TeamCompositionRules.SlotTypes, FreelancerSet>
                            {
                                {
                                    TeamCompositionRules.SlotTypes.TeamA,
                                    new FreelancerSet
                                    {
                                        Roles = new List<CharacterRole> { CharacterRole.Assassin, CharacterRole.Tank, CharacterRole.Support },
                                    }
                                },
                                {
                                    TeamCompositionRules.SlotTypes.TeamB,
                                    new FreelancerSet
                                    {
                                        Roles = new List<CharacterRole> { CharacterRole.Assassin, CharacterRole.Tank, CharacterRole.Support }
                                    }
                                }
                            }
                        }
                    }
                }
            };
            LobbyQueue.LobbyQueue queue = new LobbyQueue.LobbyQueue(GameType.Coop, gameConfig);
            Queues.Add(GameType.Coop, queue);
        }

        private void CreatePvPQueue()
        {
            LobbyGameConfig gameConfig = new LobbyGameConfig()
            {
                GameType = GameType.PvP,
                IsActive = true,
                GameOptionFlags = GameOptionFlag.EnableTeamAIOutput | GameOptionFlag.ReplaceHumansWithBots,
                Spectators = 0,
                TeamAPlayers = 1,
                TeamABots = 0,
                TeamBPlayers = 1,
                TeamBBots = 0,
                ResolveTimeoutLimit = 160,
                RoomName = "default",
                Map = String.Empty,
                SubTypes = new List<GameSubType>
                {
                    new GameSubType
                    {
                        DuplicationRule = FreelancerDuplicationRuleTypes.noneInTeam,
                        GameMapConfigs = GameMapConfig.GetDeatmatchMaps(),
                        InstructionsToDisplay = GameSubType.GameLoadScreenInstructions.Default,
                        LocalizedName = "GenericPvP@SubTypes",
                        PersistedStatBucket = PersistedStatBucket.Deathmatch_Unranked,
                        RewardBucket = GameBalanceVars.GameRewardBucketType.HumanVsBotsRewards,
                        RoleBalancingRule = FreelancerRoleBalancingRuleTypes.balanceBothTeams,
                        TeamAPlayers = 1,
                        TeamABots = 0,
                        TeamBPlayers = 1,
                        TeamBBots = 0,
                        Mods = new List<GameSubType.SubTypeMods>
                        {
                            GameSubType.SubTypeMods.HumansHaveFirstSlots,
                            //GameSubType.SubTypeMods.ShowWithAITeammates
                        },
                        TeamComposition = new TeamCompositionRules
                        {
                            Rules = new Dictionary<TeamCompositionRules.SlotTypes, FreelancerSet>
                            {
                                {
                                    TeamCompositionRules.SlotTypes.TeamA,
                                    new FreelancerSet
                                    {
                                        Roles = new List<CharacterRole> { CharacterRole.Assassin, CharacterRole.Tank, CharacterRole.Support },
                                    }
                                },
                                {
                                    TeamCompositionRules.SlotTypes.TeamB,
                                    new FreelancerSet
                                    {
                                        Roles = new List<CharacterRole> { CharacterRole.Assassin, CharacterRole.Tank, CharacterRole.Support }
                                    }
                                }
                            }
                        }
                    }
                }
            };
            LobbyQueue.LobbyQueue queue = new LobbyQueue.LobbyQueue(GameType.PvP, gameConfig);
            Queues.Add(GameType.PvP, queue);
        }

        private void CreateRankedQueue()
        {
        }

        private void CreateCustomQueue()
        {
        }

        /*private static void StartPracticeGame(ClientConnection connection) {
            Log.Print(LogType.Debug, "StartPracticeGame");

            //LobbyGameConfig lobbyGameConfig = CreatePracticeLobbyGameConfig();

            LobbyPlayerInfo playerInfo = connection.CreateLobbyPlayerInfo();
            playerInfo.PlayerId = 1;
            playerInfo.ControllingPlayerId = 1;
            playerInfo.TeamId = Team.TeamA;
            playerInfo.ReadyState = ReadyState.Ready;


            GameAssignmentNotification notification = new GameAssignmentNotification();

            notification.GameResult = GameResult.NoResult;
            notification.Observer = false;
            notification.Reconnection = false;
            notification.GameInfo = CreatePracticeLobbyGameInfo();
            notification.GameplayOverrides = DummyLobbyData.CreateLobbyGameplayOverrides();
            notification.PlayerInfo = playerInfo;
            
            //Log.Print(LogType.Network, $"Responding {JsonConvert.SerializeObject(practice)}");
            _ = connection.SendMessage(notification);

            GameInfoNotification gameInfoNotification = new GameInfoNotification
            {
                GameInfo = CreatePracticeLobbyGameInfo(),
                PlayerInfo = playerInfo,
                TeamInfo = new LobbyTeamInfo
                {
                    TeamPlayerInfo = new List<LobbyPlayerInfo>{ playerInfo }
                }
            };
            gameInfoNotification.GameInfo.GameServerAddress = "ws://127.0.0.1:6061";
            gameInfoNotification.GameInfo.GameStatus = GameStatus.Launched;
            gameInfoNotification.GameInfo.GameServerHost = "Practice Game Host";

            //var practiceGameInfo = DummyLobbyData.CreatePracticeGameInfoNotification(connection);
            //Log.Print(LogType.Network, $"Responding {JsonConvert.SerializeObject(practiceGameInfo)}");
            _ = connection.SendMessage(gameInfoNotification);
            return;
        }

        private static LobbyGameInfo CreatePracticeLobbyGameInfo()
        {
            return new LobbyGameInfo
            {
                AcceptTimeout = TimeSpan.Zero,
                GameResult = GameResult.NoResult,
                GameServerAddress = null,
                GameServerHost = null,
                GameStatus = GameStatus.Launching,
                GameServerProcessCode = "LeProcessCode",
                MonitorServerProcessCode = "",
                LoadoutSelectTimeout = TimeSpan.FromMinutes(1),
                SelectSubPhaseBan1Timeout = TimeSpan.FromMinutes(1),
                SelectSubPhaseBan2Timeout = TimeSpan.FromSeconds(30),
                SelectSubPhaseFreelancerSelectTimeout = TimeSpan.FromSeconds(30),
                SelectSubPhaseTradeTimeout = TimeSpan.FromSeconds(30),
                GameConfig = CreatePracticeLobbyGameConfig()
            };
        }

        private static LobbyGameConfig CreatePracticeLobbyGameConfig() {
            GameTypeAvailability practiceGameType = GameTypesUtils.GetPracticeGameTypeAvailability();
            return new LobbyGameConfig
            {
                GameOptionFlags = GameOptionFlag.AutoLaunch | GameOptionFlag.NoInputIdleDisconnect | GameOptionFlag.EnableTeamAIOutput,
                GameType = GameType.Practice,
                InstanceSubTypeBit = 1,
                IsActive = true,
                ResolveTimeoutLimit = 160,
                TeamAPlayers = 1,
                Map = practiceGameType.SubTypes[0].GameMapConfigs[0].Map,
                SubTypes = practiceGameType.SubTypes,
                RoomName = GenerateRoomName()
            };
        }

        private async static Task SendNotification()
        {
            foreach (var client in Instance.Queue)
            {
                MatchmakingQueueAssignmentNotification notification = new MatchmakingQueueAssignmentNotification()
                {
                    Reason = "",
                    MatchmakingQueueInfo = new LobbyMatchmakingQueueInfo()
                    {
                        ShowQueueSize = true,
                        QueuedPlayers = QueuedPlayers[client.SelectedGameType],
                        AverageWaitTime = TimeSpan.FromSeconds(100),
                        PlayersPerMinute = 0,
                        GameConfig = new LobbyGameConfig(),
                        QueueStatus = QueueStatus.QueueDoesntHaveEnoughHumans
                    }
                };

                await client.SendMessage(notification);
            }
        }*/
    }
}
