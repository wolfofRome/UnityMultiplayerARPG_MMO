﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MySql.Data.MySqlClient;
using System.Threading.Tasks;

namespace Insthync.MMOG
{
    public partial class MySQLDatabase
    {
        private Dictionary<int, int> ReadKillMonsters(string killMonsters)
        {
            var result = new Dictionary<int, int>();
            var splitSets = killMonsters.Split(';');
            foreach (var set in splitSets)
            {
                var splitData = set.Split(':');
                result[int.Parse(splitData[0])] = int.Parse(splitData[1]);
            }
            return result;
        }

        private string WriteKillMonsters(Dictionary<int, int> killMonsters)
        {
            var result = "";
            foreach (var keyValue in killMonsters)
            {
                result += keyValue.Key + ":" + keyValue.Value + ";";
            }
            return result;
        }

        private bool ReadCharacterQuest(MySQLRowsReader reader, out CharacterQuest result, bool resetReader = true)
        {
            if (resetReader)
                reader.ResetReader();

            if (reader.Read())
            {
                result = new CharacterQuest();
                result.dataId = reader.GetInt32("dataId");
                result.isComplete = reader.GetBoolean("isComplete");
                result.killedMonsters = ReadKillMonsters(reader.GetString("killMonsters"));
                return true;
            }
            result = CharacterQuest.Empty;
            return false;
        }

        public async Task CreateCharacterQuest(MySqlConnection connection, string characterId, CharacterQuest characterQuest)
        {
            await ExecuteNonQuery(connection, "INSERT INTO characterquest (id, characterId, dataId, isComplete, killedMonsters) VALUES (@id, @characterId, @dataId, @isComplete, @killedMonsters)",
                new MySqlParameter("@id", characterId + "_" + characterQuest.dataId),
                new MySqlParameter("@characterId", characterId),
                new MySqlParameter("@dataId", characterQuest.dataId),
                new MySqlParameter("@isComplete", characterQuest.isComplete),
                new MySqlParameter("@killedMonsters", WriteKillMonsters(characterQuest.killedMonsters)));
        }

        public async Task<List<CharacterQuest>> ReadCharacterQuests(string characterId)
        {
            var result = new List<CharacterQuest>();
            var reader = await ExecuteReader("SELECT * FROM characterquest WHERE characterId=@characterId",
                new MySqlParameter("@characterId", characterId));
            CharacterQuest tempQuest;
            while (ReadCharacterQuest(reader, out tempQuest, false))
            {
                result.Add(tempQuest);
            }
            return result;
        }

        public async Task DeleteCharacterQuests(MySqlConnection connection, string characterId)
        {
            await ExecuteNonQuery(connection, "DELETE FROM characterquest WHERE characterId=@characterId", new MySqlParameter("@characterId", characterId));
        }
    }
}
