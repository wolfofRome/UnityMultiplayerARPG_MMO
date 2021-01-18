﻿using Cysharp.Threading.Tasks;
using LiteNetLibManager;
using UnityEngine;

namespace MultiplayerARPG.MMO
{
    public class MMOServerGuildMessageHandlers : MonoBehaviour, IServerGuildMessageHandlers
    {
        public ChatNetworkManager ChatNetworkManager { get; private set; }

#if UNITY_STANDALONE && !CLIENT_BUILD
        public DatabaseService.DatabaseServiceClient DbServiceClient
        {
            get { return MMOServerInstance.Singleton.DatabaseNetworkManager.ServiceClient; }
        }
#endif

        private void Awake()
        {
            ChatNetworkManager = GetComponent<ChatNetworkManager>();
        }

        public async UniTaskVoid HandleRequestAcceptGuildInvitation(RequestHandlerData requestHandler, RequestAcceptGuildInvitationMessage request, RequestProceedResultDelegate<ResponseAcceptGuildInvitationMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            await UniTask.Yield();
            BasePlayerCharacterEntity playerCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.Invoke(AckResponseCode.Error, new ResponseAcceptGuildInvitationMessage()
                {
                    error = UITextKeys.UI_ERROR_NOT_LOGGED_IN,
                });
                return;
            }
            ValidateGuildRequestResult validateResult = GameInstance.ServerGuildHandlers.CanAcceptGuildInvitation(request.guildId, playerCharacter);
            if (!validateResult.IsSuccess)
            {
                GameInstance.ServerGameMessageHandlers.SendGameMessage(requestHandler.ConnectionId, validateResult.GameMessage);
                result.Invoke(AckResponseCode.Error, new ResponseAcceptGuildInvitationMessage()
                {
                    error = validateResult.GameMessage,
                });
                return;
            }
            playerCharacter.GuildId = request.guildId;
            validateResult.Guild.AddMember(playerCharacter);
            GameInstance.ServerGuildHandlers.SetGuild(request.guildId, validateResult.Guild);
            GameInstance.ServerGuildHandlers.RemoveGuildInvitation(request.guildId, playerCharacter.Id);
            // Save to database
            _ = DbServiceClient.UpdateCharacterGuildAsync(new UpdateCharacterGuildReq()
            {
                SocialCharacterData = DatabaseServiceUtils.ToByteString(SocialCharacterData.Create(playerCharacter)),
                GuildId = request.guildId,
                GuildRole = validateResult.Guild.GetMemberRole(playerCharacter.Id)
            });
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
                ChatNetworkManager.SendAddSocialMember(null, MMOMessageTypes.UpdateGuildMember, request.guildId, playerCharacter.Id, playerCharacter.CharacterName, playerCharacter.DataId, playerCharacter.Level);
            GameInstance.ServerGameMessageHandlers.SendGameMessage(requestHandler.ConnectionId, UITextKeys.UI_GUILD_INVITATION_ACCEPTED);
            GameInstance.ServerGameMessageHandlers.SendSetGuildData(requestHandler.ConnectionId, validateResult.Guild);
            GameInstance.ServerGameMessageHandlers.SendAddGuildMembersToOne(requestHandler.ConnectionId, validateResult.Guild);
            GameInstance.ServerGameMessageHandlers.SendAddGuildMembersToMembers(validateResult.Guild, playerCharacter.Id, playerCharacter.CharacterName, playerCharacter.DataId, playerCharacter.Level);
            result.Invoke(AckResponseCode.Success, new ResponseAcceptGuildInvitationMessage());
#endif
        }

