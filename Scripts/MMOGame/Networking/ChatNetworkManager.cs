﻿using System.Collections.Generic;
using UnityEngine;
using LiteNetLibManager;
using LiteNetLib;

namespace MultiplayerARPG.MMO
{
    [DefaultExecutionOrder(-896)]
    public partial class ChatNetworkManager : LiteNetLibManager.LiteNetLibManager, IAppServer
    {
        [Header("Central Network Connection")]
        public BaseTransportFactory centralTransportFactory;
        public string centralNetworkAddress = "127.0.0.1";
        public int centralNetworkPort = 6000;
        public string machineAddress = "127.0.0.1";

        public BaseTransportFactory CentralTransportFactory
        {
            get { return centralTransportFactory; }
        }

        public CentralAppServerRegister CentralAppServerRegister { get; private set; }

        public string CentralNetworkAddress { get { return centralNetworkAddress; } }
        public int CentralNetworkPort { get { return centralNetworkPort; } }
        public string AppAddress { get { return machineAddress; } }
        public int AppPort { get { return networkPort; } }
        public string AppExtra { get { return string.Empty; } }
        public CentralServerPeerType PeerType { get { return CentralServerPeerType.Chat; } }
        private MapNetworkManager mapNetworkManager;
        private readonly HashSet<long> mapServerConnectionIds = new HashSet<long>();
        private readonly Dictionary<string, SocialCharacterData> mapUsersById = new Dictionary<string, SocialCharacterData>();
        private readonly Dictionary<string, long> connectionIdsByCharacterId = new Dictionary<string, long>();
        private readonly Dictionary<string, long> connectionIdsByCharacterName = new Dictionary<string, long>();

        protected override void Start()
        {
            base.Start();
            if (useWebSocket)
            {
                if (centralTransportFactory == null || !(centralTransportFactory is IWebSocketTransportFactory))
                {
                    WebSocketTransportFactory webSocketTransportFactory = gameObject.AddComponent<WebSocketTransportFactory>();
                    webSocketTransportFactory.Secure = webSocketSecure;
                    webSocketTransportFactory.SslProtocols = webSocketSslProtocols;
                    webSocketTransportFactory.CertificateFilePath = webSocketCertificateFilePath;
                    webSocketTransportFactory.CertificatePassword = webSocketCertificatePassword;
                    centralTransportFactory = webSocketTransportFactory;
                }
            }
            else
            {
                if (centralTransportFactory == null)
                    centralTransportFactory = gameObject.AddComponent<LiteNetLibTransportFactory>();
            }
            CentralAppServerRegister = new CentralAppServerRegister(CentralTransportFactory.Build(), this);
            this.InvokeInstanceDevExtMethods("OnInitCentralAppServerRegister");
        }

        protected override void RegisterMessages()
        {
            base.RegisterMessages();
            RegisterClientMessage(MMOMessageTypes.Chat, HandleChatAtClient);
            RegisterClientMessage(MMOMessageTypes.UpdateMapUser, HandleUpdateMapUserAtClient);
            RegisterClientMessage(MMOMessageTypes.UpdatePartyMember, HandleUpdatePartyMemberAtClient);
            RegisterClientMessage(MMOMessageTypes.UpdateParty, HandleUpdatePartyAtClient);
            RegisterClientMessage(MMOMessageTypes.UpdateGuildMember, HandleUpdateGuildMemberAtClient);
            RegisterClientMessage(MMOMessageTypes.UpdateGuild, HandleUpdateGuildAtClient);
            RegisterServerMessage(MMOMessageTypes.Chat, HandleChatAtServer);
            RegisterServerMessage(MMOMessageTypes.UpdateMapUser, HandleUpdateMapUserAtServer);
            RegisterServerMessage(MMOMessageTypes.UpdatePartyMember, HandleUpdatePartyMemberAtServer);
            RegisterServerMessage(MMOMessageTypes.UpdateParty, HandleUpdatePartyAtServer);
            RegisterServerMessage(MMOMessageTypes.UpdateGuildMember, HandleUpdateGuildMemberAtServer);
            RegisterServerMessage(MMOMessageTypes.UpdateGuild, HandleUpdateGuildAtServer);
            // Keeping `RegisterClientMessages` and `RegisterServerMessages` for backward compatibility, can use any of below dev extension methods
            this.InvokeInstanceDevExtMethods("RegisterClientMessages");
            this.InvokeInstanceDevExtMethods("RegisterServerMessages");
            this.InvokeInstanceDevExtMethods("RegisterMessages");
        }

