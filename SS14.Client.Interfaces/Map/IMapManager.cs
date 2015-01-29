using GorgonLibrary;
using Lidgren.Network;
using System.Drawing;

namespace SS14.Client.Interfaces.Map
{
    public delegate void TileChangeEvent(PointF tileWorldPosition);

    public interface IMapManager
    {
        event TileChangeEvent OnTileChanged;
        int GetTileSpacing();
        int GetWallThickness();
        bool IsSolidTile(Vector2D pos);
        void HandleNetworkMessage(NetIncomingMessage message);
        void HandleAtmosDisplayUpdate(NetIncomingMessage message);

        void Shutdown();

        ITile[] GetAllTilesIn(RectangleF Area);
        ITile[] GetAllFloorIn(RectangleF Area);

        ITile GetFloorAt(Vector2D pos);
        ITile[] GetAllTilesAt(Vector2D pos);

        int GetMapWidth();
        int GetMapHeight();

        void Init();
    }
}