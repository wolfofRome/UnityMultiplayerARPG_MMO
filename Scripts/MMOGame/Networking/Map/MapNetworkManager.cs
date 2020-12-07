﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;
using LiteNetLibManager;
using Cysharp.Threading.Tasks;
using System;

namespace MultiplayerARPG.MMO
{
    public sealed partial class MapNetworkManager : BaseGameNetworkManager, IAppServer
    {
        public const float TERMINATE_INSTANCE_DELAY = 30f;  // Close instance when no clients connected within 30 seconds

        public struct PendingSpawnPlayerCharacter
        {
            public long connectionId;
            public string userId;
            public string selectCharacterId;
        }

        public struct InstanceMapWarpingLocation
        {
            public string mapName;
            public Vector3 position;
            public bool overrideRotation;
            public Vector3 rotation;
        }

        /// <summary>
        /// If this is not empty it mean this is temporary instance map
        /// So it won't have to save current map, current position to database
        /// </summary>
        public string MapInstanceId { get; set; }
        public Vector3 MapInstanceWarpToPosition { get; set; }
        public bool MapInstanceWarpOverrideRotation { get; set; }
        public Vector3 MapInstanceWarpToRotation { get; set; }

        [Header("Central Network Connection")]
        public BaseTransportFactory centralTransportFactory;
        public string centralNetworkAddress = "127.0.0.1";
        public int centralNetworkPort = 6000;
        public string machineAddress = "127.0.0.1";

        [Header("Database")]
        public float autoSaveDuration = 2f;

        [Header("Map Spawn")]
        public long mapSpawnDuration = 0;

        public Action onClientConnected;
        public Action<DisconnectInfo> onClientDisconnected;

        private float terminatingTime;

        public BaseTransportFactory CentralTransportFactory
        {
            get { return centralTransportFactory; }
        }

#if UNITY_STANDALONE && !CLIENT_BUILD
        public CentralAppServerRegister CentralAppServerRegister { get; private set; }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        public ChatNetworkManager ChatNetworkManager { get; private set; }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        public DatabaseService.DatabaseServiceClient DbServiceClient
        {
            get { return MMOServerInstance.Singleton.DatabaseNetworkManager.ServiceClient; }
        }
#endif

        public string CentralNetworkAddress { get { return centralNetworkAddress; } }
        public int CentralNetworkPort { get { return centralNetworkPort; } }
        public string AppAddress { get { return machineAddress; } }
        public int AppPort { get { return networkPort; } }
        public string AppExtra
        {
            get
            {
                if (IsInstanceMap())
                    return MapInstanceId;
                return CurrentMapInfo.Id;
            }
        }
        public CentralServerPeerType PeerType
        {
            get
            {
                if (IsInstanceMap())
                    return CentralServerPeerType.InstanceMapServer;
                return CentralServerPeerType.MapServer;
            }
        }
        private float lastSaveTime;
        // Listing
#if UNITY_STANDALONE && !CLIENT_BUILD
        private readonly List<PendingSpawnPlayerCharacter> pendingSpawnPlayerCharacters = new List<PendingSpawnPlayerCharacter>();
        private readonly Dictionary<uint, KeyValuePair<string, Vector3>> instanceMapCurrentLocations = new Dictionary<uint, KeyValuePair<string, Vector3>>();
        private readonly Dictionary<string, CentralServerPeerInfo> mapServerConnectionIdsBySceneName = new Dictionary<string, CentralServerPeerInfo>();
        private readonly Dictionary<string, CentralServerPeerInfo> instanceMapServerConnectionIdsByInstanceId = new Dictionary<string, CentralServerPeerInfo>();
        private readonly Dictionary<string, HashSet<uint>> instanceMapWarpingCharactersByInstanceId = new Dictionary<string, HashSet<uint>>();
        private readonly Dictionary<string, InstanceMapWarpingLocation> instanceMapWarpingLocations = new Dictionary<string, InstanceMapWarpingLocation>();
        private readonly Dictionary<string, SocialCharacterData> usersById = new Dictionary<string, SocialCharacterData>();
        private readonly Dictionary<StorageId, List<CharacterItem>> allStorageItems = new Dictionary<StorageId, List<CharacterItem>>();
        private readonly Dictionary<StorageId, HashSet<uint>> usingStorageCharacters = new Dictionary<StorageId, HashSet<uint>>();
        // Database operations
        private readonly HashSet<StorageId> loadingStorageIds = new HashSet<StorageId>();
        private readonly HashSet<int> loadingPartyIds = new HashSet<int>();
        private readonly HashSet<int> loadingGuildIds = new HashSet<int>();
        private readonly HashSet<string> savingCharacters = new HashSet<string>();
        private readonly HashSet<string> savingBuildings = new HashSet<string>();
#endif

        protected override void Awake()
        {
            base.Awake();
            if (useWebSocket)
            {
                if (centralTransportFactory == null || !centralTransportFactory.CanUseWithWebGL)
                    centralTransportFactory = gameObject.AddComponent<WebSocketTransportFactory>();
            }
            else
            {
                if (centralTransportFactory == null)
                    centralTransportFactory = gameObject.AddComponent<LiteNetLibTransportFactory>();
            }
#if UNITY_STANDALONE && !CLIENT_BUILD
            CentralAppServerRegister = new CentralAppServerRegister(CentralTransportFactory.Build(), this);
            CentralAppServerRegister.onAppServerRegistered = OnAppServerRegistered;
            CentralAppServerRegister.RegisterMessage(MMOMessageTypes.AppServerAddress, HandleResponseAppServerAddress);
            CentralAppServerRegister.RegisterResponse<RequestSpawnMapMessage, ResponseSpawnMapMessage>(MMORequestTypes.RequestSpawnMap);
            this.InvokeInstanceDevExtMethods("OnInitCentralAppServerRegister");
            ChatNetworkManager = gameObject.AddComponent<ChatNetworkManager>();
#endif
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
#if UNITY_STANDALONE && !CLIENT_BUILD
            float tempUnscaledTime = Time.unscaledTime;
            if (IsServer)
            {
                CentralAppServerRegister.Update();
                if (tempUnscaledTime - lastSaveTime > autoSaveDuration)
                {
                    lastSaveTime = tempUnscaledTime;
                    SaveCharactersRoutine().Forget();
                    if (!IsInstanceMap())
                    {
                        // Don't save building if it's instance map
                        SaveBuildingsRoutine().Forget();
                    }
                }
                if (IsInstanceMap())
                {
                    // Quitting application when no players
                    if (Players.Count > 0)
                        terminatingTime = tempUnscaledTime;
                    else if (tempUnscaledTime - terminatingTime >= TERMINATE_INSTANCE_DELAY)
                        Application.Quit();
                }

                if (pendingSpawnPlayerCharacters.Count > 0 && IsReadyToInstantiateObjects())
                {
                    // Spawn pending player characters
                    LiteNetLibPlayer player;
                    foreach (PendingSpawnPlayerCharacter spawnPlayerCharacter in pendingSpawnPlayerCharacters)
                    {
                        if (!Players.TryGetValue(spawnPlayerCharacter.connectionId, out player))
                            continue;
                        player.IsReady = true;
                        SetPlayerReadyRoutine(spawnPlayerCharacter.connectionId, spawnPlayerCharacter.userId, spawnPlayerCharacter.selectCharacterId).Forget();
                    }
                    pendingSpawnPlayerCharacters.Clear();
                }
            }
#endif
        }

