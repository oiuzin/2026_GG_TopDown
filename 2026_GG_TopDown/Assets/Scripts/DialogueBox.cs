using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
public class DialogueBox : MonoBehaviour
{
    public GameObject panel;
    public Image portraitImage;
    public TMP_Text nameText;
    public TMP_Text bodyText;
    public GameObject choicePanel;
    public TMP_Text choiceText1;
    public TMP_Text choiceText2;
    public bool isOpen = false;
    public Talk nearTalk;
     public Door nearDoor; 
    private Talk current;
    private int index = 0;
    private bool choosing = false;
    private int pick = 1;

    void Update()
    {

        Keyboard kb = Keyboard.current;
        if (choosing)
        {
            bool up = kb.upArrowKey.wasPressedThisFrame;
            bool down = kb.downArrowKey.wasPressedThisFrame;
            if (up) { pick = 1; DrawChoices(); }
            if (down) { pick = 2; DrawChoices(); }
            if (kb.eKey.wasPressedThisFrame) { Choose(pick); }

        }
        else if (kb.eKey.wasPressedThisFrame)
        {
            if (isOpen) { Next(); }
            else if (nearTalk != null) { Open(nearTalk); }
            else if (nearDoor != null) { nearDoor.Use(); }
        }
    }
    public void Open(Talk talk)
    {
        current = talk;
        index = 0;
        isOpen = true;
        panel.SetActive(true);
        ShowLine();
    }
    void ShowLine()
    {
        nameText.text = current.lines[index].speaker;
        bodyText.text = current.lines[index].text;
        Sprite face = current.lines[index].portrait;
        portraitImage.sprite = face;
        portraitImage.gameObject.SetActive(face != null);
        bool last = index == current.lines.Length - 1;
        if (last && current.choice1 != "") { ShowChoices(); }
    }
    void Next()
    {
        index += 1;
        if (index < current.lines.Length) { ShowLine(); }
        else { Close(); }
    }
    void ShowChoices()
    {
        choosing = true;
        pick = 1;
        DrawChoices();
        choicePanel.SetActive(true);
    }
    void DrawChoices()
    {
        choiceText1.text = "   " + current.choice1;
        choiceText2.text = "   " + current.choice2;
        if (pick == 1) { choiceText1.text = "> " + current.choice1; }
        else { choiceText2.text = "> " + current.choice2; }
    }

    void Choose(int number)
    {
        Talk chosen = current;
        Close();
        if (number == 1) { chosen.onChoice1.Invoke(); }
        else { chosen.onChoice2.Invoke(); }
    }
    void Close()
    {
        choosing = false;
        isOpen = false;
        panel.SetActive(false);
        choicePanel.SetActive(false);
    }
}
