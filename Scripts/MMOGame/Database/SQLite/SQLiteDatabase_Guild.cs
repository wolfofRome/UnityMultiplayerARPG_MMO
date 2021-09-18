﻿#if UNITY_STANDALONE && !CLIENT_BUILD
using Cysharp.Threading.Tasks;
using Mono.Data.Sqlite;

namespace MultiplayerARPG.MMO
{
    public partial class SQLiteDatabase
    {
        public override async UniTask<int> CreateGuild(string guildName, string leaderId)
        {
            await UniTask.Yield();
            int id = 0;
            ExecuteReader((reader) =>
            {
                if (reader.Read())
                    id = (int)reader.GetInt64(0);
            }, "INSERT INTO guild (guildName, leaderId, options) VALUES (@guildName, @leaderId, @options);" +
                "SELECT LAST_INSERT_ROWID();",
                new SqliteParameter("@guildName", guildName),
                new SqliteParameter("@leaderId", leaderId),
                new SqliteParameter("@options", "{}"));
            if (id > 0)
                ExecuteNonQuery("UPDATE characters SET guildId=@id WHERE id=@leaderId",
                    new SqliteParameter("@id", id),
                    new SqliteParameter("@leaderId", leaderId));
            return id;
        }

        public override async UniTask<GuildData> ReadGuild(int id, GuildRoleData[] defaultGuildRoles)
        {
            await UniTask.Yield();
            GuildData result = null;
            ExecuteReader((reader) =>
            {
                if (reader.Read())
                {
                    result = new GuildData(id,
                        reader.GetString(0),
                        reader.GetString(1),
                        defaultGuildRoles);
                    result.level = reader.GetInt16(2);
                    result.exp = reader.GetInt32(3);
                    result.skillPoint = reader.GetInt16(4);
                    result.guildMessage = reader.GetString(5);
                    result.guildMessage2 = reader.GetString(6);
                    result.gold = reader.GetInt32(7);
                    result.score = reader.GetInt32(8);
                    result.options = reader.GetString(9);
                    result.autoAcceptRequests = reader.GetBoolean(10);
                    result.rank = reader.GetInt32(11);
                }
            }, "SELECT `guildName`, `leaderId`, `level`, `exp`, `skillPoint`, `guildMessage`, `guildMessage2`, `gold`, `score`, `options`, `autoAcceptRequests`, `rank` FROM guild WHERE id=@id LIMIT 1",
                new SqliteParameter("@id", id));
            if (result != null)
            {
                // Guild roles
                ExecuteReader((reader) =>
                {
                    byte guildRole;
                    GuildRoleData guildRoleData;
                    while (reader.Read())
                    {
                        guildRole = reader.GetByte(0);
                        guildRoleData = new GuildRoleData();
                        guildRoleData.roleName = reader.GetString(1);
                        guildRoleData.canInvite = reader.GetBoolean(2);
                        guildRoleData.canKick = reader.GetBoolean(3);
                        guildRoleData.shareExpPercentage = reader.GetByte(4);
                        result.SetRole(guildRole, guildRoleData);
                    }
                }, "SELECT guildRole, name, canInvite, canKick, shareExpPercentage FROM guildrole WHERE guildId=@id",
                    new SqliteParameter("@id", id));
                // Guild members
                ExecuteReader((reader) =>
                {
                    SocialCharacterData guildMemberData;
                    while (reader.Read())
                    {
                        // Get some required data, other data will be set at server side
                        guildMemberData = new SocialCharacterData();
                        guildMemberData.id = reader.GetString(0);
                        guildMemberData.dataId = reader.GetInt32(1);
                        guildMemberData.characterName = reader.GetString(2);
                        guildMemberData.level = reader.GetInt16(3);
                        result.AddMember(guildMemberData, reader.GetByte(4));
                    }
                }, "SELECT id, dataId, characterName, level, guildRole FROM characters WHERE guildId=@id",
                    new SqliteParameter("@id", id));
                // Guild skills
                ExecuteReader((reader) =>
                {
                    while (reader.Read())
                    {
                        result.SetSkillLevel(reader.GetInt32(0), reader.GetInt16(1));
                    }
                }, "SELECT dataId, level FROM guildskill WHERE guildId=@id",
                    new SqliteParameter("@id", id));
            }
            return result;
        }

        public override async UniTask UpdateGuildLevel(int id, short level, int exp, short skillPoint)
        {
            await UniTask.Yield();
            ExecuteNonQuery("UPDATE guild SET level=@level, exp=@exp, skillPoint=@skillPoint WHERE id=@id",
                new SqliteParameter("@level", level),
                new SqliteParameter("@exp", exp),
                new SqliteParameter("@skillPoint", skillPoint),
                new SqliteParameter("@id", id));
        }

        public override async UniTask UpdateGuildLeader(int id, string leaderId)
        {
            await UniTask.Yield();
            ExecuteNonQuery("UPDATE guild SET leaderId=@leaderId WHERE id=@id",
                new SqliteParameter("@leaderId", leaderId),
                new SqliteParameter("@id", id));
        }

        public override async UniTask UpdateGuildMessage(int id, string guildMessage)
        {
            await UniTask.Yield();
            ExecuteNonQuery("UPDATE guild SET guildMessage=@guildMessage WHERE id=@id",
                new SqliteParameter("@guildMessage", guildMessage),
                new SqliteParameter("@id", id));
        }