        protected override void Clean()
        {
            base.Clean();
#if UNITY_STANDALONE && !CLIENT_BUILD
            instanceMapCurrentLocations.Clear();
            mapServerConnectionIdsBySceneName.Clear();
            instanceMapServerConnectionIdsByInstanceId.Clear();
            instanceMapWarpingCharactersByInstanceId.Clear();
            instanceMapWarpingLocations.Clear();
            usersById.Clear();
            allStorageItems.Clear();
            usingStorageCharacters.Clear();
            loadingStorageIds.Clear();
            loadingPartyIds.Clear();
            loadingGuildIds.Clear();
            savingCharacters.Clear();
            savingBuildings.Clear();
#endif
        }

        protected override void UpdateOnlineCharacter(BasePlayerCharacterEntity playerCharacterEntity)
        {
            base.UpdateOnlineCharacter(playerCharacterEntity);
#if UNITY_STANDALONE && !CLIENT_BUILD
            SocialCharacterData tempUserData;
            if (ChatNetworkManager.IsClientConnected && usersById.TryGetValue(playerCharacterEntity.Id, out tempUserData))
            {
                tempUserData.dataId = playerCharacterEntity.DataId;
                tempUserData.level = playerCharacterEntity.Level;
                tempUserData.currentHp = playerCharacterEntity.CurrentHp;
                tempUserData.maxHp = playerCharacterEntity.MaxHp;
                tempUserData.currentMp = playerCharacterEntity.CurrentMp;
                tempUserData.maxMp = playerCharacterEntity.MaxMp;
                tempUserData.partyId = playerCharacterEntity.PartyId;
                tempUserData.guildId = playerCharacterEntity.GuildId;
                usersById[playerCharacterEntity.Id] = tempUserData;
                UpdateMapUser(ChatNetworkManager.Client, UpdateUserCharacterMessage.UpdateType.Online, tempUserData);
            }
#endif
        }

        protected override void OnDestroy()
        {
            // Save immediately
#if UNITY_STANDALONE && !CLIENT_BUILD
            if (IsServer)
            {
                foreach (BasePlayerCharacterEntity playerCharacter in playerCharacters.Values)
                {
                    if (playerCharacter == null) continue;
                    DbServiceClient.UpdateCharacter(new UpdateCharacterReq()
                    {
                        CharacterData = playerCharacter.CloneTo(new PlayerCharacterData()).ToByteString()
                    });
                }
                string mapName = CurrentMapInfo.Id;
                foreach (BuildingEntity buildingEntity in buildingEntities.Values)
                {
                    if (buildingEntity == null) continue;
                    DbServiceClient.UpdateBuilding(new UpdateBuildingReq()
                    {
                        MapName = mapName,
                        BuildingData = buildingEntity.CloneTo(new BuildingSaveData()).ToByteString()
                    });
                }
            }
#endif
            base.OnDestroy();
        }

#if UNITY_STANDALONE && !CLIENT_BUILD
        public override void RegisterPlayerCharacter(BasePlayerCharacterEntity playerCharacterEntity)
        {
            // Set user data to map server
            if (!usersById.ContainsKey(playerCharacterEntity.Id))
            {
                SocialCharacterData userData = new SocialCharacterData();
                userData.userId = playerCharacterEntity.UserId;
                userData.id = playerCharacterEntity.Id;
                userData.characterName = playerCharacterEntity.CharacterName;
                userData.dataId = playerCharacterEntity.DataId;
                userData.level = playerCharacterEntity.Level;
                userData.currentHp = playerCharacterEntity.CurrentHp;
                userData.maxHp = playerCharacterEntity.MaxHp;
                userData.currentMp = playerCharacterEntity.CurrentMp;
                userData.maxMp = playerCharacterEntity.MaxMp;
                usersById.Add(userData.id, userData);
                // Add map user to central server and chat server
                UpdateMapUser(CentralAppServerRegister, UpdateUserCharacterMessage.UpdateType.Add, userData);
                if (ChatNetworkManager.IsClientConnected)
                    UpdateMapUser(ChatNetworkManager.Client, UpdateUserCharacterMessage.UpdateType.Add, userData);
            }
            base.RegisterPlayerCharacter(playerCharacterEntity);
        }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        public override void UnregisterPlayerCharacter(long connectionId)
        {
            // Send remove character from map server
            BasePlayerCharacterEntity playerCharacter;
            SocialCharacterData userData;
            if (playerCharacters.TryGetValue(connectionId, out playerCharacter) &&
                usersById.TryGetValue(playerCharacter.Id, out userData))
            {
                usersById.Remove(playerCharacter.Id);
                // Remove map user from central server and chat server
                UpdateMapUser(CentralAppServerRegister, UpdateUserCharacterMessage.UpdateType.Remove, userData);
                if (ChatNetworkManager.IsClientConnected)
                    UpdateMapUser(ChatNetworkManager.Client, UpdateUserCharacterMessage.UpdateType.Remove, userData);
            }
            base.UnregisterPlayerCharacter(connectionId);
        }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        public override void OnPeerDisconnected(long connectionId, DisconnectInfo disconnectInfo)
        {
            OnPeerDisconnectedRoutine(connectionId, disconnectInfo).Forget();
        }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        private async UniTaskVoid OnPeerDisconnectedRoutine(long connectionId, DisconnectInfo disconnectInfo)
        {
            // Save player character data
            BasePlayerCharacterEntity playerCharacterEntity;
            if (playerCharacters.TryGetValue(connectionId, out playerCharacterEntity))
            {
                PlayerCharacterData saveCharacterData = playerCharacterEntity.CloneTo(new PlayerCharacterData());
                while (savingCharacters.Contains(saveCharacterData.Id))
                {
                    await UniTask.Yield();
                }
                await SaveCharacterRoutine(saveCharacterData, playerCharacterEntity.UserId);
            }
            UnregisterPlayerCharacter(connectionId);
            base.OnPeerDisconnected(connectionId, disconnectInfo);
        }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        public override void OnStopServer()
        {
            base.OnStopServer();
            CentralAppServerRegister.OnStopServer();
            if (ChatNetworkManager.IsClientConnected)
                ChatNetworkManager.StopClient();
        }
#endif

