using UnityEngine;

namespace WarMission
{
    /// <summary>Spectator camera for AI battles: frames all living soldiers from a fixed side angle.</summary>
    public class BattleCamera : MonoBehaviour
    {
        [Tooltip("Direction the camera looks from (flat), relative to the world.")]
        [SerializeField] Vector3 viewFrom = new Vector3(1f, 0f, -0.35f);
        [SerializeField] float pitch = 35f;
        [SerializeField] float minDistance = 12f;
        [Tooltip("Extra distance per metre of spread between soldiers.")]
        [SerializeField] float distancePerSpread = 1.1f;
        [SerializeField] float followSharpness = 2f;

        Vector3 focus;
        float distance;
        bool initialized;

        void LateUpdate()
        {
            var members = TeamRegistry.Members;
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < members.Count; i++)
            {
                if (!members[i].IsAlive) continue;
                sum += members[i].transform.position;
                count++;
            }
            if (count == 0) return;

            Vector3 center = sum / count;
            float spread = 0f;
            for (int i = 0; i < members.Count; i++)
                if (members[i].IsAlive) spread = Mathf.Max(spread, Vector3.Distance(center, members[i].transform.position));

            float wantedDistance = minDistance + spread * distancePerSpread;
            if (!initialized)
            {
                focus = center;
                distance = wantedDistance;
                initialized = true;
            }
            float blend = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            focus = Vector3.Lerp(focus, center, blend);
            distance = Mathf.Lerp(distance, wantedDistance, blend);

            Vector3 flat = new Vector3(viewFrom.x, 0f, viewFrom.z).normalized;
            Vector3 back = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, flat)) * flat;
            transform.position = focus + Vector3.up + back * distance;
            transform.LookAt(focus + Vector3.up);
        }
    }
}
