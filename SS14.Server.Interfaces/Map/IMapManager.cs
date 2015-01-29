using Lidgren.Network;
using SS14.Server.Interfaces.Tiles;
using SS14.Shared;
using System.Drawing;

namespace SS14.Server.Interfaces.Map
{
    public interface IMapManager
    {
        bool InitMap(string mapName);
        void HandleNetworkMessage(NetIncomingMessage message);
        //void NetworkUpdateTile(ITile t);
        void SaveMap();

        NetOutgoingMessage CreateMapMessage(MapMessage messageType);
        void SendMessage(NetOutgoingMessage message);
        void SendMap(NetConnection connection);
        int GetTileSpacing();

        void Shutdown();

        ITile[] GetAllTilesIn(RectangleF Area);
        ITile[] GetAllFloorIn(RectangleF Area);

        ITile GetFloorAt(Vector2 pos);
        ITile[] GetAllTilesAt(Vector2 pos);

        int GetMapWidth();
        int GetMapHeight();

        ITile GenerateNewTile(Vector2 pos, string type, Direction dir = Direction.North);
        void DestroyTile(ITile s);
        RectangleF GetWorldArea();
    }
}