        public override void OnClientConnected()
        {
            base.OnClientConnected();
            if (onClientConnected != null)
                onClientConnected.Invoke();
        }

        public override void OnClientDisconnected(DisconnectInfo disconnectInfo)
        {
            base.OnClientDisconnected(disconnectInfo);
            if (onClientDisconnected != null)
                onClientDisconnected.Invoke(disconnectInfo);
        }

#if UNITY_STANDALONE && !CLIENT_BUILD
        protected override async UniTask PreSpawnEntities()
        {
            // Spawn buildings
            if (!IsInstanceMap())
            {
                // Load buildings
                // Don't load buildings if it's instance map
                BuildingsResp resp = await DbServiceClient.ReadBuildingsAsync(new ReadBuildingsReq()
                {
                    MapName = CurrentMapInfo.Id,
                });
                HashSet<StorageId> storageIds = new HashSet<StorageId>();
                List<BuildingSaveData> buildings = resp.List.MakeListFromRepeatedByteString<BuildingSaveData>();
                BuildingEntity buildingEntity;
                foreach (BuildingSaveData building in buildings)
                {
                    buildingEntity = CreateBuildingEntity(building, true);
                    if (buildingEntity is StorageEntity)
                        storageIds.Add(new StorageId(StorageType.Building, (buildingEntity as StorageEntity).Id));
                }
                List<UniTask> tasks = new List<UniTask>();
                // Load building storage
                foreach (StorageId storageId in storageIds)
                {
                    tasks.Add(LoadStorageRoutine(storageId));
                }
                // Wait until all building storage loaded
                await UniTask.WhenAll(tasks);
            }
        }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        protected override async UniTask PostSpawnEntities()
        {
            await UniTask.Yield();
            CentralAppServerRegister.OnStartServer();
        }
#endif

        #region Character spawn function
        public override void SerializeClientReadyData(NetDataWriter writer)
        {
            writer.Put(MMOClientInstance.UserId);
            writer.Put(MMOClientInstance.AccessToken);
            writer.Put(MMOClientInstance.SelectCharacterId);
        }

#if UNITY_STANDALONE && !CLIENT_BUILD
        public override async UniTask<bool> DeserializeClientReadyData(LiteNetLibIdentity playerIdentity, long connectionId, NetDataReader reader)
        {
            string userId = reader.GetString();
            string accessToken = reader.GetString();
            string selectCharacterId = reader.GetString();

            if (playerCharacters.ContainsKey(connectionId))
            {
                if (LogError)
                    Logging.LogError(LogTag, "User trying to hack: " + userId);
                Transport.ServerDisconnect(connectionId);
                return false;
            }

            ValidateAccessTokenResp validateAccessTokenResp = await DbServiceClient.ValidateAccessTokenAsync(new ValidateAccessTokenReq()
            {
                UserId = userId,
                AccessToken = accessToken
            });

            if (!validateAccessTokenResp.IsPass)
            {
                if (LogError)
                    Logging.LogError(LogTag, "Invalid access token for user: " + userId);
                Transport.ServerDisconnect(connectionId);
            }

            if (!IsReadyToInstantiateObjects())
            {
                if (LogWarn)
                    Logging.LogWarning(LogTag, "Not ready to spawn player: " + userId);
                // Add to pending list to spawn player later when map server is ready to instantiate object
                pendingSpawnPlayerCharacters.Add(new PendingSpawnPlayerCharacter()
                {
                    connectionId = connectionId,
                    userId = userId,
                    selectCharacterId = selectCharacterId
                });
                return false;
            }

            SetPlayerReadyRoutine(connectionId, userId, selectCharacterId).Forget();
            return true;
        }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        private async UniTaskVoid SetPlayerReadyRoutine(long connectionId, string userId, string selectCharacterId)
        {
            CharacterResp characterResp = await DbServiceClient.ReadCharacterAsync(new ReadCharacterReq()
            {
                UserId = userId,
                CharacterId = selectCharacterId
            });
            PlayerCharacterData playerCharacterData = characterResp.CharacterData.FromByteString<PlayerCharacterData>();
            // If data is empty / cannot find character, disconnect user
            if (playerCharacterData == null)
            {
                if (LogError)
                    Logging.LogError(LogTag, "Cannot find select character: " + selectCharacterId + " for user: " + userId);
                Transport.ServerDisconnect(connectionId);
            }
            else
            {
                BasePlayerCharacterEntity entityPrefab = playerCharacterData.GetEntityPrefab() as BasePlayerCharacterEntity;
                // If it is not allow this character data, disconnect user
                if (entityPrefab == null)
                {
                    if (LogError)
                        Logging.LogError(LogTag, "Cannot find player character with entity Id: " + playerCharacterData.EntityId);
                    Transport.ServerDisconnect(connectionId);
                }
                else
                {
                    // Prepare saving location for this character
                    string savingCurrentMapName = playerCharacterData.CurrentMapName;
                    Vector3 savingCurrentPosition = playerCharacterData.CurrentPosition;

                    if (IsInstanceMap())
                    {
                        playerCharacterData.CurrentPosition = MapInstanceWarpToPosition;
                        if (MapInstanceWarpOverrideRotation)
                            playerCharacterData.CurrentRotation = MapInstanceWarpToRotation;
                    }

                    // Spawn character entity and set its data
                    GameObject spawnObj = Instantiate(entityPrefab.gameObject, playerCharacterData.CurrentPosition, Quaternion.identity);
                    BasePlayerCharacterEntity playerCharacterEntity = spawnObj.GetComponent<BasePlayerCharacterEntity>();
                    playerCharacterData.CloneTo(playerCharacterEntity);
                    Assets.NetworkSpawn(spawnObj, 0, connectionId);

                    // Set currencies
                    // Gold
                    GoldResp getGoldResp = await DbServiceClient.GetGoldAsync(new GetGoldReq()
                    {
                        UserId = userId
                    });
                    playerCharacterEntity.UserGold = getGoldResp.Gold;
                    // Cash
                    CashResp getCashResp = await DbServiceClient.GetCashAsync(new GetCashReq()
                    {
                        UserId = userId
                    });
                    playerCharacterEntity.UserCash = getCashResp.Cash;

                    // Prepare saving location for this character
                    if (IsInstanceMap())
                        instanceMapCurrentLocations.Add(playerCharacterEntity.ObjectId, new KeyValuePair<string, Vector3>(savingCurrentMapName, savingCurrentPosition));

                    // Set user Id
                    playerCharacterEntity.UserId = userId;

                    // Load user level
                    GetUserLevelResp getUserLevelResp = await DbServiceClient.GetUserLevelAsync(new GetUserLevelReq()
                    {
                        UserId = userId
                    });
                    playerCharacterEntity.UserLevel = (byte)getUserLevelResp.UserLevel;

                    // Load party data, if this map-server does not have party data
                    if (playerCharacterEntity.PartyId > 0)
                    {
                        if (!parties.ContainsKey(playerCharacterEntity.PartyId))
                            await LoadPartyRoutine(playerCharacterEntity.PartyId);
                        if (parties.ContainsKey(playerCharacterEntity.PartyId))
                        {
                            PartyData party = parties[playerCharacterEntity.PartyId];
                            SendCreatePartyToClient(playerCharacterEntity.ConnectionId, party);
                            SendAddPartyMembersToClient(playerCharacterEntity.ConnectionId, party);
                        }
                        else
                            playerCharacterEntity.ClearParty();
                    }

                    // Load guild data, if this map-server does not have guild data
                    if (playerCharacterEntity.GuildId > 0)
                    {
                        if (!guilds.ContainsKey(playerCharacterEntity.GuildId))
                            await LoadGuildRoutine(playerCharacterEntity.GuildId);
                        if (guilds.ContainsKey(playerCharacterEntity.GuildId))
                        {
                            GuildData guild = guilds[playerCharacterEntity.GuildId];
                            playerCharacterEntity.GuildName = guild.guildName;
                            playerCharacterEntity.GuildRole = guild.GetMemberRole(playerCharacterEntity.Id);
                            SendCreateGuildToClient(playerCharacterEntity.ConnectionId, guild);
                            SendAddGuildMembersToClient(playerCharacterEntity.ConnectionId, guild);
                            SendSetGuildMessageToClient(playerCharacterEntity.ConnectionId, guild);
                            SendSetGuildRolesToClient(playerCharacterEntity.ConnectionId, guild);
                            SendSetGuildMemberRolesToClient(playerCharacterEntity.ConnectionId, guild);
                            SendSetGuildSkillLevelsToClient(playerCharacterEntity.ConnectionId, guild);
                            SendSetGuildGoldToClient(playerCharacterEntity.ConnectionId, guild);
                            SendGuildLevelExpSkillPointToClient(playerCharacterEntity.ConnectionId, guild);
                        }
                        else
                            playerCharacterEntity.ClearGuild();
                    }

                    // Summon saved summons
                    for (int i = 0; i < playerCharacterEntity.Summons.Count; ++i)
                    {
                        CharacterSummon summon = playerCharacterEntity.Summons[i];
                        summon.Summon(playerCharacterEntity, summon.Level, summon.summonRemainsDuration, summon.Exp, summon.CurrentHp, summon.CurrentMp);
                        playerCharacterEntity.Summons[i] = summon;
                    }

                    // Summon saved mount entity
                    if (GameInstance.VehicleEntities.ContainsKey(playerCharacterData.MountDataId))
                        playerCharacterEntity.Mount(GameInstance.VehicleEntities[playerCharacterData.MountDataId]);

                    // Force make caches, to calculate current stats to fill empty slots items
                    playerCharacterEntity.ForceMakeCaches();
                    playerCharacterEntity.FillEmptySlots();

                    // Notify clients that this character is spawn or dead
                    if (!playerCharacterEntity.IsDead())
                        playerCharacterEntity.CallAllOnRespawn();
                    else
                        playerCharacterEntity.CallAllOnDead();

                    // Register player character entity to the server
                    RegisterPlayerCharacter(playerCharacterEntity);

                    // Setup subscribers
                    LiteNetLibPlayer player = GetPlayer(connectionId);
                    foreach (LiteNetLibIdentity spawnedObject in Assets.GetSpawnedObjects())
                    {
                        if (spawnedObject.ConnectionId == player.ConnectionId)
                            continue;

                        if (spawnedObject.ShouldAddSubscriber(player))
                            spawnedObject.AddSubscriber(player);
                    }
                }
            }
        }
#endif
        #endregion

