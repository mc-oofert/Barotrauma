﻿using Barotrauma.Networking;

namespace Barotrauma
{
    partial class Mission
    {
        partial void ShowMessageProjSpecific(int missionState)
        {
            int messageIndex = missionState - 1;
            if (messageIndex >= Headers.Count && messageIndex >= Messages.Count) { return; }
            if (messageIndex < 0) { return; }

            string header = messageIndex < Headers.Count ? Headers[messageIndex] : "";
            string message = messageIndex < Messages.Count ? Messages[messageIndex] : "";

            GameServer.Log(TextManager.Get("MissionInfo") + ": " + header + " - " + message, ServerLog.MessageType.ServerMessage);
        }

        public virtual void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            msg.Write((ushort)State);
        }

        public virtual void ServerWrite(IWriteMessage msg)
        {
            msg.Write((ushort)State);
        }
    }
}