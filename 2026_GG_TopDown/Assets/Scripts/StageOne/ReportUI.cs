using UnityEngine;
using UnityEngine.UI;

internal sealed class ReportUI
{
    private static Font font;
    public readonly RectTransform Root;

    public ReportUI(Transform owner, Font interfaceFont)
    {
        var screen = new GameObject("Screen", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        screen.transform.SetParent(owner, false);
        screen.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = screen.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        Root = (RectTransform)screen.transform;
        font = interfaceFont;
    }

    public static RectTransform Place(Transform parent, string name, float left, float bottom, float right, float top)
    {
        var item = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)item.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(left, bottom);
        rect.anchorMax = new Vector2(right, top);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    public static Image Image(Transform parent, string name, float l, float b, float r, float t,
        Color color, Sprite sprite = null)
    {
        var image = Place(parent, name, l, b, r, t).gameObject.AddComponent<Image>();
        image.color = color;
        image.sprite = sprite;
        image.raycastTarget = false;
        image.preserveAspect = sprite != null;
        return image;
    }

    public static Text Label(Transform parent, string value, float l, float b, float r, float t,
        int size, Color color, TextAnchor alignment = TextAnchor.MiddleCenter)
    {
        var label = Place(parent, "Text", l, b, r, t).gameObject.AddComponent<Text>();
        label.font = font;
        label.text = value;
        label.fontSize = size;
        label.color = color;
        label.alignment = alignment;
        label.raycastTarget = false;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = Mathf.Max(14, size / 2);
        label.resizeTextMaxSize = size;
        return label;
    }

    public static Button Button(Transform parent, string value, float l, float b, float r, float t,
        Color paper, Color ink, UnityEngine.Events.UnityAction action)
    {
        var image = Image(parent, value, l, b, r, t, paper);
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        Label(image.transform, value, 0.04f, 0, 0.96f, 1, 39, ink);
        return button;
    }

    public static Sprite Asset(string path, float ppu = 100)
    {
        var texture = Resources.Load<Texture2D>("AnomalyReport/" + path);
        return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), ppu);
    }

    public static Sprite[] Sheet(string path, int columns, int rows, float ppu)
    {
        var texture = Resources.Load<Texture2D>("AnomalyReport/" + path);
        int width = texture.width / columns;
        int height = texture.height / rows;
        var sprites = new Sprite[columns * rows];
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
        {
            var rect = new Rect(x * width, (rows - y - 1) * height, width, height);
            sprites[y * columns + x] = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), ppu);
        }
        return sprites;
    }
}
