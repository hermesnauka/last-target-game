using UnityEngine;

// Client-side prediction only: visual position/health are corrected by the
// authoritative NetworkClient/server per SR-1 in SSDLC_Unity_CPP_Shooter_Plan.md.
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private int maxHealth = 100;

    private int currentHealth;

    void Start()
    {
        currentHealth = maxHealth;
    }

    void Update()
    {
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");

        Vector3 predictedMove = new Vector3(moveX, moveY, 0f) * moveSpeed * Time.deltaTime;
        transform.position += predictedMove;
    }

    // Called when the server broadcasts an authoritative correction.
    public void ApplyServerCorrection(Vector3 authoritativePosition, int authoritativeHealth)
    {
        transform.position = authoritativePosition;
        currentHealth = authoritativeHealth;
    }
}