        public override async UniTask UpdateGuildMessage2(int id, string guildMessage)
        {
            await UniTask.Yield();
            ExecuteNonQuery("UPDATE guild SET guildMessage2=@guildMessage WHERE id=@id",
                new SqliteParameter("@guildMessage", guildMessage),
                new SqliteParameter("@id", id));
        }

        public override async UniTask UpdateGuildScore(int id, int score)
        {

            await UniTask.Yield();
            ExecuteNonQuery("UPDATE guild SET score=@score WHERE id=@id",
                new SqliteParameter("@score", score),
                new SqliteParameter("@id", id));
        }

        public override async UniTask UpdateGuildOptions(int id, string options)
        {

            await UniTask.Yield();
            ExecuteNonQuery("UPDATE guild SET options=@options WHERE id=@id",
                new SqliteParameter("@options", options),
                new SqliteParameter("@id", id));
        }

        public override async UniTask UpdateGuildAutoAcceptRequests(int id, bool autoAcceptRequests)
        {

            await UniTask.Yield();
            ExecuteNonQuery("UPDATE guild SET autoAcceptRequests=@autoAcceptRequests WHERE id=@id",
                new SqliteParameter("@autoAcceptRequests", autoAcceptRequests),
                new SqliteParameter("@id", id));
        }

        public override async UniTask UpdateGuildRank(int id, int rank)
        {

            await UniTask.Yield();
            ExecuteNonQuery("UPDATE guild SET rank=@rank WHERE id=@id",
                new SqliteParameter("@rank", rank),
                new SqliteParameter("@id", id));
        }

        public override async UniTask UpdateGuildRole(int id, byte guildRole, string name, bool canInvite, bool canKick, byte shareExpPercentage)
        {
            await UniTask.Yield();
            ExecuteNonQuery("DELETE FROM guildrole WHERE guildId=@guildId AND guildRole=@guildRole",
                new SqliteParameter("@guildId", id),
                new SqliteParameter("@guildRole", guildRole));
            ExecuteNonQuery("INSERT INTO guildrole (guildId, guildRole, name, canInvite, canKick, shareExpPercentage) " +
                "VALUES (@guildId, @guildRole, @name, @canInvite, @canKick, @shareExpPercentage)",
                new SqliteParameter("@guildId", id),
                new SqliteParameter("@guildRole", guildRole),
                new SqliteParameter("@name", name),
                new SqliteParameter("@canInvite", canInvite),
                new SqliteParameter("@canKick", canKick),
                new SqliteParameter("@shareExpPercentage", shareExpPercentage));
        }

        public override async UniTask UpdateGuildMemberRole(string characterId, byte guildRole)
        {
            await UniTask.Yield();
            ExecuteNonQuery("UPDATE characters SET guildRole=@guildRole WHERE id=@characterId",
                new SqliteParameter("@characterId", characterId),
                new SqliteParameter("@guildRole", guildRole));
        }

        public override async UniTask UpdateGuildSkillLevel(int id, int dataId, short level, short skillPoint)
        {
            await UniTask.Yield();
            ExecuteNonQuery("DELETE FROM guildskill WHERE guildId=@guildId AND dataId=@dataId",
                new SqliteParameter("@guildId", id),
                new SqliteParameter("@dataId", dataId));
            ExecuteNonQuery("INSERT INTO guildskill (guildId, dataId, level) " +
                "VALUES (@guildId, @dataId, @level)",
                new SqliteParameter("@guildId", id),
                new SqliteParameter("@dataId", dataId),
                new SqliteParameter("@level", level));
            ExecuteNonQuery("UPDATE guild SET skillPoint=@skillPoint WHERE id=@id",
                new SqliteParameter("@skillPoint", skillPoint),
                new SqliteParameter("@id", id));
        }

        public override async UniTask DeleteGuild(int id)
        {
            await UniTask.Yield();
            ExecuteNonQuery("DELETE FROM guild WHERE id=@id;" +
                "UPDATE characters SET guildId=0 WHERE guildId=@id;",
                new SqliteParameter("@id", id));
        }

        public override async UniTask<long> FindGuildName(string guildName)
        {
            await UniTask.Yield();
            object result = ExecuteScalar("SELECT COUNT(*) FROM guild WHERE guildName LIKE @guildName",
                new SqliteParameter("@guildName", guildName));
            return result != null ? (long)result : 0;
        }

        public override async UniTask UpdateCharacterGuild(string characterId, int guildId, byte guildRole)
        {
            await UniTask.Yield();
            ExecuteNonQuery("UPDATE characters SET guildId=@guildId, guildRole=@guildRole WHERE id=@characterId",
                new SqliteParameter("@characterId", characterId),
                new SqliteParameter("@guildId", guildId),
                new SqliteParameter("@guildRole", guildRole));
        }

        public override async UniTask<int> GetGuildGold(int guildId)
        {
            await UniTask.Yield();
            int gold = 0;
            ExecuteReader((reader) =>
            {
                if (reader.Read())
                    gold = reader.GetInt32(0);
            }, "SELECT gold FROM guild WHERE id=@id LIMIT 1",
                new SqliteParameter("@id", guildId));
            return gold;
        }

        public override async UniTask UpdateGuildGold(int guildId, int gold)
        {
            await UniTask.Yield();
            ExecuteNonQuery("UPDATE guild SET gold=@gold WHERE id=@id",
                new SqliteParameter("@id", guildId),
                new SqliteParameter("@gold", gold));
        }
    }
}
#endif