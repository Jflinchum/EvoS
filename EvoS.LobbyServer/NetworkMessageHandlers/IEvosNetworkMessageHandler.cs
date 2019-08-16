﻿using System;
using System.Collections.Generic;
using System.Text;
using EvoS.Framework.Network;

namespace EvoS.LobbyServer.NetworkMessageHandlers
{
    interface IEvosNetworkMessageHandler
    {
        void OnMessage(EvosMessageStream stream);
        bool DoLogPacket();
    }
}