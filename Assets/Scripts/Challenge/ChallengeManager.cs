using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class ChallengeManager : MonoBehaviour
{
    [Header("Challenge Settings")]
    [Tooltip("The ordered list of points to visit. Index 0 is the start point.")]
    public List<ChallengePoint> points;
    
    [Tooltip("Global time limit for intermediate points if they don't have their own.")]
    public float defaultTimeLimit = 5.0f;

    [Header("Events")]
    public UnityEvent onChallengeStart;
    public UnityEvent onChallengeComplete;
    public UnityEvent onChallengeFail;

    private int currentPointIndex = 0;
    private bool isChallengeActive = false;
    private float currentTimer = 0f;
    private bool timerRunning = false;

    private void Start()
    {
        // Ensure points list is populated if possible, or warn
        if (points == null || points.Count == 0)
        {
            Debug.LogWarning("ChallengeManager: No points assigned!");
        }
        
        ResetChallenge();
    }

    private void Update()
    {
        if (timerRunning)
        {
            currentTimer -= Time.deltaTime;
            if (currentTimer <= 0)
            {
                FailChallenge();
            }
        }
    }

    public void ResetChallenge()
    {
        isChallengeActive = false;
        timerRunning = false;
        currentPointIndex = 0;

        // Reset all points
        if (points != null)
        {
            foreach (var point in points)
            {
                if (point != null) point.Deactivate();
            }

            // Activate start point (Index 0)
            if (points.Count > 0 && points[0] != null)
            {
                points[0].Activate(); // Start point is waiting for interaction
            }
        }
    }

    public void OnPointHit(ChallengePoint point)
    {
        // Validation
        if (points == null || !points.Contains(point)) return;
        int hitIndex = points.IndexOf(point);

        // If hitting the current target
        if (hitIndex == currentPointIndex)
        {
            // If it's the start point (Index 0)
            if (hitIndex == 0)
            {
                StartChallenge();
            }
            else
            {
                AdvanceToNextPoint();
            }
        }
    }

    private void StartChallenge()
    {
        if (isChallengeActive) return;

        isChallengeActive = true;
        onChallengeStart.Invoke();
        Debug.Log("Challenge Started!");

        // Start point hit, deactivate it and move to next
        if (points != null && points.Count > 0)
        {
            points[0].Deactivate();
            AdvanceToNextPoint();
        }
    }

    private void AdvanceToNextPoint()
    {
        // Deactivate the current point (the one that was just hit)
        if (currentPointIndex >= 0 && currentPointIndex < points.Count)
        {
            points[currentPointIndex].Deactivate();
        }

        currentPointIndex++;

        // Check completion
        if (currentPointIndex >= points.Count)
        {
            CompleteChallenge();
            return;
        }

        // Activate next point
        ChallengePoint nextPoint = points[currentPointIndex];
        if (nextPoint != null)
        {
            nextPoint.Activate();

            // Check if this is the last point (Reward Point)
            if (currentPointIndex == points.Count - 1)
            {
                // Stop timer, Challenge Chase is over.
                timerRunning = false;
                Debug.Log("Chase Finished! End Point (Reward) Activated. Waiting for pickup...");
            }
            else
            {
                // Start timer for this point
                float limit = nextPoint.timeLimit > 0 ? nextPoint.timeLimit : defaultTimeLimit;
                currentTimer = limit;
                timerRunning = true;
                Debug.Log($"Next Point Activated! Time Limit: {limit}s");
            }
        }
    }

    private void FailChallenge()
    {
        isChallengeActive = false;
        timerRunning = false;
        onChallengeFail.Invoke();
        Debug.Log("Challenge Failed! Resetting...");
        ResetChallenge();
    }

    private void CompleteChallenge()
    {
        isChallengeActive = false;
        timerRunning = false;
        onChallengeComplete.Invoke();
        Debug.Log("Challenge Completed!");
        
        GenerateReward();
    }

    private void GenerateReward()
    {
        // TODO: Implement reward logic
        Debug.Log("Challenge Reward Generated! (Placeholder)");
    }
}
