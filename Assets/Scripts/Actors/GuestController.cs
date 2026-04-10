using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnDeadHotel.Actors;
using UnDeadHotel.AI;
using UnDeadHotel.World;

namespace UnDeadHotel.Actors
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class GuestController : BaseActor
    {
        [Header("AI Config")]
        public float dangerDetectionRadius = 15f;
        public float viewAngle = 200f;
        public float hearingRadius = 2.0f;
        public LayerMask obstacleLayer;
        public float panicPersistence = 4.0f;
        public float dangerScanInterval = 0.2f;
        public float outsideRoomComfortDuration = 10.0f;

        [Header("Wander Settings")]
        public float wanderRadius = 10f;
        public float minWanderWaitTime = 0.5f;
        public float maxWanderWaitTime = 2.0f;
        public int wanderCandidateSamples = 8;

        [Header("Flee Settings")]
        public float fleeReplanInterval = 0.25f;
        public float fleeNearRadius = 4f;
        public float fleeFarRadius = 8f;
        public int fleeAnglesPerRing = 8;
        public float fleeProbeRadius = 2f;
        public float fleePathCostWeight = 0.35f;
        public float fleeOpennessWeight = 0.25f;
        public float stuckDistanceThreshold = 0.3f;
        public float stuckTimeThreshold = 1.0f;

        private struct ThreatMemory
        {
            public Vector3 position;
            public float expiresAt;

            public ThreatMemory(Vector3 position, float expiresAt)
            {
                this.position = position;
                this.expiresAt = expiresAt;
            }
        }

        private NavMeshAgent agent;
        private Selector rootNode;
        private Sequence fleeSequence;

        private BaseActor currentThreat;
        private Vector3 lastKnownThreatPosition;
        private float panicTimer = 999f;
        private float outsideRoomComfortTimer;
        private float wanderTimer;
        private float nextWanderTime;
        private Vector3 currentShelterTarget;
        private bool isRegisteredInSpatialIndex;
        private float nextDangerScanTime;
        private bool hasVisibleThreatThisScan;
        private readonly List<BaseActor> nearbyZombies = new List<BaseActor>(16);
        private readonly List<Vector3> visibleThreatPositions = new List<Vector3>(16);
        private readonly List<ThreatMemory> recentThreatPositions = new List<ThreatMemory>(24);
        private readonly List<Vector3> activeThreatPositions = new List<Vector3>(32);
        private bool hasProcessedDeath;
        private NavMeshPath wanderPath;
        private NavMeshPath fleePath;
        private float nextFleeReplanTime;
        private bool forceFleeReplan;
        private int consecutiveStuckEvents;
        private bool useExpandedRingsForNextReplan;
        private float fleeStuckTimer;
        private Vector3 fleeStuckReferencePosition;
        private bool hasFleeStuckReference;

        private void OnEnable()
        {
            TryRegisterInSpatialIndex();
        }

        protected override void Start()
        {
            base.Start();
            agent = GetComponent<NavMeshAgent>();
            agent.speed = moveSpeed;
            teamID = 0; // Human
            outsideRoomComfortTimer = Mathf.Max(0f, outsideRoomComfortDuration);
            wanderPath = new NavMeshPath();
            fleePath = new NavMeshPath();

            if (obstacleLayer == 0) obstacleLayer = 1 << 6; // Default to Wall if unset

            // Build the Behavior Tree
            BuildBehaviorTree();
            TryRegisterInSpatialIndex();
        }

        private void OnDisable()
        {
            UnregisterFromSpatialIndex();
        }

        private void OnDestroy()
        {
            UnregisterFromSpatialIndex();
        }

        private void BuildBehaviorTree()
        {
            // Priority 1: Flee from Zombies
             
            fleeSequence = new Sequence("Flee Sequence", new List<Node>
            {
                new ActionNode("Check For Danger", CheckForDanger),
                new ActionNode("Flee", ActionFlee)
            });

            // Priority 2: Seek Shelter (Hide in Room)
            Sequence shelterSequence = new Sequence("Seek Shelter Sequence", new List<Node>
            {
                new ActionNode("Needs Shelter", NeedsShelter),
                new ActionNode("Seek Shelter", ActionSeekShelter)
            });

            // Priority 3: Wander
            Sequence wanderSequence = new Sequence("Wander Sequence", new List<Node>
            {
                new ActionNode("Wander Room", ActionWander)
            });

            rootNode = new Selector("Guest Behavior", new List<Node>
            {
                fleeSequence,
                shelterSequence,
                wanderSequence
            });
        }

        public string GetBehaviorTreeStatus()
        {
            if (rootNode == null) return "Initializing...";
            return rootNode.GetTreeStateAsString(0);
        }

        private void Update()
        {
            if (currentHealth <= 0) return;
            rootNode?.ResetState();
            rootNode?.Evaluate();
        }

        // --- Nodes ---

        private NodeState CheckForDanger()
        {
            if (Time.time >= nextDangerScanTime)
            {
                PerformDangerScan();
                nextDangerScanTime = Time.time + Mathf.Max(0.01f, dangerScanInterval);
            }

            PruneExpiredThreatMemory();

            if (hasVisibleThreatThisScan && currentThreat != null && currentThreat.currentHealth > 0f)
            {
                panicTimer = 0f;
                return NodeState.Success;
            }

            currentThreat = null;
            panicTimer += Time.deltaTime;

            // Continue to panic if a threat was recently lost
            if (panicTimer < panicPersistence)
            {
                return NodeState.Success;
            }

            ResetFleeState();
            return NodeState.Failure;
        }

        private bool CanSeeTarget(BaseActor target)
        {
            Vector3 dirToTarget = target.transform.position - transform.position;
            float dist = dirToTarget.magnitude;

            if (dist > dangerDetectionRadius) return false;

            // 1. Hearing Radius (360 degrees)
            if (dist < hearingRadius) return true;

            // 2. FOV Check
            float angle = Vector3.Angle(transform.forward, dirToTarget);
            if (angle < viewAngle / 2f)
            {
                // 3. LOS Raycast (Eye level to target center)
                Vector3 eyePos = transform.position + Vector3.up * 1.5f;
                Vector3 targetPos = target.transform.position + Vector3.up * 0.8f;
                Vector3 rayDir = targetPos - eyePos;

                if (!Physics.Raycast(eyePos, rayDir, dist, obstacleLayer))
                {
                    return true;
                }
            }

            return false;
        }

        private NodeState ActionFlee()
        {
            if (!IsAgentReady()) return NodeState.Running;

            agent.speed = moveSpeed * 1.5f; // Run faster when fleeing.
            RebuildActiveThreatPositions();
            if (activeThreatPositions.Count == 0)
            {
                return NodeState.Running;
            }

            UpdateFleeStuckState();

            bool shouldReplan = forceFleeReplan
                || Time.time >= nextFleeReplanTime
                || !agent.hasPath
                || (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.25f);

            if (shouldReplan)
            {
                bool consumeExpandedRings = useExpandedRingsForNextReplan;
                if (TryFindBestFleeDestination(out Vector3 bestDestination))
                {
                    agent.SetDestination(bestDestination);
                    forceFleeReplan = false;
                    nextFleeReplanTime = Time.time + Mathf.Max(0.05f, fleeReplanInterval);
                }

                if (consumeExpandedRings)
                {
                    useExpandedRingsForNextReplan = false;
                }
            }

            AlignFacingToVelocity();
            return NodeState.Running;
        }

        private NodeState NeedsShelter()
        {
            // Sample all areas with a generous radius to account for vertical pivot offset (y=1.06 vs y=0.0)
            // Because SamplePosition finds the mathematically closest NavMesh point, it will perfectly identify
            // the floor directly directly underneath the Guest rather than snapping sideways through walls.
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 2.0f, NavMesh.AllAreas))
            {
                int mask = hit.mask;
                if ((mask & (1 << 3)) != 0) 
                {
                    outsideRoomComfortTimer = Mathf.Max(0f, outsideRoomComfortDuration);

                    // If we have an active destination placed deep inside the room, keep going until we reach it!
                    if (currentShelterTarget != Vector3.zero)
                    {
                        if (agent.pathPending || agent.remainingDistance > 1.5f)
                        {
                            return NodeState.Success;
                        }
                        currentShelterTarget = Vector3.zero;
                    }
                    return NodeState.Failure; // We are completely safe deep inside a room!
                }
            }

            // Not in room: countdown comfort timer before deciding to seek shelter.
            if (outsideRoomComfortTimer > 0f)
            {
                outsideRoomComfortTimer = Mathf.Max(0f, outsideRoomComfortTimer - Time.deltaTime);
                return NodeState.Failure;
            }

            return NodeState.Success; // Comfort timer expired, we now seek shelter.
        }

        private NodeState ActionSeekShelter()
        {
            if (!agent.isOnNavMesh) return NodeState.Running;

            agent.speed = moveSpeed;

            // If we already have a path and are moving to the shelter target, keep running
            if (currentShelterTarget != Vector3.zero && (agent.pathPending || agent.hasPath))
            {
                // Verify we are actually moving TO the shelter, and didn't just get overwritten by Wander
                if (Vector3.Distance(agent.destination, currentShelterTarget) < 2f)
                {
                    if (agent.pathPending || agent.remainingDistance > 1.0f) return NodeState.Running;
                }
            }
            
            int roomMask = 1 << 3;

            // Sample the NavMesh to find the mathematically closest Room edge
            NavMeshHit roomHit;
            if (NavMesh.SamplePosition(transform.position, out roomHit, 100f, roomMask))
            {
                // Push the coordinate a few meters DEEPER into the room based on the physical approach vector
                Vector3 approachDirection = (roomHit.position - transform.position).normalized;
                if (approachDirection != Vector3.zero && Vector3.Distance(transform.position, roomHit.position) > 1.0f) 
                {
                    Vector3 deeperTarget = roomHit.position + (approachDirection * 3.5f); // Padding 3.5 meters inside
                    
                    // Validate that the deeper point is logically traversable on the Room NavMesh
                    NavMeshHit deepHit;
                    if (NavMesh.SamplePosition(deeperTarget, out deepHit, 4.0f, roomMask))
                    {
                        currentShelterTarget = deepHit.position;
                    }
                    else 
                    {
                        currentShelterTarget = roomHit.position; 
                    }
                }
                else
                {
                    currentShelterTarget = roomHit.position;
                }

                agent.SetDestination(currentShelterTarget);
                return NodeState.Running;
            }
            
            // If we absolutely couldn't find a room nearby, fail the shelter behavior to fallback to wandering
            return NodeState.Failure;
        }

        private NodeState ActionWander()
        {
            if (!IsAgentReady()) return NodeState.Running;

            agent.speed = moveSpeed * 0.7f; // Walk casually

            // If we've reached our destination or don't have one, increment timer
            if (!agent.pathPending && (!agent.hasPath || agent.remainingDistance < 0.5f))
            {
                wanderTimer += Time.deltaTime;

                if (wanderTimer >= nextWanderTime)
                {
                    float actualWanderRadius = wanderRadius;
                    int maskToUse = NavMesh.AllAreas;
                     
                    // If we are currently inside a room, only wander INSIDE rooms, and keep pacing very tight!
                    NavMeshHit roomCheck;
                    if (NavMesh.SamplePosition(transform.position, out roomCheck, 2.0f, NavMesh.AllAreas))
                    {
                        if ((roomCheck.mask & (1 << 3)) != 0)
                        {
                            maskToUse = 1 << 3; 
                            actualWanderRadius = 4f; 
                        }
                    }

                    Vector3 bestTarget = transform.position;
                    float bestScore = float.MaxValue;
                    bool foundValidTarget = false;
                    int sampleCount = Mathf.Max(1, wanderCandidateSamples);
                    Vector3 fallbackTarget = transform.position;
                    bool hasFallbackTarget = false;

                    // Sample multiple NavMesh-valid points and choose the cheapest reachable one.
                    for (int i = 0; i < sampleCount; i++)
                    {
                        Vector2 randomDir = Random.insideUnitCircle * actualWanderRadius;
                        Vector3 candidate = transform.position + new Vector3(randomDir.x, 0f, randomDir.y);

                        NavMeshHit candidateHit;
                        if (!NavMesh.SamplePosition(candidate, out candidateHit, 2.0f, maskToUse))
                        {
                            continue;
                        }

                        // Keep at least one sampled fallback so we still move even if path scoring fails.
                        if (!hasFallbackTarget)
                        {
                            fallbackTarget = candidateHit.position;
                            hasFallbackTarget = true;
                        }

                        if (wanderPath == null)
                        {
                            wanderPath = new NavMeshPath();
                        }

                        if (!agent.CalculatePath(candidateHit.position, wanderPath) || wanderPath.status != NavMeshPathStatus.PathComplete)
                        {
                            continue;
                        }

                        float pathLength = 0f;
                        Vector3[] corners = wanderPath.corners;
                        for (int c = 1; c < corners.Length; c++)
                        {
                            pathLength += Vector3.Distance(corners[c - 1], corners[c]);
                        }

                        if (pathLength < bestScore)
                        {
                            bestScore = pathLength;
                            bestTarget = candidateHit.position;
                            foundValidTarget = true;
                        }
                    }

                    if (foundValidTarget)
                    {
                        agent.SetDestination(bestTarget);
                    }
                    else if (hasFallbackTarget)
                    {
                        agent.SetDestination(fallbackTarget);
                    }

                    wanderTimer = 0f;
                    nextWanderTime = Random.Range(minWanderWaitTime, maxWanderWaitTime);
                }
            }
            else
            {
                // We are still traveling to our destination, keep timer at 0
                wanderTimer = 0f;
            }

            return NodeState.Running;
        }

        private bool IsAgentReady()
        {
            return agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh;
        }

        public float GetOutsideRoomComfortTimeRemaining()
        {
            return Mathf.Max(0f, outsideRoomComfortTimer);
        }

        private void OnDrawGizmosSelected()
        {
            // Draw Vision Cone
            Gizmos.color = (currentThreat != null) ? Color.red : Color.green;
            Vector3 leftRay = Quaternion.Euler(0, -viewAngle / 2, 0) * transform.forward;
            Vector3 rightRay = Quaternion.Euler(0, viewAngle / 2, 0) * transform.forward;
            Gizmos.DrawRay(transform.position + Vector3.up, leftRay * dangerDetectionRadius);
            Gizmos.DrawRay(transform.position + Vector3.up, rightRay * dangerDetectionRadius);
            
            // Draw Hearing Radius
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, hearingRadius);
        }

        private void TryRegisterInSpatialIndex()
        {
            if (isRegisteredInSpatialIndex) return;

            ActorSpatialIndex index = ActorSpatialIndex.Instance;
            if (index == null) return;

            index.RegisterHuman(this);
            isRegisteredInSpatialIndex = true;
        }

        private void UnregisterFromSpatialIndex()
        {
            if (!isRegisteredInSpatialIndex) return;

            ActorSpatialIndex index = ActorSpatialIndex.Instance;
            if (index != null)
            {
                index.UnregisterHuman(this);
            }

            isRegisteredInSpatialIndex = false;
        }

        private void PerformDangerScan()
        {
            hasVisibleThreatThisScan = false;
            visibleThreatPositions.Clear();

            ActorSpatialIndex index = ActorSpatialIndex.Instance;
            if (index == null)
            {
                return;
            }

            index.QueryZombiesInRadius(transform.position, dangerDetectionRadius, nearbyZombies);

            BaseActor closestVisibleThreat = null;
            float closestDistanceSqr = float.MaxValue;
            int closestId = int.MaxValue;

            for (int i = 0; i < nearbyZombies.Count; i++)
            {
                BaseActor actor = nearbyZombies[i];
                if (actor == null || actor.teamID != 1 || actor.currentHealth <= 0f) continue;

                if (!CanSeeTarget(actor)) continue;

                Vector3 threatPosition = actor.transform.position;
                visibleThreatPositions.Add(threatPosition);
                AddOrRefreshThreatMemory(threatPosition);

                float distSqr = (threatPosition - transform.position).sqrMagnitude;
                int actorId = actor.GetInstanceID();

                if (distSqr < closestDistanceSqr || (Mathf.Approximately(distSqr, closestDistanceSqr) && actorId < closestId))
                {
                    closestVisibleThreat = actor;
                    closestDistanceSqr = distSqr;
                    closestId = actorId;
                }
            }

            if (closestVisibleThreat != null)
            {
                currentThreat = closestVisibleThreat;
                lastKnownThreatPosition = closestVisibleThreat.transform.position;
                hasVisibleThreatThisScan = true;
            }
            else
            {
                currentThreat = null;
            }
        }

        private void PruneExpiredThreatMemory()
        {
            float now = Time.time;
            for (int i = recentThreatPositions.Count - 1; i >= 0; i--)
            {
                if (recentThreatPositions[i].expiresAt <= now)
                {
                    recentThreatPositions.RemoveAt(i);
                }
            }
        }

        private void AddOrRefreshThreatMemory(Vector3 threatPosition)
        {
            float expiryTime = Time.time + Mathf.Max(0.01f, panicPersistence);
            const float mergeDistanceSqr = 1.0f;

            for (int i = 0; i < recentThreatPositions.Count; i++)
            {
                ThreatMemory memory = recentThreatPositions[i];
                if ((memory.position - threatPosition).sqrMagnitude <= mergeDistanceSqr)
                {
                    memory.position = threatPosition;
                    memory.expiresAt = expiryTime;
                    recentThreatPositions[i] = memory;
                    return;
                }
            }

            recentThreatPositions.Add(new ThreatMemory(threatPosition, expiryTime));
        }

        private void RebuildActiveThreatPositions()
        {
            activeThreatPositions.Clear();

            for (int i = 0; i < visibleThreatPositions.Count; i++)
            {
                AddUniqueThreatPosition(activeThreatPositions, visibleThreatPositions[i]);
            }

            float now = Time.time;
            for (int i = 0; i < recentThreatPositions.Count; i++)
            {
                ThreatMemory memory = recentThreatPositions[i];
                if (memory.expiresAt > now)
                {
                    AddUniqueThreatPosition(activeThreatPositions, memory.position);
                }
            }

            if (activeThreatPositions.Count == 0 && panicTimer < panicPersistence && lastKnownThreatPosition != Vector3.zero)
            {
                activeThreatPositions.Add(lastKnownThreatPosition);
            }
        }

        private void AddUniqueThreatPosition(List<Vector3> positions, Vector3 candidate)
        {
            const float duplicateDistanceSqr = 0.25f;
            for (int i = 0; i < positions.Count; i++)
            {
                if ((positions[i] - candidate).sqrMagnitude <= duplicateDistanceSqr)
                {
                    return;
                }
            }

            positions.Add(candidate);
        }

        private bool TryFindBestFleeDestination(out Vector3 bestDestination)
        {
            bestDestination = transform.position;
            if (activeThreatPositions.Count == 0)
            {
                return false;
            }

            float nearRadius = Mathf.Max(0.5f, fleeNearRadius);
            float farRadius = Mathf.Max(nearRadius + 0.5f, fleeFarRadius);
            if (useExpandedRingsForNextReplan)
            {
                nearRadius = Mathf.Max(nearRadius, 6f);
                farRadius = Mathf.Max(farRadius, 10f);
            }

            int angleCount = Mathf.Max(4, fleeAnglesPerRing);
            float angleStep = 360f / angleCount;

            bool hasBest = false;
            float bestScore = float.MinValue;
            float bestPathLength = float.MaxValue;

            for (int ring = 0; ring < 2; ring++)
            {
                float radius = ring == 0 ? nearRadius : farRadius;
                for (int i = 0; i < angleCount; i++)
                {
                    float radians = Mathf.Deg2Rad * (angleStep * i);
                    Vector3 direction = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians));
                    Vector3 candidate = transform.position + (direction * radius);

                    TryScoreFleeCandidate(candidate, ref hasBest, ref bestScore, ref bestPathLength, ref bestDestination);
                }
            }

            Vector3 awayFromCentroidDirection = ComputeAwayFromThreatCentroidDirection();
            Vector3 centroidCandidate = transform.position + (awayFromCentroidDirection * farRadius);
            TryScoreFleeCandidate(centroidCandidate, ref hasBest, ref bestScore, ref bestPathLength, ref bestDestination);

            return hasBest;
        }

        private Vector3 ComputeAwayFromThreatCentroidDirection()
        {
            Vector3 weightedCentroid = Vector3.zero;
            float totalWeight = 0f;

            for (int i = 0; i < activeThreatPositions.Count; i++)
            {
                Vector3 toThreat = activeThreatPositions[i] - transform.position;
                float distSqr = Mathf.Max(0.25f, toThreat.sqrMagnitude);
                float weight = 1f / distSqr;
                weightedCentroid += activeThreatPositions[i] * weight;
                totalWeight += weight;
            }

            if (totalWeight > 0f)
            {
                weightedCentroid /= totalWeight;
            }
            else
            {
                weightedCentroid = lastKnownThreatPosition;
            }

            Vector3 away = transform.position - weightedCentroid;
            away.y = 0f;
            if (away.sqrMagnitude <= 0.0001f)
            {
                away = transform.forward;
                away.y = 0f;
            }

            if (away.sqrMagnitude <= 0.0001f)
            {
                away = Vector3.forward;
            }

            return away.normalized;
        }

        private void TryScoreFleeCandidate(
            Vector3 rawCandidate,
            ref bool hasBest,
            ref float bestScore,
            ref float bestPathLength,
            ref Vector3 bestDestination)
        {
            NavMeshHit sampled;
            float sampleRadius = Mathf.Max(1f, fleeProbeRadius);
            if (!NavMesh.SamplePosition(rawCandidate, out sampled, sampleRadius, NavMesh.AllAreas))
            {
                return;
            }

            if (fleePath == null)
            {
                fleePath = new NavMeshPath();
            }

            if (!agent.CalculatePath(sampled.position, fleePath) || fleePath.status != NavMeshPathStatus.PathComplete)
            {
                return;
            }

            float pathLength = CalculatePathLength(fleePath);
            float nearestThreatDistance = CalculateNearestThreatDistance(sampled.position);
            float openness = CalculateOpenness(sampled.position);
            float score = nearestThreatDistance - (pathLength * fleePathCostWeight) + (openness * fleeOpennessWeight);

            bool isBetter = false;
            if (!hasBest || score > bestScore + 0.0001f)
            {
                isBetter = true;
            }
            else if (Mathf.Abs(score - bestScore) <= 0.0001f && pathLength < bestPathLength - 0.0001f)
            {
                isBetter = true;
            }
            else if (Mathf.Abs(score - bestScore) <= 0.0001f && Mathf.Abs(pathLength - bestPathLength) <= 0.0001f)
            {
                if (sampled.position.x < bestDestination.x - 0.0001f)
                {
                    isBetter = true;
                }
                else if (Mathf.Abs(sampled.position.x - bestDestination.x) <= 0.0001f
                    && sampled.position.z < bestDestination.z - 0.0001f)
                {
                    isBetter = true;
                }
            }

            if (!isBetter)
            {
                return;
            }

            hasBest = true;
            bestScore = score;
            bestPathLength = pathLength;
            bestDestination = sampled.position;
        }

        private float CalculatePathLength(NavMeshPath path)
        {
            if (path == null || path.corners == null || path.corners.Length < 2)
            {
                return 0f;
            }

            float length = 0f;
            Vector3[] corners = path.corners;
            for (int i = 1; i < corners.Length; i++)
            {
                length += Vector3.Distance(corners[i - 1], corners[i]);
            }

            return length;
        }

        private float CalculateNearestThreatDistance(Vector3 candidatePosition)
        {
            if (activeThreatPositions.Count == 0)
            {
                return 0f;
            }

            float nearestSqr = float.MaxValue;
            for (int i = 0; i < activeThreatPositions.Count; i++)
            {
                float distSqr = (activeThreatPositions[i] - candidatePosition).sqrMagnitude;
                if (distSqr < nearestSqr)
                {
                    nearestSqr = distSqr;
                }
            }

            return Mathf.Sqrt(Mathf.Max(0f, nearestSqr));
        }

        private float CalculateOpenness(Vector3 position)
        {
            int openCount = 0;
            const int probeDirections = 8;
            float probeSampleRadius = Mathf.Max(0.5f, fleeProbeRadius * 0.5f);
            float probeDistance = Mathf.Max(0.5f, fleeProbeRadius);

            for (int i = 0; i < probeDirections; i++)
            {
                float radians = Mathf.Deg2Rad * ((360f / probeDirections) * i);
                Vector3 direction = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians));
                Vector3 probePoint = position + (direction * probeDistance);

                NavMeshHit hit;
                if (NavMesh.SamplePosition(probePoint, out hit, probeSampleRadius, NavMesh.AllAreas))
                {
                    openCount++;
                }
            }

            return openCount;
        }

        private void UpdateFleeStuckState()
        {
            if (!agent.hasPath || agent.pathPending)
            {
                ResetStuckTracking();
                return;
            }

            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.25f)
            {
                ResetStuckTracking();
                return;
            }

            Vector3 currentPosition = transform.position;
            if (!hasFleeStuckReference)
            {
                fleeStuckReferencePosition = currentPosition;
                fleeStuckTimer = 0f;
                hasFleeStuckReference = true;
                return;
            }

            Vector3 planarDelta = currentPosition - fleeStuckReferencePosition;
            planarDelta.y = 0f;
            float movedDistance = planarDelta.magnitude;
            if (movedDistance >= Mathf.Max(0.01f, stuckDistanceThreshold))
            {
                fleeStuckReferencePosition = currentPosition;
                fleeStuckTimer = 0f;
                consecutiveStuckEvents = 0;
                return;
            }

            fleeStuckTimer += Time.deltaTime;
            if (fleeStuckTimer < Mathf.Max(0.1f, stuckTimeThreshold))
            {
                return;
            }

            forceFleeReplan = true;
            hasFleeStuckReference = false;
            fleeStuckTimer = 0f;
            consecutiveStuckEvents++;
            if (consecutiveStuckEvents >= 2)
            {
                useExpandedRingsForNextReplan = true;
                consecutiveStuckEvents = 0;
            }
        }

        private void ResetStuckTracking()
        {
            hasFleeStuckReference = false;
            fleeStuckTimer = 0f;
            consecutiveStuckEvents = 0;
        }

        private void ResetFleeState()
        {
            forceFleeReplan = false;
            useExpandedRingsForNextReplan = false;
            nextFleeReplanTime = 0f;
            ResetStuckTracking();
        }

        private void AlignFacingToVelocity()
        {
            Vector3 velocity = agent.velocity;
            velocity.y = 0f;
            if (velocity.sqrMagnitude <= 0.04f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);
        }

        protected override void Die()
        {
            if (!hasProcessedDeath)
            {
                hasProcessedDeath = true;

                bool killedByZombie = lastDamageSourceTeamID == 1;
                if (killedByZombie && InfectionConversionService.Instance != null)
                {
                    InfectionConversionService.Instance.TryConvertGuestToZombie(transform.position, transform.rotation);
                }
            }

            base.Die();
        }
    }
}
