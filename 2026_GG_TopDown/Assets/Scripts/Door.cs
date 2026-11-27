using UnityEngine;
public class Door : MonoBehaviour
{
    public Transform target;
    public GameObject prompt;
    public DialogueBox dialogueBox;
    public int needKeys = 0;
    public Talk lockedTalk;
    private Transform player;
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            player = other.transform;
            prompt.SetActive(true);
            dialogueBox.nearDoor = this;
        }
    }
    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            prompt.SetActive(false);
            dialogueBox.nearDoor = null;
        }
    }
    public void Use()
    {
        if (!TryUse(player)) lockedTalk.Say();
    }

    public bool TryUse(Transform visitor)
    {
        if (!visitor || !target) return false;
        var inventory = visitor.GetComponent<Inventory>();
        if (needKeys > 0 && (!inventory || inventory.keys < needKeys)) return false;
        visitor.position = target.position;
        return true;
    }
}