        #region Network message handlers
        protected override void HandleWarpAtClient(MessageHandlerData messageHandler)
        {
            MMOWarpMessage message = messageHandler.ReadMessage<MMOWarpMessage>();
            Assets.offlineScene.SceneName = string.Empty;
            StopClient();
            StartClient(message.networkAddress, message.networkPort);
        }

#if UNITY_STANDALONE && !CLIENT_BUILD
        protected override void HandleChatAtServer(MessageHandlerData messageHandler)
        {
            ChatMessage message = FillChatChannelId(messageHandler.ReadMessage<ChatMessage>());
            // Local chat will processes immediately, not have to be sent to chat server
            if (message.channel == ChatChannel.Local)
            {
                ReadChatMessage(message);
                return;
            }
            if (message.channel == ChatChannel.System)
            {
                if (CanSendSystemAnnounce(message.sender))
                {
                    // Send chat message to chat server, for MMO mode chat message handling by chat server
                    if (ChatNetworkManager.IsClientConnected)
                    {
                        ChatNetworkManager.SendEnterChat(null, MMOMessageTypes.Chat, message.channel, message.message, message.sender, message.receiver, message.channelId);
                    }
                }
                return;
            }
            // Send chat message to chat server, for MMO mode chat message handling by chat server
            if (ChatNetworkManager.IsClientConnected)
            {
                ChatNetworkManager.SendEnterChat(null, MMOMessageTypes.Chat, message.channel, message.message, message.sender, message.receiver, message.channelId);
            }
        }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        protected override async UniTaskVoid HandleRequestCashShopInfo(
            RequestHandlerData requestHandler, EmptyMessage request,
            RequestProceedResultDelegate<ResponseCashShopInfoMessage> result)
        {
            // Set response data
            ResponseCashShopInfoMessage.Error error = ResponseCashShopInfoMessage.Error.None;
            int cash = 0;
            List<int> cashShopItemIds = new List<int>();
            BasePlayerCharacterEntity playerCharacter;
            if (!playerCharacters.TryGetValue(requestHandler.ConnectionId, out playerCharacter))
            {
                // Cannot find user
                error = ResponseCashShopInfoMessage.Error.UserNotFound;
            }
            else
            {
                // Get user cash amount
                CashResp getCashResp = await DbServiceClient.GetCashAsync(new GetCashReq()
                {
                    UserId = playerCharacter.UserId
                });
                cash = getCashResp.Cash;
                // Set cash shop item ids
                cashShopItemIds.AddRange(GameInstance.CashShopItems.Keys);
            }
            // Send response message
            result.Invoke(
                error == ResponseCashShopInfoMessage.Error.None ? AckResponseCode.Success : AckResponseCode.Error,
                new ResponseCashShopInfoMessage()
                {
                    error = error,
                    cash = cash,
                    cashShopItemIds = cashShopItemIds.ToArray(),
                });
        }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        protected override async UniTaskVoid HandleRequestCashShopBuy(
            RequestHandlerData requestHandler, RequestCashShopBuyMessage request,
            RequestProceedResultDelegate<ResponseCashShopBuyMessage> result)
        {
            // Set response data
            ResponseCashShopBuyMessage.Error error = ResponseCashShopBuyMessage.Error.None;
            int dataId = request.dataId;
            int cash = 0;
            BasePlayerCharacterEntity playerCharacter;
            if (!playerCharacters.TryGetValue(requestHandler.ConnectionId, out playerCharacter))
            {
                // Cannot find user
                error = ResponseCashShopBuyMessage.Error.UserNotFound;
            }
            else
            {
                // Get user cash amount
                CashResp getCashResp = await DbServiceClient.GetCashAsync(new GetCashReq()
                {
                    UserId = playerCharacter.UserId
                });
                cash = getCashResp.Cash;
                CashShopItem cashShopItem;
                if (!GameInstance.CashShopItems.TryGetValue(dataId, out cashShopItem))
                {
                    // Cannot find item
                    error = ResponseCashShopBuyMessage.Error.ItemNotFound;
                }
                else if (cash < cashShopItem.sellPrice)
                {
                    // Not enough cash
                    error = ResponseCashShopBuyMessage.Error.NotEnoughCash;
                }
                else if (playerCharacter.IncreasingItemsWillOverwhelming(cashShopItem.receiveItems))
                {
                    // Cannot carry all rewards
                    error = ResponseCashShopBuyMessage.Error.CannotCarryAllRewards;
                }
                else
                {
                    // Decrease cash amount
                    CashResp changeCashResp = await DbServiceClient.ChangeCashAsync(new ChangeCashReq()
                    {
                        UserId = playerCharacter.UserId,
                        ChangeAmount = -cashShopItem.sellPrice
                    });
                    cash = changeCashResp.Cash;
                    playerCharacter.UserCash = cash;
                    // Increase character gold
                    playerCharacter.Gold = playerCharacter.Gold.Increase(cashShopItem.receiveGold);
                    // Increase character item
                    foreach (ItemAmount receiveItem in cashShopItem.receiveItems)
                    {
                        if (receiveItem.item == null || receiveItem.amount <= 0) continue;
                        playerCharacter.AddOrSetNonEquipItems(CharacterItem.Create(receiveItem.item, 1, receiveItem.amount));
                    }
                    playerCharacter.FillEmptySlots();
                }
            }
            // Send response message
            result.Invoke(
                error == ResponseCashShopBuyMessage.Error.None ? AckResponseCode.Success : AckResponseCode.Error,
                new ResponseCashShopBuyMessage()
                {
                    error = error,
                    dataId = dataId,
                    cash = cash,
                });
        }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        protected override async UniTaskVoid HandleRequestCashPackageInfo(
            RequestHandlerData requestHandler, EmptyMessage request,
            RequestProceedResultDelegate<ResponseCashPackageInfoMessage> result)
        {
            // Set response data
            ResponseCashPackageInfoMessage.Error error = ResponseCashPackageInfoMessage.Error.None;
            int cash = 0;
            List<int> cashPackageIds = new List<int>();
            BasePlayerCharacterEntity playerCharacter;
            if (!playerCharacters.TryGetValue(requestHandler.ConnectionId, out playerCharacter))
            {
                // Cannot find user
                error = ResponseCashPackageInfoMessage.Error.UserNotFound;
            }
            else
            {
                // Get user cash amount
                CashResp getCashResp = await DbServiceClient.GetCashAsync(new GetCashReq()
                {
                    UserId = playerCharacter.UserId
                });
                cash = getCashResp.Cash;
                // Set cash package ids
                cashPackageIds.AddRange(GameInstance.CashPackages.Keys);
            }
            // Send response message
            result.Invoke(
                error == ResponseCashPackageInfoMessage.Error.None ? AckResponseCode.Success : AckResponseCode.Error,
                new ResponseCashPackageInfoMessage()
                {
                    error = error,
                    cash = cash,
                    cashPackageIds = cashPackageIds.ToArray(),
                });
        }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        protected override async UniTaskVoid HandleRequestCashPackageBuyValidation(
            RequestHandlerData requestHandler, RequestCashPackageBuyValidationMessage request,
            RequestProceedResultDelegate<ResponseCashPackageBuyValidationMessage> result)
        {
            // TODO: Validate purchasing at server side
            // Set response data
            ResponseCashPackageBuyValidationMessage.Error error = ResponseCashPackageBuyValidationMessage.Error.None;
            int dataId = request.dataId;
            int cash = 0;
            BasePlayerCharacterEntity playerCharacter;
            if (!playerCharacters.TryGetValue(requestHandler.ConnectionId, out playerCharacter))
            {
                // Cannot find user
                error = ResponseCashPackageBuyValidationMessage.Error.UserNotFound;
            }
            else
            {
                // Get user cash amount
                CashResp getCashResp = await DbServiceClient.GetCashAsync(new GetCashReq()
                {
                    UserId = playerCharacter.UserId
                });
                cash = getCashResp.Cash;
                CashPackage cashPackage;
                if (!GameInstance.CashPackages.TryGetValue(dataId, out cashPackage))
                {
                    // Cannot find package
                    error = ResponseCashPackageBuyValidationMessage.Error.PackageNotFound;
                }
                else
                {
                    // Increase cash amount
                    CashResp changeCashResp = await DbServiceClient.ChangeCashAsync(new ChangeCashReq()
                    {
                        UserId = playerCharacter.UserId,
                        ChangeAmount = cashPackage.cashAmount
                    });
                    cash = changeCashResp.Cash;
                    playerCharacter.UserCash = cash;
                }
            }
            // Send response message
            result.Invoke(
                error == ResponseCashPackageBuyValidationMessage.Error.None ? AckResponseCode.Success : AckResponseCode.Error,
                new ResponseCashPackageBuyValidationMessage()
                {
                    error = error,
                    dataId = dataId,
                    cash = cash,
                });
        }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        protected override async UniTaskVoid HandleRequestMailList(RequestHandlerData requestHandler, RequestMailListMessage request, RequestProceedResultDelegate<ResponseMailListMessage> result)
        {
            List<MailListEntry> mails = new List<MailListEntry>();
            BasePlayerCharacterEntity playerCharacter;
            if (playerCharacters.TryGetValue(requestHandler.ConnectionId, out playerCharacter))
            {
                MailListResp resp = await DbServiceClient.MailListAsync(new MailListReq()
                {
                    UserId = playerCharacter.UserId,
                    OnlyNewMails = request.onlyNewMails,
                });
                mails.AddRange(resp.List.MakeListFromRepeatedByteString<MailListEntry>());
            }
            result.Invoke(AckResponseCode.Success, new ResponseMailListMessage()
            {
                onlyNewMails = request.onlyNewMails,
                mails = mails.ToArray(),
            });
        }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        protected override async UniTaskVoid HandleRequestReadMail(RequestHandlerData requestHandler, RequestReadMailMessage request, RequestProceedResultDelegate<ResponseReadMailMessage> result)
        {
            BasePlayerCharacterEntity playerCharacter;
            if (playerCharacters.TryGetValue(requestHandler.ConnectionId, out playerCharacter))
            {
                UpdateReadMailStateResp resp = await DbServiceClient.UpdateReadMailStateAsync(new UpdateReadMailStateReq()
                {
                    MailId = request.id,
                    UserId = playerCharacter.UserId,
                });
                ResponseReadMailMessage.Error error = (ResponseReadMailMessage.Error)resp.Error;
                result.Invoke(
                    error == ResponseReadMailMessage.Error.None ? AckResponseCode.Success : AckResponseCode.Error, 
                    new ResponseReadMailMessage()
                    {
                        error = error,
                        mail = resp.Mail.FromByteString<Mail>(),
                    });
            }
            else
            {
                result.Invoke(AckResponseCode.Error, new ResponseReadMailMessage()
                {
                    error = ResponseReadMailMessage.Error.NotAvailable,
                });
            }
        }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        protected override async UniTaskVoid HandleRequestClaimMailItems(RequestHandlerData requestHandler, RequestClaimMailItemsMessage request, RequestProceedResultDelegate<ResponseClaimMailItemsMessage> result)
        {
            BasePlayerCharacterEntity playerCharacter;
            if (playerCharacters.TryGetValue(requestHandler.ConnectionId, out playerCharacter))
            {
                ResponseClaimMailItemsMessage.Error error = ResponseClaimMailItemsMessage.Error.None;
                GetMailResp mailResp = await DbServiceClient.GetMailAsync(new GetMailReq()
                {
                    MailId = request.id,
                    UserId = playerCharacter.UserId,
                });
                Mail mail = mailResp.Mail.FromByteString<Mail>();
                if (mail.IsClaim)
                {
                    error = ResponseClaimMailItemsMessage.Error.AlreadyClaimed;
                }
                else if (mail.IsDelete)
                {
                    error = ResponseClaimMailItemsMessage.Error.NotAllowed;
                }
                else
                {
                    if (mail.Items.Count > 0)
                    {
                        List<CharacterItem> increasingItems = new List<CharacterItem>();
                        foreach (KeyValuePair<int, short> mailItem in mail.Items)
                        {
                            increasingItems.Add(CharacterItem.Create(mailItem.Key, amount: mailItem.Value));
                        }
                        if (playerCharacter.IncreasingItemsWillOverwhelming(increasingItems))
                            error = ResponseClaimMailItemsMessage.Error.CannotCarry;
                        else
                            playerCharacter.IncreaseItems(increasingItems);
                    }
                    if (error == ResponseClaimMailItemsMessage.Error.None && mail.Currencies.Count > 0)
                    {
                        List<CharacterCurrency> increasingCurrencies = new List<CharacterCurrency>();
                        foreach (KeyValuePair<int, int> mailCurrency in mail.Currencies)
                        {
                            increasingCurrencies.Add(CharacterCurrency.Create(mailCurrency.Key, amount: mailCurrency.Value));
                        }
                        playerCharacter.IncreaseCurrencies(increasingCurrencies);
                    }
                    if (error == ResponseClaimMailItemsMessage.Error.None && mail.Gold > 0)
                    {
                        playerCharacter.Gold = playerCharacter.Gold.Increase(mail.Gold);
                    }
                }
                if (error != ResponseClaimMailItemsMessage.Error.None)
                {
                    result.Invoke(AckResponseCode.Error, new ResponseClaimMailItemsMessage()
                    {
                        error = error,
                    });
                    return;
                }
                UpdateClaimMailItemsStateResp resp = await DbServiceClient.UpdateClaimMailItemsStateAsync(new UpdateClaimMailItemsStateReq()
                {
                    MailId = request.id,
                    UserId = playerCharacter.UserId,
                });
                error = (ResponseClaimMailItemsMessage.Error)resp.Error;
                result.Invoke(
                    error == ResponseClaimMailItemsMessage.Error.None ? AckResponseCode.Success : AckResponseCode.Error,
                    new ResponseClaimMailItemsMessage()
                    {
                        error = error,
                        mail = resp.Mail.FromByteString<Mail>(),
                    });
            }
            else
            {
                result.Invoke(AckResponseCode.Error, new ResponseClaimMailItemsMessage()
                {
                    error = ResponseClaimMailItemsMessage.Error.NotAvailable,
                });
            }
        }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        protected override async UniTaskVoid HandleRequestDeleteMail(RequestHandlerData requestHandler, RequestDeleteMailMessage request, RequestProceedResultDelegate<ResponseDeleteMailMessage> result)
        {
            BasePlayerCharacterEntity playerCharacter;
            if (playerCharacters.TryGetValue(requestHandler.ConnectionId, out playerCharacter))
            {
                UpdateDeleteMailStateResp resp = await DbServiceClient.UpdateDeleteMailStateAsync(new UpdateDeleteMailStateReq()
                {
                    MailId = request.id,
                    UserId = playerCharacter.UserId,
                });
                ResponseDeleteMailMessage.Error error = (ResponseDeleteMailMessage.Error)resp.Error;
                result.Invoke(
                    error == ResponseDeleteMailMessage.Error.None ? AckResponseCode.Success : AckResponseCode.Error,
                    new ResponseDeleteMailMessage()
                    {
                        error = error,
                    });
            }
            else
            {
                result.Invoke(AckResponseCode.Error, new ResponseDeleteMailMessage()
                {
                    error = ResponseDeleteMailMessage.Error.NotAvailable,
                });
            }
        }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        protected override async UniTaskVoid HandleRequestSendMail(RequestHandlerData requestHandler, RequestSendMailMessage request, RequestProceedResultDelegate<ResponseSendMailMessage> result)
        {
            BasePlayerCharacterEntity playerCharacter;
            if (playerCharacters.TryGetValue(requestHandler.ConnectionId, out playerCharacter))
            {
                // Validate gold
                if (request.gold < 0)
                    request.gold = 0;
                if (playerCharacter.Gold >= request.gold)
                {
                    playerCharacter.Gold -= request.gold;
                }
                else
                {
                    result.Invoke(AckResponseCode.Error, new ResponseSendMailMessage()
                    {
                        error = ResponseSendMailMessage.Error.NotEnoughGold,
                    });
                    return;
                }
                // Find receiver
                GetUserIdByCharacterNameResp userIdResp = await DbServiceClient.GetUserIdByCharacterNameAsync(new GetUserIdByCharacterNameReq()
                {
                    CharacterName = request.receiverName,
                });
                string receiverId = userIdResp.UserId;
                if (string.IsNullOrEmpty(receiverId))
                {
                    result.Invoke(AckResponseCode.Error, new ResponseSendMailMessage()
                    {
                        error = ResponseSendMailMessage.Error.NoReceiver,
                    });
                    return;
                }
                Mail mail = new Mail()
                {
                    SenderId = playerCharacter.UserId,
                    SenderName = playerCharacter.CharacterName,
                    ReceiverId = receiverId,
                    Title = request.title,
                    Content = request.content,
                    Gold = request.gold,
                };
                SendMailResp resp = await DbServiceClient.SendMailAsync(new SendMailReq()
                {
                    Mail = DatabaseServiceUtils.ToByteString(mail),
                });
                ResponseSendMailMessage.Error error = (ResponseSendMailMessage.Error)resp.Error;
                result.Invoke(
                    error == ResponseSendMailMessage.Error.None ? AckResponseCode.Success : AckResponseCode.Error,
                    new ResponseSendMailMessage()
                    {
                        error = error,
                    });
            }
            else
            {
                result.Invoke(AckResponseCode.Error, new ResponseSendMailMessage()
                {
                    error = ResponseSendMailMessage.Error.NotAvailable,
                });
            }
        }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        private void HandleResponseAppServerAddress(MessageHandlerData messageHandler)
        {
            ResponseAppServerAddressMessage message = messageHandler.ReadMessage<ResponseAppServerAddressMessage>();
            CentralServerPeerInfo peerInfo = message.peerInfo;
            switch (peerInfo.peerType)
            {
                case CentralServerPeerType.MapServer:
                    if (!string.IsNullOrEmpty(peerInfo.extra))
                    {
                        if (LogInfo)
                            Logging.Log(LogTag, "Register map server: " + peerInfo.extra);
                        mapServerConnectionIdsBySceneName[peerInfo.extra] = peerInfo;
                    }
                    break;
                case CentralServerPeerType.InstanceMapServer:
                    if (!string.IsNullOrEmpty(peerInfo.extra))
                    {
                        if (LogInfo)
                            Logging.Log(LogTag, "Register instance map server: " + peerInfo.extra);
                        instanceMapServerConnectionIdsByInstanceId[peerInfo.extra] = peerInfo;
                        // Warp characters
                        HashSet<uint> warpingCharacters;
                        if (instanceMapWarpingCharactersByInstanceId.TryGetValue(peerInfo.extra, out warpingCharacters))
                        {
                            BasePlayerCharacterEntity warpingCharacterEntity;
                            foreach (uint warpingCharacter in warpingCharacters)
                            {
                                if (!Assets.TryGetSpawnedObject(warpingCharacter, out warpingCharacterEntity))
                                    continue;
                                WarpCharacterToInstanceRoutine(warpingCharacterEntity, peerInfo.extra).Forget();
                            }
                        }
                    }
                    break;
                case CentralServerPeerType.Chat:
                    if (!ChatNetworkManager.IsClientConnected)
                    {
                        if (LogInfo)
                            Logging.Log(LogTag, "Connecting to chat server");
                        ChatNetworkManager.StartClient(this, peerInfo.networkAddress, peerInfo.networkPort);
                    }
                    break;
            }
        }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        private void OnAppServerRegistered(AckResponseCode responseCode)
        {
            if (responseCode == AckResponseCode.Success)
                UpdateMapUsers(CentralAppServerRegister, UpdateUserCharacterMessage.UpdateType.Add);
        }
#endif
        #endregion

