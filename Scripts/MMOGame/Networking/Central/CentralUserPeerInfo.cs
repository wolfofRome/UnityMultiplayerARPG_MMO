﻿using LiteNetLibManager;
using LiteNetLib.Utils;

namespace Insthync.MMOG
{
    public class CentralUserPeerInfo : ILiteNetLibMessage
    {
        public string userId;
        public string accessToken;
        public string selectCharacterId;
        public string selectCharacterName;
        public string selectCharacterCurrentMap;

        public void Deserialize(NetDataReader reader)
        {
            userId = reader.GetString();
            accessToken = reader.GetString();
            selectCharacterId = reader.GetString();
            selectCharacterName = reader.GetString();
            selectCharacterCurrentMap = reader.GetString();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(userId);
            writer.Put(accessToken);
            writer.Put(selectCharacterId);
            writer.Put(selectCharacterName);
            writer.Put(selectCharacterCurrentMap);
        }
    }
}
