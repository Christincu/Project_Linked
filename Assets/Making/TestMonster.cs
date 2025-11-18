using UnityEngine;

public class TestMonster : MonoBehaviour
{
    public int maxHP = 60;
    private int currentHP;

    private Renderer rend;
    private Color originalColor;
    private float flashDuration = 0.1f;

    [Header("Auto Damage")]
    public float startAutoDamageAfter = 7f; // 10초 뒤부터
    public int damagePerSecond = 20;         // 초당 20

    private void Start()
    {
        currentHP = maxHP;

        rend = GetComponent<Renderer>();
        originalColor = rend.material.color;

        // 10초 기다렸다가 초당 데미지 시작
        InvokeRepeating(nameof(ApplyAutoDamage), startAutoDamageAfter, 1f);
    }

    private void ApplyAutoDamage()
    {
        TakeDamage(damagePerSecond);
    }

    public void TakeDamage(int damage)
    {
        if (currentHP <= 0) return;

        currentHP -= damage;

        StopAllCoroutines();
        StartCoroutine(FlashRed());

        if (currentHP <= 0)
        {
            Die();
        }
    }

    private System.Collections.IEnumerator FlashRed()
    {
        rend.material.color = Color.red;
        yield return new WaitForSeconds(flashDuration);
        rend.material.color = originalColor;
    }

    private void Die()
    {
        Destroy(gameObject);
    }
}