        protected virtual void Clean()
        {
            this.InvokeInstanceDevExtMethods("Clean");
            mapNetworkManager = null;
            mapServerConnectionIds.Clear();
            mapUsersById.Clear();
            connectionIdsByCharacterId.Clear();
            connectionIdsByCharacterName.Clear();
        }

        public void StartClient(MapNetworkManager mapNetworkManager, string networkAddress, int networkPort)
        {
            // Start client as map server
            this.mapNetworkManager = mapNetworkManager;
            base.StartClient(networkAddress, networkPort);
        }

        public override void OnStartServer()
        {
            this.InvokeInstanceDevExtMethods("OnStartServer");
            CentralAppServerRegister.OnStartServer();
            base.OnStartServer();
        }

        public override void OnStopServer()
        {
            if (!IsServer)
                Clean();
            CentralAppServerRegister.OnStopServer();
            base.OnStopServer();
        }

        public override void OnStartClient(LiteNetLibClient client)
        {
            this.InvokeInstanceDevExtMethods("OnStartClient", client);
            base.OnStartClient(client);
        }

        public override void OnStopClient()
        {
            if (!IsServer)
                Clean();
            base.OnStopClient();
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            if (IsServer)
                CentralAppServerRegister.Update();
        }

        public override void OnPeerConnected(long connectionId)
        {
            base.OnPeerConnected(connectionId);
            if (!mapServerConnectionIds.Contains(connectionId))
            {
                mapServerConnectionIds.Add(connectionId);
                // Send add map users
                foreach (SocialCharacterData userData in mapUsersById.Values)
                {
                    UpdateMapUser(connectionId, UpdateUserCharacterMessage.UpdateType.Add, userData);
                }
            }
        }

        public override void OnPeerDisconnected(long connectionId, DisconnectInfo disconnectInfo)
        {
            base.OnPeerDisconnected(connectionId, disconnectInfo);
            if (mapServerConnectionIds.Remove(connectionId))
            {
                SocialCharacterData userData;
                foreach (KeyValuePair<string, long> entry in connectionIdsByCharacterId)
                {
                    // Find characters which connected to disconnecting map server
                    if (connectionId != entry.Value || !mapUsersById.TryGetValue(entry.Key, out userData))
                        continue;

                    // Send remove messages to other map servers
                    UpdateMapUser(UpdateUserCharacterMessage.UpdateType.Remove, userData, connectionId);
                }
            }
        }

        public override void OnClientConnected()
        {
            base.OnClientConnected();
            // Send map users to chat server from map server
            if (mapNetworkManager != null)
                mapNetworkManager.OnChatServerConnected();
        }

        private void HandleChatAtClient(MessageHandlerData messageHandler)
        {
            ChatMessage message = messageHandler.ReadMessage<ChatMessage>();
            if (mapNetworkManager != null)
                mapNetworkManager.OnChatMessageReceive(message);
        }

        private void HandleUpdateMapUserAtClient(MessageHandlerData messageHandler)
        {
            UpdateUserCharacterMessage message = messageHandler.ReadMessage<UpdateUserCharacterMessage>();
            if (mapNetworkManager != null)
                mapNetworkManager.OnUpdateMapUser(message);
        }

