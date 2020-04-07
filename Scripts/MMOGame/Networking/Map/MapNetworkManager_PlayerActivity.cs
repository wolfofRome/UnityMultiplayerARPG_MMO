﻿using LiteNetLib;
using LiteNetLibManager;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace MultiplayerARPG.MMO
{
    public partial class MapNetworkManager
    {
        public override string GetCurrentMapId(BasePlayerCharacterEntity playerCharacterEntity)
        {
            if (!IsInstanceMap())
                return base.GetCurrentMapId(playerCharacterEntity);
            return instanceMapCurrentLocations[playerCharacterEntity.ObjectId].Key;
        }

        public override Vector3 GetCurrentPosition(BasePlayerCharacterEntity playerCharacterEntity)
        {
            if (!IsInstanceMap())
                return base.GetCurrentPosition(playerCharacterEntity);
            return instanceMapCurrentLocations[playerCharacterEntity.ObjectId].Value;
        }

        protected override void WarpCharacter(BasePlayerCharacterEntity playerCharacterEntity, string mapName, Vector3 position)
        {
            if (!CanWarpCharacter(playerCharacterEntity))
                return;

            // If map name is empty, just teleport character to target position
            if (string.IsNullOrEmpty(mapName) || (mapName.Equals(CurrentMapInfo.Id) && !IsInstanceMap()))
            {
                playerCharacterEntity.Teleport(position);
                return;
            }

            WarpCharacterRoutine(playerCharacterEntity, mapName, position);
        }

        /// <summary>
        /// Warp to different map.
        /// </summary>
        /// <param name="playerCharacterEntity"></param>
        /// <param name="mapName"></param>
        /// <param name="position"></param>
        /// <returns></returns>
        private async void WarpCharacterRoutine(BasePlayerCharacterEntity playerCharacterEntity, string mapName, Vector3 position)
        {
            // If warping to different map
            long connectionId = playerCharacterEntity.ConnectionId;
            CentralServerPeerInfo peerInfo;
            BaseMapInfo mapInfo;
            if (!string.IsNullOrEmpty(mapName) &&
                playerCharacters.ContainsKey(connectionId) &&
                mapServerConnectionIdsBySceneName.TryGetValue(mapName, out peerInfo) &&
                GameInstance.MapInfos.TryGetValue(mapName, out mapInfo) &&
                mapInfo.IsSceneSet())
            {
                // Add this character to warping list
                playerCharacterEntity.IsWarping = true;
                // Unregister player character
                UnregisterPlayerCharacter(connectionId);
                // Clone character data to save
                PlayerCharacterData savingCharacterData = new PlayerCharacterData();
                playerCharacterEntity.CloneTo(savingCharacterData);
                savingCharacterData.CurrentMapName = mapName;
                savingCharacterData.CurrentPosition = position;
                while (savingCharacters.Contains(savingCharacterData.Id))
                {
                    await Task.Yield();
                }
                await SaveCharacterRoutine(savingCharacterData, playerCharacterEntity.UserId);
                // Remove this character from warping list
                playerCharacterEntity.IsWarping = false;
                // Destroy character from server
                playerCharacterEntity.NetworkDestroy();
                // Send message to client to warp
                MMOWarpMessage message = new MMOWarpMessage();
                message.networkAddress = peerInfo.networkAddress;
                message.networkPort = peerInfo.networkPort;
                ServerSendPacket(connectionId, DeliveryMethod.ReliableOrdered, MsgTypes.Warp, message);
            }
        }

        protected override void WarpCharacterToInstance(BasePlayerCharacterEntity playerCharacterEntity, string mapName, Vector3 position)
        {
            if (!CanWarpCharacter(playerCharacterEntity))
                return;
            // Generate instance id
            string instanceId = GenericUtils.GetUniqueId();
            RequestSpawnMapMessage requestSpawnMapMessage = new RequestSpawnMapMessage();
            requestSpawnMapMessage.mapId = mapName;
            requestSpawnMapMessage.instanceId = instanceId;
            // Prepare data for warp character later when instance map server registered to this map server
            HashSet<uint> instanceMapWarpingCharacters = new HashSet<uint>();
            PartyData party;
            if (parties.TryGetValue(playerCharacterEntity.PartyId, out party))
            {
                // If character is party member, will bring party member to join instance
                if (party.IsLeader(playerCharacterEntity))
                {
                    List<BasePlayerCharacterEntity> aliveAllies = playerCharacterEntity.FindAliveCharacters<BasePlayerCharacterEntity>(CurrentGameInstance.joinInstanceMapDistance, true, false, false);
                    foreach (BasePlayerCharacterEntity aliveAlly in aliveAllies)
                    {
                        if (!party.IsMember(aliveAlly))
                            continue;
                        instanceMapWarpingCharacters.Add(aliveAlly.ObjectId);
                        aliveAlly.IsWarping = true;
                    }
                    instanceMapWarpingCharacters.Add(playerCharacterEntity.ObjectId);
                    playerCharacterEntity.IsWarping = true;
                }
            }
            else
            {
                // If no party enter instance alone
                instanceMapWarpingCharacters.Add(playerCharacterEntity.ObjectId);
                playerCharacterEntity.IsWarping = true;
            }
            instanceMapWarpingCharactersByInstanceId.Add(instanceId, instanceMapWarpingCharacters);
            instanceMapWarpingLocations.Add(instanceId, new KeyValuePair<string, Vector3>(mapName, position));
            CentralAppServerRegister.SendRequest(MMOMessageTypes.RequestSpawnMap, requestSpawnMapMessage, OnRequestSpawnMap);
        }

        private async void WarpCharacterToInstanceRoutine(BasePlayerCharacterEntity playerCharacterEntity, string instanceId)
        {
            // If warping to different map
            long connectionId = playerCharacterEntity.ConnectionId;
            CentralServerPeerInfo peerInfo;
            KeyValuePair<string, Vector3> warpingLocation;
            BaseMapInfo mapInfo;
            if (playerCharacters.ContainsKey(connectionId) &&
                instanceMapWarpingLocations.TryGetValue(instanceId, out warpingLocation) &&
                instanceMapServerConnectionIdsByInstanceId.TryGetValue(instanceId, out peerInfo) &&
                GameInstance.MapInfos.TryGetValue(warpingLocation.Key, out mapInfo) &&
                mapInfo.IsSceneSet())
            {
                // Add this character to warping list
                playerCharacterEntity.IsWarping = true;
                // Unregister player character
                UnregisterPlayerCharacter(connectionId);
                // Clone character data to save
                PlayerCharacterData savingCharacterData = new PlayerCharacterData();
                playerCharacterEntity.CloneTo(savingCharacterData);
                while (savingCharacters.Contains(savingCharacterData.Id))
                {
                    await Task.Yield();
                }
                await SaveCharacterRoutine(savingCharacterData, playerCharacterEntity.UserId);
                // Remove this character from warping list
                playerCharacterEntity.IsWarping = false;
                // Destroy character from server
                playerCharacterEntity.NetworkDestroy();
                // Send message to client to warp
                MMOWarpMessage message = new MMOWarpMessage();
                message.networkAddress = peerInfo.networkAddress;
                message.networkPort = peerInfo.networkPort;
                ServerSendPacket(connectionId, DeliveryMethod.ReliableOrdered, MsgTypes.Warp, message);
            }
        }

        private void OnRequestSpawnMap(AckResponseCode responseCode, BaseAckMessage messageData)
        {
            ResponseSpawnMapMessage castedMessage = messageData as ResponseSpawnMapMessage;
            if (LogInfo)
                Logging.Log(LogTag, "Spawn Map Ack Id: " + messageData.ackId + "  Status: " + responseCode + " Error: " + castedMessage.error);
            if (responseCode == AckResponseCode.Error ||
                responseCode == AckResponseCode.Timeout)
            {
                // Remove warping characters who warping to instance map
                HashSet<uint> instanceMapWarpingCharacters;
                if (instanceMapWarpingCharactersByInstanceId.TryGetValue(castedMessage.instanceId, out instanceMapWarpingCharacters))
                {
                    BasePlayerCharacterEntity playerCharacterEntity;
                    foreach (uint warpingCharacter in instanceMapWarpingCharacters)
                    {
                        if (Assets.TryGetSpawnedObject(warpingCharacter, out playerCharacterEntity))
                            playerCharacterEntity.IsWarping = false;
                    }
                    instanceMapWarpingCharactersByInstanceId.Remove(castedMessage.instanceId);
                }
                instanceMapWarpingLocations.Remove(castedMessage.instanceId);
            }
        }

        protected override bool IsInstanceMap()
        {
            return !string.IsNullOrEmpty(mapInstanceId);
        }

        public override void CreateParty(BasePlayerCharacterEntity playerCharacterEntity, bool shareExp, bool shareItem)
        {
            if (!CanCreateParty(playerCharacterEntity))
                return;
            CreatePartyRoutine(playerCharacterEntity, shareExp, shareItem);
        }

        private async void CreatePartyRoutine(BasePlayerCharacterEntity playerCharacterEntity, bool shareExp, bool shareItem)
        {
            CreatePartyResp createPartyResp = await DbServiceClient.CreatePartyAsync(new CreatePartyReq()
            {
                LeaderCharacterId = playerCharacterEntity.Id,
                ShareExp = shareExp,
                ShareItem = shareItem
            });
            int partyId = createPartyResp.PartyId;
            // Create party
            base.CreateParty(playerCharacterEntity, shareExp, shareItem, partyId);
            // Save to database
            await DbServiceClient.UpdateCharacterPartyAsync(new UpdateCharacterPartyReq()
            {
                CharacterId = playerCharacterEntity.Id,
                PartyId = partyId
            });
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
            {
                ChatNetworkManager.SendCreateParty(null, MMOMessageTypes.UpdateParty, partyId, shareExp, shareItem, playerCharacterEntity.Id);
                ChatNetworkManager.SendAddSocialMember(null, MMOMessageTypes.UpdatePartyMember, partyId, playerCharacterEntity.Id, playerCharacterEntity.CharacterName, playerCharacterEntity.DataId, playerCharacterEntity.Level);
            }
        }

        public override void ChangePartyLeader(BasePlayerCharacterEntity playerCharacterEntity, string characterId)
        {
            int partyId;
            PartyData party;
            if (!CanChangePartyLeader(playerCharacterEntity, characterId, out partyId, out party))
                return;

            base.ChangePartyLeader(playerCharacterEntity, characterId);
            // Save to database
            DbServiceClient.UpdatePartyLeaderAsync(new UpdatePartyLeaderReq()
            {
                PartyId = partyId,
                LeaderCharacterId = characterId
            });
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
                ChatNetworkManager.SendChangePartyLeader(null, MMOMessageTypes.UpdateParty, partyId, characterId);
        }

        public override void PartySetting(BasePlayerCharacterEntity playerCharacterEntity, bool shareExp, bool shareItem)
        {
            int partyId;
            PartyData party;
            if (!CanPartySetting(playerCharacterEntity, out partyId, out party))
                return;

            base.PartySetting(playerCharacterEntity, shareExp, shareItem);
            // Save to database
            DbServiceClient.UpdatePartyAsync(new UpdatePartyReq()
            {
                PartyId = partyId,
                ShareExp = shareExp,
                ShareItem = shareItem
            });
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
                ChatNetworkManager.SendPartySetting(null, MMOMessageTypes.UpdateParty, partyId, shareExp, shareItem);
        }

        public override void AddPartyMember(BasePlayerCharacterEntity inviteCharacterEntity, BasePlayerCharacterEntity acceptCharacterEntity)
        {
            int partyId;
            PartyData party;
            if (!CanAddPartyMember(inviteCharacterEntity, acceptCharacterEntity, out partyId, out party))
                return;

            base.AddPartyMember(inviteCharacterEntity, acceptCharacterEntity);
            // Save to database
            DbServiceClient.UpdateCharacterPartyAsync(new UpdateCharacterPartyReq()
            {
                CharacterId = acceptCharacterEntity.Id,
                PartyId = partyId
            });
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
                ChatNetworkManager.SendAddSocialMember(null, MMOMessageTypes.UpdatePartyMember, partyId, acceptCharacterEntity.Id, acceptCharacterEntity.CharacterName, acceptCharacterEntity.DataId, acceptCharacterEntity.Level);
        }

        public override void KickFromParty(BasePlayerCharacterEntity playerCharacterEntity, string characterId)
        {
            int partyId;
            PartyData party;
            if (!CanKickFromParty(playerCharacterEntity, characterId, out partyId, out party))
                return;

            base.KickFromParty(playerCharacterEntity, characterId);
            // Save to database
            DbServiceClient.UpdateCharacterPartyAsync(new UpdateCharacterPartyReq()
            {
                CharacterId = characterId,
                PartyId = 0
            });
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
                ChatNetworkManager.SendRemoveSocialMember(null, MMOMessageTypes.UpdatePartyMember, partyId, characterId);
        }

        public override void LeaveParty(BasePlayerCharacterEntity playerCharacterEntity)
        {
            int partyId;
            PartyData party;
            if (!CanLeaveParty(playerCharacterEntity, out partyId, out party))
                return;

            // If it is leader kick all members and terminate party
            if (party.IsLeader(playerCharacterEntity))
            {
                foreach (string memberId in party.GetMemberIds())
                {
                    BasePlayerCharacterEntity memberCharacterEntity;
                    if (playerCharactersById.TryGetValue(memberId, out memberCharacterEntity))
                    {
                        memberCharacterEntity.ClearParty();
                        SendPartyTerminateToClient(memberCharacterEntity.ConnectionId, partyId);
                    }
                    // Save to database
                    DbServiceClient.UpdateCharacterPartyAsync(new UpdateCharacterPartyReq()
                    {
                        CharacterId = memberId,
                        PartyId = 0
                    });
                    // Broadcast via chat server
                    if (ChatNetworkManager.IsClientConnected)
                        ChatNetworkManager.SendRemoveSocialMember(null, MMOMessageTypes.UpdatePartyMember, partyId, memberId);
                }
                parties.Remove(partyId);
                // Save to database
                DbServiceClient.DeletePartyAsync(new DeletePartyReq()
                {
                    PartyId = partyId
                });
                // Broadcast via chat server
                if (ChatNetworkManager.IsClientConnected)
                    ChatNetworkManager.SendPartyTerminate(null, MMOMessageTypes.UpdateParty, partyId);
            }
            else
            {
                playerCharacterEntity.ClearParty();
                SendPartyTerminateToClient(playerCharacterEntity.ConnectionId, partyId);
                party.RemoveMember(playerCharacterEntity.Id);
                parties[partyId] = party;
                SendRemovePartyMemberToClients(party, playerCharacterEntity.Id);
                // Save to database
                DbServiceClient.UpdateCharacterPartyAsync(new UpdateCharacterPartyReq()
                {
                    CharacterId = playerCharacterEntity.Id,
                    PartyId = 0
                });
                // Broadcast via chat server
                if (ChatNetworkManager.IsClientConnected)
                    ChatNetworkManager.SendRemoveSocialMember(null, MMOMessageTypes.UpdatePartyMember, partyId, playerCharacterEntity.Id);
            }
        }

        public override void CreateGuild(BasePlayerCharacterEntity playerCharacterEntity, string guildName)
        {
            if (!CanCreateGuild(playerCharacterEntity, guildName))
                return;
            CreateGuildRoutine(playerCharacterEntity, guildName);
        }

        private async void CreateGuildRoutine(BasePlayerCharacterEntity playerCharacterEntity, string guildName)
        {
            FindGuildNameResp findGuildNameResp = await DbServiceClient.FindGuildNameAsync(new FindGuildNameReq()
            {
                GuildName = guildName
            });
            if (findGuildNameResp.FoundAmount > 0)
            {
                // Cannot create guild because guild name is already existed
                SendServerGameMessage(playerCharacterEntity.ConnectionId, GameMessage.Type.ExistedGuildName);
            }
            else
            {
                CreateGuildResp createGuildResp = await DbServiceClient.CreateGuildAsync(new CreateGuildReq()
                {
                    LeaderCharacterId = playerCharacterEntity.Id,
                    GuildName = guildName
                });
                int guildId = createGuildResp.GuildId;
                // Create guild
                base.CreateGuild(playerCharacterEntity, guildName, guildId);
                // Save to database
                await DbServiceClient.UpdateCharacterGuildAsync(new UpdateCharacterGuildReq()
                {
                    CharacterId = playerCharacterEntity.Id,
                    GuildId = guildId,
                    GuildRole = guilds[guildId].GetMemberRole(playerCharacterEntity.Id)
                });
                // Broadcast via chat server
                if (ChatNetworkManager.IsClientConnected)
                {
                    ChatNetworkManager.SendCreateGuild(null, MMOMessageTypes.UpdateGuild, guildId, guildName, playerCharacterEntity.Id);
                    ChatNetworkManager.SendAddSocialMember(null, MMOMessageTypes.UpdateGuildMember, guildId, playerCharacterEntity.Id, playerCharacterEntity.CharacterName, playerCharacterEntity.DataId, playerCharacterEntity.Level);
                }
            }
        }

        public override void ChangeGuildLeader(BasePlayerCharacterEntity playerCharacterEntity, string characterId)
        {
            int guildId;
            GuildData guild;
            if (!CanChangeGuildLeader(playerCharacterEntity, characterId, out guildId, out guild))
                return;

            base.ChangeGuildLeader(playerCharacterEntity, characterId);
            // Save to database
            DbServiceClient.UpdateGuildLeaderAsync(new UpdateGuildLeaderReq()
            {
                GuildId = guildId,
                LeaderCharacterId = characterId
            });
            DbServiceClient.UpdateGuildMemberRoleAsync(new UpdateGuildMemberRoleReq()
            {
                MemberCharacterId = characterId,
                GuildRole = guild.GetMemberRole(characterId)
            });
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
                ChatNetworkManager.SendChangeGuildLeader(null, MMOMessageTypes.UpdateGuild, guildId, characterId);
        }

        public override void SetGuildMessage(BasePlayerCharacterEntity playerCharacterEntity, string guildMessage)
        {
            int guildId;
            GuildData guild;
            if (!CanSetGuildMessage(playerCharacterEntity, guildMessage, out guildId, out guild))
                return;

            base.SetGuildMessage(playerCharacterEntity, guildMessage);
            // Save to database
            DbServiceClient.UpdateGuildMessageAsync(new UpdateGuildMessageReq()
            {
                GuildId = guildId,
                GuildMessage = guildMessage
            });
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
                ChatNetworkManager.SendSetGuildMessage(null, MMOMessageTypes.UpdateGuild, guildId, guildMessage);
        }

        public override void SetGuildRole(BasePlayerCharacterEntity playerCharacterEntity, byte guildRole, string roleName, bool canInvite, bool canKick, byte shareExpPercentage)
        {
            int guildId;
            GuildData guild;
            if (!CanSetGuildRole(playerCharacterEntity, guildRole, roleName, out guildId, out guild))
                return;

            guild.SetRole(guildRole, roleName, canInvite, canKick, shareExpPercentage);
            guilds[guildId] = guild;
            // Change characters guild role
            foreach (string memberId in guild.GetMemberIds())
            {
                BasePlayerCharacterEntity memberCharacterEntity;
                if (playerCharactersById.TryGetValue(memberId, out memberCharacterEntity))
                {
                    memberCharacterEntity.GuildRole = guildRole;
                    // Save to database
                    DbServiceClient.UpdateGuildMemberRoleAsync(new UpdateGuildMemberRoleReq()
                    {
                        MemberCharacterId = memberId,
                        GuildRole = guildRole
                    });
                }
            }
            SendSetGuildRoleToClients(guild, guildRole, roleName, canInvite, canKick, shareExpPercentage);
            // Save to database
            DbServiceClient.UpdateGuildRoleAsync(new UpdateGuildRoleReq()
            {
                GuildId = guildId,
                GuildRole = guildRole,
                RoleName = roleName,
                CanInvite = canInvite,
                CanKick = canKick,
                ShareExpPercentage = shareExpPercentage
            });
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
                ChatNetworkManager.SendSetGuildRole(null, MMOMessageTypes.UpdateGuild, guildId, guildRole, roleName, canInvite, canKick, shareExpPercentage);
        }

        public override void SetGuildMemberRole(BasePlayerCharacterEntity playerCharacterEntity, string characterId, byte guildRole)
        {
            int guildId;
            GuildData guild;
            if (!CanSetGuildMemberRole(playerCharacterEntity, out guildId, out guild))
                return;

            base.SetGuildMemberRole(playerCharacterEntity, characterId, guildRole);
            // Save to database
            DbServiceClient.UpdateGuildMemberRoleAsync(new UpdateGuildMemberRoleReq()
            {
                MemberCharacterId = characterId,
                GuildRole = guildRole
            });
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
                ChatNetworkManager.SendSetGuildMemberRole(null, MMOMessageTypes.UpdateGuild, guildId, characterId, guildRole);
        }

        public override void AddGuildMember(BasePlayerCharacterEntity inviteCharacterEntity, BasePlayerCharacterEntity acceptCharacterEntity)
        {
            int guildId;
            GuildData guild;
            if (!CanAddGuildMember(inviteCharacterEntity, acceptCharacterEntity, out guildId, out guild))
                return;

            base.AddGuildMember(inviteCharacterEntity, acceptCharacterEntity);
            // Save to database
            DbServiceClient.UpdateCharacterGuildAsync(new UpdateCharacterGuildReq()
            {
                CharacterId = acceptCharacterEntity.Id,
                GuildId = guildId,
                GuildRole = guild.GetMemberRole(acceptCharacterEntity.Id)
            });
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
                ChatNetworkManager.SendAddSocialMember(null, MMOMessageTypes.UpdateGuildMember, guildId, acceptCharacterEntity.Id, acceptCharacterEntity.CharacterName, acceptCharacterEntity.DataId, acceptCharacterEntity.Level);
        }

        public override void KickFromGuild(BasePlayerCharacterEntity playerCharacterEntity, string characterId)
        {
            int guildId;
            GuildData guild;
            if (!CanKickFromGuild(playerCharacterEntity, characterId, out guildId, out guild))
                return;

            base.KickFromGuild(playerCharacterEntity, characterId);
            // Save to database
            DbServiceClient.UpdateCharacterGuildAsync(new UpdateCharacterGuildReq()
            {
                CharacterId = characterId,
                GuildId = 0,
                GuildRole = 0
            });
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
                ChatNetworkManager.SendRemoveSocialMember(null, MMOMessageTypes.UpdateGuildMember, guildId, characterId);
        }

        public override void LeaveGuild(BasePlayerCharacterEntity playerCharacterEntity)
        {
            int guildId;
            GuildData guild;
            if (!CanLeaveGuild(playerCharacterEntity, out guildId, out guild))
                return;

            // If it is leader kick all members and terminate guild
            if (guild.IsLeader(playerCharacterEntity))
            {
                foreach (string memberId in guild.GetMemberIds())
                {
                    BasePlayerCharacterEntity memberCharacterEntity;
                    if (playerCharactersById.TryGetValue(memberId, out memberCharacterEntity))
                    {
                        memberCharacterEntity.ClearGuild();
                        SendGuildTerminateToClient(memberCharacterEntity.ConnectionId, guildId);
                    }
                    // Save to database
                    DbServiceClient.UpdateCharacterGuildAsync(new UpdateCharacterGuildReq()
                    {
                        CharacterId = memberId,
                        GuildId = 0,
                        GuildRole = 0
                    });
                    // Broadcast via chat server
                    if (ChatNetworkManager.IsClientConnected)
                        ChatNetworkManager.SendRemoveSocialMember(null, MMOMessageTypes.UpdateGuildMember, guildId, memberId);
                }
                guilds.Remove(guildId);
                // Save to database
                DbServiceClient.DeleteGuildAsync(new DeleteGuildReq()
                {
                    GuildId = guildId
                });
                // Broadcast via chat server
                if (ChatNetworkManager.IsClientConnected)
                    ChatNetworkManager.SendGuildTerminate(null, MMOMessageTypes.UpdateGuild, guildId);
            }
            else
            {
                playerCharacterEntity.ClearGuild();
                SendGuildTerminateToClient(playerCharacterEntity.ConnectionId, guildId);
                guild.RemoveMember(playerCharacterEntity.Id);
                guilds[guildId] = guild;
                SendRemoveGuildMemberToClients(guild, playerCharacterEntity.Id);
                // Save to database
                DbServiceClient.UpdateCharacterGuildAsync(new UpdateCharacterGuildReq()
                {
                    CharacterId = playerCharacterEntity.Id,
                    GuildId = 0,
                    GuildRole = 0
                });
                // Broadcast via chat server
                if (ChatNetworkManager.IsClientConnected)
                    ChatNetworkManager.SendRemoveSocialMember(null, MMOMessageTypes.UpdateGuildMember, guildId, playerCharacterEntity.Id);
            }
        }

        public override void IncreaseGuildExp(BasePlayerCharacterEntity playerCharacterEntity, int exp)
        {
            int guildId;
            GuildData guild;
            if (!CanIncreaseGuildExp(playerCharacterEntity, exp, out guildId, out guild))
                return;
            base.IncreaseGuildExp(playerCharacterEntity, exp);
            // Save to database
            DbServiceClient.UpdateGuildLevelAsync(new UpdateGuildLevelReq()
            {
                GuildId = guildId,
                Level = guild.level,
                Exp = guild.exp,
                SkillPoint = guild.skillPoint
            });
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
                ChatNetworkManager.SendGuildLevelExpSkillPoint(null, MMOMessageTypes.UpdateGuild, guildId, guild.level, guild.exp, guild.skillPoint);
        }

        public override void AddGuildSkill(BasePlayerCharacterEntity playerCharacterEntity, int dataId)
        {
            int guildId;
            GuildData guild;
            if (!CanAddGuildSkill(playerCharacterEntity, dataId, out guildId, out guild))
                return;
            base.AddGuildSkill(playerCharacterEntity, dataId);
            // Save to database
            DbServiceClient.UpdateGuildSkillLevelAsync(new UpdateGuildSkillLevelReq()
            {
                GuildId = guildId,
                DataId = dataId,
                SkillLevel = guild.GetSkillLevel(dataId),
                SkillPoint = guild.skillPoint
            });
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
            {
                ChatNetworkManager.SendSetGuildSkillLevel(null, MMOMessageTypes.UpdateGuild, guildId, dataId, guild.GetSkillLevel(dataId));
                ChatNetworkManager.SendSetGuildGold(null, MMOMessageTypes.UpdateGuild, guildId, guild.gold);
                ChatNetworkManager.SendGuildLevelExpSkillPoint(null, MMOMessageTypes.UpdateGuild, guildId, guild.level, guild.exp, guild.skillPoint);
            }
        }

        public override void OpenStorage(BasePlayerCharacterEntity playerCharacterEntity)
        {
            if (!CanAccessStorage(playerCharacterEntity, playerCharacterEntity.CurrentStorageId))
            {
                SendServerGameMessage(playerCharacterEntity.ConnectionId, GameMessage.Type.CannotAccessStorage);
                return;
            }
            if (!storageItems.ContainsKey(playerCharacterEntity.CurrentStorageId))
                storageItems[playerCharacterEntity.CurrentStorageId] = new List<CharacterItem>();
            if (!usingStorageCharacters.ContainsKey(playerCharacterEntity.CurrentStorageId))
                usingStorageCharacters[playerCharacterEntity.CurrentStorageId] = new HashSet<uint>();
            usingStorageCharacters[playerCharacterEntity.CurrentStorageId].Add(playerCharacterEntity.ObjectId);
            OpenStorageRoutine(playerCharacterEntity);
        }

        private async void OpenStorageRoutine(BasePlayerCharacterEntity playerCharacterEntity)
        {
            List<CharacterItem> result = new List<CharacterItem>();
            if (playerCharacterEntity.CurrentStorageId.storageType == StorageType.Guild)
            {
                // Have to reload guild storage because it can be changed by other players in other map-server
                await LoadStorageRoutine(playerCharacterEntity.CurrentStorageId);
            }
            result = storageItems[playerCharacterEntity.CurrentStorageId];
            // Prepare storage data
            Storage storage = GetStorage(playerCharacterEntity.CurrentStorageId);
            bool isLimitSlot = storage.slotLimit > 0;
            short slotLimit = storage.slotLimit;
            result.FillEmptySlots(isLimitSlot, slotLimit);
            // Update storage items
            playerCharacterEntity.StorageItems = result;
        }

        public override void CloseStorage(BasePlayerCharacterEntity playerCharacterEntity)
        {
            if (usingStorageCharacters.ContainsKey(playerCharacterEntity.CurrentStorageId))
                usingStorageCharacters[playerCharacterEntity.CurrentStorageId].Remove(playerCharacterEntity.ObjectId);
            playerCharacterEntity.StorageItems.Clear();
        }

        public override void MoveItemToStorage(BasePlayerCharacterEntity playerCharacterEntity, StorageId storageId, short nonEquipIndex, short amount, short storageItemIndex)
        {
            if (!CanAccessStorage(playerCharacterEntity, storageId))
            {
                SendServerGameMessage(playerCharacterEntity.ConnectionId, GameMessage.Type.CannotAccessStorage);
                return;
            }
            if (!storageItems.ContainsKey(storageId))
                storageItems[storageId] = new List<CharacterItem>();
            MoveItemToStorageRoutine(playerCharacterEntity, storageId, nonEquipIndex, amount, storageItemIndex);
        }

        private async void MoveItemToStorageRoutine(BasePlayerCharacterEntity playerCharacterEntity, StorageId storageId, short nonEquipIndex, short amount, short storageItemIndex)
        {
            List<CharacterItem> storageItemList = new List<CharacterItem>();
            if (storageId.storageType == StorageType.Guild)
            {
                // Have to reload guild storage because it can be changed by other players in other map-server
                await LoadStorageRoutine(storageId);
            }
            storageItemList = storageItems[storageId];

            if (nonEquipIndex < 0 || nonEquipIndex >= playerCharacterEntity.NonEquipItems.Count)
            {
                // Don't do anything, if non equip item index is invalid
            }
            else
            {
                // Prepare storage data
                Storage storage = GetStorage(storageId);
                bool isLimitWeight = storage.weightLimit > 0;
                bool isLimitSlot = storage.slotLimit > 0;
                short weightLimit = storage.weightLimit;
                short slotLimit = storage.slotLimit;
                // Prepare item data
                CharacterItem movingItem = playerCharacterEntity.NonEquipItems[nonEquipIndex].Clone();
                movingItem.id = GenericUtils.GetUniqueId();
                movingItem.amount = amount;
                if (storageItemIndex < 0 ||
                    storageItemIndex >= storageItemList.Count ||
                    storageItemList[storageItemIndex].dataId == movingItem.dataId)
                {
                    // Add to storage or merge
                    bool isOverwhelming = storageItemList.IncreasingItemsWillOverwhelming(
                        movingItem.dataId, movingItem.amount, isLimitWeight, weightLimit,
                        storageItemList.GetTotalItemWeight(), isLimitSlot, slotLimit);
                    if (!isOverwhelming && storageItemList.IncreaseItems(movingItem))
                    {
                        // Remove from inventory
                        playerCharacterEntity.DecreaseItemsByIndex(nonEquipIndex, amount);
                    }
                }
                else
                {
                    // Swapping
                    CharacterItem storageItem = storageItemList[storageItemIndex];
                    CharacterItem nonEquipItem = playerCharacterEntity.NonEquipItems[nonEquipIndex];
                    storageItem.id = GenericUtils.GetUniqueId();
                    nonEquipItem.id = GenericUtils.GetUniqueId();
                    storageItemList[storageItemIndex] = nonEquipItem;
                    playerCharacterEntity.NonEquipItems[nonEquipIndex] = storageItem;
                }
                storageItemList.FillEmptySlots(isLimitSlot, slotLimit);
                // Update storage list immediately
                if (storageId.storageType == StorageType.Guild)
                {
                    // TODO: Have to test about race condition while running multiple-server
                    UpdateStorageItemsReq req = new UpdateStorageItemsReq()
                    {
                        StorageType = (EStorageType)storageId.storageType,
                        StorageOwnerId = storageId.storageOwnerId
                    };
                    DatabaseServiceUtils.CopyToRepeatedByteString(storageItemList, req.StorageCharacterItems);
                    await DbServiceClient.UpdateStorageItemsAsync(req);
                }
                // Update storage items to characters that open the storage
                UpdateStorageItemsToCharacters(usingStorageCharacters[storageId], storageItemList);
            }
        }

        public override void MoveItemFromStorage(BasePlayerCharacterEntity playerCharacterEntity, StorageId storageId, short storageItemIndex, short amount, short nonEquipIndex)
        {
            if (!CanAccessStorage(playerCharacterEntity, storageId))
            {
                SendServerGameMessage(playerCharacterEntity.ConnectionId, GameMessage.Type.CannotAccessStorage);
                return;
            }
            if (!storageItems.ContainsKey(storageId))
                storageItems[storageId] = new List<CharacterItem>();
            MoveItemFromStorageRoutine(playerCharacterEntity, storageId, storageItemIndex, amount, nonEquipIndex);
        }

        private async void MoveItemFromStorageRoutine(BasePlayerCharacterEntity playerCharacterEntity, StorageId storageId, short storageItemIndex, short amount, short nonEquipIndex)
        {
            List<CharacterItem> storageItemList = new List<CharacterItem>();
            if (storageId.storageType == StorageType.Guild)
            {
                // Have to reload guild storage because it can be changed by other players in other map-server
                await LoadStorageRoutine(storageId);
            }
            storageItemList = storageItems[storageId];

            if (storageItemIndex < 0 || storageItemIndex >= storageItemList.Count)
            {
                // Don't do anything, if storage item index is invalid
            }
            else
            {
                // Prepare storage data
                Storage storage = GetStorage(storageId);
                bool isLimitSlot = storage.slotLimit > 0;
                short slotLimit = storage.slotLimit;
                // Prepare item data
                CharacterItem movingItem = storageItemList[storageItemIndex].Clone();
                movingItem.id = GenericUtils.GetUniqueId();
                movingItem.amount = amount;
                if (nonEquipIndex < 0 ||
                    nonEquipIndex >= playerCharacterEntity.NonEquipItems.Count ||
                    playerCharacterEntity.NonEquipItems[nonEquipIndex].dataId == movingItem.dataId)
                {
                    // Add to inventory or merge
                    bool isOverwhelming = playerCharacterEntity.IncreasingItemsWillOverwhelming(movingItem.dataId, movingItem.amount);
                    if (!isOverwhelming && playerCharacterEntity.IncreaseItems(movingItem))
                    {
                        // Remove from storage
                        storageItemList.DecreaseItemsByIndex(storageItemIndex, amount, isLimitSlot);
                    }
                }
                else
                {
                    // Swapping
                    CharacterItem storageItem = storageItemList[storageItemIndex];
                    CharacterItem nonEquipItem = playerCharacterEntity.NonEquipItems[nonEquipIndex];
                    storageItem.id = GenericUtils.GetUniqueId();
                    nonEquipItem.id = GenericUtils.GetUniqueId();
                    storageItemList[storageItemIndex] = nonEquipItem;
                    playerCharacterEntity.NonEquipItems[nonEquipIndex] = storageItem;
                }
                storageItemList.FillEmptySlots(isLimitSlot, slotLimit);
                playerCharacterEntity.FillEmptySlots();
                // Update storage list immediately
                if (storageId.storageType == StorageType.Guild)
                {
                    // TODO: Have to test about race condition while running multiple-server
                    UpdateStorageItemsReq req = new UpdateStorageItemsReq()
                    {
                        StorageType = (EStorageType)storageId.storageType,
                        StorageOwnerId = storageId.storageOwnerId
                    };
                    DatabaseServiceUtils.CopyToRepeatedByteString(storageItemList, req.StorageCharacterItems);
                    await DbServiceClient.UpdateStorageItemsAsync(req);
                }
                // Update storage items to characters that open the storage
                UpdateStorageItemsToCharacters(usingStorageCharacters[storageId], storageItemList);
            }
        }

        public override void IncreaseStorageItems(StorageId storageId, CharacterItem addingItem, Action<bool> callback, int minSlotIndex = 0)
        {
            if (!storageItems.ContainsKey(storageId))
                storageItems[storageId] = new List<CharacterItem>();
            IncreaseStorageItemsRoutine(storageId, addingItem, callback, minSlotIndex);
        }

        private async void IncreaseStorageItemsRoutine(StorageId storageId, CharacterItem addingItem, Action<bool> callback, int minSlotIndex = 0)
        {
            List<CharacterItem> storageItemList = new List<CharacterItem>();
            if (storageId.storageType == StorageType.Guild)
            {
                // Have to reload guild storage because it can be changed by other players in other map-server
                await LoadStorageRoutine(storageId);
            }
            storageItemList = storageItems[storageId];

            // Prepare storage data
            Storage storage = GetStorage(storageId);
            bool isLimitSlot = storage.slotLimit > 0;
            short slotLimit = storage.slotLimit;
            // Increase item to storage
            bool increaseResult = storageItemList.IncreaseItems(addingItem, minSlotIndex);
            if (callback != null)
                callback.Invoke(increaseResult);
            // Update slots
            storageItemList.FillEmptySlots(isLimitSlot, slotLimit);
            // Update storage list immediately
            if (storageId.storageType == StorageType.Guild)
            {
                // TODO: Have to test about race condition while running multiple-server
                UpdateStorageItemsReq req = new UpdateStorageItemsReq()
                {
                    StorageType = (EStorageType)storageId.storageType,
                    StorageOwnerId = storageId.storageOwnerId
                };
                DatabaseServiceUtils.CopyToRepeatedByteString(storageItemList, req.StorageCharacterItems);
                await DbServiceClient.UpdateStorageItemsAsync(req);
            }
            // Update storage items to characters that open the storage
            UpdateStorageItemsToCharacters(usingStorageCharacters[storageId], storageItemList);
        }

        public override void DecreaseStorageItems(StorageId storageId, int dataId, short amount, Action<bool, Dictionary<CharacterItem, short>> callback)
        {
            if (!storageItems.ContainsKey(storageId))
                storageItems[storageId] = new List<CharacterItem>();
            DecreaseStorageItemsRoutine(storageId, dataId, amount, callback);
        }

        private async void DecreaseStorageItemsRoutine(StorageId storageId, int dataId, short amount, Action<bool, Dictionary<CharacterItem, short>> callback)
        {
            List<CharacterItem> storageItemList = new List<CharacterItem>();
            if (storageId.storageType == StorageType.Guild)
            {
                // Have to reload guild storage because it can be changed by other players in other map-server
                await LoadStorageRoutine(storageId);
            }
            storageItemList = storageItems[storageId];

            // Prepare storage data
            Storage storage = GetStorage(storageId);
            bool isLimitSlot = storage.slotLimit > 0;
            short slotLimit = storage.slotLimit;
            // Increase item to storage
            Dictionary<CharacterItem, short> decreaseItems;
            bool decreaseResult = storageItemList.DecreaseItems(dataId, amount, isLimitSlot, out decreaseItems);
            if (callback != null)
                callback.Invoke(decreaseResult, decreaseItems);
            // Update slots
            storageItemList.FillEmptySlots(isLimitSlot, slotLimit);
            // Update storage list immediately
            if (storageId.storageType == StorageType.Guild)
            {
                // TODO: Have to test about race condition while running multiple-server
                UpdateStorageItemsReq req = new UpdateStorageItemsReq()
                {
                    StorageType = (EStorageType)storageId.storageType,
                    StorageOwnerId = storageId.storageOwnerId
                };
                DatabaseServiceUtils.CopyToRepeatedByteString(storageItemList, req.StorageCharacterItems);
                await DbServiceClient.UpdateStorageItemsAsync(req);
            }
            // Update storage items to characters that open the storage
            UpdateStorageItemsToCharacters(usingStorageCharacters[storageId], storageItemList);
        }

        public override void SwapOrMergeStorageItem(BasePlayerCharacterEntity playerCharacterEntity, StorageId storageId, short fromIndex, short toIndex)
        {
            if (!CanAccessStorage(playerCharacterEntity, storageId))
            {
                SendServerGameMessage(playerCharacterEntity.ConnectionId, GameMessage.Type.CannotAccessStorage);
                return;
            }
            if (!storageItems.ContainsKey(storageId))
                storageItems[storageId] = new List<CharacterItem>();
            SwapOrMergeStorageItemRoutine(playerCharacterEntity, storageId, fromIndex, toIndex);
        }

        private async void SwapOrMergeStorageItemRoutine(BasePlayerCharacterEntity playerCharacterEntity, StorageId storageId, short fromIndex, short toIndex)
        {
            List<CharacterItem> storageItemList = new List<CharacterItem>();
            if (storageId.storageType == StorageType.Guild)
            {
                // Have to reload guild storage because it can be changed by other players in other map-server
                await LoadStorageRoutine(storageId);
            }
            storageItemList = storageItems[storageId];

            if (fromIndex >= storageItemList.Count || toIndex >= storageItemList.Count)
            {
                // Don't do anything, if storage item index is invalid
            }
            else
            {
                // Prepare storage data
                Storage storage = GetStorage(storageId);
                bool isLimitSlot = storage.slotLimit > 0;
                short slotLimit = storage.slotLimit;
                // Prepare item data
                CharacterItem fromItem = storageItemList[fromIndex];
                CharacterItem toItem = storageItemList[toIndex];
                fromItem.id = GenericUtils.GetUniqueId();
                toItem.id = GenericUtils.GetUniqueId();
                if (fromItem.dataId.Equals(toItem.dataId) && !fromItem.IsFull() && !toItem.IsFull())
                {
                    // Merge if same id and not full
                    short maxStack = toItem.GetMaxStack();
                    if (toItem.amount + fromItem.amount <= maxStack)
                    {
                        toItem.amount += fromItem.amount;
                        storageItemList[fromIndex] = CharacterItem.Empty;
                        storageItemList[toIndex] = toItem;
                    }
                    else
                    {
                        short remains = (short)(toItem.amount + fromItem.amount - maxStack);
                        toItem.amount = maxStack;
                        fromItem.amount = remains;
                        storageItemList[fromIndex] = fromItem;
                        storageItemList[toIndex] = toItem;
                    }
                }
                else
                {
                    // Swap
                    storageItemList[fromIndex] = toItem;
                    storageItemList[toIndex] = fromItem;
                }
                storageItemList.FillEmptySlots(isLimitSlot, slotLimit);
                // Update storage list immediately
                if (storageId.storageType == StorageType.Guild)
                {
                    // TODO: Have to test about race condition while running multiple-server
                    UpdateStorageItemsReq req = new UpdateStorageItemsReq()
                    {
                        StorageType = (EStorageType)storageId.storageType,
                        StorageOwnerId = storageId.storageOwnerId
                    };
                    DatabaseServiceUtils.CopyToRepeatedByteString(storageItemList, req.StorageCharacterItems);
                    await DbServiceClient.UpdateStorageItemsAsync(req);
                }
                // Update storage items to characters that open the storage
                UpdateStorageItemsToCharacters(usingStorageCharacters[storageId], storageItemList);
            }
        }

        public override bool IsStorageEntityOpen(StorageEntity storageEntity)
        {
            if (storageEntity == null)
                return false;
            StorageId id = new StorageId(StorageType.Building, storageEntity.Id);
            return usingStorageCharacters.ContainsKey(id) &&
                usingStorageCharacters[id].Count > 0;
        }

        public override List<CharacterItem> GetStorageEntityItems(StorageEntity storageEntity)
        {
            if (storageEntity == null)
                return new List<CharacterItem>();
            StorageId id = new StorageId(StorageType.Building, storageEntity.Id);
            if (!storageItems.ContainsKey(id))
                storageItems[id] = new List<CharacterItem>();
            return storageItems[id];
        }

        private void UpdateStorageItemsToCharacters(HashSet<uint> objectIds, List<CharacterItem> storageItems)
        {
            BasePlayerCharacterEntity playerCharacterEntity;
            foreach (uint objectId in objectIds)
            {
                if (Assets.TryGetSpawnedObject(objectId, out playerCharacterEntity))
                {
                    // Update storage items
                    playerCharacterEntity.StorageItems = storageItems;
                }
            }
        }

        public override void DepositGold(BasePlayerCharacterEntity playerCharacterEntity, int amount)
        {
            DepositGoldRoutine(playerCharacterEntity, amount);
        }

        private async void DepositGoldRoutine(BasePlayerCharacterEntity playerCharacterEntity, int amount)
        {
            if (playerCharacterEntity.Gold - amount >= 0)
            {
                // Get gold amount
                GoldResp goldResp = await DbServiceClient.GetGoldAsync(new GetGoldReq()
                {
                    UserId = playerCharacterEntity.UserId
                });
                int gold = goldResp.Gold + amount;
                // Update gold amount
                await DbServiceClient.UpdateGoldAsync(new UpdateGoldReq()
                {
                    UserId = playerCharacterEntity.UserId,
                    Amount = gold
                });
                playerCharacterEntity.UserGold = gold;
                playerCharacterEntity.Gold -= amount;
            }
            else
                SendServerGameMessage(playerCharacterEntity.ConnectionId, GameMessage.Type.NotEnoughGoldToDeposit);
        }

        public override void WithdrawGold(BasePlayerCharacterEntity playerCharacterEntity, int amount)
        {
            WithdrawGoldRoutine(playerCharacterEntity, amount);
        }

        private async void WithdrawGoldRoutine(BasePlayerCharacterEntity playerCharacterEntity, int amount)
        {
            // Get gold amount
            GoldResp goldResp = await DbServiceClient.GetGoldAsync(new GetGoldReq()
            {
                UserId = playerCharacterEntity.UserId
            });
            int gold = goldResp.Gold - amount;
            if (gold >= 0)
            {
                // Update gold amount
                await DbServiceClient.UpdateGoldAsync(new UpdateGoldReq()
                {
                    UserId = playerCharacterEntity.UserId,
                    Amount = gold
                });
                playerCharacterEntity.UserGold = gold;
                playerCharacterEntity.Gold += amount;
            }
            else
                SendServerGameMessage(playerCharacterEntity.ConnectionId, GameMessage.Type.NotEnoughGoldToWithdraw);
        }

        public override void DepositGuildGold(BasePlayerCharacterEntity playerCharacterEntity, int amount)
        {
            DepositGuildGoldRoutine(playerCharacterEntity, amount);
        }

        private async void DepositGuildGoldRoutine(BasePlayerCharacterEntity playerCharacterEntity, int amount)
        {
            GuildData guild;
            if (guilds.TryGetValue(playerCharacterEntity.GuildId, out guild))
            {
                if (playerCharacterEntity.Gold - amount >= 0)
                {
                    // Get gold amount
                    GuildGoldResp goldResp = await DbServiceClient.GetGuildGoldAsync(new GetGuildGoldReq()
                    {
                        GuildId = playerCharacterEntity.GuildId
                    });
                    int gold = goldResp.GuildGold + amount;
                    // Update gold amount
                    await DbServiceClient.UpdateGuildGoldAsync(new UpdateGuildGoldReq()
                    {
                        GuildId = playerCharacterEntity.GuildId,
                        Amount = gold
                    });
                    guild.gold = gold;
                    playerCharacterEntity.Gold -= amount;
                    guilds[playerCharacterEntity.GuildId] = guild;
                    SendSetGuildGoldToClients(guild);
                }
                else
                    SendServerGameMessage(playerCharacterEntity.ConnectionId, GameMessage.Type.NotEnoughGoldToDeposit);
            }
            else
                SendServerGameMessage(playerCharacterEntity.ConnectionId, GameMessage.Type.NotJoinedGuild);
        }

        public override void WithdrawGuildGold(BasePlayerCharacterEntity playerCharacterEntity, int amount)
        {
            WithdrawGuildGoldRoutine(playerCharacterEntity, amount);
        }

        private async void WithdrawGuildGoldRoutine(BasePlayerCharacterEntity playerCharacterEntity, int amount)
        {
            GuildData guild;
            if (guilds.TryGetValue(playerCharacterEntity.GuildId, out guild))
            {
                // Get gold amount
                GuildGoldResp goldResp = await DbServiceClient.GetGuildGoldAsync(new GetGuildGoldReq()
                {
                    GuildId = playerCharacterEntity.GuildId
                });
                int gold = goldResp.GuildGold - amount;
                if (gold >= 0)
                {
                    // Update gold amount
                    await DbServiceClient.UpdateGoldAsync(new UpdateGoldReq()
                    {
                        UserId = playerCharacterEntity.UserId,
                        Amount = gold
                    });
                    guild.gold = gold;
                    playerCharacterEntity.Gold += amount;
                    guilds[playerCharacterEntity.GuildId] = guild;
                    SendSetGuildGoldToClients(guild);
                }
                else
                    SendServerGameMessage(playerCharacterEntity.ConnectionId, GameMessage.Type.NotEnoughGoldToWithdraw);
            }
            else
                SendServerGameMessage(playerCharacterEntity.ConnectionId, GameMessage.Type.NotJoinedGuild);
        }

        public override void FindCharacters(BasePlayerCharacterEntity playerCharacterEntity, string characterName)
        {
            FindCharactersRoutine(playerCharacterEntity, characterName);
        }

        private async void FindCharactersRoutine(BasePlayerCharacterEntity playerCharacterEntity, string characterName)
        {
            FindCharactersResp resp = await DbServiceClient.FindCharactersAsync(new FindCharactersReq()
            {
                CharacterName = characterName
            });
            SendUpdateFoundCharactersToClient(playerCharacterEntity.ConnectionId, resp.List.MakeArrayFromRepeatedByteString<SocialCharacterData>());
        }

        public override void AddFriend(BasePlayerCharacterEntity playerCharacterEntity, string friendCharacterId)
        {
            AddFriendRoutine(playerCharacterEntity, friendCharacterId);
        }

        private async void AddFriendRoutine(BasePlayerCharacterEntity playerCharacterEntity, string friendCharacterId)
        {
            await DbServiceClient.CreateFriendAsync(new CreateFriendReq()
            {
                CharacterId1 = playerCharacterEntity.Id,
                CharacterId2 = friendCharacterId
            });
            GetFriends(playerCharacterEntity);
        }

        public override void RemoveFriend(BasePlayerCharacterEntity playerCharacterEntity, string friendCharacterId)
        {
            RemoveFriendRoutine(playerCharacterEntity, friendCharacterId);
        }

        private async void RemoveFriendRoutine(BasePlayerCharacterEntity playerCharacterEntity, string friendCharacterId)
        {
            await DbServiceClient.DeleteFriendAsync(new DeleteFriendReq()
            {
                CharacterId1 = playerCharacterEntity.Id,
                CharacterId2 = friendCharacterId
            });
            GetFriends(playerCharacterEntity);
        }

        public override void GetFriends(BasePlayerCharacterEntity playerCharacterEntity)
        {
            GetFriendsRoutine(playerCharacterEntity);
        }

        private async void GetFriendsRoutine(BasePlayerCharacterEntity playerCharacterEntity)
        {
            ReadFriendsResp readFriendsResp = await DbServiceClient.ReadFriendsAsync(new ReadFriendsReq()
            {
                CharacterId = playerCharacterEntity.Id
            });
            SendUpdateFriendsToClient(playerCharacterEntity.ConnectionId, readFriendsResp.List.MakeArrayFromRepeatedByteString<SocialCharacterData>());
        }
    }
}
