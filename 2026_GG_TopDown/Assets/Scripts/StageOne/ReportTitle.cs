using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class ReportTitle : MonoBehaviour
{
    [SerializeField] private Font interfaceFont;
    [SerializeField] private SpriteRenderer editorPreview;

    private void Start()
    {
        AudioListener.volume = PlayerPrefs.GetFloat("ReportVolume", 1);
        editorPreview.enabled = false;
        var events = new GameObject("Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
        var screen = new ReportUI(transform, interfaceFont).Root;
        var ink = new Color32(8, 30, 39, 255);
        var paper = new Color32(164, 218, 224, 245);
        ReportUI.Image(screen, "VHS background", 0, 0, 1, 1, Color.white,
            ReportUI.Asset("Title/background"));
        ReportUI.Label(screen, "극지 탐사대 안전 교육 기록", .25f, .80f, .75f, .88f, 33, ink);
        var logo = ReportUI.Image(screen, "Title logo", .28f, .51f, .72f, .78f, Color.white,
            ReportUI.Asset("Title/logo-white"));
        var red = ReportUI.Image(screen, "Red logo", .28f, .51f, .72f, .78f, Color.clear,
            ReportUI.Asset("Title/logo-red"));
        var menu = ReportUI.Place(screen, "Menu", 0, 0, 1, 1).gameObject.AddComponent<CanvasGroup>();
        var transition = gameObject.AddComponent<VhsNewGame>();
        var start = ReportUI.Button(menu.transform, "▶ 새 게임", .411f, .36f, .589f, .416f, paper, ink, () =>
        {
            PlayerPrefs.DeleteKey(ReportStageOne.CheckpointKey);
            PlayerPrefs.Save();
            transition.StartNewGame();
        });
        var resume = ReportUI.Button(menu.transform, "이어하기", .411f, .30f, .589f, .356f, paper, ink,
            transition.StartNewGame);
        resume.interactable = PlayerPrefs.HasKey(ReportStageOne.CheckpointKey);
        if (!resume.interactable) resume.GetComponentInChildren<Text>().color = new Color32(70, 98, 105, 255);
        var settings = ReportUI.Place(screen, "Settings", 0, 0, 1, 1);
        var settingsButton = ReportUI.Button(menu.transform, "설정", .411f, .24f, .589f, .296f, paper, ink, () =>
        {
            menu.gameObject.SetActive(false);
            settings.gameObject.SetActive(true);
            EventSystem.current.SetSelectedGameObject(settings.GetComponentInChildren<Button>().gameObject);
        });
        ReportUI.Button(menu.transform, "종료", .411f, .18f, .589f, .236f, paper, ink, Application.Quit);
        ReportUI.Label(settings, "소리 설정", .35f, .42f, .65f, .49f, 42, ink);
        Text volume = ReportUI.Label(settings, VolumeLabel(), .35f, .34f, .65f, .42f, 35, ink);
        ReportUI.Button(settings, "−", .39f, .26f, .48f, .33f, paper, ink, () => SetVolume(-.1f, volume));
        ReportUI.Button(settings, "+", .52f, .26f, .61f, .33f, paper, ink, () => SetVolume(.1f, volume));
        ReportUI.Button(settings, "돌아가기", .41f, .17f, .59f, .23f, paper, ink, () =>
        {
            settings.gameObject.SetActive(false);
            menu.gameObject.SetActive(true);
            EventSystem.current.SetSelectedGameObject(settingsButton.gameObject);
        });
        settings.gameObject.SetActive(false);
        var blackout = ReportUI.Image(screen, "Blackout", 0, 0, 1, 1, Color.clear);
        transition.Configure(logo, red, menu, blackout, "StageOne");
        events.GetComponent<EventSystem>().SetSelectedGameObject(start.gameObject);
    }

    private static string VolumeLabel() => $"음량 {Mathf.RoundToInt(AudioListener.volume * 100)}%";

    private static void SetVolume(float change, Text label)
    {
        AudioListener.volume = Mathf.Clamp01(AudioListener.volume + change);
        PlayerPrefs.SetFloat("ReportVolume", AudioListener.volume);
        PlayerPrefs.Save();
        label.text = VolumeLabel();
    }
}
