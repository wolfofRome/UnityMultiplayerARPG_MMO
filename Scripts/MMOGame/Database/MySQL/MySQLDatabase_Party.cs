﻿using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MultiplayerARPG.MMO
{
    public partial class MySQLDatabase
    {
        public override async Task<int> CreateParty(bool shareExp, bool shareItem, string leaderId)
        {
            return 0;
        }

        public override async Task<PartyData> ReadParty(int id)
        {
            PartyData result = null;
            return result;
        }

        public override async Task UpdateParty(int id, bool shareExp, bool shareItem)
        {
        }

        public override async Task DeleteParty(int id)
        {
        }
    }
}
