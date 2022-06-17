﻿using Cysharp.Threading.Tasks;
using LiteNetLibManager;
using UnityEngine;

namespace MultiplayerARPG.MMO
{
    public partial class MMOServerFriendMessageHandlers : MonoBehaviour, IServerFriendMessageHandlers
    {
#if UNITY_STANDALONE && !CLIENT_BUILD
        public IDatabaseClient DbServiceClient
        {
            get { return MMOServerInstance.Singleton.DatabaseNetworkManager; }
        }
#endif

        public async UniTaskVoid HandleRequestFindCharacters(RequestHandlerData requestHandler, RequestFindCharactersMessage request, RequestProceedResultDelegate<ResponseSocialCharacterListMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            IPlayerCharacterData playerCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.InvokeError(new ResponseSocialCharacterListMessage()
                {
                    message = UITextKeys.UI_ERROR_NOT_LOGGED_IN,
                });
                return;
            }
            AsyncResponseData<SocialCharactersResp> resp = await DbServiceClient.FindCharactersAsync(new FindCharacterNameReq()
            {
                FinderId = playerCharacter.Id,
                CharacterName = request.characterName,
                Skip = 0,
                Limit = 50,
            });
            if (!resp.IsSuccess)
            {
                result.InvokeError(new ResponseSocialCharacterListMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                });
                return;
            }
            result.InvokeSuccess(new ResponseSocialCharacterListMessage()
            {
                characters = resp.Response.List,
            });
#endif
        }

        public async UniTaskVoid HandleRequestGetFriends(RequestHandlerData requestHandler, EmptyMessage request, RequestProceedResultDelegate<ResponseGetFriendsMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            IPlayerCharacterData playerCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.InvokeError(new ResponseGetFriendsMessage()
                {
                    message = UITextKeys.UI_ERROR_NOT_LOGGED_IN,
                });
                return;
            }
            AsyncResponseData<SocialCharactersResp> resp = await DbServiceClient.ReadFriendsAsync(new ReadFriendsReq()
            {
                CharacterId = playerCharacter.Id,
                ReadById2 = false,
                State = 0,
                Skip = 0,
                Limit = 50,
            });
            if (!resp.IsSuccess)
            {
                result.InvokeError(new ResponseGetFriendsMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                });
                return;
            }
            result.InvokeSuccess(new ResponseGetFriendsMessage()
            {
                friends = resp.Response.List,
            });
#endif
        }

        public async UniTaskVoid HandleRequestAddFriend(RequestHandlerData requestHandler, RequestAddFriendMessage request, RequestProceedResultDelegate<ResponseAddFriendMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            IPlayerCharacterData playerCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.InvokeError(new ResponseAddFriendMessage()
                {
                    message = UITextKeys.UI_ERROR_NOT_LOGGED_IN,
                });
                return;
            }
            AsyncResponseData<EmptyMessage> resp = await DbServiceClient.CreateFriendAsync(new CreateFriendReq()
            {
                Character1Id = playerCharacter.Id,
                Character2Id = request.friendId,
                State = 0,
            });
            if (!resp.IsSuccess)
            {
                result.InvokeError(new ResponseAddFriendMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                });
                return;
            }
            result.InvokeSuccess(new ResponseAddFriendMessage()
            {
                message = UITextKeys.UI_FRIEND_ADDED,
            });
#endif
        }

        public async UniTaskVoid HandleRequestRemoveFriend(RequestHandlerData requestHandler, RequestRemoveFriendMessage request, RequestProceedResultDelegate<ResponseRemoveFriendMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            IPlayerCharacterData playerCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.InvokeError(new ResponseRemoveFriendMessage()
                {
                    message = UITextKeys.UI_ERROR_NOT_LOGGED_IN,
                });
                return;
            }
            AsyncResponseData<EmptyMessage> resp = await DbServiceClient.DeleteFriendAsync(new DeleteFriendReq()
            {
                Character1Id = playerCharacter.Id,
                Character2Id = request.friendId,
            });
            if (!resp.IsSuccess)
            {
                result.InvokeError(new ResponseRemoveFriendMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                });
                return;
            }
            result.InvokeSuccess(new ResponseRemoveFriendMessage()
            {
                message = UITextKeys.UI_FRIEND_REMOVED,
            });
