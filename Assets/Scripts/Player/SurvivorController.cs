using UnityEngine;
using UnityEngine.AI;
using UnDeadHotel.Actors;
using UnDeadHotel.World;

namespace UnDeadHotel.Player
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class SurvivorController : BaseActor
    {
        public NavMeshAgent agent { get; private set; }
        private bool isRegisteredInSpatialIndex;

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

        public void MoveToDestination(Vector3 target)
        {
            if (agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.SetDestination(target);
            }
            else
            {
                Debug.LogWarning($"{gameObject.name} tried to move but agent is not on NavMesh!");
            }
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
    }
}
