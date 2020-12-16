﻿using Cysharp.Threading.Tasks;
using LiteNetLibManager;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerARPG.MMO
{
    public class MMOServerStorageMessageHandlers : MonoBehaviour, IServerStorageMessageHandlers
    {
#if UNITY_STANDALONE && !CLIENT_BUILD
        public DatabaseService.DatabaseServiceClient DbServiceClient
        {
            get { return MMOServerInstance.Singleton.DatabaseNetworkManager.ServiceClient; }
        }
#endif

        public async UniTaskVoid HandleRequestMoveItemFromStorage(RequestHandlerData requestHandler, RequestMoveItemFromStorageMessage request, RequestProceedResultDelegate<ResponseMoveItemFromStorageMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            StorageId storageId = new StorageId(request.storageType, request.storageOwnerId);
            IPlayerCharacterData playerCharacter;
            if (!GameInstance.ServerPlayerCharacterHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.Invoke(AckResponseCode.Error, new ResponseMoveItemFromStorageMessage()
                {
                    error = ResponseMoveItemFromStorageMessage.Error.CharacterNotFound,
                });
                return;
            }
            if (!GameInstance.ServerStorageHandlers.CanAccessStorage(playerCharacter, storageId))
            {
                result.Invoke(AckResponseCode.Error, new ResponseMoveItemFromStorageMessage()
                {
                    error = ResponseMoveItemFromStorageMessage.Error.NotAllowed,
                });
                return;
            }
            // Prepare storage data
            MoveItemFromStorageReq req = new MoveItemFromStorageReq();
            req.StorageType = (EStorageType)request.storageType;
            req.StorageOwnerId = request.storageOwnerId;
            req.CharacterId = request.characterId;
            req.StorageItemIndex = request.storageItemIndex;
            req.StorageItemAmount = request.amount;
            req.InventoryItemIndex = request.inventoryIndex;
            MoveItemFromStorageResp resp = await DbServiceClient.MoveItemFromStorageAsync(req);
            if (resp.Error != EStorageError.StorageErrorNone)
            {
                ResponseMoveItemFromStorageMessage.Error error = ResponseMoveItemFromStorageMessage.Error.None;
                switch (resp.Error)
                {
                    case EStorageError.StorageErrorInvalidInventoryIndex:
                    case EStorageError.StorageErrorInvalidStorageIndex:
                        error = ResponseMoveItemFromStorageMessage.Error.InvalidItemIndex;
                        break;
                    case EStorageError.StorageErrorInventoryWillOverwhelming:
                    case EStorageError.StorageErrorStorageWillOverwhelming:
                        error = ResponseMoveItemFromStorageMessage.Error.CannotCarryAllItems;
                        break;
                }
                result.Invoke(AckResponseCode.Error, new ResponseMoveItemFromStorageMessage()
                {
                    error = error,
                });
                return;
            }
            playerCharacter.NonEquipItems = DatabaseServiceUtils.MakeListFromRepeatedByteString<CharacterItem>(resp.InventoryItemItems);
            GameInstance.ServerStorageHandlers.SetStorageItems(storageId, DatabaseServiceUtils.MakeListFromRepeatedByteString<CharacterItem>(resp.StorageCharacterItems));
            GameInstance.ServerStorageHandlers.NotifyStorageItemsUpdated(request.storageType, request.storageOwnerId);
            // Success
            result.Invoke(AckResponseCode.Success, new ResponseMoveItemFromStorageMessage());
#endif
        }

        public async UniTaskVoid HandleRequestMoveItemToStorage(RequestHandlerData requestHandler, RequestMoveItemToStorageMessage request, RequestProceedResultDelegate<ResponseMoveItemToStorageMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            StorageId storageId = new StorageId(request.storageType, request.storageOwnerId);
            IPlayerCharacterData playerCharacter;
            if (!GameInstance.ServerPlayerCharacterHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.Invoke(AckResponseCode.Error, new ResponseMoveItemToStorageMessage()
                {
                    error = ResponseMoveItemToStorageMessage.Error.CharacterNotFound,
                });
                return;
            }
            if (!GameInstance.ServerStorageHandlers.CanAccessStorage(playerCharacter, storageId))
            {
                result.Invoke(AckResponseCode.Error, new ResponseMoveItemToStorageMessage()
                {
                    error = ResponseMoveItemToStorageMessage.Error.NotAllowed,
                });
                return;
            }
            MoveItemToStorageReq req = new MoveItemToStorageReq();
            req.StorageType = (EStorageType)request.storageType;
            req.StorageOwnerId = request.storageOwnerId;
            req.CharacterId = request.characterId;
            req.InventoryItemIndex = request.inventoryIndex;
            req.InventoryItemAmount = request.amount;
            req.StorageItemIndex = request.storageItemIndex;
            MoveItemToStorageResp resp = await DbServiceClient.MoveItemToStorageAsync(req);
            if (resp.Error != EStorageError.StorageErrorNone)
            {
                ResponseMoveItemToStorageMessage.Error error = ResponseMoveItemToStorageMessage.Error.None;
                switch (resp.Error)
                {
                    case EStorageError.StorageErrorInvalidInventoryIndex:
                    case EStorageError.StorageErrorInvalidStorageIndex:
                        error = ResponseMoveItemToStorageMessage.Error.InvalidItemIndex;
                        break;
                    case EStorageError.StorageErrorInventoryWillOverwhelming:
                    case EStorageError.StorageErrorStorageWillOverwhelming:
                        error = ResponseMoveItemToStorageMessage.Error.CannotCarryAllItems;
                        break;
                }
                result.Invoke(AckResponseCode.Error, new ResponseMoveItemToStorageMessage()
                {
                    error = error,
                });
                return;
            }
            playerCharacter.NonEquipItems = DatabaseServiceUtils.MakeListFromRepeatedByteString<CharacterItem>(resp.InventoryItemItems);
            GameInstance.ServerStorageHandlers.SetStorageItems(storageId, DatabaseServiceUtils.MakeListFromRepeatedByteString<CharacterItem>(resp.StorageCharacterItems));
            GameInstance.ServerStorageHandlers.NotifyStorageItemsUpdated(request.storageType, request.storageOwnerId);
            // Success
            result.Invoke(AckResponseCode.Success, new ResponseMoveItemToStorageMessage());
#endif
        }

        public async UniTaskVoid HandleRequestSwapOrMergeStorageItem(RequestHandlerData requestHandler, RequestSwapOrMergeStorageItemMessage request, RequestProceedResultDelegate<ResponseSwapOrMergeStorageItemMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            StorageId storageId = new StorageId(request.storageType, request.storageOwnerId);
            IPlayerCharacterData playerCharacter;
            if (!GameInstance.ServerPlayerCharacterHandlers.TryGetPlayerCharacter(requestHandler.ConnectionId, out playerCharacter))
            {
                result.Invoke(AckResponseCode.Error, new ResponseSwapOrMergeStorageItemMessage()
                {
                    error = ResponseSwapOrMergeStorageItemMessage.Error.CharacterNotFound,
                });
                return;
            }
            if (!GameInstance.ServerStorageHandlers.CanAccessStorage(playerCharacter, storageId))
            {
                result.Invoke(AckResponseCode.Error, new ResponseSwapOrMergeStorageItemMessage()
                {
                    error = ResponseSwapOrMergeStorageItemMessage.Error.NotAllowed,
                });
                return;
            }
            SwapOrMergeStorageItemReq req = new SwapOrMergeStorageItemReq();
            req.StorageType = (EStorageType)request.storageType;
            req.StorageOwnerId = request.storageOwnerId;
            req.CharacterId = request.characterId;
            req.FromIndex = request.fromIndex;
            req.ToIndex = request.toIndex;
            SwapOrMergeStorageItemResp resp = await DbServiceClient.SwapOrMergeStorageItemAsync(req);
            if (resp.Error != EStorageError.StorageErrorNone)
            {
                ResponseSwapOrMergeStorageItemMessage.Error error = ResponseSwapOrMergeStorageItemMessage.Error.None;
                switch (resp.Error)
                {
                    case EStorageError.StorageErrorInvalidInventoryIndex:
                    case EStorageError.StorageErrorInvalidStorageIndex:
                        error = ResponseSwapOrMergeStorageItemMessage.Error.InvalidItemIndex;
                        break;
                }
                result.Invoke(AckResponseCode.Error, new ResponseSwapOrMergeStorageItemMessage()
                {
                    error = error,
                });
            }
            GameInstance.ServerStorageHandlers.SetStorageItems(storageId, DatabaseServiceUtils.MakeListFromRepeatedByteString<CharacterItem>(resp.StorageCharacterItems));
            GameInstance.ServerStorageHandlers.NotifyStorageItemsUpdated(request.storageType, request.storageOwnerId);
            // Success
            result.Invoke(AckResponseCode.Success, new ResponseSwapOrMergeStorageItemMessage());
#endif
        }
    }
}
