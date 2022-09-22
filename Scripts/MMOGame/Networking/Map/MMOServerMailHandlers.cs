﻿using Cysharp.Threading.Tasks;
using LiteNetLibManager;
using UnityEngine;

namespace MultiplayerARPG.MMO
{
    public partial class MMOServerMailHandlers : MonoBehaviour, IServerMailHandlers
    {
#if UNITY_EDITOR || UNITY_SERVER
        public IDatabaseClient DbServiceClient
        {
            get { return MMOServerInstance.Singleton.DatabaseNetworkManager; }
        }
#endif

        public async UniTask<bool> SendMail(Mail mail)
        {
#if UNITY_EDITOR || UNITY_SERVER
            AsyncResponseData<SendMailResp> resp = await DbServiceClient.SendMailAsync(new SendMailReq()
            {
                Mail = mail,
            });
            if (resp.IsSuccess && resp.Response.Error == 0)
                return true;
#endif
            return false;
        }
    }
}
