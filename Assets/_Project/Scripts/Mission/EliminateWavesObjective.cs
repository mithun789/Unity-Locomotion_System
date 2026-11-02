using UnityEngine;

namespace WarMission
{
    /// <summary>Survive and kill every wave a WaveSpawner sends.</summary>
    public class EliminateWavesObjective : MissionObjective
    {
        [SerializeField] WaveSpawner spawner;

        public override string Progress => spawner == null ? ""
            : spawner.IsBetweenWaves
                ? $"Next wave in {Mathf.CeilToInt(spawner.TimeToNextWave)} s  ({spawner.CurrentWave}/{spawner.TotalWaves} done)"
                : $"Wave {spawner.CurrentWave}/{spawner.TotalWaves}  Enemies left {spawner.AliveCount}";

        protected override void OnActivate(MissionManager mission)
        {
            if (spawner == null)
            {
                Debug.LogWarning("[Mission] EliminateWavesObjective has no WaveSpawner; skipping.", this);
                IsComplete = true;
                return;
            }
            spawner.WaveStarted += wave => mission.Announce($"Wave {wave} incoming");
            spawner.Begin(mission.PlayerTransform);
        }

        public override void Tick(MissionManager mission)
        {
            if (spawner != null && spawner.IsFinished) IsComplete = true;
        }
    }
}
