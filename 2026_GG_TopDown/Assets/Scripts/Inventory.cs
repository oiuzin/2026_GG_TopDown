using UnityEngine;
using TMPro;
public class Inventory : MonoBehaviour
{
    public int keys = 0;
    public TMP_Text keyText;
    public void AddKey()
    {
        keys += 1;
        if (keyText) keyText.text = "열쇠 " + keys;
    }
}
