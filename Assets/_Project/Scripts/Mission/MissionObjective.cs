using UnityEngine;

namespace WarMission
{
    /// <summary>
    /// One step of a mission. MissionManager activates objectives in order and moves on when one completes.
    /// Subclasses report progress text and, optionally, a world position for the HUD marker.
    /// </summary>
    public abstract class MissionObjective : MonoBehaviour
    {
        [SerializeField] string title = "Objective";

        public string Title => title;
        public bool IsActive { get; private set; }
        public bool IsComplete { get; protected set; }
        /// <summary>Short progress line under the title, for example "Wave 2/3  Enemies 4".</summary>
        public virtual string Progress => "";
        /// <summary>The HUD draws a marker with the distance when true.</summary>
        public virtual bool HasMarker => false;
        public virtual Vector3 MarkerPosition => transform.position;

        public void Activate(MissionManager mission)
        {
            IsActive = true;
            OnActivate(mission);
        }

        public void Deactivate() => IsActive = false;

        protected virtual void OnActivate(MissionManager mission) { }

        /// <summary>Called every frame while active.</summary>
        public virtual void Tick(MissionManager mission) { }
    }
}