        public async UniTaskVoid HandleRequestDeclineGuildInvitation(RequestHandlerData requestHandler, RequestDeclineGuildInvitationMessage request, RequestProceedResultDelegate<ResponseDeclineGuildInvitationMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            await UniTask.Yield();
            BasePlayerCharacterEntity playerCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.Invoke(AckResponseCode.Error, new ResponseDeclineGuildInvitationMessage()
                {
                    error = UITextKeys.UI_ERROR_NOT_LOGGED_IN,
                });
                return;
            }
            ValidateGuildRequestResult validateResult = GameInstance.ServerGuildHandlers.CanDeclineGuildInvitation(request.guildId, playerCharacter);
            if (!validateResult.IsSuccess)
            {
                GameInstance.ServerGameMessageHandlers.SendGameMessage(requestHandler.ConnectionId, validateResult.GameMessage);
                result.Invoke(AckResponseCode.Error, new ResponseDeclineGuildInvitationMessage()
                {
                    error = validateResult.GameMessage,
                });
                return;
            }
            GameInstance.ServerGuildHandlers.RemoveGuildInvitation(request.guildId, playerCharacter.Id);
            GameInstance.ServerGameMessageHandlers.SendGameMessage(requestHandler.ConnectionId, UITextKeys.UI_GUILD_INVITATION_DECLINED);
            result.Invoke(AckResponseCode.Success, new ResponseDeclineGuildInvitationMessage());
#endif
        }

        public async UniTaskVoid HandleRequestSendGuildInvitation(RequestHandlerData requestHandler, RequestSendGuildInvitationMessage request, RequestProceedResultDelegate<ResponseSendGuildInvitationMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            await UniTask.Yield();
            BasePlayerCharacterEntity playerCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.Invoke(AckResponseCode.Error, new ResponseSendGuildInvitationMessage()
                {
                    error = UITextKeys.UI_ERROR_NOT_LOGGED_IN,
                });
                return;
            }
            BasePlayerCharacterEntity inviteeCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacterById(request.inviteeId, out inviteeCharacter))
            {
                GameInstance.ServerGameMessageHandlers.SendGameMessage(requestHandler.ConnectionId, UITextKeys.UI_ERROR_CHARACTER_NOT_FOUND);
                result.Invoke(AckResponseCode.Error, new ResponseSendGuildInvitationMessage()
                {
                    error = UITextKeys.UI_ERROR_CHARACTER_NOT_FOUND,
                });
                return;
            }
            ValidateGuildRequestResult validateResult = GameInstance.ServerGuildHandlers.CanSendGuildInvitation(playerCharacter, inviteeCharacter);
            if (!validateResult.IsSuccess)
            {
                GameInstance.ServerGameMessageHandlers.SendGameMessage(requestHandler.ConnectionId, validateResult.GameMessage);
                result.Invoke(AckResponseCode.Error, new ResponseSendGuildInvitationMessage()
                {
                    error = validateResult.GameMessage,
                });
                return;
            }
            GameInstance.ServerGuildHandlers.AppendGuildInvitation(playerCharacter.GuildId, request.inviteeId);
            GameInstance.ServerGameMessageHandlers.SendNotifyGuildInvitation(inviteeCharacter.ConnectionId, new GuildInvitationData()
            {
                InviterId = playerCharacter.Id,
                InviterName = playerCharacter.CharacterName,
                InviterLevel = playerCharacter.Level,
                GuildId = validateResult.GuildId,
                GuildName = validateResult.Guild.guildName,
                GuildLevel = validateResult.Guild.level,
            });
            result.Invoke(AckResponseCode.Success, new ResponseSendGuildInvitationMessage());
