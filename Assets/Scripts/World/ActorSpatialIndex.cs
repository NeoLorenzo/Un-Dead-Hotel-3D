using System.Collections.Generic;
using UnityEngine;
using UnDeadHotel.Actors;

namespace UnDeadHotel.World
{
    /// <summary>
    /// Runtime-only spatial index for nearby human lookups.
    /// </summary>
    public sealed class ActorSpatialIndex
    {
        public static ActorSpatialIndex Instance { get; private set; }

        private readonly float cellSize;
        private readonly float rebuildInterval;
        private readonly HashSet<BaseActor> humans = new HashSet<BaseActor>();
        private readonly HashSet<BaseActor> zombies = new HashSet<BaseActor>();
        private readonly Dictionary<Vector2Int, List<BaseActor>> humanCells = new Dictionary<Vector2Int, List<BaseActor>>();
        private readonly Dictionary<Vector2Int, List<BaseActor>> zombieCells = new Dictionary<Vector2Int, List<BaseActor>>();
        private readonly List<BaseActor> staleHumans = new List<BaseActor>();
        private readonly List<BaseActor> staleZombies = new List<BaseActor>();
        private float nextRebuildTime;

        private ActorSpatialIndex(float cellSize, float rebuildInterval)
        {
            this.cellSize = Mathf.Max(0.01f, cellSize);
            this.rebuildInterval = Mathf.Max(0.01f, rebuildInterval);
            nextRebuildTime = 0f;
        }

        public static ActorSpatialIndex CreateRuntimeInstance(float cellSize, float rebuildInterval)
        {
            Instance = new ActorSpatialIndex(cellSize, rebuildInterval);
            return Instance;
        }

        public static void ClearRuntimeInstance()
        {
            Instance = null;
        }

        public void RegisterHuman(BaseActor actor)
        {
            if (actor == null) return;
            humans.Add(actor);
        }

        public void UnregisterHuman(BaseActor actor)
        {
            if (actor == null) return;
            humans.Remove(actor);
        }

        public void RegisterZombie(BaseActor actor)
        {
            if (actor == null) return;
            zombies.Add(actor);
        }

        public void UnregisterZombie(BaseActor actor)
        {
            if (actor == null) return;
            zombies.Remove(actor);
        }

        public void Tick(float currentTime)
        {
            if (currentTime < nextRebuildTime) return;

            RebuildCells();
            nextRebuildTime = currentTime + rebuildInterval;
        }

        public void QueryHumansInRadius(Vector3 center, float radius, List<BaseActor> resultsBuffer)
        {
            QueryActorsInRadius(humanCells, center, radius, resultsBuffer);
        }

        public void QueryZombiesInRadius(Vector3 center, float radius, List<BaseActor> resultsBuffer)
        {
            QueryActorsInRadius(zombieCells, center, radius, resultsBuffer);
        }

        private void RebuildCells()
        {
            humanCells.Clear();
            zombieCells.Clear();
            staleHumans.Clear();
            staleZombies.Clear();

            foreach (BaseActor actor in humans)
            {
                if (actor == null || !actor.isActiveAndEnabled)
                {
                    staleHumans.Add(actor);
                    continue;
                }

                Vector2Int cell = WorldToCell(actor.transform.position);
                if (!humanCells.TryGetValue(cell, out List<BaseActor> bucket))
                {
                    bucket = new List<BaseActor>();
                    humanCells[cell] = bucket;
                }
                bucket.Add(actor);
            }

            foreach (BaseActor actor in zombies)
            {
                if (actor == null || !actor.isActiveAndEnabled)
                {
                    staleZombies.Add(actor);
                    continue;
                }

                Vector2Int cell = WorldToCell(actor.transform.position);
                if (!zombieCells.TryGetValue(cell, out List<BaseActor> bucket))
                {
                    bucket = new List<BaseActor>();
                    zombieCells[cell] = bucket;
                }
                bucket.Add(actor);
            }

            for (int i = 0; i < staleHumans.Count; i++)
            {
                humans.Remove(staleHumans[i]);
            }

            for (int i = 0; i < staleZombies.Count; i++)
            {
                zombies.Remove(staleZombies[i]);
            }
        }

        private void QueryActorsInRadius(Dictionary<Vector2Int, List<BaseActor>> sourceCells, Vector3 center, float radius, List<BaseActor> resultsBuffer)
        {
            if (resultsBuffer == null) return;
            resultsBuffer.Clear();

            if (sourceCells.Count == 0 || radius <= 0f) return;

            int minX = ToCellCoord(center.x - radius);
            int maxX = ToCellCoord(center.x + radius);
            int minY = ToCellCoord(center.z - radius);
            int maxY = ToCellCoord(center.z + radius);
            float radiusSqr = radius * radius;

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    Vector2Int key = new Vector2Int(x, y);
                    if (!sourceCells.TryGetValue(key, out List<BaseActor> actorsInCell)) continue;

                    for (int i = 0; i < actorsInCell.Count; i++)
                    {
                        BaseActor actor = actorsInCell[i];
                        if (actor == null || !actor.isActiveAndEnabled) continue;

                        Vector3 delta = actor.transform.position - center;
                        delta.y = 0f;
                        if (delta.sqrMagnitude <= radiusSqr)
                        {
                            resultsBuffer.Add(actor);
                        }
                    }
                }
            }
        }

        private Vector2Int WorldToCell(Vector3 position)
        {
            return new Vector2Int(ToCellCoord(position.x), ToCellCoord(position.z));
        }

        private int ToCellCoord(float value)
        {
            return Mathf.FloorToInt(value / cellSize);
        }
    }
}
