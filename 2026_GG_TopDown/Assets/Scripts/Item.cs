using UnityEngine;

public class Item : MonoBehaviour
{
    private bool collected;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player")) Collect(other.GetComponent<Inventory>());
    }

    public bool Collect(Inventory inventory)
    {
        if (collected || !inventory) return false;
        collected = true;
        inventory.AddKey();
        Destroy(gameObject);
        return true;
    }
}
