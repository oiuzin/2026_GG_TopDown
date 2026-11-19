using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public sealed class ReportStageOne : MonoBehaviour
{
    [SerializeField] private Font interfaceFont;
    [SerializeField] private Material spriteMaterial;
    public const string CheckpointKey = "AnomalyReport.StageOne.Chase";
    private enum Phase { Survey, Report, Verify, Chase, Finished }

    private static readonly Color Ink = new Color32(13, 24, 33, 250);
    private static readonly Color Paper = new Color32(210, 230, 233, 255);
    private static readonly Color Signal = new Color32(136, 222, 226, 255);
    private static readonly Vector2Int[] Directions =
        { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
    private static readonly Vector2Int Entry = new Vector2Int(19, 20);
    private static readonly Vector2Int EnemyEntry = new Vector2Int(10, 16);

    private readonly List<Spot> spots = new List<Spot>();
    private readonly List<SpriteRenderer> crew = new List<SpriteRenderer>();
    private readonly HashSet<string> conversations = new HashSet<string>();
    private readonly HashSet<string> observations = new HashSet<string>();
    private readonly HashSet<string> evidence = new HashSet<string>();
    private readonly Dictionary<string, Sprite> portraits = new Dictionary<string, Sprite>();
    private readonly Dictionary<Vector2Int, Vector2Int> parents = new Dictionary<Vector2Int, Vector2Int>();
    private readonly Queue<Vector2Int> frontier = new Queue<Vector2Int>();
    private Phase phase;
    private Action afterDialogue;
    private string[] dialogue;
    private int line;
    private bool cutscene;
    private bool choosing;
    private Text heading, objective, hint, speaker, message, advance;
    private RectTransform hud, panel, choices, endPanel;
    private Image portrait, blackout, document;
    [SerializeField] private Tilemap ground;
    [SerializeField] private Tilemap walls;
    [SerializeField] private SpriteRenderer shutter;
    private AudioSource effects;
    private SpriteRenderer player, enemy;
    private Sprite[] playerFrames, enemyFrames, morphFrames;
    private Camera view;
    private Spot nearest;
    private Vector2 enemyTarget;
    private int playerDirection, enemyDirection;
    private float enemyGrace;
    private bool locked;

    private sealed class Spot
    {
        public Vector2 Position;
        public string Name;
        public Action Inspect;
        public Func<bool> Available;
        public SpriteRenderer Marker;
    }

    private void Start()
    {
        AudioListener.volume = PlayerPrefs.GetFloat("ReportVolume", 1);
        effects = gameObject.AddComponent<AudioSource>();
        BuildInterface();
        playerFrames = ReportUI.Sheet("Crew/R/walk-128px", 3, 4, 104);
        enemyFrames = ReportUI.Sheet("Crew/spider", 3, 4, 78);
        morphFrames = ReportUI.Sheet("Crew/morph", 4, 2, 104);
        portraits.Add("R", ReportUI.Asset("Crew/R/dialogue"));
        player = RendererAt("R", playerFrames[0], Entry, 5);
        enemy = RendererAt("E 변이체", enemyFrames[0], EnemyEntry, 5);
        enemy.enabled = false;
        SetupInteractions();

        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        view = cameraObject.GetComponent<Camera>();
        view.orthographic = true;
        view.orthographicSize = 5.9f;
        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = new Color32(7, 15, 24, 255);
        Follow(1);

        if (PlayerPrefs.HasKey(CheckpointKey))
        {
            BeginChase();
            return;
        }

        player.transform.position = MainBuildingPlan.World(new Vector2Int(8, 20));
        Follow(1);
        Speak(null, "D|아, 드디어 일어났네. 너 없으면 오늘도 내가 일 다 할 뻔했잖아.",
            "R|어제도 네가 그렇게 말했어.",
            "A|밤에 문 두드리는 소리 못 들었어요?",
            "C|R, 관제실에 있는 어젯밤 기록을 확인해 줘.",
            "T|전력 로그를 다시 확인해 볼게요.",
            "E|여기선 다들 비슷하게 입고 있으니까, 제가 잘못 봤을 수도 있죠.");
    }

    private SpriteRenderer RendererAt(string name, Sprite sprite, Vector2Int cell, int order)
    {
        var item = new GameObject(name, typeof(SpriteRenderer));
        item.transform.SetParent(transform, false);
        item.transform.position = MainBuildingPlan.World(cell);
        var renderer = item.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sharedMaterial = spriteMaterial;
        renderer.sortingOrder = order;
        return renderer;
    }

    private void SetupInteractions()
    {
        AddCrew("D", 21, 4, "기록보다 먼저 커피 좀 확인해. 이건 생존 문제야.",
            "어젯밤엔 창가 쪽에서 이상한 소리가 났어.");
        AddCrew("A", 8, 8, "본관동 안쪽에서 누가 문을 두드렸어요.",
            "문을 열어 봤을 땐 아무도 없었어요.");
        AddCrew("C", 35, 4, "기록 확인, 전력 점검, 통신 확인. 순서대로.",
            "밖으로 나가는 건 눈보라가 잦아든 뒤에.");
        AddCrew("T", 33, 2, "어젯밤 전력 사용량이 맞지 않아요.",
            "로그에는 사용자가 없어요. 그러니까 더 이상하죠.");
        AddCrew("E", 4, 7, "아, 잘못 봤을 수도 있죠. 여기선 다들 비슷하게 입고 있으니까.",
            "누군가 이상하다고 느껴지면 먼저 자기 기억을 확인해 봐요.");

        Inspect("공동 사진", 19, 4, "여섯 명이 찍힌 사진이다. 누가 찍었는지는 기억나지 않는다.",
            "사진 뒤에는 다섯 명의 코드만 적혀 있다.", true);
        Inspect("식당 머그잔", 5, 19, "잔이 여섯 개. 손잡이마다 작은 흠집이 있다.",
            "누군가 한 개를 치웠다. 자국은 여섯 개 그대로 남아 있다.");
        Inspect("커피 주전자", 24, 3, "아직 따뜻하다. D가 아까부터 이것만 찾았다.",
            "누군가 방금 커피를 따른 것 같은데 주전자는 차갑다.");
        Inspect("복도 긁힌 자국", 16, 15, "무거운 물건을 끌고 간 흔적이다.");
        Inspect("숙소 개인 메모", 3, 14, "오늘도 같은 꿈.");
        Inspect("전력 로그", 30, 4, "사용자 표시가 없는 기록이 세 건 있다.",
            "사용자 기록은 다섯 개. 전력 소비량은 여섯 작업대 분량이다.");
        Inspect("공식 명부", 27, 20, "오늘 작업 지시에는 관제실 확인이 적혀 있다.",
            "등록 대원은 R, D, A, C, T. 다섯 명뿐이다.", true);

        AddSpot("발전기 손상 사진", 32, 4, () =>
        {
            if (phase == Phase.Survey && conversations.Count == 5 && observations.Count >= 4)
            {
                phase = Phase.Report;
                Speak(null, "R|금속판이 안쪽으로 휘었어. 이건 공구 자국이 아닌데.",
                    "T|사진의 손상 폭을 보세요. 같은 방향으로 긁혔어요.",
                    "C|일단 본부에 보고하자. 지원이 필요해.");
            }
            else Speak(null, "R|금속판이 안쪽으로 휘었다. 긁힌 자국이 크고 깊다.");
        }, () => phase <= Phase.Verify);

        AddSpot("본부 무전", 37, 11, Radio, () => phase <= Phase.Verify);
        AddSpot("인원 보고 단말", 33, 12, () =>
        {
            if (phase == Phase.Verify) OpenChoices();
            else Speak(null, "R|인원 현황을 입력하는 단말이다.");
        }, () => phase <= Phase.Verify);

        AddSpot("건물 전체 봉쇄", 36, 2, LockDown, () => phase == Phase.Chase);
    }

    private void AddCrew(string code, int x, int y, string first, string second)
    {
        portraits.Add(code, ReportUI.Asset("Crew/" + code + "/dialogue"));
        var frame = ReportUI.Sheet("Crew/" + code + "/walk-128px", 3, 4, 104)[0];
        crew.Add(RendererAt(code, frame, new Vector2Int(x, y), 5));
        AddSpot(code + " 대화", x, y, () =>
        {
            bool firstTalk = conversations.Add(code);
            if (firstTalk) Speak(null, code + "|" + first, code + "|" + second);
            else Speak(null, code + "|" + second);
        }, () => phase <= Phase.Verify);
    }

    private void Inspect(string name, int x, int y, string first, string revised = null,
        bool verification = false)
    {
        AddSpot(name, x, y, () =>
        {
            if (phase == Phase.Verify && revised != null)
            {
                if (verification) evidence.Add(name);
                Speak(CheckEvidence, "R|" + revised);
                if (name == "공식 명부") ShowDocument("official_roster_R_D_A_C_T");
            }
            else
            {
                observations.Add(name);
                Speak(null, "R|" + first);
            }
        }, () => phase <= Phase.Verify);
    }

    private void AddSpot(string name, int x, int y, Action action, Func<bool> available)
    {
        var marker = RendererAt(name + " 표시", null, new Vector2Int(x, y), 8);
        marker.sprite = ReportUI.Asset("Main/hazard_stripe", 96);
        marker.transform.localScale = new Vector3(1.4f, .45f, 1);
        marker.transform.position += Vector3.up * .67f;
        marker.enabled = false;
        spots.Add(new Spot { Position = MainBuildingPlan.World(new Vector2Int(x, y)),
            Name = name, Inspect = action, Available = available, Marker = marker });
    }

    private void Radio()
    {
        if (phase != Phase.Report)
        {
            Speak(null, phase == Phase.Verify ? "R|잡음 사이에서 숨소리가 들린다."
                : "R|신호가 불안정하다. 발전기 상태부터 확인해야겠다.");
            return;
        }

        phase = Phase.Verify;
        Speak(null, "본부|현장 기지, 응답 바랍니다. 발전 설비 이상 신호가 접수되었습니다.",
            "C|여기는 현장 기지. 현재 여섯 명 모두 생존. 부상자 없습니다.",
            "본부|……여섯 명이라고 하셨습니까?",
            "본부|등록 인원은 총 다섯 명입니다. R, D, A, C, T. 추가 등록 인원은 없습니다.",
            "R|본부, 현장에는 분명 여섯 명이 있습니다.",
            "E|일단 진정해요. 서류가 잘못됐을 수도 있죠.",
            "본부|단독 행동을 금지하십시오. 모든 이상 징후를 기록하고 보고하십시오.",
            "C|어제 기록과 오늘 기록부터 맞춰 보자.");
    }

    private void OpenChoices()
    {
        choosing = true;
        Present("인원 보고 단말", "현장 인원을 선택해 전송하세요.");
        advance.text = "숫자 5 / 6 또는 버튼";
        choices.gameObject.SetActive(true);
        choices.GetComponentInChildren<Button>().Select();
    }

    private void SendReport(bool six)
    {
        if (!choosing) return;
        choosing = false;
        choices.gameObject.SetActive(false);
        EventSystem.current.SetSelectedGameObject(null);
        if (six)
        {
            evidence.Add("인원 보고 오류");
            Speak(CheckEvidence, "시스템|현장 인원 보고 06 — 등록 정보와 일치하지 않습니다.",
                "R|여섯 명이 여기 있는데 다섯 명만 적으라고요?",
                "본부|확인된 인원만 기재하십시오. 모든 이상 징후를 누락 없이 기록하십시오.",
                "C|전송은 보류해. 종이 기록은 따로 남겨 두자.");
        }
        else Speak(null, "시스템|05 입력 정상. 전송 대기 중.",
            "R|입력은 되지만 현장 인원과 다르다. 06도 확인해 봐야겠다.");
    }

    private void CheckEvidence()
    {
        if (evidence.Count == 3) StartCoroutine(TransformE());
    }

    private IEnumerator TransformE()
    {
        cutscene = true;
        player.transform.position = MainBuildingPlan.World(Entry);
        Follow(1);
        blackout.color = Color.black;
        PlayEffect("scrape");
        yield return new WaitForSeconds(.55f);

        enemy.transform.position = MainBuildingPlan.World(new Vector2Int(17, 19));
        enemy.enabled = true;
        blackout.color = new Color(.18f, 0, 0, .22f);
        foreach (Sprite frame in morphFrames)
        {
            enemy.sprite = frame;
            yield return new WaitForSeconds(.075f);
        }
        blackout.color = Color.black;
        foreach (SpriteRenderer member in crew) member.enabled = false;
        yield return new WaitForSeconds(.40f);
        enemy.sprite = enemyFrames[0];
        blackout.color = new Color(.15f, 0, 0, .22f);
        cutscene = false;
        Speak(BeginChase, "A|또 들렸어요. 금속을 긁는 소리.",
            "T|저건… 저건 사람이 아니에요.",
            "C|R, 관제실로 뛰어. 지금.",
            "E|R. 어디 가요? 우리 아직 얘기 끝나지 않았는데.");
    }

    private void BeginChase()
    {
        phase = Phase.Chase;
        PlayerPrefs.SetInt(CheckpointKey, 1);
        PlayerPrefs.Save();
        foreach (SpriteRenderer member in crew) member.enabled = false;
        player.transform.position = MainBuildingPlan.World(Entry);
        enemy.transform.position = MainBuildingPlan.World(EnemyEntry);
        enemyTarget = enemy.transform.position;
        enemy.enabled = true;
        enemyGrace = 1.2f;
        blackout.color = new Color(.18f, 0, 0, .19f);
        Follow(1);
    }

    private void LockDown()
    {
        phase = Phase.Finished;
        locked = true;
        shutter.enabled = true;
        PlayEffect("shutter");
        Speak(() =>
        {
            PlayerPrefs.DeleteKey(CheckpointKey);
            PlayerPrefs.Save();
            End("STAGE 01 종료", "본관동 전체 봉쇄 완료. R의 의식이 끊겼다.");
        }, "시스템|본관동 전체 봉쇄 완료. 출입 통제 활성화.",
            "E|R. 문 열어요.",
            "E|다들 돌아올 텐데. 혼자 기다릴 수 있겠어요?",
            "R|무슨 뜻이야.",
            "E|그냥, 기다리면 알게 될 거예요.");
    }

    private void End(string title, string description)
    {
        phase = Phase.Finished;
        blackout.color = Color.black;
        hud.gameObject.SetActive(false);
        endPanel.gameObject.SetActive(true);
        ReportUI.Label(endPanel, title, .15f, .54f, .85f, .72f, 70, Signal);
        ReportUI.Label(endPanel, description, .13f, .44f, .87f, .53f, 34, Paper);
        if (!locked) ReportUI.Button(endPanel, "추격 다시 시작", .36f, .29f, .64f, .39f, Ink, Paper,
            () => SceneManager.LoadScene("StageOne")).Select();
        var titleButton = ReportUI.Button(endPanel, "타이틀로", .36f, .17f, .64f, .26f, Ink, Paper,
            () => SceneManager.LoadScene("ReportTitle"));
        if (locked) titleButton.Select();
    }

    private void BuildInterface()
    {
        new GameObject("Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
        var root = new ReportUI(transform, interfaceFont).Root;
        blackout = ReportUI.Image(root, "Emergency light", 0, 0, 1, 1, Color.clear);
        hud = ReportUI.Place(root, "HUD", 0, 0, 1, 1);
        var header = ReportUI.Image(hud, "Objective background", .025f, .833f, .48f, .971f, Ink);
        heading = ReportUI.Label(header.transform, "", .046f, .52f, .95f, .93f, 33, Signal, TextAnchor.MiddleLeft);
        objective = ReportUI.Label(header.transform, "", .046f, .11f, .95f, .51f, 25, Paper, TextAnchor.MiddleLeft);
        hint = ReportUI.Label(hud, "", .20f, .038f, .80f, .108f, 31, Paper);
        var shadow = hint.gameObject.AddComponent<Outline>();
        shadow.effectColor = Ink;
        shadow.effectDistance = new Vector2(2, -2);

        panel = ReportUI.Image(root, "Dialogue", .065f, .048f, .935f, .288f, Ink).rectTransform;
        portrait = ReportUI.Image(panel, "Portrait", .022f, .14f, .155f, .88f, Color.white);
        speaker = ReportUI.Label(panel, "", .174f, .68f, .91f, .93f, 36, Signal, TextAnchor.MiddleLeft);
        message = ReportUI.Label(panel, "", .174f, .30f, .951f, .67f, 34, Paper, TextAnchor.MiddleLeft);
        advance = ReportUI.Label(panel, "", .67f, .03f, .84f, .27f, 23, Paper, TextAnchor.MiddleRight);
        var next = ReportUI.Button(panel, "다음", .85f, .047f, .95f, .265f, new Color32(35, 65, 75, 255), Paper, NextLine);
        next.navigation = new Navigation { mode = Navigation.Mode.None };
        document = ReportUI.Image(panel, "Evidence preview", .67f, .31f, .96f, .94f, Color.white);
        document.enabled = false;
        choices = ReportUI.Place(panel, "Report options", 0, 0, 1, 1);
        ReportUI.Image(choices, "Options background", .18f, .03f, .96f, .29f, Ink);
        ReportUI.Button(choices, "05 전송", .45f, .052f, .63f, .265f, new Color32(35, 65, 75, 255), Paper, () => SendReport(false));
        ReportUI.Button(choices, "06 전송", .66f, .052f, .84f, .265f, new Color32(35, 65, 75, 255), Paper, () => SendReport(true));
        choices.gameObject.SetActive(false);
        panel.gameObject.SetActive(false);
        endPanel = ReportUI.Place(root, "Result", 0, 0, 1, 1);
        endPanel.gameObject.SetActive(false);
    }

    private void Speak(Action completion, params string[] lines)
    {
        dialogue = lines;
        afterDialogue = completion;
        line = 0;
        ShowLine();
    }

    private void ShowLine()
    {
        string entry = dialogue[line];
        int divider = entry.IndexOf('|');
        Present(entry.Substring(0, divider), entry.Substring(divider + 1));
        advance.text = "F / Enter";
    }

    private void Present(string name, string text)
    {
        panel.gameObject.SetActive(true);
        document.enabled = false;
        message.rectTransform.anchorMax = new Vector2(.951f, .67f);
        hint.gameObject.SetActive(false);
        speaker.text = name;
        message.text = text;
        portrait.enabled = portraits.TryGetValue(name, out Sprite thumbnail);
        portrait.sprite = thumbnail;
    }

    private void ShowDocument(string texture)
    {
        document.sprite = ReportUI.Asset("Main/" + texture);
        document.preserveAspect = true;
        document.enabled = true;
        message.rectTransform.anchorMax = new Vector2(.64f, .67f);
    }

    private void PlayEffect(string name)
    {
        effects.PlayOneShot(Resources.Load<AudioClip>("AnomalyReport/" + name), .55f);
    }

    private void NextLine()
    {
        if (dialogue == null || choosing) return;
        if (++line < dialogue.Length)
        {
            ShowLine();
            return;
        }

        dialogue = null;
        panel.gameObject.SetActive(false);
        hint.gameObject.SetActive(true);
        var completion = afterDialogue;
        afterDialogue = null;
        completion?.Invoke();
    }

    private void Update()
    {
        if (!player) return;
        var keys = Keyboard.current;
        var pad = Gamepad.current;
        bool interact = (keys != null && (keys.fKey.wasPressedThisFrame || keys.eKey.wasPressedThisFrame
            || keys.enterKey.wasPressedThisFrame || keys.spaceKey.wasPressedThisFrame))
            || (pad != null && pad.buttonSouth.wasPressedThisFrame);
        if (choosing)
        {
            if (keys != null && (keys.digit5Key.wasPressedThisFrame || keys.numpad5Key.wasPressedThisFrame)) SendReport(false);
            else if (keys != null && (keys.digit6Key.wasPressedThisFrame || keys.numpad6Key.wasPressedThisFrame)) SendReport(true);
            return;
        }
        if (dialogue != null)
        {
            if (interact) NextLine();
            return;
        }
        if (cutscene || phase == Phase.Finished) return;

        Vector2 move = pad != null ? pad.leftStick.ReadValue() : Vector2.zero;
        if (keys != null)
        {
            move.x += Axis(keys.dKey.isPressed || keys.rightArrowKey.isPressed, keys.aKey.isPressed || keys.leftArrowKey.isPressed);
            move.y += Axis(keys.wKey.isPressed || keys.upArrowKey.isPressed, keys.sKey.isPressed || keys.downArrowKey.isPressed);
        }
        var motion = Vector2.ClampMagnitude(move, 1) * (phase == Phase.Chase ? 5.65f : 5.25f)
            * Mathf.Min(Time.deltaTime, .035f);
        var position = (Vector2)player.transform.position;
        var step = position + Vector2.right * motion.x;
        if (Walkable(step, .24f)) position = step;
        step = position + Vector2.up * motion.y;
        if (Walkable(step, .24f)) position = step;
        player.transform.position = position;
        Animate(player, playerFrames, motion, ref playerDirection);
        Follow(1 - Mathf.Exp(-9 * Time.deltaTime));

        ShowObjective(position);
        FindSpot(position);
        if (interact && nearest != null) nearest.Inspect();
        if (phase == Phase.Chase && dialogue == null) MoveEnemy(position);
    }

    private static int Axis(bool positive, bool negative) => (positive ? 1 : 0) - (negative ? 1 : 0);

    private bool WalkableCell(Vector2Int cell)
    {
        var tile = new Vector3Int(cell.x, -cell.y, 0);
        return ground.HasTile(tile) && !walls.HasTile(tile);
    }

    private bool Walkable(Vector2 center, float radius)
    {
        return WalkableCell(MainBuildingPlan.Cell(center + new Vector2(-radius, -radius)))
            && WalkableCell(MainBuildingPlan.Cell(center + new Vector2(-radius, radius)))
            && WalkableCell(MainBuildingPlan.Cell(center + new Vector2(radius, -radius)))
            && WalkableCell(MainBuildingPlan.Cell(center + new Vector2(radius, radius)));
    }

    private void FindSpot(Vector2 position)
    {
        nearest = null;
        float best = 2.2f;
        foreach (Spot spot in spots)
        {
            bool available = spot.Available();
            spot.Marker.enabled = available && (position - spot.Position).sqrMagnitude < 22f;
            float distance = (position - spot.Position).sqrMagnitude;
            if (available && distance < best && ClearPath(position, spot.Position))
            {
                nearest = spot;
                best = distance;
            }
        }

        hint.text = nearest != null ? $"[F]  {nearest.Name}" : "WASD / 방향키 이동   ·   F 조사 및 대화";
        foreach (Spot spot in spots)
            spot.Marker.color = spot == nearest ? Signal : new Color(.75f, .78f, .83f, .6f);
    }

    private bool ClearPath(Vector2 start, Vector2 destination)
    {
        int samples = Mathf.CeilToInt(Vector2.Distance(start, destination) * 6);
        for (int i = 1; i < samples; i++)
            if (!Walkable(Vector2.Lerp(start, destination, i / (float)samples), 0)) return false;
        return true;
    }

    private void ShowObjective(Vector2 position)
    {
        Vector2Int cell = MainBuildingPlan.Cell(position);
        string room = "복도";
        if (cell.y < 6) room = cell.x < 8 ? "침실 A" : cell.x < 16 ? "침실 B" : cell.x < 27 ? "휴게실" : "관제실";
        else if (cell.x < 6 && cell.y < 17) room = cell.y < 11 ? "침실 C" : "침실 D";
        else if (cell.x < 16 && cell.y > 17) room = "식당";
        else if (cell.x > 30 && cell.y > 9) room = cell.y < 14 ? "통신실" : "창고";
        else if (cell.x > 22 && cell.x < 30 && cell.y > 18) room = "기록실";
        heading.text = "본관동 1F  /  " + room;
        switch (phase)
        {
            case Phase.Survey:
                objective.text = conversations.Count == 5 && observations.Count >= 4
                    ? "관제실의 발전기 손상 사진 확인"
                    : $"대원 대화 {conversations.Count}/5   ·   주변 조사 {Mathf.Min(4, observations.Count)}/4";
                break;
            case Phase.Report:
                objective.text = "통신실에서 본부에 무전";
                break;
            case Phase.Verify:
                objective.text = $"공동 사진 · 기록실 명부 · 인원 보고 단말  {evidence.Count}/3";
                break;
            case Phase.Chase:
                objective.color = new Color32(255, 160, 143, 255);
                objective.text = "북동쪽 관제실로 이동해 전체 봉쇄를 작동하세요";
                break;
        }
    }

    private void MoveEnemy(Vector2 position)
    {
        enemyGrace -= Time.deltaTime;
        if (enemyGrace > 0) return;
        var from = (Vector2)enemy.transform.position;
        if ((enemyTarget - from).sqrMagnitude < .001f)
        {
            var current = MainBuildingPlan.Cell(from);
            var destination = MainBuildingPlan.Cell(position);
            enemyTarget = MainBuildingPlan.World(NextCell(current, destination));
        }

        var next = Vector2.MoveTowards(from, enemyTarget, 4.85f * Mathf.Min(Time.deltaTime, .035f));
        enemy.transform.position = next;
        Animate(enemy, enemyFrames, next - from, ref enemyDirection);
        if ((position - next).sqrMagnitude < .50f && ClearPath(next, position))
            End("신호 소실", "E가 R을 따라잡았다.");
    }

    private Vector2Int NextCell(Vector2Int start, Vector2Int destination)
    {
        if (start == destination || !WalkableCell(destination)) return start;
        frontier.Clear();
        parents.Clear();
        frontier.Enqueue(start);
        parents.Add(start, start);
        while (frontier.Count > 0)
        {
            Vector2Int current = frontier.Dequeue();
            foreach (Vector2Int direction in Directions)
            {
                Vector2Int next = current + direction;
                if (!WalkableCell(next) || parents.ContainsKey(next)) continue;
                parents[next] = current;
                if (next == destination)
                {
                    while (parents[next] != start) next = parents[next];
                    return next;
                }
                frontier.Enqueue(next);
            }
        }
        return start;
    }

    private static void Animate(SpriteRenderer renderer, Sprite[] frames, Vector2 motion, ref int direction)
    {
        bool moving = motion.sqrMagnitude > .000001f;
        if (moving) direction = Mathf.Abs(motion.y) > Mathf.Abs(motion.x) ? (motion.y < 0 ? 0 : 1)
            : (motion.x < 0 ? 2 : 3);
        int frame = moving ? Mathf.FloorToInt(Time.time * 9) % 3 : 0;
        renderer.sprite = frames[direction * 3 + frame];
    }

    private void Follow(float fraction)
    {
        var position = player.transform.position;
        var target = new Vector3(position.x, position.y, -10);
        view.transform.position = Vector3.Lerp(view.transform.position, target, fraction);
    }
}