#endif
        }

        public async UniTaskVoid HandleRequestCreateGuild(RequestHandlerData requestHandler, RequestCreateGuildMessage request, RequestProceedResultDelegate<ResponseCreateGuildMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            await UniTask.Yield();
            IPlayerCharacterData playerCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.Invoke(AckResponseCode.Error, new ResponseCreateGuildMessage()
                {
                    error = UITextKeys.UI_ERROR_NOT_LOGGED_IN,
                });
                return;
            }
            ValidateGuildRequestResult validateResult = playerCharacter.CanCreateGuild(request.guildName);
            if (!validateResult.IsSuccess)
            {
                GameInstance.ServerGameMessageHandlers.SendGameMessage(requestHandler.ConnectionId, validateResult.GameMessage);
                result.Invoke(AckResponseCode.Error, new ResponseCreateGuildMessage()
                {
                    error = validateResult.GameMessage,
                });
                return;
            }
            FindGuildNameResp findGuildNameResp = await DbServiceClient.FindGuildNameAsync(new FindGuildNameReq()
            {
                GuildName = request.guildName,
            });
            if (findGuildNameResp.FoundAmount > 0)
            {
                GameInstance.ServerGameMessageHandlers.SendGameMessage(requestHandler.ConnectionId, UITextKeys.UI_ERROR_GUILD_NAME_EXISTED);
                result.Invoke(AckResponseCode.Error, new ResponseCreateGuildMessage()
                {
                    error = UITextKeys.UI_ERROR_GUILD_NAME_EXISTED,
                });
                return;
            }
            GuildResp createGuildResp = await DbServiceClient.CreateGuildAsync(new CreateGuildReq()
            {
                LeaderCharacterId = playerCharacter.Id,
                GuildName = request.guildName,
            });
            GuildData guild = DatabaseServiceUtils.FromByteString<GuildData>(createGuildResp.GuildData);
            GameInstance.Singleton.SocialSystemSetting.DecreaseCreateGuildResource(playerCharacter);
            GameInstance.ServerGuildHandlers.SetGuild(guild.id, guild);
            playerCharacter.GuildId = guild.id;
            playerCharacter.GuildRole = guild.GetMemberRole(playerCharacter.Id);
            playerCharacter.SharedGuildExp = 0;
            if (playerCharacter is BasePlayerCharacterEntity)
            {
                // Sync guild name to client
                (playerCharacter as BasePlayerCharacterEntity).GuildName = request.guildName;
            }
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
            {
                ChatNetworkManager.SendCreateGuild(null, MMOMessageTypes.UpdateGuild, guild.id, request.guildName, playerCharacter.Id);
                ChatNetworkManager.SendAddSocialMember(null, MMOMessageTypes.UpdateGuildMember, guild.id, playerCharacter.Id, playerCharacter.CharacterName, playerCharacter.DataId, playerCharacter.Level);
            }
            GameInstance.ServerGameMessageHandlers.SendSetGuildData(requestHandler.ConnectionId, guild);
            GameInstance.ServerGameMessageHandlers.SendAddGuildMembersToOne(requestHandler.ConnectionId, guild);
            result.Invoke(AckResponseCode.Success, new ResponseCreateGuildMessage());
#endif
        }

        public async UniTaskVoid HandleRequestChangeGuildLeader(RequestHandlerData requestHandler, RequestChangeGuildLeaderMessage request, RequestProceedResultDelegate<ResponseChangeGuildLeaderMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            await UniTask.Yield();
            IPlayerCharacterData playerCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.Invoke(AckResponseCode.Error, new ResponseChangeGuildLeaderMessage()
                {
                    error = UITextKeys.UI_ERROR_NOT_LOGGED_IN,
                });
                return;
            }
            ValidateGuildRequestResult validateResult = GameInstance.ServerGuildHandlers.CanChangeGuildLeader(playerCharacter, request.memberId);
            if (!validateResult.IsSuccess)
            {
                GameInstance.ServerGameMessageHandlers.SendGameMessage(requestHandler.ConnectionId, validateResult.GameMessage);
                result.Invoke(AckResponseCode.Error, new ResponseChangeGuildLeaderMessage()
                {
                    error = validateResult.GameMessage,
                });
                return;
            }
            byte swappingGuildRole = validateResult.Guild.GetMemberRole(request.memberId);
            validateResult.Guild.SetLeader(request.memberId);
            validateResult.Guild.SetMemberRole(playerCharacter.Id, swappingGuildRole);
            playerCharacter.GuildRole = swappingGuildRole;
            GameInstance.ServerGuildHandlers.SetGuild(validateResult.GuildId, validateResult.Guild);
            // Save to database
            _ = DbServiceClient.UpdateGuildLeaderAsync(new UpdateGuildLeaderReq()
            {
                GuildId = validateResult.GuildId,
                LeaderCharacterId = request.memberId
            });
            _ = DbServiceClient.UpdateGuildMemberRoleAsync(new UpdateGuildMemberRoleReq()
            {
                MemberCharacterId = request.memberId,
                GuildRole = validateResult.Guild.GetMemberRole(request.memberId)
            });
            _ = DbServiceClient.UpdateGuildMemberRoleAsync(new UpdateGuildMemberRoleReq()
            {
                MemberCharacterId = request.memberId,
                GuildRole = validateResult.Guild.GetMemberRole(request.memberId)
            });
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
                ChatNetworkManager.SendChangeGuildLeader(null, MMOMessageTypes.UpdateGuild, validateResult.GuildId, request.memberId);
            GameInstance.ServerGameMessageHandlers.SendSetGuildLeaderToMembers(validateResult.Guild);
            GameInstance.ServerGameMessageHandlers.SendSetGuildMemberRoleToMembers(validateResult.Guild, request.memberId, 0);
            result.Invoke(AckResponseCode.Success, new ResponseChangeGuildLeaderMessage());
