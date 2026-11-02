using UnityEngine;

public class GoalTrigger : MonoBehaviour
{
    [SerializeField] private bool isPlayerGoal;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Ball"))
        {
            BallController b = other.GetComponent<BallController>();
            if (b != null)
            {
                b.HandleGoalZoneTrigger(isPlayerGoal);
            }
        }
    }
}
