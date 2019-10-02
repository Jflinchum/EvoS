using System.Numerics;
using EvoS.Framework.Network.Unity;

namespace EvoS.GameServer.Network.Messages.Unity
{
    public class ObjectSpawnMessage : MessageBase
    {
        public override void Deserialize(NetworkReader reader)
        {
            netId = reader.ReadNetworkId();
            assetId = reader.ReadNetworkHash128();
            position = reader.ReadVector3();
            payload = reader.ReadBytesAndSize();
            uint num = 16u;
            if (reader.Length - reader.Position >= num)
            {
                rotation = reader.ReadQuaternion();
            }
        }

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(netId);
            writer.Write(assetId);
            writer.Write(position);
            writer.WriteBytesFull(payload);
            writer.Write(rotation);
        }

        public NetworkInstanceId netId;
        public NetworkHash128 assetId;
        public Vector3 position;
        public byte[] payload;
        public Quaternion rotation;
    }
}
