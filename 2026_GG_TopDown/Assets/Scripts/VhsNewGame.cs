using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class VhsNewGame : MonoBehaviour
{
    [SerializeField] private Image titleLogo;
    [SerializeField] private CanvasGroup menu;
    [SerializeField] private Image blackout;
    [SerializeField] private string firstScene;

    private Image redLogo;
    private bool starting;

    internal void Configure(Image logo, Image red, CanvasGroup group, Image cover, string scene)
    {
        titleLogo = logo;
        redLogo = red;
        menu = group;
        blackout = cover;
        firstScene = scene;
    }

    public void StartNewGame()
    {
        if (starting || !titleLogo || !menu || !blackout) return;
        if (!Application.CanStreamedLevelBeLoaded(firstScene))
        {
            Debug.LogError("Set First Scene to a scene included in the build.", this);
            return;
        }
        starting = true;
        menu.interactable = false;
        menu.blocksRaycasts = false;
        StartCoroutine(PlayTransition());
    }

    private IEnumerator PlayTransition()
    {
        Color white = Color.white;
        Color red = new Color32(210, 31, 44, 255);
        for (float elapsed = 0; elapsed < 0.26f; elapsed += Time.unscaledDeltaTime)
        {
            float progress = elapsed / 0.26f;
            titleLogo.color = redLogo ? Color.Lerp(white, Color.clear, progress) : Color.Lerp(white, red, progress);
            if (redLogo) redLogo.color = new Color(1, 1, 1, progress);
            menu.alpha = 1f - elapsed / 0.26f;
            yield return null;
        }
        titleLogo.color = redLogo ? Color.clear : red;
        if (redLogo) redLogo.color = white;
        menu.alpha = 0;
        yield return new WaitForSecondsRealtime(0.24f);

        for (float elapsed = 0; elapsed < 0.22f; elapsed += Time.unscaledDeltaTime)
        {
            blackout.color = new Color(0, 0, 0, elapsed / 0.22f);
            yield return null;
        }
        blackout.color = Color.black;
        SceneManager.LoadScene(firstScene);
    }
}
