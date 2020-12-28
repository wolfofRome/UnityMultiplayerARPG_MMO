﻿using System.Collections.Generic;
using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;
using LiteNetLibManager;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Concurrent;

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
        public int mapSpawnMillisecondsTimeout = 0;

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
        private readonly ConcurrentDictionary<uint, KeyValuePair<string, Vector3>> instanceMapCurrentLocations = new ConcurrentDictionary<uint, KeyValuePair<string, Vector3>>();
        private readonly ConcurrentDictionary<string, CentralServerPeerInfo> mapServerConnectionIdsBySceneName = new ConcurrentDictionary<string, CentralServerPeerInfo>();
        private readonly ConcurrentDictionary<string, CentralServerPeerInfo> instanceMapServerConnectionIdsByInstanceId = new ConcurrentDictionary<string, CentralServerPeerInfo>();
        private readonly ConcurrentDictionary<string, HashSet<uint>> instanceMapWarpingCharactersByInstanceId = new ConcurrentDictionary<string, HashSet<uint>>();
        private readonly ConcurrentDictionary<string, InstanceMapWarpingLocation> instanceMapWarpingLocations = new ConcurrentDictionary<string, InstanceMapWarpingLocation>();
        private readonly ConcurrentDictionary<string, SocialCharacterData> usersById = new ConcurrentDictionary<string, SocialCharacterData>();
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
            // Server Handlers
            ServerUserHandlers = gameObject.GetOrAddComponent<IServerUserHandlers, DefaultServerUserHandlers>();
            ServerBuildingHandlers = gameObject.GetOrAddComponent<IServerBuildingHandlers, DefaultServerBuildingHandlers>();
            ServerOnlineCharacterHandlers = gameObject.GetOrAddComponent<IServerOnlineCharacterHandlers, DefaultServerOnlineCharacterHandlers>();
            ServerGameMessageHandlers = gameObject.GetOrAddComponent<IServerGameMessageHandlers, DefaultServerGameMessageHandlers>();
            ServerStorageHandlers = gameObject.GetOrAddComponent<IServerStorageHandlers, MMOServerStorageHandlers>();
            ServerPartyHandlers = gameObject.GetOrAddComponent<IServerPartyHandlers, DefaultServerPartyHandlers>();
            ServerGuildHandlers = gameObject.GetOrAddComponent<IServerGuildHandlers, MMOServerGuildHandlers>();
            // Server Message Handlers
            ServerCashShopMessageHandlers = gameObject.GetOrAddComponent<IServerCashShopMessageHandlers, MMOServerCashShopMessageHandlers>();
            ServerMailMessageHandlers = gameObject.GetOrAddComponent<IServerMailMessageHandlers, MMOServerMailMessageHandlers>();
            ServerInventoryMessageHandlers = gameObject.GetOrAddComponent<IServerInventoryMessageHandlers, DefaultServerInventoryMessageHandlers>();
            ServerStorageMessageHandlers = gameObject.GetOrAddComponent<IServerStorageMessageHandlers, MMOServerStorageMessageHandlers>();
            ServerPartyMessageHandlers = gameObject.GetOrAddComponent<IServerPartyMessageHandlers, MMOServerPartyMessageHandlers>();
            ServerGuildMessageHandlers = gameObject.GetOrAddComponent<IServerGuildMessageHandlers, MMOServerGuildMessageHandlers>();
            ServerFriendMessageHandlers = gameObject.GetOrAddComponent<IServerFriendMessageHandlers, MMOServerFriendMessageHandlers>();
            ServerBankMessageHandlers = gameObject.GetOrAddComponent<IServerBankMessageHandlers, MMOServerBankMessageHandlers>();
            // Client handlers
            ClientCashShopHandlers = gameObject.GetOrAddComponent<IClientCashShopHandlers, DefaultClientCashShopHandlers>();
            ClientMailHandlers = gameObject.GetOrAddComponent<IClientMailHandlers, DefaultClientMailHandlers>();
            ClientStorageHandlers = gameObject.GetOrAddComponent<IClientStorageHandlers, DefaultClientStorageHandlers>();
            ClientInventoryHandlers = gameObject.GetOrAddComponent<IClientInventoryHandlers, DefaultClientInventoryHandlers>();
            ClientPartyHandlers = gameObject.GetOrAddComponent<IClientPartyHandlers, DefaultClientPartyHandlers>();
            ClientGuildHandlers = gameObject.GetOrAddComponent<IClientGuildHandlers, DefaultClientGuildHandlers>();
            ClientFriendHandlers = gameObject.GetOrAddComponent<IClientFriendHandlers, DefaultClientFriendHandlers>();
            ClientBankHandlers = gameObject.GetOrAddComponent<IClientBankHandlers, DefaultClientBankHandlers>();
            ClientOnlineCharacterHandlers = gameObject.GetOrAddComponent<IClientOnlineCharacterHandlers, DefaultClientOnlineCharacterHandlers>();
            ClientGameMessageHandlers = gameObject.GetOrAddComponent<IClientGameMessageHandlers, DefaultClientGameMessageHandlers>();
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
                foreach (BasePlayerCharacterEntity playerCharacter in ServerUserHandlers.GetPlayerCharacters())
                {
                    if (playerCharacter == null) continue;
                    DbServiceClient.UpdateCharacter(new UpdateCharacterReq()
                    {
                        CharacterData = playerCharacter.CloneTo(new PlayerCharacterData()).ToByteString()
                    });
                }
                string mapName = CurrentMapInfo.Id;
                foreach (BuildingEntity buildingEntity in ServerBuildingHandlers.GetBuildings())
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
        public override void RegisterPlayerCharacter(long connectionId, BasePlayerCharacterEntity playerCharacterEntity)
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
                usersById.TryAdd(userData.id, userData);
                // Add map user to central server and chat server
                UpdateMapUser(CentralAppServerRegister, UpdateUserCharacterMessage.UpdateType.Add, userData);
                if (ChatNetworkManager.IsClientConnected)
                    UpdateMapUser(ChatNetworkManager.Client, UpdateUserCharacterMessage.UpdateType.Add, userData);
            }
            base.RegisterPlayerCharacter(connectionId, playerCharacterEntity);
        }
