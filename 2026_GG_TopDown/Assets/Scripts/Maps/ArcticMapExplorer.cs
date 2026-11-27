using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public sealed class ArcticMapExplorer : MonoBehaviour
{
    [Serializable]
    private sealed class Passage
    {
        public string label;
        public Vector2Int point;
        public Vector2Int destination;
        public string scene;
    }

    [SerializeField] private string mapName;
    [SerializeField] private Tilemap ground;
    [SerializeField] private Tilemap walls;
    [SerializeField] private Tilemap obstacles;
    [SerializeField] private Camera mapCamera;
    [SerializeField] private Material spriteMaterial;
    [SerializeField] private Font interfaceFont;
    [SerializeField] private Vector2Int spawn;
    [SerializeField] private Passage[] passages;

    private static Vector2Int? arrival;
    private SpriteRenderer player;
    private Sprite[] frames;
    private Text prompt;
    private int direction;

    private void Start()
    {
        var body = new GameObject("맵 탐색용 R", typeof(SpriteRenderer));
        body.transform.SetParent(transform, false);
        player = body.GetComponent<SpriteRenderer>();
        player.sharedMaterial = spriteMaterial;
        player.sortingOrder = 6;
        frames = ReportUI.Sheet("Crew/R/walk-128px", 3, 4, 96);
        player.sprite = frames[0];
        Place(arrival ?? spawn);
        arrival = null;

        var screen = new ReportUI(transform, interfaceFont).Root;
        var banner = ReportUI.Image(screen, "Map name", .025f, .90f, .32f, .973f,
            new Color32(11, 25, 37, 236));
        ReportUI.Label(banner.transform, mapName, .055f, .08f, .95f, .92f, 36,
            new Color32(186, 229, 237, 255), TextAnchor.MiddleLeft);
        prompt = ReportUI.Label(screen, "", .17f, .04f, .83f, .115f, 32, Color.white);
        prompt.gameObject.AddComponent<Outline>().effectColor = Color.black;
    }

    private void Update()
    {
        var keys = Keyboard.current;
        var pad = Gamepad.current;
        Vector2 input = pad != null ? pad.leftStick.ReadValue() : Vector2.zero;
        if (keys != null)
        {
            input.x += Axis(keys.dKey.isPressed || keys.rightArrowKey.isPressed, keys.aKey.isPressed || keys.leftArrowKey.isPressed);
            input.y += Axis(keys.wKey.isPressed || keys.upArrowKey.isPressed, keys.sKey.isPressed || keys.downArrowKey.isPressed);
        }

        Vector2 position = player.transform.position;
        Vector2 motion = Vector2.ClampMagnitude(input, 1) * (5 * Mathf.Min(Time.deltaTime, .035f));
        var step = position + Vector2.right * motion.x;
        if (Free(step)) position = step;
        step = position + Vector2.up * motion.y;
        if (Free(step)) position = step;
        bool moving = ((Vector2)player.transform.position - position).sqrMagnitude > .00001f;
        player.transform.position = position;
        if (moving) direction = Mathf.Abs(motion.y) >= Mathf.Abs(motion.x) ? (motion.y < 0 ? 0 : 1) : (motion.x < 0 ? 2 : 3);
        player.sprite = frames[direction * 3 + (moving ? (int)(Time.time * 9) % 3 : 0)];
        Follow();

        var passage = NearbyPassage(position);
        prompt.text = passage == null ? "WASD / 방향키 이동   ·   E 문 사용" : "[E]  " + passage.label;
        if (passage == null || !((keys != null && keys.eKey.wasPressedThisFrame)
            || (pad != null && pad.buttonSouth.wasPressedThisFrame))) return;

        if (string.IsNullOrEmpty(passage.scene))
        {
            Place(passage.destination);
            prompt.text = "";
        }
        else
        {
            arrival = passage.destination;
            SceneManager.LoadScene(passage.scene);
        }
    }

    private Passage NearbyPassage(Vector2 position)
    {
        Passage selection = null;
        float best = 1.18f * 1.18f;
        foreach (Passage passage in passages)
        {
            Vector2 target = World(passage.point);
            float distance = (position - target).sqrMagnitude;
            if (distance >= best) continue;
            if (!Open(Vector2.Lerp(position, target, .33f)) || !Open(Vector2.Lerp(position, target, .67f))) continue;
            best = distance;
            selection = passage;
        }
        return selection;
    }

    private void Place(Vector2Int cell)
    {
        player.transform.position = World(cell);
        Follow();
    }

    private void Follow()
    {
        Vector3 point = player.transform.position;
        mapCamera.transform.position = new Vector3(point.x, point.y, -10);
    }

    private bool Free(Vector2 at) =>
        Open(at + new Vector2(-.22f, -.22f)) && Open(at + new Vector2(-.22f, .22f))
        && Open(at + new Vector2(.22f, -.22f)) && Open(at + new Vector2(.22f, .22f));

    private bool Open(Vector2 position)
    {
        Vector3Int cell = ground.WorldToCell(position);
        return ground.HasTile(cell) && (!walls || !walls.HasTile(cell)) && !obstacles.HasTile(cell);
    }

    private static Vector2 World(Vector2Int cell) => new Vector2(cell.x, -cell.y);
    private static int Axis(bool pos, bool neg) => (pos ? 1 : 0) - (neg ? 1 : 0);
}
