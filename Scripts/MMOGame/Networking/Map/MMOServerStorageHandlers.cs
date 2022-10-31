﻿using Cysharp.Threading.Tasks;
using LiteNetLibManager;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerARPG.MMO
{
    public partial class MMOServerStorageHandlers : MonoBehaviour, IServerStorageHandlers
    {
#if UNITY_EDITOR || UNITY_SERVER
        private readonly ConcurrentDictionary<StorageId, List<CharacterItem>> storageItems = new ConcurrentDictionary<StorageId, List<CharacterItem>>();
        private readonly ConcurrentDictionary<StorageId, HashSet<long>> usingStorageClients = new ConcurrentDictionary<StorageId, HashSet<long>>();
        private readonly ConcurrentDictionary<long, StorageId> usingStorageIds = new ConcurrentDictionary<long, StorageId>();
#endif

#if UNITY_EDITOR || UNITY_SERVER
        public IDatabaseClient DbServiceClient
        {
            get { return MMOServerInstance.Singleton.DatabaseNetworkManager; }
        }
#endif

        public async UniTaskVoid OpenStorage(long connectionId, IPlayerCharacterData playerCharacter, StorageId storageId)
        {
#if UNITY_EDITOR || UNITY_SERVER
            if (!CanAccessStorage(playerCharacter, storageId))
            {
                GameInstance.ServerGameMessageHandlers.SendGameMessage(connectionId, UITextKeys.UI_ERROR_CANNOT_ACCESS_STORAGE);
                return;
            }
            // Store storage usage states
            if (!usingStorageClients.ContainsKey(storageId))
                usingStorageClients.TryAdd(storageId, new HashSet<long>());
            usingStorageClients[storageId].Add(connectionId);
            usingStorageIds.TryRemove(connectionId, out _);
            usingStorageIds.TryAdd(connectionId, storageId);
            // Load storage items from database
            AsyncResponseData<ReadStorageItemsResp> resp = await DbServiceClient.ReadStorageItemsAsync(new ReadStorageItemsReq()
            {
                StorageType = storageId.storageType,
                StorageOwnerId = storageId.storageOwnerId,
            });
            if (!resp.IsSuccess)
            {
                return;
            }
            List<CharacterItem> storageItems = resp.Response.StorageCharacterItems;
            SetStorageItems(storageId, storageItems);
            // Notify storage items to client
            uint storageObjectId;
            Storage storage = GetStorage(storageId, out storageObjectId);
            GameInstance.ServerGameMessageHandlers.NotifyStorageOpened(connectionId, storageId.storageType, storageId.storageOwnerId, storageObjectId, storage.weightLimit, storage.slotLimit);
            storageItems.FillEmptySlots(storage.slotLimit > 0, storage.slotLimit);
            GameInstance.ServerGameMessageHandlers.NotifyStorageItems(connectionId, storageItems);
#endif
        }

        public async UniTaskVoid CloseStorage(long connectionId)
        {
#if UNITY_EDITOR || UNITY_SERVER
            StorageId storageId;
            if (usingStorageIds.TryGetValue(connectionId, out storageId) && usingStorageClients.ContainsKey(storageId))
            {
                usingStorageClients[storageId].Remove(connectionId);
                usingStorageIds.TryRemove(connectionId, out _);
                GameInstance.ServerGameMessageHandlers.NotifyStorageClosed(connectionId);
            }
#endif
            await UniTask.Yield();
        }

        public bool TryGetOpenedStorageId(long connectionId, out StorageId storageId)
        {
#if UNITY_EDITOR || UNITY_SERVER
            return usingStorageIds.TryGetValue(connectionId, out storageId);
#else
            storageId = default;
            return false;
#endif
        }

        public async UniTask<List<CharacterItem>> ConvertStorageItems(StorageId storageId, List<StorageConvertItemsEntry> convertItems)
        {
#if UNITY_EDITOR || UNITY_SERVER
            // Prepare storage data
            StorageType storageType = storageId.storageType;
            string storageOwnerId = storageId.storageOwnerId;
            Storage storage = GetStorage(storageId, out _);
            bool isLimitWeight = storage.weightLimit > 0;
            bool isLimitSlot = storage.slotLimit > 0;
            short weightLimit = storage.weightLimit;
            short slotLimit = storage.slotLimit;
            // Refresh storage item from database
            AsyncResponseData<ReadStorageItemsResp> readResp = await DbServiceClient.ReadStorageItemsAsync(new ReadStorageItemsReq()
            {
                StorageType = storageType,
                StorageOwnerId = storageOwnerId,
                ReadForUpdate = true,
            });
            if (!readResp.IsSuccess)
                return null;
            List<CharacterItem> storageItems = readResp.Response.StorageCharacterItems;
            List<CharacterItem> droppingItems = new List<CharacterItem>();
            for (int i = 0; i < convertItems.Count; ++i)
            {
                int dataId = convertItems[i].dataId;
                short amount = convertItems[i].amount;
                int convertedDataId = convertItems[i].convertedDataId;
                short convertedAmount = convertItems[i].convertedAmount;
                // Decrease item from storage
                if (!storageItems.DecreaseItems(dataId, amount, isLimitSlot, out _))
                    continue;
                // Increase item to storage
                if (GameInstance.Items.ContainsKey(convertedDataId) && convertedAmount > 0)
                {
                    // Increase item to storage
                    CharacterItem droppingItem = CharacterItem.Create(convertedDataId, convertedAmount);
                    if (!storageItems.IncreasingItemsWillOverwhelming(convertedDataId, convertedAmount, isLimitWeight, weightLimit, storageItems.GetTotalItemWeight(), isLimitSlot, slotLimit))
                    {
                        storageItems.IncreaseItems(droppingItem);
                    }
                    else
                    {
                        droppingItems.Add(droppingItem);
                    }
                }
            }
            // Update slots
            storageItems.FillEmptySlots(isLimitSlot, slotLimit);
            // Update storage items to database
            AsyncResponseData<EmptyMessage> updateResp = await DbServiceClient.UpdateStorageItemsAsync(new UpdateStorageItemsReq()
            {
                StorageType = storageType,
                StorageOwnerId = storageOwnerId,
                StorageItems = storageItems,
                UpdateCharacterData = false,
            });
            if (!updateResp.IsSuccess)
                return null;
            SetStorageItems(storageId, storageItems);
            NotifyStorageItemsUpdated(storageId.storageType, storageId.storageOwnerId);
            return droppingItems;
#else
            return null;
#endif
        }

        public List<CharacterItem> GetStorageItems(StorageId storageId)
        {
#if UNITY_EDITOR || UNITY_SERVER
            if (!storageItems.ContainsKey(storageId))
                storageItems.TryAdd(storageId, new List<CharacterItem>());
            return storageItems[storageId];
#else
            return new List<CharacterItem>();
#endif
        }

        public void SetStorageItems(StorageId storageId, List<CharacterItem> items)
        {
#if UNITY_EDITOR || UNITY_SERVER
            if (!storageItems.ContainsKey(storageId))
                storageItems.TryAdd(storageId, new List<CharacterItem>());
            storageItems[storageId] = items;
#endif
        }

        public Storage GetStorage(StorageId storageId, out uint objectId)
        {
            objectId = 0;
            Storage storage = default(Storage);
            switch (storageId.storageType)
            {
                case StorageType.Player:
                    storage = GameInstance.Singleton.playerStorage;
                    break;
                case StorageType.Guild:
                    storage = GameInstance.Singleton.guildStorage;
                    break;
                case StorageType.Building:
                    StorageEntity buildingEntity;
                    if (GameInstance.ServerBuildingHandlers.TryGetBuilding(storageId.storageOwnerId, out buildingEntity))
                    {
                        objectId = buildingEntity.ObjectId;
                        storage = buildingEntity.Storage;
                    }
                    break;
            }
            return storage;
        }

        public bool CanAccessStorage(IPlayerCharacterData playerCharacter, StorageId storageId)
        {
            switch (storageId.storageType)
            {
                case StorageType.Player:
                    if (!playerCharacter.UserId.Equals(storageId.storageOwnerId))
                        return false;
                    break;
                case StorageType.Guild:
                    if (!GameInstance.ServerGuildHandlers.ContainsGuild(playerCharacter.GuildId) ||
                        !playerCharacter.GuildId.ToString().Equals(storageId.storageOwnerId))
                        return false;
                    break;
                case StorageType.Building:
                    StorageEntity buildingEntity;
                    if (!GameInstance.ServerBuildingHandlers.TryGetBuilding(storageId.storageOwnerId, out buildingEntity) ||
                        !(buildingEntity.IsCreator(playerCharacter.Id) || buildingEntity.CanUseByEveryone))
                        return false;
                    break;
            }
            return true;
        }

        public bool IsStorageEntityOpen(StorageEntity storageEntity)
        {
#if UNITY_EDITOR || UNITY_SERVER
            if (storageEntity == null)
                return false;
            StorageId id = new StorageId(StorageType.Building, storageEntity.Id);
            return usingStorageClients.ContainsKey(id) && usingStorageClients[id].Count > 0;
#else
            return false;
#endif
        }

        public List<CharacterItem> GetStorageEntityItems(StorageEntity storageEntity)
        {
#if UNITY_EDITOR || UNITY_SERVER
            if (storageEntity == null)
                return new List<CharacterItem>();
            return GetStorageItems(new StorageId(StorageType.Building, storageEntity.Id));
#else
            return new List<CharacterItem>();
#endif
        }

        public void ClearStorage()
        {
#if UNITY_EDITOR || UNITY_SERVER
            storageItems.Clear();
            usingStorageClients.Clear();
            usingStorageIds.Clear();
#endif
        }

        public void NotifyStorageItemsUpdated(StorageType storageType, string storageOwnerId)
        {
#if UNITY_EDITOR || UNITY_SERVER
            StorageId storageId = new StorageId(storageType, storageOwnerId);
            GameInstance.ServerGameMessageHandlers.NotifyStorageItemsToClients(usingStorageClients[storageId], GetStorageItems(storageId));
#endif
        }

        public IDictionary<StorageId, List<CharacterItem>> GetAllStorageItems()
        {
#if UNITY_EDITOR || UNITY_SERVER
            return storageItems;
#else
            return new ConcurrentDictionary<StorageId, List<CharacterItem>>();
#endif
        }
    }
}
