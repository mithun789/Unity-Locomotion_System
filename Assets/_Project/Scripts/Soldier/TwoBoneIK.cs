using UnityEngine;

namespace WarMission
{
    /// <summary>Analytic two-bone IK (upper arm, forearm, hand) with a pole hint. Runs on world transforms.</summary>
    public static class TwoBoneIK
    {
        /// <param name="hint">World point the elbow bends toward.</param>
        public static void Solve(Transform upper, Transform mid, Transform end, Vector3 targetPosition,
                                 Quaternion targetRotation, Vector3 hint)
        {
            Vector3 a = upper.position;
            float upperLength = Vector3.Distance(a, mid.position);
            float lowerLength = Vector3.Distance(mid.position, end.position);
            Vector3 toTarget = targetPosition - a;
            float distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upperLength - lowerLength) + 0.001f,
                                         upperLength + lowerLength - 0.001f);
            Vector3 dir = toTarget.normalized;

            // Bend plane from the hint: elbow goes to the hint's side of the shoulder-target line.
            Vector3 bend = Vector3.ProjectOnPlane(hint - a, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(mid.position - a, dir);
            bend.Normalize();

            // Law of cosines: angle at the shoulder between the target line and the upper arm.
            float cos = (upperLength * upperLength + distance * distance - lowerLength * lowerLength) / (2f * upperLength * distance);
            float angle = Mathf.Acos(Mathf.Clamp(cos, -1f, 1f));
            Vector3 elbow = a + (dir * Mathf.Cos(angle) + bend * Mathf.Sin(angle)) * upperLength;

            upper.rotation = Quaternion.FromToRotation(mid.position - a, elbow - a) * upper.rotation;
            Vector3 hand = a + dir * distance;
            mid.rotation = Quaternion.FromToRotation(end.position - mid.position, hand - mid.position) * mid.rotation;
            end.rotation = targetRotation;
        }
    }
}
