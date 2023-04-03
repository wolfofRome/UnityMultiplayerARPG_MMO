﻿using LiteNetLib.Utils;

namespace MultiplayerARPG.MMO
{
    public struct UpdateCharacterPartyReq : INetSerializable
    {
        public int PartyId { get; set; }
        public SocialCharacterData SocialCharacterData { get; set; }

        public void Deserialize(NetDataReader reader)
        {
            PartyId = reader.GetInt();
            SocialCharacterData = reader.Get<SocialCharacterData>();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(PartyId);
            writer.Put(SocialCharacterData);
        }
    }
}