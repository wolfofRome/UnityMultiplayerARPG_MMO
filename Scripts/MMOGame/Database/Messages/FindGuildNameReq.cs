﻿using LiteNetLib.Utils;

namespace MultiplayerARPG.MMO
{
    public partial struct FindGuildNameReq : INetSerializable
    {
        public string GuildName { get; set; }

        public void Deserialize(NetDataReader reader)
        {
            GuildName = reader.GetString();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(GuildName);
        }
    }
}