#endif
        }

        public async UniTaskVoid HandleRequestKickMemberFromGuild(RequestHandlerData requestHandler, RequestKickMemberFromGuildMessage request, RequestProceedResultDelegate<ResponseKickMemberFromGuildMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            await UniTask.Yield();
            IPlayerCharacterData playerCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.Invoke(AckResponseCode.Error, new ResponseKickMemberFromGuildMessage()
                {
                    error = UITextKeys.UI_ERROR_NOT_LOGGED_IN,
                });
                return;
            }
            ValidateGuildRequestResult validateResult = GameInstance.ServerGuildHandlers.CanKickMemberFromGuild(playerCharacter, request.memberId);
            if (!validateResult.IsSuccess)
            {
                GameInstance.ServerGameMessageHandlers.SendGameMessage(requestHandler.ConnectionId, validateResult.GameMessage);
                result.Invoke(AckResponseCode.Error, new ResponseKickMemberFromGuildMessage()
                {
                    error = validateResult.GameMessage,
                });
                return;
            }
            IPlayerCharacterData memberCharacter;
            long memberConnectionId;
            if (GameInstance.ServerUserHandlers.TryGetPlayerCharacterById(request.memberId, out memberCharacter) &&
                GameInstance.ServerUserHandlers.TryGetConnectionId(request.memberId, out memberConnectionId))
            {
                memberCharacter.ClearGuild();
                GameInstance.ServerGameMessageHandlers.SendClearGuildData(memberConnectionId, validateResult.GuildId);
            }
            validateResult.Guild.RemoveMember(request.memberId);
            GameInstance.ServerGuildHandlers.SetGuild(validateResult.GuildId, validateResult.Guild);
            // Save to database
            _ = DbServiceClient.ClearCharacterGuildAsync(new ClearCharacterGuildReq()
            {
                CharacterId = request.memberId
            });
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
                ChatNetworkManager.SendRemoveSocialMember(null, MMOMessageTypes.UpdateGuildMember, validateResult.GuildId, request.memberId);
            GameInstance.ServerGameMessageHandlers.SendRemoveGuildMemberToMembers(validateResult.Guild, request.memberId);
            result.Invoke(AckResponseCode.Success, new ResponseKickMemberFromGuildMessage());
