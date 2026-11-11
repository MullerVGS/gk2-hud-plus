using System.Collections;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Map
{
    /// <summary>
    /// Onde existe chão no mundo, em células de 1 unidade: as caixas de todos os ladrilhos de terreno
    /// (<c>LazyTerrainMeshData</c>) registrados no <c>ChunkManager</c>, rasterizadas uma vez por sessão (~40 ms). As
    /// regiões do mapa e as áreas internas são maiores que o terreno modelado; fora dele a câmera mostra vazio.
    /// Guarda também a altura do ladrilho, que o mapa 2D não tem.
    /// </summary>
    internal class TerrainMap
    {
        private const float Cell = 1f;
        private static readonly FieldInfoRef Layers = new FieldInfoRef(typeof(ChunkManager), "layers");

        private readonly int width, height;
        private readonly float originX, originZ;
        private readonly float[] ground;
        private readonly ChunkManager source;

        public bool IsFor(ChunkManager cm) => source == cm;

        public TerrainMap(ChunkManager cm)
        {
            source = cm;
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            var tiles = new System.Collections.Generic.List<BurstableBounds>(24000);
            foreach (object layer in (IList)Layers.Get(cm))
            {
                var l = (ChunkManagerLayer)layer;
                if (l.layerType != ChunkManagerLayerType.StaticObjects || l.chunks == null) continue;
                foreach (Chunk chunk in l.chunks)
                {
                    if (chunk == null || chunk.chunkableObjects == null) continue;
                    foreach (IChunkableObject o in chunk.chunkableObjects)
                    {
                        if (!(o is LazyTerrainMeshData tile)) continue;
                        // Os limites de visibilidade (GetChunkableData) têm folga; os da malha do ladrilho são justos.
                        var b = new BurstableBounds { center = tile.bounds.center, size = tile.bounds.size };
                        tiles.Add(b);
                        x0 = Mathf.Min(x0, b.center.x - b.size.x * 0.5f);
                        x1 = Mathf.Max(x1, b.center.x + b.size.x * 0.5f);
                        z0 = Mathf.Min(z0, b.center.z - b.size.z * 0.5f);
                        z1 = Mathf.Max(z1, b.center.z + b.size.z * 0.5f);
                    }
                }
            }
            if (tiles.Count == 0)
            {
                ground = new float[0];
                return;
            }
            originX = Mathf.Floor(x0);
            originZ = Mathf.Floor(z0);
            width = Mathf.CeilToInt((x1 - originX) / Cell) + 1;
            height = Mathf.CeilToInt((z1 - originZ) / Cell) + 1;
            ground = new float[width * height];
            for (int i = 0; i < ground.Length; i++) ground[i] = float.NaN;
            foreach (BurstableBounds b in tiles)
            {
                // Só células cujo centro cai dentro do ladrilho.
                int cx0 = Mathf.Max(0, Mathf.CeilToInt((b.center.x - b.size.x * 0.5f - originX) / Cell - 0.5f));
                int cx1 = Mathf.Min(width - 1, Mathf.FloorToInt((b.center.x + b.size.x * 0.5f - originX) / Cell - 0.5f));
                int cz0 = Mathf.Max(0, Mathf.CeilToInt((b.center.z - b.size.z * 0.5f - originZ) / Cell - 0.5f));
                int cz1 = Mathf.Min(height - 1, Mathf.FloorToInt((b.center.z + b.size.z * 0.5f - originZ) / Cell - 0.5f));
                float y = b.center.y;
                for (int z = cz0; z <= cz1; z++)
                    for (int x = cx0; x <= cx1; x++)
                    {
                        int i = z * width + x;
                        if (float.IsNaN(ground[i]) || y > ground[i]) ground[i] = y;
                    }
            }
        }

        public bool Empty => ground.Length == 0;

        /// <summary>Há chão neste ponto? Devolve a altura do ladrilho.</summary>
        public bool TryGround(float x, float z, out float y)
        {
            y = 0f;
            int cx = Mathf.FloorToInt((x - originX) / Cell), cz = Mathf.FloorToInt((z - originZ) / Cell);
            if (cx < 0 || cz < 0 || cx >= width || cz >= height) return false;
            y = ground[cz * width + cx];
            return !float.IsNaN(y);
        }

        /// <summary>Campo privado lido por reflexão, resolvido uma vez.</summary>
        private sealed class FieldInfoRef
        {
            private readonly System.Reflection.FieldInfo field;
            public FieldInfoRef(System.Type type, string name) { field = AccessTools.Field(type, name); }
            public object Get(object target) => field.GetValue(target);
        }
    }
}
