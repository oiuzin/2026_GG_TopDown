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
    public const string CheckpointKey = "AnomalyReport.StageOne.RecordChase";
    private enum Phase { Survey, Report, Verify, Chase, Finished }

    private const float CrewPixelsPerUnit = 80f;
    private static readonly Color Ink = new Color32(13, 24, 33, 250);
    private static readonly Color Paper = new Color32(210, 230, 233, 255);
    private static readonly Color Signal = new Color32(136, 222, 226, 255);
    private static readonly Vector2Int[] Directions =
        { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
    private static readonly Vector2Int Entry = new Vector2Int(24, 11);
    private static readonly Vector2Int EnemyEntry = new Vector2Int(15, 11);

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
    [SerializeField] private BoxCollider2D[] furniture;
    private readonly List<Bounds> furnitureBounds = new List<Bounds>();
    private AudioSource effects;
    private SpriteRenderer player, enemy;
    private Sprite[] playerFrames, enemyFrames, morphFrames;
    [SerializeField] private Camera view;
    private Spot nearest;
    private Vector2 enemyTarget;
    private int playerDirection, enemyDirection;
    private float enemyGrace, lungeCooldown, lungeWindup, lungeRemaining;
    private bool locked;
    private bool usedHatch;

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
        foreach (BoxCollider2D obstacle in furniture)
            if (obstacle.isActiveAndEnabled) furnitureBounds.Add(obstacle.bounds);
        BuildInterface();
        playerFrames = ReportUI.Sheet("Crew/R/walk-128px", 3, 4, CrewPixelsPerUnit);
        enemyFrames = ReportUI.Sheet("Crew/spider", 3, 4, 43);
        morphFrames = ReportUI.Sheet("Crew/morph", 4, 2, 60);
        LoadPortrait("R");
        player = RendererAt("R", playerFrames[0], Entry, 5);
        enemy = RendererAt("E 변이체", enemyFrames[0], EnemyEntry, 5);
        enemy.enabled = false;
        SetupInteractions();

        Follow(1);

        if (PlayerPrefs.HasKey(CheckpointKey))
        {
            BeginChase();
            return;
        }

        player.transform.position = MainBuildingPlan.World(new Vector2Int(15, 5));
        Follow(1);
        ShowObjective(player.transform.position);
        Speak(null, "D|아, 드디어 일어났네. 너 없으면 오늘도 내가 일 다 할 뻔했잖아.",
            "R|어제는 우리 여섯이 끝냈잖아. 오늘도 늘 하던 대로 하면 돼.",
            "A|아까 복도에서 무거운 걸 끌고 가는 소리가 났어요.",
            "C|우선 시설 상태부터 확인합니다. R 씨, 분석실 기록을 맡아 주십시오.",
            "T|어젯밤부터 전력 숫자가 이상하던데요. 이번에도 제가 확인하는 거죠?",
            "E|R 씨, 어제 잠은 좀 잤어요? 밤에 복도에서 뵌 것 같은데.",
            "R|내가요?",
            "E|…아, 아니었나요? 그럼 제가 잘못 봤나 보네요.");
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
        AddCrew("D", 9, 4, "내가 어제 표시해 둔 공구가 없어. 혹시 봤어?",
            "커피부터 확인해 줘. 이건 생존 문제야.");
        AddCrew("A", 12, 7, "복도에서 뭔가 끌려가는 소리를 들었어요.",
            "문을 열었을 때는 아무도 없었어요.");
        AddCrew("C", 25, 5, "R 씨, 분석실에서 어젯밤 기록을 확인해 주십시오.",
            "기록 확인이 먼저입니다. 다음은 전력, 마지막으로 통신입니다.");
        AddCrew("T", 17, 17, "같은 전력 오류가 세 번 났어요. 이쯤 되면 원인부터 봐야죠.",
            "사용자는 안 찍혔어요. 나중에 제가 썼다고만 하지 마세요.");
        AddCrew("E", 31, 3, "창밖 좀 봐요. 여기 오기 전엔 눈이 이렇게 조용할 줄 알았는데.",
            "…바람 소리요? 익숙해져서 못 느꼈나 보네요.");

        Inspect("공동 사진", 21, 3, "여섯 명이 찍힌 사진이다. 찍은 사람은 기억나지 않는다.",
            "늘 같이 찍던 자리다. 어제는 이 사진이 책상 위에 있었는데.", documentArt: "team_photo_board");
        Inspect("공동 머그잔", 14, 6, "잔은 여섯 개. 손잡이마다 작은 흠집이 있다.",
            "늘 쓰는 잔 여섯 개가 그대로다. 물만 식었다.", documentArt: "enamel_mugs");
        Inspect("커피 주전자", 10, 6, "아직 따뜻하다. D가 아까부터 이것만 찾았다.",
            "방금 따른 것처럼 보이는데 주전자가 차갑다.", documentArt: "coffee_kettle");
        Inspect("작업 배치표", 29, 3, "내 이름 옆에 ‘분석실 확인’이 적혀 있다.",
            "오늘 야간 점검은 취소됐다고 했는데, 복도 점검 칸에 완료 표시가 있다.");
        Inspect("연구실 창문", 31, 2, "눈 때문에 맞은편 건물도 잘 보이지 않는다.",
            "유리 위 손자국이 안쪽을 향해 번져 있다.");
        Inspect("개인 메모", 19, 6, "메모에 「오늘도 같은 꿈」이라고 쓰여 있다.",
            "내 필체와 비슷하다. 쓴 기억은 없다.");
        Inspect("복도 긁힌 자국", 24, 12, "무거운 물건을 끌고 간 흔적이다.");
        Inspect("복도 비상등", 14, 9, "주전원이 꺼져도 이 불은 남는다.");
        Inspect("전력 로그", 18, 19, "새벽에 잠깐 전원이 끊겼다. 상세 기록은 무전 후 열어 볼 수 있다.",
            "03:12에 주전원을 차단했다. 그 뒤에도 복도 쪽 감지기는 계속 반응했다.", true, "power_log_terminal");
        Inspect("종이 점검표", 22, 19, "수기로 날짜와 설비 상태를 적는 점검표다.",
            "어제 점검자는 복도에서 끌리는 소리를 들었다고 적었다. 오늘 A가 들은 것과 같다.");
        Inspect("복도 출입 기록", 23, 19, "복도 출입 기록이다. 사진을 확인한 뒤 원본 기록들을 대조하자.",
            "03:12. 양쪽 셔터는 닫혀 있었다. 그런데 연구동 안쪽의 잠금실 문이 안에서 열렸다.",
            true, "door_access_log");
        Inspect("잠금실 출입문", 27, 13, "외부 잠금 스위치가 있다. 안쪽에는 점검용 비상구가 보인다.");

        AddSpot("발전기 손상 사진", 9, 19, () =>
        {
            if (phase == Phase.Survey && conversations.Count == 5 && observations.Count >= 4)
            {
                phase = Phase.Report;
                Speak(null, "R|긁힌 방향이 하나인데 철판까지 안으로 휘었다. 공구로 이게 되나?",
                    "T|이걸 정비 실수라고 올리기엔 손상 폭이 너무 크죠.",
                    "C|사진을 보존하십시오. 분석실 무전기로 지원을 요청하겠습니다.");
            }
            else Speak(null, "R|금속판이 안쪽으로 휘었다. 긁힌 자국은 크고 깊다.");
            ShowDocument("generator_damage_photo");
        }, () => phase <= Phase.Verify);

        AddSpot("본부 무전기", 13, 19, Radio, () => phase <= Phase.Verify);
        AddSpot("보안 단말", 15, 16, () =>
        {
            if (phase == Phase.Verify) OpenChoices();
            else Speak(null, "R|복도 CCTV 원본을 열 수 있는 보안 단말이다.");
        }, () => phase <= Phase.Verify);
        AddSpot("본관동 연결 통로", 1, 11, () => Speak(null, "R|셔터가 내려가 있다. 통행 승인이 필요하다."),
            () => phase <= Phase.Verify);
        AddSpot("발전동 연결 통로", 38, 11, () => Speak(null, "R|비상 지침이 해제되기 전에는 건널 수 없다."),
            () => phase <= Phase.Verify);

        AddSpot("점검용 비상구", 26, 19, EscapeLockRoom, () => phase == Phase.Chase);
        AddSpot("외부 잠금 스위치", 24, 17, LockDown, () => phase == Phase.Chase);
    }

    private void LoadPortrait(string code)
    {
        var texture = Resources.Load<Texture2D>($"AnomalyReport/Crew/{code}/dialogue");
        int topMargin = code switch { "C" => 10, "T" => 4, "D" => 1, _ => 0 };
        var visible = new Rect(0, 0, texture.width, texture.height - topMargin);
        portraits.Add(code, Sprite.Create(texture, visible, new Vector2(.5f, .5f)));
    }

    private void AddCrew(string code, int x, int y, string first, string second)
    {
        LoadPortrait(code);
        var frame = ReportUI.Sheet("Crew/" + code + "/walk-128px", 3, 4, CrewPixelsPerUnit)[0];
        crew.Add(RendererAt(code, frame, new Vector2Int(x, y), 5));
        AddSpot(code + " 대화", x, y, () =>
        {
            bool firstTalk = conversations.Add(code);
            if (firstTalk) Speak(null, code + "|" + first, code + "|" + second);
            else Speak(null, code + "|" + (phase == Phase.Verify ? code switch
            {
                "C" => "구두 보고는 보류하십시오. 전력 로그와 출입 기록을 따로 남깁니다.",
                "T" => "화면에 뜬 것만 믿긴 어렵죠. 종이 기록부터 대조해 보세요.",
                "E" => "복도 쪽은 아직 안 봤어요? …같이 가 드릴까요.",
                "A" => "무전 끝나고도 잡음이 좀 오래 남았어요. 듣고 있었던 것처럼요.",
                _ => "사진은 분명 우리가 찍은 건데. 그날 뭐 했는지는 잘 안 떠오르네."
            } : second));
        }, () => phase <= Phase.Verify);
    }

    private void Inspect(string name, int x, int y, string first, string revised = null,
        bool verification = false, string documentArt = null)
    {
        AddSpot(name, x, y, () =>
        {
            if (phase == Phase.Verify && revised != null)
            {
                if (verification) evidence.Add(name);
                Speak(CheckEvidence, "R|" + revised);
                if (documentArt != null) ShowDocument(documentArt);
            }
            else
            {
                observations.Add(name);
                Speak(null, "R|" + first);
                if (documentArt != null) ShowDocument(documentArt);
            }
        }, () => phase <= Phase.Verify);
    }

    private void AddSpot(string name, int x, int y, Action action, Func<bool> available)
    {
        var marker = RendererAt(name + " 표시", null, new Vector2Int(x, y), 8);
        marker.sprite = ReportUI.Asset("Main/hazard_stripe", 96);
        marker.color = Signal;
        marker.transform.localScale = new Vector3(1.15f, .45f, 1);
        marker.transform.position += Vector3.up * .88f;
        marker.enabled = false;
        spots.Add(new Spot { Position = MainBuildingPlan.World(new Vector2Int(x, y)),
            Name = name, Inspect = action, Available = available, Marker = marker });
    }

    private void Radio()
    {
        if (phase != Phase.Report)
        {
            Speak(null, phase == Phase.Verify ? "R|채널이 닫혔다. 전력 로그와 출입 기록을 맞춰 봐야겠다."
                : "R|발전기 상태부터 확인해야 보고할 수 있겠다.");
            return;
        }

        phase = Phase.Verify;
        Speak(null, "본부|연구동, 수신했습니다. 손상 사진을 보존하고 외부 이동을 중단하십시오.",
            "C|확인했습니다. 손상 범위와 현장 상태를 정리해서 전송하겠습니다.",
            "본부|현장 상태는 단말의 점검 양식에 입력하십시오. 지원 요청은 접수됐습니다.",
            "R|기록 보존 권한이 열렸다. 손상 사진이 찍힌 시간부터 맞춰 보자.");
    }

    private void OpenChoices()
    {
        choosing = true;
        Present("보안 단말", "복도 CCTV 원본을 열거나 본부에 상태를 재전송할 수 있습니다.");
        advance.text = "숫자 1 / 2 또는 버튼";
        choices.gameObject.SetActive(true);
        choices.GetComponentInChildren<Button>().Select();
    }

    private void HandleSecurityTerminal(bool openArchive)
    {
        if (!choosing) return;
        choosing = false;
        choices.gameObject.SetActive(false);
        EventSystem.current.SetSelectedGameObject(null);

        if (openArchive)
        {
            evidence.Add("복도 CCTV 원본");
            Speak(CheckEvidence, "시스템|03:12 — 북쪽 복도 카메라. 영상 손상.",
                "R|화면은 끊겼는데 소리는 남아 있다. 복도 바깥에서 나는 소리가 아니다.");
        }
        else
        {
            Speak(null, "시스템|상태 보고는 발송 대기 중입니다. 외부 통신에 응답이 없습니다.",
                "R|기다리는 동안 단말에 남은 CCTV 원본을 열어 보자.");
        }
    }

    private void CheckEvidence()
    {
        if (evidence.Count != 3) return;

        Speak(() => StartCoroutine(TransformE()),
            "R|03:12. 불이 꺼지고, 잠금실 문이 안에서 열렸다. CCTV에 남은 소리도 그때다.",
            "T|셋 다 같은 시각이네요. 기록이 이상한 줄 알았는데, 정말 복도에 뭔가 있었나 봐요.",
            "C|원본을 보존하십시오. 전원 여기 모여서 움직입니다.",
            "A|저기… 다시 들려요. 문 뒤에서요.",
            "E|…그 소리요? 아. 제가 너무 크게 냈나 보네요.",
            "R|E 씨. 손에 든 거, 내려놔요.",
            // J3, 괴담에 떨어졌어도 출근을 해야 하는구나, 272화.
            "E|음… 그냥… 삼킬까…?");
    }

    private IEnumerator TransformE()
    {
        cutscene = true;
        player.transform.position = MainBuildingPlan.World(Entry);
        Follow(1);
        blackout.color = Color.black;
        PlayEffect("scrape");
        yield return new WaitForSeconds(.55f);

        enemy.transform.position = MainBuildingPlan.World(new Vector2Int(21, 11));
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
        Speak(BeginChase, "A|E 씨… 왜 목소리가 두 개로 들려요?",
            "T|잠깐만요. 저게 왜 이쪽으로 오는 건데요?",
            "C|R 씨, 복도 끝 잠금실로. 뒤돌아보지 마십시오.",
            // J3, 괴담에 떨어졌어도 출근을 해야 하는구나, 150화.
            "E|…살아 있었구나.",
            "E|R 씨. 뛰면 넘어져요. …천천히 와도 되는데.");
    }

    private void BeginChase()
    {
        phase = Phase.Chase;
        usedHatch = false;
        PlayerPrefs.SetInt(CheckpointKey, 1);
        PlayerPrefs.Save();
        foreach (SpriteRenderer member in crew) member.enabled = false;
        player.transform.position = MainBuildingPlan.World(Entry);
        enemy.transform.position = MainBuildingPlan.World(EnemyEntry);
        enemyTarget = enemy.transform.position;
        enemy.enabled = true;
        enemyGrace = .85f;
        lungeCooldown = 2.8f;
        lungeWindup = lungeRemaining = 0;
        blackout.color = new Color(.18f, 0, 0, .19f);
        Follow(1);
    }

    private static bool IsInLockRoom(Vector2 position)
    {
        Vector2Int cell = MainBuildingPlan.Cell(position);
        return cell.x >= 26 && cell.x <= 32 && cell.y >= 15 && cell.y <= 20;
    }

    private void EscapeLockRoom()
    {
        if (!IsInLockRoom(player.transform.position) || !IsInLockRoom(enemy.transform.position))
        {
            hint.text = "E가 잠금실 안까지 들어오면 비상구를 이용하세요.";
            return;
        }

        usedHatch = true;
        player.transform.position = MainBuildingPlan.World(new Vector2Int(24, 18));
        Follow(1);
    }

    private void LockDown()
    {
        if (!usedHatch || IsInLockRoom(player.transform.position) || !IsInLockRoom(enemy.transform.position))
        {
            hint.text = "E가 잠금실 안에 있고 R이 밖에 있어야 잠글 수 있습니다.";
            return;
        }

        phase = Phase.Finished;
        locked = true;
        var wallTile = walls.GetTile(new Vector3Int(27, -14, 0));
        walls.SetTile(new Vector3Int(28, -14, 0), wallTile);
        walls.SetTile(new Vector3Int(29, -14, 0), wallTile);
        shutter.enabled = true;
        PlayEffect("shutter");
        Speak(() =>
        {
            PlayerPrefs.DeleteKey(CheckpointKey);
            PlayerPrefs.Save();
            End("STAGE 01 종료", "잠금실 봉쇄 완료. R은 연구동 복도에서 의식을 잃었다.");
        }, "시스템|잠금실 출입문 잠금 완료. 수동 해제 필요.",
            "E|R 씨. 문 좀 열어 주세요.",
            "E|잔은 치우지 마세요. …아침엔 늘 그 자리에 있었잖아요.",
            "R|…그래. 늘 여섯이었지.",
            "E|네. 제가 기억해요.");
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
        var portraitFrame = ReportUI.Image(panel, "Portrait frame", .026f, .12f, .152f, .88f,
            new Color32(21, 38, 48, 255));
        portrait = ReportUI.Image(portraitFrame.transform, "Portrait", .065f, .03f, .935f, .96f, Color.white);
        portrait.preserveAspect = false;
        speaker = ReportUI.Label(panel, "", .174f, .68f, .91f, .93f, 36, Signal, TextAnchor.MiddleLeft);
        message = ReportUI.Label(panel, "", .174f, .30f, .951f, .67f, 34, Paper, TextAnchor.MiddleLeft);
        advance = ReportUI.Label(panel, "", .67f, .03f, .84f, .27f, 23, Paper, TextAnchor.MiddleRight);
        var next = ReportUI.Button(panel, "다음", .85f, .047f, .95f, .265f, new Color32(35, 65, 75, 255), Paper, NextLine);
        next.navigation = new Navigation { mode = Navigation.Mode.None };
        document = ReportUI.Image(panel, "Evidence preview", .67f, .31f, .96f, .94f, Color.white);
        document.enabled = false;
        choices = ReportUI.Place(panel, "Report options", 0, 0, 1, 1);
        ReportUI.Image(choices, "Options background", .18f, .03f, .96f, .29f, Ink);
        ReportUI.Button(choices, "1 원본 열기", .45f, .052f, .63f, .265f, new Color32(35, 65, 75, 255), Paper, () => HandleSecurityTerminal(true));
        ReportUI.Button(choices, "2 상태 보고", .66f, .052f, .84f, .265f, new Color32(35, 65, 75, 255), Paper, () => HandleSecurityTerminal(false));
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
        portrait.transform.parent.gameObject.SetActive(portrait.enabled);
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
            if (keys != null && (keys.digit1Key.wasPressedThisFrame || keys.numpad1Key.wasPressedThisFrame)) HandleSecurityTerminal(true);
            else if (keys != null && (keys.digit2Key.wasPressedThisFrame || keys.numpad2Key.wasPressedThisFrame)) HandleSecurityTerminal(false);
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
        if (phase == Phase.Chase && dialogue == null) MoveEnemy(player.transform.position);
    }

    private static int Axis(bool positive, bool negative) => (positive ? 1 : 0) - (negative ? 1 : 0);

    private bool WalkableCell(Vector2Int cell)
    {
        var tile = new Vector3Int(cell.x, -cell.y, 0);
        return ground.HasTile(tile) && !walls.HasTile(tile);
    }

    private bool Walkable(Vector2 center, float radius)
    {
        return !HitsFurniture(center, radius)
            && WalkableCell(MainBuildingPlan.Cell(center + new Vector2(-radius, -radius)))
            && WalkableCell(MainBuildingPlan.Cell(center + new Vector2(-radius, radius)))
            && WalkableCell(MainBuildingPlan.Cell(center + new Vector2(radius, -radius)))
            && WalkableCell(MainBuildingPlan.Cell(center + new Vector2(radius, radius)));
    }

    private bool HitsFurniture(Vector2 center, float radius)
    {
        foreach (Bounds box in furnitureBounds)
        {
            if (center.x + radius > box.min.x && center.x - radius < box.max.x
                && center.y + radius > box.min.y && center.y - radius < box.max.y) return true;
        }
        return false;
    }

    private bool EnemyCell(Vector2Int cell) => Walkable(MainBuildingPlan.World(cell), .19f);

    private void FindSpot(Vector2 position)
    {
        nearest = null;
        float best = 2.2f;
        foreach (Spot spot in spots)
        {
            bool available = spot.Available();
            float distance = (position - spot.Position).sqrMagnitude;
            if (available && distance < best && ClearPath(position, spot.Position))
            {
                nearest = spot;
                best = distance;
            }
        }

        hint.text = nearest != null ? $"[F]  {nearest.Name}" : "WASD / 방향키 이동   ·   F 조사 및 대화";
        foreach (Spot spot in spots)
            spot.Marker.enabled = spot == nearest;
    }

    private bool ClearPath(Vector2 start, Vector2 destination, bool includeFurniture = false)
    {
        int samples = Mathf.CeilToInt(Vector2.Distance(start, destination) * 6);
        for (int i = 1; i < samples; i++)
        {
            Vector2 point = Vector2.Lerp(start, destination, i / (float)samples);
            if (!WalkableCell(MainBuildingPlan.Cell(point)) || (includeFurniture && HitsFurniture(point, 0)))
                return false;
        }
        return true;
    }

    private void ShowObjective(Vector2 position)
    {
        Vector2Int cell = MainBuildingPlan.Cell(position);
        string room = "복도";
        if (cell.x < 6) room = "본관동 연결 통로";
        else if (cell.x > 33) room = "발전동 연결 통로";
        else if (cell.y < 8) room = "연구실";
        else if (cell.y > 14) room = cell.x > 25 ? "잠금실" : "분석실";
        heading.text = "연구동 1F  /  " + room;
        switch (phase)
        {
            case Phase.Survey:
                objective.text = conversations.Count == 5 && observations.Count >= 4
                    ? "분석실에서 발전기 손상 사진 확인"
                    : $"대원 대화 {conversations.Count}/5   ·   주변 조사 {Mathf.Min(4, observations.Count)}/4";
                break;
            case Phase.Report:
                objective.text = "분석실 무전기로 지원 요청";
                break;
            case Phase.Verify:
                objective.text = $"전력 로그 · 복도 출입 기록 · CCTV 원본  {evidence.Count}/3";
                break;
            case Phase.Chase:
                objective.color = new Color32(255, 160, 143, 255);
                bool inside = IsInLockRoom(position);
                bool enemyInside = IsInLockRoom(enemy.transform.position);
                if (inside)
                    objective.text = enemyInside ? "E가 들어왔습니다. 왼쪽 점검용 비상구로 탈출하세요"
                        : "E가 문을 지나 잠금실 안으로 들어오게 하세요";
                else if (enemyInside && usedHatch)
                    objective.text = "복도 쪽 외부 잠금 스위치를 작동하세요";
                else
                    objective.text = "동쪽 잠금실로 E를 유인하세요";
                break;
        }
    }

    private void MoveEnemy(Vector2 position)
    {
        float elapsed = Mathf.Min(Time.deltaTime, .035f);
        enemyGrace -= elapsed;
        if (enemyGrace > 0) return;
        var from = (Vector2)enemy.transform.position;
        float distance = Vector2.Distance(from, position);
        lungeCooldown -= elapsed;

        if (lungeWindup > 0)
        {
            lungeWindup -= elapsed;
            if (lungeWindup > 0) return;
            lungeRemaining = .38f;
        }
        else if (lungeRemaining <= 0 && lungeCooldown <= 0 && distance > 1.15f && distance < 3.3f
            && ClearPath(from, position, true))
        {
            lungeWindup = .24f;
            lungeCooldown = 3.5f;
            enemy.color = new Color32(255, 142, 134, 255);
            PlayEffect("scrape");
            return;
        }

        if ((enemyTarget - from).sqrMagnitude < .001f)
        {
            var current = MainBuildingPlan.Cell(from);
            enemyTarget = MainBuildingPlan.World(NextCell(current, ClosestEnemyCell(position)));
        }

        bool lunging = lungeRemaining > 0;
        lungeRemaining -= elapsed;
        float speed = lunging ? 7.1f : Mathf.Lerp(5.4f, 4.85f, Mathf.InverseLerp(2f, 9f, distance));
        var next = Vector2.MoveTowards(from, enemyTarget, speed * elapsed);
        enemy.transform.position = next;
        enemy.color = lunging ? new Color32(255, 195, 183, 255) : Color.white;
        Animate(enemy, enemyFrames, next - from, ref enemyDirection);
        if ((position - next).sqrMagnitude < .58f && ClearPath(next, position, true))
            End("신호 소실", "E가 R을 따라잡았다.");
    }

    private Vector2Int ClosestEnemyCell(Vector2 position)
    {
        var playerCell = MainBuildingPlan.Cell(position);
        if (EnemyCell(playerCell)) return playerCell;
        var best = playerCell;
        float distance = float.PositiveInfinity;
        foreach (Vector2Int step in Directions)
        {
            Vector2Int adjacent = playerCell + step;
            Vector2 center = MainBuildingPlan.World(adjacent);
            float candidate = (position - center).sqrMagnitude;
            if (candidate < distance && EnemyCell(adjacent) && ClearPath(center, position, true))
            {
                best = adjacent;
                distance = candidate;
            }
        }
        return best;
    }

    private Vector2Int NextCell(Vector2Int start, Vector2Int destination)
    {
        if (start == destination || !EnemyCell(destination)) return start;
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
                if (parents.ContainsKey(next) || !EnemyCell(next)) continue;
                Vector2 midpoint = (MainBuildingPlan.World(next) + MainBuildingPlan.World(current)) * .5f;
                if (HitsFurniture(midpoint, .19f)) continue;
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