#endif
        }

        public async UniTaskVoid HandleRequestLeaveGuild(RequestHandlerData requestHandler, EmptyMessage request, RequestProceedResultDelegate<ResponseLeaveGuildMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            await UniTask.Yield();
            IPlayerCharacterData playerCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.Invoke(AckResponseCode.Error, new ResponseLeaveGuildMessage()
                {
                    error = UITextKeys.UI_ERROR_NOT_LOGGED_IN,
                });
                return;
            }
            ValidateGuildRequestResult validateResult = GameInstance.ServerGuildHandlers.CanLeaveGuild(playerCharacter);
            if (!validateResult.IsSuccess)
            {
                GameInstance.ServerGameMessageHandlers.SendGameMessage(requestHandler.ConnectionId, validateResult.GameMessage);
                result.Invoke(AckResponseCode.Error, new ResponseLeaveGuildMessage()
                {
                    error = validateResult.GameMessage,
                });
                return;
            }
            if (validateResult.Guild.IsLeader(playerCharacter.Id))
            {
                IPlayerCharacterData memberCharacter;
                long memberConnectionId;
                foreach (string memberId in validateResult.Guild.GetMemberIds())
                {
                    if (GameInstance.ServerUserHandlers.TryGetPlayerCharacterById(memberId, out memberCharacter) &&
                        GameInstance.ServerUserHandlers.TryGetConnectionId(memberId, out memberConnectionId))
                    {
                        memberCharacter.ClearGuild();
                        GameInstance.ServerGameMessageHandlers.SendClearGuildData(memberConnectionId, validateResult.GuildId);
                    }
                    // Save to database
                    _ = DbServiceClient.ClearCharacterGuildAsync(new ClearCharacterGuildReq()
                    {
                        CharacterId = memberId
                    });
                    // Broadcast via chat server
                    if (ChatNetworkManager.IsClientConnected)
                        ChatNetworkManager.SendRemoveSocialMember(null, MMOMessageTypes.UpdateGuildMember, validateResult.GuildId, memberId);
                }
                GameInstance.ServerGuildHandlers.RemoveGuild(validateResult.GuildId);
                // Save to database
                _ = DbServiceClient.DeleteGuildAsync(new DeleteGuildReq()
                {
                    GuildId = validateResult.GuildId
                });
                // Broadcast via chat server
                if (ChatNetworkManager.IsClientConnected)
                    ChatNetworkManager.SendGuildTerminate(null, MMOMessageTypes.UpdateGuild, validateResult.GuildId);
            }
            else
            {
                playerCharacter.ClearGuild();
                validateResult.Guild.RemoveMember(playerCharacter.Id);
                GameInstance.ServerGuildHandlers.SetGuild(validateResult.GuildId, validateResult.Guild);
                GameInstance.ServerGameMessageHandlers.SendRemoveGuildMemberToMembers(validateResult.Guild, playerCharacter.Id);
                GameInstance.ServerGameMessageHandlers.SendClearGuildData(requestHandler.ConnectionId, validateResult.GuildId);
                // Save to database
                _ = DbServiceClient.ClearCharacterGuildAsync(new ClearCharacterGuildReq()
                {
                    CharacterId = playerCharacter.Id
                });
                // Broadcast via chat server
                if (ChatNetworkManager.IsClientConnected)
                    ChatNetworkManager.SendRemoveSocialMember(null, MMOMessageTypes.UpdateGuildMember, validateResult.GuildId, playerCharacter.Id);
            }
            result.Invoke(AckResponseCode.Success, new ResponseLeaveGuildMessage());
#endif
        }

        public async UniTaskVoid HandleRequestChangeGuildMessage(RequestHandlerData requestHandler, RequestChangeGuildMessageMessage request, RequestProceedResultDelegate<ResponseChangeGuildMessageMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            await UniTask.Yield();
            IPlayerCharacterData playerCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.Invoke(AckResponseCode.Error, new ResponseChangeGuildMessageMessage()
                {
                    error = UITextKeys.UI_ERROR_NOT_LOGGED_IN,
                });
                return;
            }
            ValidateGuildRequestResult validateResult = GameInstance.ServerGuildHandlers.CanChangeGuildMessage(playerCharacter, request.message);
            if (!validateResult.IsSuccess)
            {
                GameInstance.ServerGameMessageHandlers.SendGameMessage(requestHandler.ConnectionId, validateResult.GameMessage);
                result.Invoke(AckResponseCode.Error, new ResponseChangeGuildMessageMessage()
                {
                    error = validateResult.GameMessage,
                });
                return;
            }
            validateResult.Guild.guildMessage = request.message;
            GameInstance.ServerGuildHandlers.SetGuild(validateResult.GuildId, validateResult.Guild);
            // Save to database
            _ = DbServiceClient.UpdateGuildMessageAsync(new UpdateGuildMessageReq()
            {
                GuildId = validateResult.GuildId,
                GuildMessage = request.message
            });
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
                ChatNetworkManager.SendSetGuildMessage(null, MMOMessageTypes.UpdateGuild, validateResult.GuildId, request.message);
            GameInstance.ServerGameMessageHandlers.SendSetGuildMessageToMembers(validateResult.Guild);
            result.Invoke(AckResponseCode.Success, new ResponseChangeGuildMessageMessage());
