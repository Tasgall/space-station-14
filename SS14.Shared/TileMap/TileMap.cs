using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SS14.Shared.TileMap
{
    class TileMap
    {
        #region Static values

        private static int m_chunkSize = 100; // width and height of each chunk in Tiles

        #endregion

        #region Constructors

        TileMap()
        {
            m_chunks = new Dictionary<string, Chunk>();
        }

        #endregion

        #region Public Functions

        public void SetTile(Vector2 location, Tile tile)
        {
            ChunkIndex index = new ChunkIndex(location);

            Chunk chunk = GetOrCreateChunk(index);

            chunk.m_tiles[index.m_tileX, index.m_tileY] = tile;
        }

        public Tile GetTile(Vector2 location)
        {
            ChunkIndex index = new ChunkIndex(location);

            Chunk chunk = GetChunk(index);

            if (chunk == null)
                return new Tile(); // empty space tile

            return chunk.m_tiles[index.m_tileX, index.m_tileY];
        }

        #endregion

        #region Private Functions

        private struct ChunkIndex
        {
            public string m_indexString;
            public int m_tileX;
            public int m_tileY;
            public Vector2 m_location;

            public ChunkIndex(Vector2 location)
            {
                m_location = location;

                int chunkX = (int)location.X / m_chunkSize;
                int chunkY = (int)location.Y / m_chunkSize;

                // we want chunk coordinates to go -2, -1, 0, 1, 2, not -1, -0, 0, 1
                if (location.X < 0) --chunkX;
                if (location.Y < 0) --chunkY;

                m_indexString = chunkX.ToString() + ',' + chunkY.ToString();

                // get the tile index for the relevant chunk.
                // negatives are a bit odd here, but at worst they should just change how chunks are saved (mirrored)
                // this might have to change if we get into sub-tile elements
                m_tileX = Math.Abs((int)location.X % m_chunkSize);
                m_tileY = Math.Abs((int)location.Y % m_chunkSize);
            }
        }

        private Chunk GetChunk(ChunkIndex index)
        {
            if (!m_chunks.ContainsKey(index.m_indexString))
                return null;

            Chunk chunk;
            if (!m_chunks.TryGetValue(index.m_indexString, out chunk))
                throw new Exception("Chunk map failed to find a chunk it said it could find. This should not happen.");

            return chunk;
        }

        // gets a chunk, or creates it if it doesn't exist
        private Chunk GetOrCreateChunk(ChunkIndex index)
        {
            Chunk chunk = GetChunk(index);

            if (chunk == null)
            {
                chunk = new Chunk();

                if (chunk == null)
                    throw new Exception("Failed to create a new chunk - you probably ran out of memory.");

                m_chunks.Add(index.m_indexString, chunk);
            }

            return chunk;
        }

        #endregion

        #region Helper Structs

        public class Tile
        {
            public int m_type = 0;
        }

        private class Chunk
        {
            public Tile[,] m_tiles = new Tile[m_chunkSize, m_chunkSize];
        }

        #endregion

        #region Members

        private Dictionary<string, Chunk> m_chunks;

        #endregion
    }
}