#endif
        }

        public async UniTaskVoid HandleRequestSendFriendRequest(RequestHandlerData requestHandler, RequestSendFriendRequestMessage request, RequestProceedResultDelegate<ResponseSendFriendRequestMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            IPlayerCharacterData playerCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.InvokeError(new ResponseSendFriendRequestMessage()
                {
                    message = UITextKeys.UI_ERROR_NOT_LOGGED_IN,
                });
                return;
            }
            AsyncResponseData<EmptyMessage> resp = await DbServiceClient.CreateFriendAsync(new CreateFriendReq()
            {
                Character1Id = playerCharacter.Id,
                Character2Id = request.requesteeId,
                State = 1,
            });
            if (!resp.IsSuccess)
            {
                result.InvokeError(new ResponseSendFriendRequestMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                });
                return;
            }
            result.InvokeSuccess(new ResponseSendFriendRequestMessage()
            {
                message = UITextKeys.UI_FRIEND_REQUESTED,
            });
#endif
        }

        public async UniTaskVoid HandleRequestAcceptFriendRequest(RequestHandlerData requestHandler, RequestAcceptFriendRequestMessage request, RequestProceedResultDelegate<ResponseAcceptFriendRequestMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            IPlayerCharacterData playerCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.InvokeError(new ResponseAcceptFriendRequestMessage()
                {
                    message = UITextKeys.UI_ERROR_NOT_LOGGED_IN,
                });
                return;
            }
            AsyncResponseData<EmptyMessage> resp1 = await DbServiceClient.CreateFriendAsync(new CreateFriendReq()
            {
                Character1Id = playerCharacter.Id,
                Character2Id = request.requesterId,
                State = 0,
            });
            AsyncResponseData<EmptyMessage> resp2 = await DbServiceClient.CreateFriendAsync(new CreateFriendReq()
            {
                Character1Id = request.requesterId,
                Character2Id = playerCharacter.Id,
                State = 0,
            });
            if (!resp1.IsSuccess || !resp2.IsSuccess)
            {
                result.InvokeError(new ResponseAcceptFriendRequestMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                });
                return;
            }
            result.InvokeSuccess(new ResponseAcceptFriendRequestMessage()
            {
                message = UITextKeys.UI_FRIEND_REQUEST_ACCEPTED,
            });
#endif
        }

        public async UniTaskVoid HandleRequestDeclineFriendRequest(RequestHandlerData requestHandler, RequestDeclineFriendRequestMessage request, RequestProceedResultDelegate<ResponseDeclineFriendRequestMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            IPlayerCharacterData playerCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.InvokeError(new ResponseDeclineFriendRequestMessage()
                {
                    message = UITextKeys.UI_ERROR_NOT_LOGGED_IN,
                });
                return;
            }
            AsyncResponseData<EmptyMessage> resp = await DbServiceClient.DeleteFriendAsync(new DeleteFriendReq()
            {
                Character1Id = playerCharacter.Id,
                Character2Id = request.requesterId,
            });
            if (!resp.IsSuccess)
            {
                result.InvokeError(new ResponseDeclineFriendRequestMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                });
                return;
            }
            result.InvokeSuccess(new ResponseDeclineFriendRequestMessage()
            {
                message = UITextKeys.UI_FRIEND_REQUEST_DECLINED,
            });
#endif
        }

        public async UniTaskVoid HandleRequestGetFriendRequests(RequestHandlerData requestHandler, EmptyMessage request, RequestProceedResultDelegate<ResponseGetFriendRequestsMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            IPlayerCharacterData playerCharacter;
            if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.InvokeError(new ResponseGetFriendRequestsMessage()
                {
                    message = UITextKeys.UI_ERROR_NOT_LOGGED_IN,
                });
                return;
            }
            AsyncResponseData<SocialCharactersResp> resp = await DbServiceClient.ReadFriendsAsync(new ReadFriendsReq()
            {
                CharacterId = playerCharacter.Id,
                ReadById2 = true,
                State = 1,
                Skip = 0,
                Limit = 50,
            });
            if (!resp.IsSuccess)
            {
                result.InvokeError(new ResponseGetFriendRequestsMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                });
                return;
            }
            result.InvokeSuccess(new ResponseGetFriendRequestsMessage()
            {
                friendRequests = resp.Response.List,
            });
#endif
        }

        public async UniTaskVoid HandleRequestFriendRequestNotification(RequestHandlerData requestHandler, EmptyMessage request, RequestProceedResultDelegate<ResponseFriendRequestNotificationMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            int notificationCount = 0;
            IPlayerCharacterData playerCharacter;
            if (GameInstance.ServerUserHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                AsyncResponseData<GetFriendRequestNotificationResp> resp = await DbServiceClient.GetFriendRequestNotificationAsync(new GetFriendRequestNotificationReq()
                {
                    CharacterId = playerCharacter.Id,
                });
                if (resp.IsSuccess)
                    notificationCount = resp.Response.NotificationCount;
            }
            result.Invoke(AckResponseCode.Success, new ResponseFriendRequestNotificationMessage()
            {
                notificationCount = notificationCount,
            });
#endif
        }
    }
}