#endif
        }

        public async UniTaskVoid HandleRequestChangeGuildRole(RequestHandlerData requestHandler, RequestChangeGuildRoleMessage request, RequestProceedResultDelegate<ResponseChangeGuildRoleMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            await UniTask.Yield();
            IPlayerCharacterData playerCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.Invoke(AckResponseCode.Error, new ResponseChangeGuildRoleMessage()
                {
                    error = UITextKeys.UI_ERROR_NOT_LOGGED_IN,
                });
                return;
            }
            ValidateGuildRequestResult validateResult = GameInstance.ServerGuildHandlers.CanChangeGuildRole(playerCharacter, request.guildRole, request.name);
            if (!validateResult.IsSuccess)
            {
                GameInstance.ServerGameMessageHandlers.SendGameMessage(requestHandler.ConnectionId, validateResult.GameMessage);
                result.Invoke(AckResponseCode.Error, new ResponseChangeGuildRoleMessage()
                {
                    error = validateResult.GameMessage,
                });
                return;
            }
            validateResult.Guild.SetRole(request.guildRole, request.name, request.canInvite, request.canKick, request.shareExpPercentage);
            GameInstance.ServerGuildHandlers.SetGuild(validateResult.GuildId, validateResult.Guild);
            // Change characters guild role
            IPlayerCharacterData memberCharacter;
            foreach (string memberId in validateResult.Guild.GetMemberIds())
            {
                if (GameInstance.ServerUserHandlers.TryGetPlayerCharacterById(memberId, out memberCharacter))
                {
                    if (validateResult.Guild.GetMemberRole(memberCharacter.Id) == request.guildRole)
                        memberCharacter.SharedGuildExp = request.shareExpPercentage;
                }
            }
            // Save to database
            _ = DbServiceClient.UpdateGuildRoleAsync(new UpdateGuildRoleReq()
            {
                GuildId = validateResult.GuildId,
                GuildRole = request.guildRole,
                RoleName = request.name,
                CanInvite = request.canInvite,
                CanKick = request.canKick,
                ShareExpPercentage = request.shareExpPercentage
            });
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
                ChatNetworkManager.SendSetGuildRole(null, MMOMessageTypes.UpdateGuild, validateResult.GuildId, request.guildRole, request.name, request.canInvite, request.canKick, request.shareExpPercentage);
            GameInstance.ServerGameMessageHandlers.SendSetGuildRoleToMembers(validateResult.Guild, request.guildRole, request.name, request.canInvite, request.canKick, request.shareExpPercentage);
            result.Invoke(AckResponseCode.Success, new ResponseChangeGuildRoleMessage());
