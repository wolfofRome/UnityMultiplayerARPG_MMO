﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mono.Data.Sqlite;
using System.Threading.Tasks;

namespace Insthync.MMOG
{
    public partial class SQLiteDatabase
    {
        private bool ReadCharacterSkill(SQLiteRowsReader reader, out CharacterSkill result, bool resetReader = true)
        {
            if (resetReader)
                reader.ResetReader();

            if (reader.Read())
            {
                result = new CharacterSkill();
                result.dataId = reader.GetInt32("dataId");
                result.level = reader.GetInt32("level");
                result.coolDownRemainsDuration = reader.GetFloat("coolDownRemainsDuration");
                return true;
            }
            result = CharacterSkill.Empty;
            return false;
        }

        public async Task CreateCharacterSkill(string characterId, CharacterSkill characterSkill)
        {
            await ExecuteNonQuery("INSERT INTO characterskill (id, characterId, dataId, level, coolDownRemainsDuration) VALUES (@id, @characterId, @dataId, @level, @coolDownRemainsDuration)",
                new SqliteParameter("@id", characterId + "_" + characterSkill.dataId),
                new SqliteParameter("@characterId", characterId),
                new SqliteParameter("@dataId", characterSkill.dataId),
                new SqliteParameter("@level", characterSkill.level),
                new SqliteParameter("@coolDownRemainsDuration", characterSkill.coolDownRemainsDuration));
        }
        
        public async Task<List<CharacterSkill>> ReadCharacterSkills(string characterId)
        {
            var result = new List<CharacterSkill>();
            var reader = await ExecuteReader("SELECT * FROM characterskill WHERE characterId=@characterId",
                new SqliteParameter("@characterId", characterId));
            CharacterSkill tempSkill;
            while (ReadCharacterSkill(reader, out tempSkill, false))
            {
                result.Add(tempSkill);
            }
            return result;
        }

        public async Task DeleteCharacterSkills(string characterId)
        {
            await ExecuteNonQuery("DELETE FROM characterskill WHERE characterId=@characterId", new SqliteParameter("@characterId", characterId));
        }
    }
}
