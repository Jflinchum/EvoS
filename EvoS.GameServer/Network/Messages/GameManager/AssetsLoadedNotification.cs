using EvoS.GameServer.Network.Unity;

namespace EvoS.GameServer.Network
{
    [UNetMessage(53)]
    public class AssetsLoadedNotification : MessageBase
    {
        public long AccountId;
        public int PlayerId;

        public override void Serialize(NetworkWriter writer)
        {
            writer.WritePackedUInt64((ulong) AccountId);
            writer.WritePackedUInt32((uint) PlayerId);
        }

        public override void Deserialize(NetworkReader reader)
        {
            AccountId = (long) reader.ReadPackedUInt64();
            PlayerId = (int) reader.ReadPackedUInt32();
        }
    }
}