﻿#if UNITY_STANDALONE && !CLIENT_BUILD
using System.Collections.Generic;
using MySqlConnector;
using LiteNetLibManager;
using Cysharp.Threading.Tasks;

namespace MultiplayerARPG.MMO
{
    public partial class MySQLDatabase
    {
        private async UniTask CreateStorageItem(MySqlConnection connection, MySqlTransaction transaction, int idx, StorageType storageType, string storageOwnerId, CharacterItem characterItem)
        {
            await ExecuteNonQuery(connection, transaction, "INSERT INTO storageitem (id, idx, storageType, storageOwnerId, dataId, level, amount, durability, exp, lockRemainsDuration, ammo, sockets) VALUES (@id, @idx, @storageType, @storageOwnerId, @dataId, @level, @amount, @durability, @exp, @lockRemainsDuration, @ammo, @sockets)",
                new MySqlParameter("@id", new StorageItemId(storageType, storageOwnerId, idx).GetId()),
                new MySqlParameter("@idx", idx),
                new MySqlParameter("@storageType", (byte)storageType),
                new MySqlParameter("@storageOwnerId", storageOwnerId),
                new MySqlParameter("@dataId", characterItem.dataId),
                new MySqlParameter("@level", characterItem.level),
                new MySqlParameter("@amount", characterItem.amount),
                new MySqlParameter("@durability", characterItem.durability),
                new MySqlParameter("@exp", characterItem.exp),
                new MySqlParameter("@lockRemainsDuration", characterItem.lockRemainsDuration),
                new MySqlParameter("@ammo", characterItem.ammo),
                new MySqlParameter("@sockets", characterItem.WriteSockets()));
        }

        private bool ReadStorageItem(MySqlDataReader reader, out CharacterItem result)
        {
            if (reader.Read())
            {
                result = new CharacterItem();
                result.dataId = reader.GetInt32(0);
                result.level = reader.GetInt16(1);
                result.amount = reader.GetInt16(2);
                result.durability = reader.GetFloat(3);
                result.exp = reader.GetInt32(4);
                result.lockRemainsDuration = reader.GetFloat(5);
                result.ammo = reader.GetInt16(6);
                result.ReadSockets(reader.GetString(7));
                return true;
            }
            result = CharacterItem.Empty;
            return false;
        }

        public override async UniTask<List<CharacterItem>> ReadStorageItems(StorageType storageType, string storageOwnerId)
        {
            List<CharacterItem> result = new List<CharacterItem>();
            await ExecuteReader((reader) =>
            {
                CharacterItem tempInventory;
                while (ReadStorageItem(reader, out tempInventory))
                {
                    result.Add(tempInventory);
                }
            }, "SELECT dataId, level, amount, durability, exp, lockRemainsDuration, ammo, sockets FROM storageitem WHERE storageType=@storageType AND storageOwnerId=@storageOwnerId ORDER BY idx ASC",
                new MySqlParameter("@storageType", (byte)storageType),
                new MySqlParameter("@storageOwnerId", storageOwnerId));
            return result;
        }

        public override async UniTask UpdateStorageItems(StorageType storageType, string storageOwnerId, IList<CharacterItem> characterItems)
        {
            MySqlConnection connection = NewConnection();
            await OpenConnection(connection);
            MySqlTransaction transaction = await connection.BeginTransactionAsync();
            try
            {
                await DeleteStorageItems(connection, transaction, storageType, storageOwnerId);
                int i;
                for (i = 0; i < characterItems.Count; ++i)
                {
                    await CreateStorageItem(connection, transaction, i, storageType, storageOwnerId, characterItems[i]);
                }
                await transaction.CommitAsync();
            }
            catch (System.Exception ex)
            {
                Logging.LogError(ToString(), "Transaction, Error occurs while replacing storage items");
                Logging.LogException(ToString(), ex);
                await transaction.RollbackAsync();
            }
            await transaction.DisposeAsync();
            await connection.CloseAsync();
        }

        public async UniTask DeleteStorageItems(MySqlConnection connection, MySqlTransaction transaction, StorageType storageType, string storageOwnerId)
        {
            await ExecuteNonQuery(connection, transaction, "DELETE FROM storageitem WHERE storageType=@storageType AND storageOwnerId=@storageOwnerId",
                new MySqlParameter("@storageType", (byte)storageType),
                new MySqlParameter("@storageOwnerId", storageOwnerId));
        }
    }
}
#endif