        private void HandleUpdatePartyMemberAtClient(MessageHandlerData messageHandler)
        {
            UpdateSocialMemberMessage message = messageHandler.ReadMessage<UpdateSocialMemberMessage>();
            if (mapNetworkManager != null)
                mapNetworkManager.OnUpdatePartyMember(message);
        }

        private void HandleUpdatePartyAtClient(MessageHandlerData messageHandler)
        {
            UpdatePartyMessage message = messageHandler.ReadMessage<UpdatePartyMessage>();
            if (mapNetworkManager != null)
                mapNetworkManager.OnUpdateParty(message);
        }

        private void HandleUpdateGuildMemberAtClient(MessageHandlerData messageHandler)
        {
            UpdateSocialMemberMessage message = messageHandler.ReadMessage<UpdateSocialMemberMessage>();
            if (mapNetworkManager != null)
                mapNetworkManager.OnUpdateGuildMember(message);
        }

        private void HandleUpdateGuildAtClient(MessageHandlerData messageHandler)
        {
            UpdateGuildMessage message = messageHandler.ReadMessage<UpdateGuildMessage>();
            if (mapNetworkManager != null)
                mapNetworkManager.OnUpdateGuild(message);
        }

        private void HandleChatAtServer(MessageHandlerData messageHandler)
        {
            long connectionId = messageHandler.ConnectionId;
            ChatMessage message = messageHandler.ReadMessage<ChatMessage>();
            if (LogInfo)
                Logging.Log(LogTag, "Handle chat: " + message.channel + " sender: " + message.sender + " receiver: " + message.receiver + " message: " + message.message);
            switch (message.channel)
            {
                case ChatChannel.Local:
                case ChatChannel.Global:
                case ChatChannel.System:
                case ChatChannel.Party:
                case ChatChannel.Guild:
                    // Send message to all map servers, let's map servers filter messages
                    ServerSendPacketToAllConnections(0, DeliveryMethod.ReliableOrdered, MMOMessageTypes.Chat, message);
                    break;
                case ChatChannel.Whisper:
                    long senderConnectionId = 0;
                    long receiverConnectionId = 0;
                    // Send message to map server which have the character
                    if (!string.IsNullOrEmpty(message.sender) &&
                        connectionIdsByCharacterName.TryGetValue(message.sender, out senderConnectionId))
                        ServerSendPacket(senderConnectionId, 0, DeliveryMethod.ReliableOrdered, MMOMessageTypes.Chat, message);
                    if (!string.IsNullOrEmpty(message.receiver) &&
                        connectionIdsByCharacterName.TryGetValue(message.receiver, out receiverConnectionId) &&
                        (receiverConnectionId != senderConnectionId))
                        ServerSendPacket(receiverConnectionId, 0, DeliveryMethod.ReliableOrdered, MMOMessageTypes.Chat, message);
                    break;
            }
        }

        private void HandleUpdateMapUserAtServer(MessageHandlerData messageHandler)
        {
            long connectionId = messageHandler.ConnectionId;
            UpdateUserCharacterMessage message = messageHandler.ReadMessage<UpdateUserCharacterMessage>();
            if (mapServerConnectionIds.Contains(connectionId))
            {
                SocialCharacterData userData;
                switch (message.type)
                {
                    case UpdateUserCharacterMessage.UpdateType.Add:
                        if (!mapUsersById.ContainsKey(message.character.id))
                        {
                            mapUsersById[message.character.id] = message.character;
                            connectionIdsByCharacterId[message.character.id] = connectionId;
                            connectionIdsByCharacterName[message.character.characterName] = connectionId;
                            UpdateMapUser(UpdateUserCharacterMessage.UpdateType.Add, message.character, connectionId);
                        }
                        break;
                    case UpdateUserCharacterMessage.UpdateType.Remove:
                        if (mapUsersById.TryGetValue(message.character.id, out userData))
                        {
                            mapUsersById.Remove(userData.id);
                            connectionIdsByCharacterId.Remove(userData.id);
                            connectionIdsByCharacterName.Remove(userData.characterName);
                            UpdateMapUser(UpdateUserCharacterMessage.UpdateType.Remove, userData, connectionId);
                        }
                        break;
                    case UpdateUserCharacterMessage.UpdateType.Online:
                        if (mapUsersById.ContainsKey(message.character.id))
                        {
                            mapUsersById[message.character.id] = message.character;
                            UpdateMapUser(UpdateUserCharacterMessage.UpdateType.Online, message.character, connectionId);
                        }
                        break;
                }
            }
        }

