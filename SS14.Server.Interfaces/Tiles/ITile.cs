using SS14.Server.Interfaces.Atmos;
using SS14.Shared;
using System;
using System.Drawing;

namespace SS14.Server.Interfaces.Tiles
{
    public delegate void TileChangeHandler(Type tNew);

    public interface ITile
    {
        TileState TileState { get; set; }
        event TileChangeHandler TileChange; //This event will be used for wall mounted objects and
        void RaiseChangedEvent(Type type);
        void AddDecal(DecalType type);
        Vector2 WorldPosition { get; }
        RectangleF Bounds { get; }
        Direction dir { get;}
    }
}