        #region Connect to chat server
        public void OnChatServerConnected()
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            if (LogInfo)
                Logging.Log(LogTag, "Connected to chat server");
            UpdateMapUsers(ChatNetworkManager.Client, UpdateUserCharacterMessage.UpdateType.Add);
#endif
        }

        public void OnChatMessageReceive(ChatMessage message)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            ReadChatMessage(message);
#endif
        }

        public void OnUpdateMapUser(UpdateUserCharacterMessage message)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            int socialId;
            PartyData party;
            GuildData guild;
            switch (message.type)
            {
                case UpdateUserCharacterMessage.UpdateType.Add:
                    if (!usersById.ContainsKey(message.data.id))
                        usersById.Add(message.data.id, message.data);
                    break;
                case UpdateUserCharacterMessage.UpdateType.Remove:
                    usersById.Remove(message.data.id);
                    break;
                case UpdateUserCharacterMessage.UpdateType.Online:
                    if (usersById.ContainsKey(message.data.id))
                    {
                        NotifyOnlineCharacter(message.data.id);
                        socialId = message.data.partyId;
                        if (socialId > 0 && parties.TryGetValue(socialId, out party))
                        {
                            party.UpdateMember(message.data);
                            parties[socialId] = party;
                        }
                        socialId = message.data.guildId;
                        if (socialId > 0 && guilds.TryGetValue(socialId, out guild))
                        {
                            guild.UpdateMember(message.data);
                            guilds[socialId] = guild;
                        }
                        usersById[message.data.id] = message.data;
                    }
                    break;
            }