#endif
        }

        public async UniTaskVoid HandleRequestChangeMemberGuildRole(RequestHandlerData requestHandler, RequestChangeMemberGuildRoleMessage request, RequestProceedResultDelegate<ResponseChangeMemberGuildRoleMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            await UniTask.Yield();
            IPlayerCharacterData playerCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.Invoke(AckResponseCode.Error, new ResponseChangeMemberGuildRoleMessage()
                {
                    error = UITextKeys.UI_ERROR_NOT_LOGGED_IN,
                });
                return;
            }
            ValidateGuildRequestResult validateResult = GameInstance.ServerGuildHandlers.CanChangeGuildMemberRole(playerCharacter);
            if (!validateResult.IsSuccess)
            {
                GameInstance.ServerGameMessageHandlers.SendGameMessage(requestHandler.ConnectionId, validateResult.GameMessage);
                result.Invoke(AckResponseCode.Error, new ResponseChangeMemberGuildRoleMessage()
                {
                    error = validateResult.GameMessage,
                });
                return;
            }
            validateResult.Guild.SetMemberRole(request.memberId, request.guildRole);
            GameInstance.ServerGuildHandlers.SetGuild(validateResult.GuildId, validateResult.Guild);
            IPlayerCharacterData memberCharacter;
            if (GameInstance.ServerUserHandlers.TryGetPlayerCharacterById(request.memberId, out memberCharacter))
            {
                memberCharacter.GuildRole = request.guildRole;
                memberCharacter.SharedGuildExp = validateResult.Guild.GetRole(request.guildRole).shareExpPercentage;
            }
            // Save to database
            _ = DbServiceClient.UpdateGuildMemberRoleAsync(new UpdateGuildMemberRoleReq()
            {
                MemberCharacterId = request.memberId,
                GuildRole = request.guildRole
            });
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
                ChatNetworkManager.SendSetGuildMemberRole(null, MMOMessageTypes.UpdateGuild, validateResult.GuildId, request.memberId, request.guildRole);
            GameInstance.ServerGameMessageHandlers.SendSetGuildMemberRoleToMembers(validateResult.Guild, request.memberId, request.guildRole);
            result.Invoke(AckResponseCode.Success, new ResponseChangeMemberGuildRoleMessage());
#endif
        }

        public async UniTaskVoid HandleRequestIncreaseGuildSkillLevel(RequestHandlerData requestHandler, RequestIncreaseGuildSkillLevelMessage request, RequestProceedResultDelegate<ResponseIncreaseGuildSkillLevelMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            await UniTask.Yield();
            IPlayerCharacterData playerCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.Invoke(AckResponseCode.Error, new ResponseIncreaseGuildSkillLevelMessage()
                {
                    error = UITextKeys.UI_ERROR_NOT_LOGGED_IN,
                });
                return;
            }
            ValidateGuildRequestResult validateResult = GameInstance.ServerGuildHandlers.CanIncreaseGuildSkillLevel(playerCharacter, request.dataId);
            if (!validateResult.IsSuccess)
            {
                GameInstance.ServerGameMessageHandlers.SendGameMessage(requestHandler.ConnectionId, validateResult.GameMessage);
                result.Invoke(AckResponseCode.Error, new ResponseIncreaseGuildSkillLevelMessage()
                {
                    error = validateResult.GameMessage,
                });
                return;
            }
            // Save to database
            GuildResp resp = await DbServiceClient.AddGuildSkillAsync(new AddGuildSkillReq()
            {
                GuildId = validateResult.GuildId,
                SkillId = request.dataId,
            });
            GuildData guild = resp.GuildData.FromByteString<GuildData>();
            GameInstance.ServerGuildHandlers.SetGuild(validateResult.GuildId, guild);
            // Broadcast via chat server
            if (ChatNetworkManager.IsClientConnected)
            {
                ChatNetworkManager.SendSetGuildSkillLevel(null, MMOMessageTypes.UpdateGuild, validateResult.GuildId, request.dataId, guild.GetSkillLevel(request.dataId));
                ChatNetworkManager.SendSetGuildLevelExpSkillPoint(null, MMOMessageTypes.UpdateGuild, validateResult.GuildId, guild.level, guild.exp, guild.skillPoint);
            }
            GameInstance.ServerGameMessageHandlers.SendSetGuildSkillLevelToMembers(guild, request.dataId);
            GameInstance.ServerGameMessageHandlers.SendSetGuildLevelExpSkillPointToMembers(guild);
            result.Invoke(AckResponseCode.Success, new ResponseIncreaseGuildSkillLevelMessage());
#endif
        }
    }
}
