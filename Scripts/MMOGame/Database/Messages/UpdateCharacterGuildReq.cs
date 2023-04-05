﻿using LiteNetLib.Utils;

namespace MultiplayerARPG.MMO
{
    public partial struct UpdateCharacterGuildReq : INetSerializable
    {
        public int GuildId { get; set; }
        public byte GuildRole { get; set; }
        public SocialCharacterData SocialCharacterData { get; set; }

        public void Deserialize(NetDataReader reader)
        {
            GuildId = reader.GetInt();
            GuildRole = reader.GetByte();
            SocialCharacterData = reader.Get<SocialCharacterData>();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(GuildId);
            writer.Put(GuildRole);
            writer.Put(SocialCharacterData);
        }
    }
}