#endif

#if UNITY_STANDALONE && !CLIENT_BUILD
        public override void UnregisterPlayerCharacter(long connectionId)
        {
            // Send remove character from map server
            BasePlayerCharacterEntity playerCharacter;
            SocialCharacterData userData;
            if (ServerUserHandlers.TryGetPlayerCharacter(connectionId, out playerCharacter) &&
                usersById.TryGetValue(playerCharacter.Id, out userData))
            {
                usersById.TryRemove(playerCharacter.Id, out _);
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
            if (ServerUserHandlers.TryGetPlayerCharacter(connectionId, out playerCharacterEntity))
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

            if (ServerUserHandlers.TryGetPlayerCharacter(connectionId, out _))
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
                    Quaternion characterRotation = Quaternion.identity;
                    if (CurrentGameInstance.DimensionType == DimensionType.Dimension3D)
                        characterRotation = Quaternion.Euler(playerCharacterData.CurrentRotation);
                    GameObject spawnObj = Instantiate(entityPrefab.gameObject, playerCharacterData.CurrentPosition, characterRotation);
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
                        instanceMapCurrentLocations.TryAdd(playerCharacterEntity.ObjectId, new KeyValuePair<string, Vector3>(savingCurrentMapName, savingCurrentPosition));

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
                        if (!ServerPartyHandlers.ContainsParty(playerCharacterEntity.PartyId))
                            await LoadPartyRoutine(playerCharacterEntity.PartyId);
                        PartyData party;
                        if (ServerPartyHandlers.TryGetParty(playerCharacterEntity.PartyId, out party))
                        {
                            ServerGameMessageHandlers.SendSetPartyData(playerCharacterEntity.ConnectionId, party);
                            ServerGameMessageHandlers.SendAddPartyMembersToOne(playerCharacterEntity.ConnectionId, party);
                        }
                        else
                            playerCharacterEntity.ClearParty();
                    }

                    // Load guild data, if this map-server does not have guild data
                    if (playerCharacterEntity.GuildId > 0)
                    {
                        if (!ServerGuildHandlers.ContainsGuild(playerCharacterEntity.GuildId))
                            await LoadGuildRoutine(playerCharacterEntity.GuildId);
                        GuildData guild;
                        if (ServerGuildHandlers.TryGetGuild(playerCharacterEntity.GuildId, out guild))
                        {
                            playerCharacterEntity.GuildName = guild.guildName;
                            playerCharacterEntity.GuildRole = guild.GetMemberRole(playerCharacterEntity.Id);
                            ServerGameMessageHandlers.SendSetGuildData(playerCharacterEntity.ConnectionId, guild);
                            ServerGameMessageHandlers.SendAddGuildMembersToOne(playerCharacterEntity.ConnectionId, guild);
                            ServerGameMessageHandlers.SendSetGuildMessage(playerCharacterEntity.ConnectionId, guild);
                            ServerGameMessageHandlers.SendSetGuildRolesToOne(playerCharacterEntity.ConnectionId, guild);
                            ServerGameMessageHandlers.SendSetGuildMemberRolesToOne(playerCharacterEntity.ConnectionId, guild);
                            ServerGameMessageHandlers.SendSetGuildSkillLevelsToOne(playerCharacterEntity.ConnectionId, guild);
                            ServerGameMessageHandlers.SendSetGuildGold(playerCharacterEntity.ConnectionId, guild);
                            ServerGameMessageHandlers.SendSetGuildLevelExpSkillPoint(playerCharacterEntity.ConnectionId, guild);
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
                    RegisterPlayerCharacter(connectionId, playerCharacterEntity);

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
                        usersById.TryAdd(message.data.id, message.data);
                    break;
                case UpdateUserCharacterMessage.UpdateType.Remove:
                    usersById.TryRemove(message.data.id, out _);
                    break;
                case UpdateUserCharacterMessage.UpdateType.Online:
                    if (usersById.ContainsKey(message.data.id))
                    {
                        ServerOnlineCharacterHandlers.MarkOnlineCharacter(message.data.id);
                        socialId = message.data.partyId;
                        if (socialId > 0 && ServerPartyHandlers.TryGetParty(socialId, out party))
                        {
                            party.UpdateMember(message.data);
                            ServerPartyHandlers.SetParty(socialId, party);
                        }
                        socialId = message.data.guildId;
                        if (socialId > 0 && ServerGuildHandlers.TryGetGuild(socialId, out guild))
                        {
                            guild.UpdateMember(message.data);
                            ServerGuildHandlers.SetGuild(socialId, guild);
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
            if (ServerPartyHandlers.TryGetParty(message.id, out party) && party.UpdateSocialGroupMember(message))
            {
                switch (message.type)
                {
                    case UpdateSocialMemberMessage.UpdateType.Add:
                        if (ServerUserHandlers.TryGetPlayerCharacterById(message.data.id, out playerCharacterEntity))
                        {
                            playerCharacterEntity.PartyId = message.id;
                            ServerGameMessageHandlers.SendSetPartyData(playerCharacterEntity.ConnectionId, party);
                            ServerGameMessageHandlers.SendAddPartyMembersToOne(playerCharacterEntity.ConnectionId, party);
                        }
                        ServerGameMessageHandlers.SendAddPartyMembersToMembers(party, message.data.id, message.data.characterName, message.data.dataId, message.data.level);
                        break;
                    case UpdateSocialMemberMessage.UpdateType.Remove:
                        if (ServerUserHandlers.TryGetPlayerCharacterById(message.data.id, out playerCharacterEntity))
                        {
                            playerCharacterEntity.ClearParty();
                            ServerGameMessageHandlers.SendClearPartyData(playerCharacterEntity.ConnectionId, message.id);
                        }
                        ServerGameMessageHandlers.SendRemovePartyMemberToMembers(party, message.data.id);
                        break;
                }
            }
        }

        public void OnUpdateParty(UpdatePartyMessage message)
        {
            BasePlayerCharacterEntity playerCharacterEntity;
            PartyData party;
            if (ServerPartyHandlers.TryGetParty(message.id, out party))
            {
                switch (message.type)
                {
                    case UpdatePartyMessage.UpdateType.ChangeLeader:
                        party.SetLeader(message.characterId);
                        ServerPartyHandlers.SetParty(message.id, party);
                        ServerGameMessageHandlers.SendSetPartyLeaderToMembers(party);
                        break;
                    case UpdatePartyMessage.UpdateType.Setting:
                        party.Setting(message.shareExp, message.shareItem);
                        ServerPartyHandlers.SetParty(message.id, party);
                        ServerGameMessageHandlers.SendSetPartySettingToMembers(party);
                        break;
                    case UpdatePartyMessage.UpdateType.Terminate:
                        foreach (string memberId in party.GetMemberIds())
                        {
                            if (ServerUserHandlers.TryGetPlayerCharacterById(memberId, out playerCharacterEntity))
                            {
                                playerCharacterEntity.ClearParty();
                                ServerGameMessageHandlers.SendClearPartyData(playerCharacterEntity.ConnectionId, message.id);
                            }
                        }
                        ServerPartyHandlers.RemoveParty(message.id);
                        break;
                }
            }
        }

        public void OnUpdateGuildMember(UpdateSocialMemberMessage message)
        {
            GuildData guild;
            BasePlayerCharacterEntity playerCharacterEntity;
            if (ServerGuildHandlers.TryGetGuild(message.id, out guild) && guild.UpdateSocialGroupMember(message))
            {
                switch (message.type)
                {
                    case UpdateSocialMemberMessage.UpdateType.Add:
                        if (ServerUserHandlers.TryGetPlayerCharacterById(message.data.id, out playerCharacterEntity))
                        {
                            playerCharacterEntity.GuildId = message.id;
                            playerCharacterEntity.GuildName = guild.guildName;
                            playerCharacterEntity.GuildRole = guild.GetMemberRole(playerCharacterEntity.Id);
                            ServerGameMessageHandlers.SendSetGuildData(playerCharacterEntity.ConnectionId, guild);
                            ServerGameMessageHandlers.SendAddGuildMembersToOne(playerCharacterEntity.ConnectionId, guild);
                        }
                        ServerGameMessageHandlers.SendAddGuildMembersToMembers(guild, message.data.id, message.data.characterName, message.data.dataId, message.data.level);
                        break;
                    case UpdateSocialMemberMessage.UpdateType.Remove:
                        if (ServerUserHandlers.TryGetPlayerCharacterById(message.data.id, out playerCharacterEntity))
                        {
                            playerCharacterEntity.ClearGuild();
                            ServerGameMessageHandlers.SendClearGuildData(playerCharacterEntity.ConnectionId, message.id);
                        }
                        ServerGameMessageHandlers.SendRemoveGuildMemberToMembers(guild, message.data.id);
                        break;
                }
            }
        }

        public void OnUpdateGuild(UpdateGuildMessage message)
        {
            BasePlayerCharacterEntity playerCharacterEntity;
            GuildData guild;
            if (ServerGuildHandlers.TryGetGuild(message.id, out guild))
            {
                switch (message.type)
                {
                    case UpdateGuildMessage.UpdateType.ChangeLeader:
                        guild.SetLeader(message.characterId);
                        ServerGuildHandlers.SetGuild(message.id, guild);
                        if (ServerUserHandlers.TryGetPlayerCharacterById(message.characterId, out playerCharacterEntity))
                            playerCharacterEntity.GuildRole = guild.GetMemberRole(playerCharacterEntity.Id);
                        ServerGameMessageHandlers.SendSetGuildLeaderToMembers(guild);
                        break;
                    case UpdateGuildMessage.UpdateType.SetGuildMessage:
                        guild.guildMessage = message.guildMessage;
                        ServerGuildHandlers.SetGuild(message.id, guild);
                        ServerGameMessageHandlers.SendSetGuildMessageToMembers(guild);
                        break;
                    case UpdateGuildMessage.UpdateType.SetGuildRole:
                        guild.SetRole(message.guildRole, message.roleName, message.canInvite, message.canKick, message.shareExpPercentage);
                        ServerGuildHandlers.SetGuild(message.id, guild);
                        foreach (string memberId in guild.GetMemberIds())
                        {
                            if (ServerUserHandlers.TryGetPlayerCharacterById(memberId, out playerCharacterEntity))
                                playerCharacterEntity.GuildRole = guild.GetMemberRole(playerCharacterEntity.Id);
                        }
                        ServerGameMessageHandlers.SendSetGuildRoleToMembers(guild, message.guildRole, message.roleName, message.canInvite, message.canKick, message.shareExpPercentage);
                        break;
                    case UpdateGuildMessage.UpdateType.SetGuildMemberRole:
                        guild.SetMemberRole(message.characterId, message.guildRole);
                        ServerGuildHandlers.SetGuild(message.id, guild);
                        if (ServerUserHandlers.TryGetPlayerCharacterById(message.characterId, out playerCharacterEntity))
                            playerCharacterEntity.GuildRole = guild.GetMemberRole(playerCharacterEntity.Id);
                        ServerGameMessageHandlers.SendSetGuildMemberRoleToMembers(guild, message.characterId, message.guildRole);
                        break;
                    case UpdateGuildMessage.UpdateType.SetSkillLevel:
                        guild.SetSkillLevel(message.dataId, message.level);
                        ServerGuildHandlers.SetGuild(message.id, guild);
                        ServerGameMessageHandlers.SendSetGuildSkillLevelToMembers(guild, message.dataId);
                        break;
                    case UpdateGuildMessage.UpdateType.SetGold:
                        guild.gold = message.gold;
                        ServerGuildHandlers.SetGuild(message.id, guild);
                        ServerGameMessageHandlers.SendSetGuildGoldToMembers(guild);
                        break;
                    case UpdateGuildMessage.UpdateType.LevelExpSkillPoint:
                        guild.level = message.level;
                        guild.exp = message.exp;
                        guild.skillPoint = message.skillPoint;
                        ServerGuildHandlers.SetGuild(message.id, guild);
                        ServerGameMessageHandlers.SendSetGuildLevelExpSkillPointToMembers(guild);
                        break;
                    case UpdateGuildMessage.UpdateType.Terminate:
                        foreach (string memberId in guild.GetMemberIds())
                        {
                            if (ServerUserHandlers.TryGetPlayerCharacterById(memberId, out playerCharacterEntity))
                            {
                                playerCharacterEntity.ClearGuild();
                                ServerGameMessageHandlers.SendClearGuildData(playerCharacterEntity.ConnectionId, message.id);
                            }
                        }
                        ServerGuildHandlers.RemoveGuild(message.id);
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
