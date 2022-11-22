﻿#if (UNITY_EDITOR || UNITY_SERVER) && UNITY_STANDALONE
using System.Collections.Generic;
using MySqlConnector;

namespace MultiplayerARPG.MMO
{
    public partial class MySQLDatabase
    {
        private bool ReadCharacterBuff(MySqlDataReader reader, out CharacterBuff result)
        {
            if (reader.Read())
            {
                result = new CharacterBuff();
                result.id = reader.GetString(0);
                result.type = (BuffType)reader.GetByte(1);
                result.dataId = reader.GetInt32(2);
                result.level = reader.GetInt16(3);
                result.buffRemainsDuration = reader.GetFloat(4);
                return true;
            }
            result = CharacterBuff.Empty;
            return false;
        }

        public void CreateCharacterBuff(MySqlConnection connection, MySqlTransaction transaction, string characterId, CharacterBuff characterBuff)
        {
            ExecuteNonQuerySync(connection, transaction, "INSERT INTO characterbuff (id, characterId, type, dataId, level, buffRemainsDuration) VALUES (@id, @characterId, @type, @dataId, @level, @buffRemainsDuration)",
                new MySqlParameter("@id", characterBuff.id),
                new MySqlParameter("@characterId", characterId),
                new MySqlParameter("@type", (byte)characterBuff.type),
                new MySqlParameter("@dataId", characterBuff.dataId),
                new MySqlParameter("@level", characterBuff.level),
                new MySqlParameter("@buffRemainsDuration", characterBuff.buffRemainsDuration));
        }

        public List<CharacterBuff> ReadCharacterBuffs(string characterId, List<CharacterBuff> result = null)
        {
            if (result == null)
                result = new List<CharacterBuff>();
            ExecuteReaderSync((reader) =>
            {
                CharacterBuff tempBuff;
                while (ReadCharacterBuff(reader, out tempBuff))
                {
                    result.Add(tempBuff);
                }
            }, "SELECT id, type, dataId, level, buffRemainsDuration FROM characterbuff WHERE characterId=@characterId ORDER BY buffRemainsDuration ASC",
                new MySqlParameter("@characterId", characterId));
            return result;
        }

        public void DeleteCharacterBuffs(MySqlConnection connection, MySqlTransaction transaction, string characterId)
        {
            ExecuteNonQuerySync(connection, transaction, "DELETE FROM characterbuff WHERE characterId=@characterId", new MySqlParameter("@characterId", characterId));
        }
    }
}
#endif