        private void HandleUpdatePartyMemberAtServer(MessageHandlerData messageHandler)
        {
            long connectionId = messageHandler.ConnectionId;
            UpdateSocialMemberMessage message = messageHandler.ReadMessage<UpdateSocialMemberMessage>();
            if (mapServerConnectionIds.Contains(connectionId))
            {
                foreach (long mapServerConnectionId in mapServerConnectionIds)
                {
                    if (mapServerConnectionId != connectionId)
                        ServerSendPacket(mapServerConnectionId, 0, DeliveryMethod.ReliableOrdered, MMOMessageTypes.UpdatePartyMember, message);
                }
            }
        }

        private void HandleUpdatePartyAtServer(MessageHandlerData messageHandler)
        {
            long connectionId = messageHandler.ConnectionId;
            UpdatePartyMessage message = messageHandler.ReadMessage<UpdatePartyMessage>();
            if (mapServerConnectionIds.Contains(connectionId))
            {
                foreach (long mapServerConnectionId in mapServerConnectionIds)
                {
                    if (mapServerConnectionId != connectionId)
                        ServerSendPacket(mapServerConnectionId, 0, DeliveryMethod.ReliableOrdered, MMOMessageTypes.UpdateParty, message);
                }
            }
        }

        private void HandleUpdateGuildMemberAtServer(MessageHandlerData messageHandler)
        {
            long connectionId = messageHandler.ConnectionId;
            UpdateSocialMemberMessage message = messageHandler.ReadMessage<UpdateSocialMemberMessage>();
            if (mapServerConnectionIds.Contains(connectionId))
            {
                foreach (long mapServerConnectionId in mapServerConnectionIds)
                {
                    if (mapServerConnectionId != connectionId)
                        ServerSendPacket(mapServerConnectionId, 0, DeliveryMethod.ReliableOrdered, MMOMessageTypes.UpdateGuildMember, message);
                }
            }
        }

        private void HandleUpdateGuildAtServer(MessageHandlerData messageHandler)
        {
            long connectionId = messageHandler.ConnectionId;
            UpdateGuildMessage message = messageHandler.ReadMessage<UpdateGuildMessage>();
            if (mapServerConnectionIds.Contains(connectionId))
            {
                foreach (long mapServerConnectionId in mapServerConnectionIds)
                {
                    if (mapServerConnectionId != connectionId)
                        ServerSendPacket(mapServerConnectionId, 0, DeliveryMethod.ReliableOrdered, MMOMessageTypes.UpdateGuild, message);
                }
            }
        }

        private void UpdateMapUser(UpdateUserCharacterMessage.UpdateType updateType, SocialCharacterData userData, long exceptConnectionId)
        {
            foreach (long mapServerConnectionId in mapServerConnectionIds)
            {
                if (mapServerConnectionId == exceptConnectionId)
                    continue;

                UpdateMapUser(mapServerConnectionId, updateType, userData);
            }
        }

        private void UpdateMapUser(long connectionId, UpdateUserCharacterMessage.UpdateType updateType, SocialCharacterData userData)
        {
            UpdateUserCharacterMessage updateMapUserMessage = new UpdateUserCharacterMessage();
            updateMapUserMessage.type = updateType;
            updateMapUserMessage.character = userData;
            ServerSendPacket(connectionId, 0, DeliveryMethod.ReliableOrdered, MMOMessageTypes.UpdateMapUser, updateMapUserMessage);
        }
    }
}
