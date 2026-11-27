using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
public class PlayerHealth : MonoBehaviour
{
    public int life = 3;
    public float safeTime = 1f;
    public TMP_Text lifeText;
    private float safeTimer = 0f;
    void Update()
    {
        safeTimer -= Time.deltaTime;
    }
    void OnCollisionStay2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Enemy") && TryTakeHit() && life == 0)
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public bool TryTakeHit()
    {
        if (safeTimer > 0 || life <= 0) return false;
        life--;
        safeTimer = safeTime;
        if (lifeText) lifeText.text = "목숨 " + life;
        return true;
    }
}
