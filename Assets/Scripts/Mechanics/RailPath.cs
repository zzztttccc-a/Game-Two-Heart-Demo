using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Game.Mechanics
{
    public class RailPath : MonoBehaviour
    {
        [Header("Path Settings")]
        [Tooltip("Drag child transforms here to define the path")]
        public List<Transform> waypoints = new List<Transform>();
        
        [Tooltip("Base speed of the player along the path")]
        public float speed = 10f;
        
        [Tooltip("Modulate speed over the path (0 to 1). 1 = Base Speed.")]
        public AnimationCurve speedCurve = AnimationCurve.Linear(0, 1, 1, 1);

        [Tooltip("If true, smooths the path using Catmull-Rom spline")]
        public bool useCurvedPath = true;
        
        [Tooltip("Points density for curved paths. Higher = smoother. This determines how many segments are generated between two waypoints.")]
        public int curveResolution = 20; 

        [Header("Player Control")]
        public bool returnControlOnEnd = true;
        public bool disableGravity = true;
        [Tooltip("If true, teleport player to path start instantly.")]
        public bool instantSnapToStart = true;
        [Tooltip("Allowed distance to start before snapping/moving towards it.")]
        public float snapTolerance = 0.25f;
        [Tooltip("Multiplier applied to base speed when approaching start point if not teleporting.")]
        public float snapSpeedMultiplier = 3f;

        private HeroController heroCtrl;
        private Rigidbody2D heroRb;
        private bool isMoving = false;

        private void Start()
        {
            // Auto-find waypoints if empty
            if (waypoints.Count == 0)
            {
                foreach (Transform child in transform)
                {
                    waypoints.Add(child);
                }
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (isMoving) return;
            
            // Check for Hero
            HeroController hc = other.GetComponent<HeroController>();
            if (hc == null) return;

            // Trigger Logic:
            // We assume this script is attached to the object that has the Trigger Collider.
            // Ideally, this Trigger Collider is positioned at the start of the path (Waypoints[0]).
            
            StartCoroutine(MoveRoutine(hc));
        }

        private IEnumerator MoveRoutine(HeroController hc)
        {
            isMoving = true;
            heroCtrl = hc;
            heroRb = hc.GetComponent<Rigidbody2D>();

            // 1. Relinquish Control
            heroCtrl.controlReqlinquished = true;
            heroCtrl.ResetInput();
            
            // Cancel current states
            heroCtrl.cState.dashing = false;
            heroCtrl.cState.backDashing = false;
            heroCtrl.cState.jumping = false;
            heroCtrl.cState.recoiling = false;
            
            if (disableGravity)
            {
                heroCtrl.AffectedByGravity(false);
            }
            
            heroRb.velocity = Vector2.zero;

            // Force Dash Animation
            // We stop the HeroController from updating animations, and play "Dash" manually
            heroCtrl.StopAnimationControl();
            heroCtrl.PlayClip("Dash", true);

            // 2. Calculate Path Points
            List<Vector3> pathPoints = new List<Vector3>();
            if (useCurvedPath && waypoints.Count >= 2)
            {
                // Generate spline points
                pathPoints = GenerateCatmullRomSpline(waypoints, curveResolution);
            }
            else
            {
                foreach (var t in waypoints) pathPoints.Add(t.position);
            }

            // Calculate Total Distance for Speed Curve
            float totalPathLength = 0f;
            for (int i = 0; i < pathPoints.Count - 1; i++)
            {
                totalPathLength += Vector3.Distance(pathPoints[i], pathPoints[i+1]);
            }

            // 3. Move along path
            if (pathPoints.Count > 0)
            {
                // Snap/Move to start
                float distToStart = Vector2.Distance(heroRb.position, pathPoints[0]);
                if (instantSnapToStart)
                {
                    heroRb.position = pathPoints[0];
                }
                else if (distToStart > snapTolerance)
                {
                    float approachSpeed = Mathf.Max(0.01f, speed * Mathf.Max(1f, snapSpeedMultiplier));
                    while (Vector2.Distance(heroRb.position, pathPoints[0]) > snapTolerance)
                    {
                        heroRb.position = Vector3.MoveTowards(heroRb.position, pathPoints[0], approachSpeed * Time.deltaTime);
                        yield return null;
                    }
                }

                float currentDistanceTraveled = 0f;

                // Follow path
                for (int i = 0; i < pathPoints.Count - 1; i++)
                {
                    Vector3 start = pathPoints[i];
                    Vector3 end = pathPoints[i + 1];
                    Vector3 direction = (end - start).normalized;

                    // Face direction & Rotation
                    float rotationAngle = 0f;
                    if (direction.x > 0.01f)
                    {
                        heroCtrl.FaceRight();
                        rotationAngle = Vector2.SignedAngle(Vector2.right, direction);
                    }
                    else if (direction.x < -0.01f)
                    {
                        heroCtrl.FaceLeft();
                        rotationAngle = Vector2.SignedAngle(Vector2.left, direction);
                    }
                    else
                    {
                        // Maintain facing but rotate
                        if (heroCtrl.cState.facingRight)
                            rotationAngle = Vector2.SignedAngle(Vector2.right, direction);
                        else
                            rotationAngle = Vector2.SignedAngle(Vector2.left, direction);
                    }
                    heroCtrl.transform.rotation = Quaternion.Euler(0, 0, rotationAngle);

                    float segmentLength = Vector3.Distance(start, end);
                    // Prevent division by zero
                    if (segmentLength <= Mathf.Epsilon) continue;

                    float tSegment = 0;

                    while (tSegment < 1f)
                    {
                        // Calculate Global T (0 to 1)
                        float currentGlobalDist = currentDistanceTraveled + (tSegment * segmentLength);
                        float globalT = Mathf.Clamp01(currentGlobalDist / totalPathLength);

                        // Get Speed from Curve
                        float speedMult = speedCurve.Evaluate(globalT);
                        float currentSpeed = speed * speedMult;
                        if (currentSpeed < 0.1f) currentSpeed = 0.1f; // Minimum speed safety

                        // Move
                        float moveStep = Time.deltaTime * currentSpeed;
                        tSegment += moveStep / segmentLength;
                        
                        heroRb.position = Vector3.Lerp(start, end, tSegment);
                        yield return null;
                    }

                    currentDistanceTraveled += segmentLength;
                }
            }

            // 4. Return Control
            if (returnControlOnEnd)
            {
                heroCtrl.controlReqlinquished = false;
                if (disableGravity)
                {
                    heroCtrl.AffectedByGravity(true);
                }
                heroRb.velocity = Vector2.zero; 
                heroCtrl.transform.rotation = Quaternion.identity; // Reset Rotation
                
                // Restore Animation Control
                heroCtrl.StartAnimationControl();
                // Optional: Force Idle or Fall depending on state
                // HeroController update will handle it next frame
            }

            isMoving = false;
        }

        private List<Vector3> GenerateCatmullRomSpline(List<Transform> controlPoints, int resolution)
        {
            List<Vector3> points = new List<Vector3>();

            if (controlPoints.Count < 2) return points;

            // Need at least 4 points for full spline. 
            // We duplicate start and end points to simulate "virtual" control points for the ends.
            List<Vector3> cPoints = new List<Vector3>();
            cPoints.Add(controlPoints[0].position); // Virtual start
            foreach (var t in controlPoints) cPoints.Add(t.position);
            cPoints.Add(controlPoints[controlPoints.Count - 1].position); // Virtual end

            for (int i = 0; i < cPoints.Count - 3; i++)
            {
                Vector3 p0 = cPoints[i];
                Vector3 p1 = cPoints[i + 1];
                Vector3 p2 = cPoints[i + 2];
                Vector3 p3 = cPoints[i + 3];

                for (int j = 0; j < resolution; j++)
                {
                    float t = j / (float)resolution;
                    points.Add(GetCatmullRomPosition(t, p0, p1, p2, p3));
                }
            }
            // Add final point
            points.Add(cPoints[cPoints.Count - 2]);

            return points;
        }

        private Vector3 GetCatmullRomPosition(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            // The coefficients of the cubic polynomial (except the 0.5f * which I added later for performance)
            Vector3 a = 2f * p1;
            Vector3 b = p2 - p0;
            Vector3 c = 2f * p0 - 5f * p1 + 4f * p2 - p3;
            Vector3 d = -p0 + 3f * p1 - 3f * p2 + p3;

            // The cubic polynomial: a + b * t + c * t^2 + d * t^3
            Vector3 pos = 0.5f * (a + (b * t) + (c * t * t) + (d * t * t * t));
            return pos;
        }

        private void OnDrawGizmos()
        {
            if (waypoints == null || waypoints.Count < 2) return;

            Gizmos.color = Color.yellow;
            
            // Draw waypoints
            foreach (var t in waypoints)
            {
                if (t != null) Gizmos.DrawWireSphere(t.position, 0.3f);
            }

            // Draw Path
            if (useCurvedPath)
            {
                // Preview Spline
                List<Vector3> drawPoints = GenerateCatmullRomSpline(waypoints, curveResolution);
                for (int i = 0; i < drawPoints.Count - 1; i++)
                {
                    Gizmos.DrawLine(drawPoints[i], drawPoints[i + 1]);
                }
            }
            else
            {
                // Preview Linear
                for (int i = 0; i < waypoints.Count - 1; i++)
                {
                    if (waypoints[i] != null && waypoints[i+1] != null)
                        Gizmos.DrawLine(waypoints[i].position, waypoints[i + 1].position);
                }
            }
        }
    }
}
