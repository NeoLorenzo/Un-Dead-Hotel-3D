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
        private bool hasProcessedDeath;
        private NavMeshPath wanderPath;

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
            // Even if currentThreat is out of sight, panic memory triggers Flee using lastKnownThreatPosition

            // Flee logic: Run in opposite direction from the last known threat position
            Vector3 directionAwayFromThreat = (transform.position - lastKnownThreatPosition).normalized;
            Vector3 fleePosition = transform.position + (directionAwayFromThreat * 10f); // Run 10 meters away

            // Ensure the flee point is on the NavMesh
            NavMeshHit hit;
            if (NavMesh.SamplePosition(fleePosition, out hit, 10f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
                agent.speed = moveSpeed * 1.5f; // Run faster when fleeing!
            }

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

                float distSqr = (actor.transform.position - transform.position).sqrMagnitude;
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