#endif
        }

        public void OnUpdatePartyMember(UpdateSocialMemberMessage message)
        {
            PartyData party;
            BasePlayerCharacterEntity playerCharacterEntity;
            if (parties.TryGetValue(message.id, out party) && UpdateSocialGroupMember(party, message))
            {
                switch (message.type)
                {
                    case UpdateSocialMemberMessage.UpdateType.Add:
                        if (playerCharactersById.TryGetValue(message.data.id, out playerCharacterEntity))
                        {
                            playerCharacterEntity.PartyId = message.id;
                            SendCreatePartyToClient(playerCharacterEntity.ConnectionId, party);
                            SendAddPartyMembersToClient(playerCharacterEntity.ConnectionId, party);
                        }
                        SendAddPartyMemberToClients(party, message.data.id, message.data.characterName, message.data.dataId, message.data.level);
                        break;
                    case UpdateSocialMemberMessage.UpdateType.Remove:
                        if (playerCharactersById.TryGetValue(message.data.id, out playerCharacterEntity))
                        {
                            playerCharacterEntity.ClearParty();
                            SendPartyTerminateToClient(playerCharacterEntity.ConnectionId, message.id);
                        }
                        SendRemovePartyMemberToClients(party, message.data.id);
                        break;
                }
            }
        }

        public void OnUpdateParty(UpdatePartyMessage message)
        {
            BasePlayerCharacterEntity playerCharacterEntity;
            PartyData party;
            if (parties.TryGetValue(message.id, out party))
            {
                switch (message.type)
                {
                    case UpdatePartyMessage.UpdateType.ChangeLeader:
                        party.SetLeader(message.characterId);
                        parties[message.id] = party;
                        SendChangePartyLeaderToClients(party);
                        break;
                    case UpdatePartyMessage.UpdateType.Setting:
                        party.Setting(message.shareExp, message.shareItem);
                        parties[message.id] = party;
                        SendPartySettingToClients(party);
                        break;
                    case UpdatePartyMessage.UpdateType.Terminate:
                        foreach (string memberId in party.GetMemberIds())
                        {
                            if (playerCharactersById.TryGetValue(memberId, out playerCharacterEntity))
                            {
                                playerCharacterEntity.ClearParty();
                                SendPartyTerminateToClient(playerCharacterEntity.ConnectionId, message.id);
                            }
                        }
                        parties.Remove(message.id);
                        break;
                }
            }
        }

        public void OnUpdateGuildMember(UpdateSocialMemberMessage message)
        {
            GuildData guild;
            BasePlayerCharacterEntity playerCharacterEntity;
            if (guilds.TryGetValue(message.id, out guild) && UpdateSocialGroupMember(guild, message))
            {
                switch (message.type)
                {
                    case UpdateSocialMemberMessage.UpdateType.Add:
                        if (playerCharactersById.TryGetValue(message.data.id, out playerCharacterEntity))
                        {
                            playerCharacterEntity.GuildId = message.id;
                            playerCharacterEntity.GuildName = guild.guildName;
                            playerCharacterEntity.GuildRole = guild.GetMemberRole(playerCharacterEntity.Id);
                            SendCreateGuildToClient(playerCharacterEntity.ConnectionId, guild);
                            SendAddGuildMembersToClient(playerCharacterEntity.ConnectionId, guild);
                        }
                        SendAddGuildMemberToClients(guild, message.data.id, message.data.characterName, message.data.dataId, message.data.level);
                        break;
                    case UpdateSocialMemberMessage.UpdateType.Remove:
                        if (playerCharactersById.TryGetValue(message.data.id, out playerCharacterEntity))
                        {
                            playerCharacterEntity.ClearGuild();
                            SendGuildTerminateToClient(playerCharacterEntity.ConnectionId, message.id);
                        }
                        SendRemoveGuildMemberToClients(guild, message.data.id);
                        break;
                }
            }
        }

        public void OnUpdateGuild(UpdateGuildMessage message)
        {
            BasePlayerCharacterEntity playerCharacterEntity;
            GuildData guild;
            if (guilds.TryGetValue(message.id, out guild))
            {
                switch (message.type)
                {
                    case UpdateGuildMessage.UpdateType.ChangeLeader:
                        guild.SetLeader(message.characterId);
                        guilds[message.id] = guild;
                        if (TryGetPlayerCharacterById(message.characterId, out playerCharacterEntity))
                            playerCharacterEntity.GuildRole = guild.GetMemberRole(playerCharacterEntity.Id);
                        SendChangeGuildLeaderToClients(guild);
                        break;
                    case UpdateGuildMessage.UpdateType.SetGuildMessage:
                        guild.guildMessage = message.guildMessage;
                        guilds[message.id] = guild;
                        SendSetGuildMessageToClients(guild);
                        break;
                    case UpdateGuildMessage.UpdateType.SetGuildRole:
                        guild.SetRole(message.guildRole, message.roleName, message.canInvite, message.canKick, message.shareExpPercentage);
                        guilds[message.id] = guild;
                        foreach (string memberId in guild.GetMemberIds())
                        {
                            if (playerCharactersById.TryGetValue(memberId, out playerCharacterEntity))
                                playerCharacterEntity.GuildRole = guild.GetMemberRole(playerCharacterEntity.Id);
                        }
                        SendSetGuildRoleToClients(guild, message.guildRole, message.roleName, message.canInvite, message.canKick, message.shareExpPercentage);
                        break;
                    case UpdateGuildMessage.UpdateType.SetGuildMemberRole:
                        guild.SetMemberRole(message.characterId, message.guildRole);
                        guilds[message.id] = guild;
                        if (TryGetPlayerCharacterById(message.characterId, out playerCharacterEntity))
                            playerCharacterEntity.GuildRole = guild.GetMemberRole(playerCharacterEntity.Id);
                        SendSetGuildMemberRoleToClients(guild, message.characterId, message.guildRole);
                        break;
                    case UpdateGuildMessage.UpdateType.SetSkillLevel:
                        guild.SetSkillLevel(message.dataId, message.level);
                        guilds[message.id] = guild;
                        SendSetGuildSkillLevelToClients(guild, message.dataId);
                        break;
                    case UpdateGuildMessage.UpdateType.SetGold:
                        guild.gold = message.gold;
                        guilds[message.id] = guild;
                        SendSetGuildGoldToClients(guild);
                        break;
                    case UpdateGuildMessage.UpdateType.LevelExpSkillPoint:
                        guild.level = message.level;
                        guild.exp = message.exp;
                        guild.skillPoint = message.skillPoint;
                        guilds[message.id] = guild;
                        SendGuildLevelExpSkillPointToClients(guild);
                        break;
                    case UpdateGuildMessage.UpdateType.Terminate:
                        foreach (string memberId in guild.GetMemberIds())
                        {
                            if (playerCharactersById.TryGetValue(memberId, out playerCharacterEntity))
                            {
                                playerCharacterEntity.ClearGuild();
                                SendGuildTerminateToClient(playerCharacterEntity.ConnectionId, message.id);
                            }
                        }
                        guilds.Remove(message.id);
                        break;
                }
            }
        }
        #endregion

        #region Update map user functions
        private void UpdateMapUsers(LiteNetLibClient transportHandler, UpdateUserCharacterMessage.UpdateType updateType)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            foreach (SocialCharacterData user in usersById.Values)
            {
                UpdateMapUser(transportHandler, updateType, user);
            }
#endif
        }

        private void UpdateMapUser(LiteNetLibClient transportHandler, UpdateUserCharacterMessage.UpdateType updateType, SocialCharacterData userData)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            UpdateUserCharacterMessage updateMapUserMessage = new UpdateUserCharacterMessage();
            updateMapUserMessage.type = updateType;
            updateMapUserMessage.data = userData;
            transportHandler.SendPacket(DeliveryMethod.ReliableOrdered, MMOMessageTypes.UpdateMapUser, updateMapUserMessage.Serialize);
#endif
        }
        #endregion
    }
}
