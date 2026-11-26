using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public sealed class ReportStageOne : MonoBehaviour
{
    [SerializeField] private Font interfaceFont;
    [SerializeField] private Material spriteMaterial;
    public const string CheckpointKey = "AnomalyReport.StageOne.ProductionChase";
    private enum Phase { Survey, Report, Verify, Blackout, Chase, Evacuate, Finished }

    private const float CrewPixelsPerUnit = 80f;
    private static readonly Color Ink = new Color32(13, 24, 33, 250);
    private static readonly Color Paper = new Color32(210, 230, 233, 255);
    private static readonly Color Signal = new Color32(136, 222, 226, 255);
    private static readonly Vector2Int[] Directions =
        { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
    private Vector2Int Entry => scenario == 1 ? new Vector2Int(8, 8) : new Vector2Int(7, 14);
    private Vector2Int EnemyEntry => scenario == 1 ? new Vector2Int(9, 14) : new Vector2Int(4, 13);

    private readonly List<Spot> spots = new List<Spot>();
    private readonly List<SpriteRenderer> crew = new List<SpriteRenderer>();
    private readonly HashSet<string> conversations = new HashSet<string>();
    private readonly HashSet<string> evidence = new HashSet<string>();
    private readonly Dictionary<string, Sprite> portraits = new Dictionary<string, Sprite>();
    private readonly Dictionary<Vector2Int, Vector2Int> parents = new Dictionary<Vector2Int, Vector2Int>();
    private readonly Queue<Vector2Int> frontier = new Queue<Vector2Int>();
    private Phase phase;
    private Action afterDialogue;
    private DialogueLine[] dialogue;
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
    private Inventory inventory;
    private PlayerHealth health;
    private Item storageKey;
    private Talk menuChoices;
    private Text choiceOneLabel, choiceTwoLabel;
    private string room = "복도";
    private bool loungeOpened;
    private bool enemyCrossed;
    private float enemyDoorDelay;
    private Vector2Int ControlEntry => scenario == 1 ? new Vector2Int(30, 89) : new Vector2Int(7, 54);

    [Serializable]
    private sealed class Passage
    {
        public string label, scene;
        public Vector2Int point, destination;
    }

    [SerializeField] private int scenario;
    [SerializeField] private Tilemap obstacles;
    [SerializeField] private Vector2Int mapSpawn;
    [SerializeField] private Sprite pickupSprite;
    [SerializeField] private Passage[] passages;
    private static Vector2Int? arrival;
    private static readonly HashSet<string> power = new HashSet<string>();
    private bool loopStarted;
    private int loopPasses;

    public static void ResetRun()
    {
        PlayerPrefs.DeleteKey(CheckpointKey);
        PlayerPrefs.Save();
        power.Clear();
        arrival = null;
    }

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
        if (furniture != null)
            foreach (BoxCollider2D obstacle in furniture)
                if (obstacle && obstacle.isActiveAndEnabled) furnitureBounds.Add(obstacle.bounds);
        BuildInterface();
        playerFrames = ReportUI.Sheet("Crew/R/walk-128px", 3, 4, CrewPixelsPerUnit);
        enemyFrames = ReportUI.Sheet("Crew/spider", 3, 4, 43);
        morphFrames = ReportUI.Sheet("Crew/morph", 4, 2, 60);
        LoadPortrait("R");
        player = RendererAt("R", playerFrames[0], Entry, 5);
        inventory = player.gameObject.AddComponent<Inventory>();
        health = player.gameObject.AddComponent<PlayerHealth>();
        menuChoices = gameObject.AddComponent<Talk>();
        menuChoices.onChoice1 = new UnityEvent();
        menuChoices.onChoice2 = new UnityEvent();
        enemy = RendererAt("E 변이체", enemyFrames[0], EnemyEntry, 5);
        enemy.enabled = false;
        if (scenario == 0) SetupInteractions();
        else
        {
            SetupPassages();
            if (scenario == 1) SetupMainStory();
            else if (scenario == 2) SetupPowerStory();
            else SetupWhiteout();
        }

        Follow(1);

        if (scenario <= 1 && PlayerPrefs.HasKey(CheckpointKey))
        {
            if (scenario == 1 && PlayerPrefs.GetInt(CheckpointKey) == 2)
                ResumeAfterLockdown();
            else
                BeginChase();
            return;
        }

        player.transform.position = MainBuildingPlan.World(scenario == 0
            ? new Vector2Int(19, 4) : arrival ?? mapSpawn);
        arrival = null;
        room = scenario == 1 ? "침실 D" : "복도";
        Follow(1);
        ShowObjective(player.transform.position);
        if (scenario == 1)
            Speak(null, "R|교대 시간 전이다. 출입 카드를 챙기고 식당에 가 보자.");
        else if (scenario == 2 && !power.Contains("부품"))
            Speak(null, "본부|발전 설비 상태를 확인하십시오.",
                "R|전원이 내려갔다. 정비실에 교체 부품이 있는지 먼저 확인하자.");
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
        AddRoom("침실 A", 6, 3, 2, 23);
        AddRoom("침실 B", 13, 3, 28, 23);
        AddRoom("휴게실", 21, 3, 54, 36);
        AddRoom("관제실", 32, 3, 2, 49);
        AddRoom("통신실", 38, 7, 28, 49);
        AddRoom("창고", 38, 13, 54, 49);
        AddRoom("침실 D", 8, 16, 2, 36);
        AddRoom("식당", 23, 16, 28, 36);
        AddRoom("침실 C", 32, 16, 54, 23);

        AddCrew("D", 56, 40, "늦었네 R. 오늘도 제일 마지막이야?", "아직 교대 전이라고? 그럼 커피 한 잔은 되겠다.");
        AddCrew("A", 62, 40, "아까 복도에서 이상한 소리가 들렸어.", "난방관이면 이렇게 일정한 간격으로 울릴 리가 없는데.");
        AddCrew("E", 59, 38, "음…… 아까도 들리긴 했어.", "어디 있었냐고? 그냥…… 여기저기.");
        AddCrew("T", 32, 40, "식사 기록은 다 남겨 뒀어.", "여섯 명 먹을 건 충분해. 나머지는 창고에서 확인하면 돼.");
        AddCrew("C", 11, 53, "설비 점검 자료부터 확인해. 사진만 보고 원인을 단정하지 말고.", "통신실 무전기를 써. 접속 기록도 남겨.");

        Inspect("침실 A 메모", 4, 27, "접힌 메모에 「점검 뒤에는 꼭 자물쇠를 확인할 것」이라고 적혀 있다.");
        Inspect("침실 B 사물함", 37, 27, "누군가 장갑 한 짝을 걸어 뒀다. 아직 축축하다.");
        Inspect("침실 C 침대", 56, 27, "이불을 급하게 털어 놓은 흔적. 아침에 일어난 뒤 손대지 않은 모양이다.");
        Inspect("침실 D 교대 기록", 4, 40, "어제와 같은 시간에 교대했다. 근무표엔 이상이 없다.");
        Inspect("식탁의 컵", 36, 40, "컵이 여섯 개 놓여 있다. 평소와 다를 것 없다.",
            documentArt: "enamel_mugs");
        Inspect("공동 사진", 59, 37, "우리 여섯이 휴게실에서 함께 찍은 사진이다.",
            documentArt: "team_photo_board");
        Inspect("복도 긁힌 자국", 24, 13, "바닥에 얕은 자국이 이어진다. 끝은 관제실 쪽이다.");
        Inspect("교신 수신 기록", 36, 52, "최근 무전은 들어온 순서대로 여기에 남는다.",
            "지원 요청을 보낸 뒤에도 수신 표시만 깜빡인다. 몇 문장은 잡음에 묻혔다.", true, "radio-transcript");
        Inspect("건물 상태 기록", 5, 52, "설비 점검 기록이 잠겨 있다. 이상 보고와 권한 확인 뒤에 열 수 있다.",
            "외부 출입 센서는 모두 닫힘이었다. 경보는 건물 내부에서 시작됐다.", true, "power_log_terminal");

        AddSpot("발전 설비 손상 사진", 8, 52, InspectDamagePhoto, () => phase <= Phase.Verify);
        AddSpot("본부 무전기", 31, 52, Radio, () => phase <= Phase.Verify);
        AddSpot("본관동 전체 봉쇄", 9, 52, () => OpenChoices("봉쇄 제어 장치",
            "비상 셔터를 내리고 외부 출입을 차단합니다.",
            "전체 봉쇄 실행", LockDown, "보류", null), () => phase == Phase.Chase);

        var keyRenderer = RendererAt("관제실 보조 출입 열쇠", ReportUI.Asset("Main/brass-key", 56),
            new Vector2Int(57, 52), 3);
        storageKey = keyRenderer.gameObject.AddComponent<Item>();
        storageKey.gameObject.SetActive(false);
    }

    private void SetupPassages()
    {
        foreach (Passage passage in passages)
        {
            bool external = !string.IsNullOrEmpty(passage.scene);
            string destination = passage.label.EndsWith(" 들어가기", StringComparison.Ordinal)
                ? passage.label.Substring(0, passage.label.Length - " 들어가기".Length) : "복도";
            Door door = external ? null : MakeDoor(passage.label, passage.destination);
            AddSpot(passage.label, passage.point.x, passage.point.y, () =>
            {
                if (external)
                {
                    arrival = passage.destination;
                    SceneManager.LoadScene(passage.scene);
                    return;
                }
                door.needKeys = scenario == 1 && destination == "관제실" && phase >= Phase.Verify ? 1 : 0;
                if (!Travel(door, destination))
                    Speak(null, "R|관제실이 잠겼다. 침실 D의 출입 카드를 찾아야 한다.");
                else if (scenario == 1 && destination == "식당" && !loungeOpened)
                    OpenLounge();
                else if (scenario == 1 && phase == Phase.Blackout && destination == "복도")
                    RevealE();
                else if (scenario == 2 && destination == "복도" && power.Contains("부품") && !power.Contains("표식")
                    && !loopStarted)
                {
                    loopStarted = true;
                    Speak(null, "R|정비실에서 나왔는데… 아까 지나온 복도가 다시 보인다.");
                }
            }, () => scenario == 1
                ? (external ? phase == Phase.Evacuate :
                    (phase != Phase.Chase || destination == "관제실"))
                : true);
        }
    }

    private void SetupMainStory()
    {
        AddCrew("D", 6, 145, "늦었네 R. 아직 커피는 따뜻해.", "여섯 명 먹을 건 충분해.");
        AddCrew("A", 12, 145, "복도에서 무거운 걸 끄는 소리가 났어.", "난방관은 아닌 것 같아.");
        AddCrew("T", 13, 145, "식사 기록은 다 남겼어.", "통신실 장비는 한 번 확인해 봐.");
        AddCrew("E", 132, 45, "음… 어제 잠은 좀 잤어요?", "저는 바람 소리가 익숙해져서요.");
        AddCrew("C", 30, 88, "설비 외벽 사진이 관제실에 도착했어.", "사진 보고 통신실에서 본부에 확인해.");
        Inspect("침실 메모", 111, 97, "아침 교대 전에 출입 카드를 챙길 것.");
        Inspect("식탁 위 여섯 잔", 5, 145, "오늘도 늘 앉던 자리에 잔을 여섯 개 놓았다.");
        Inspect("공동 사진", 130, 45, "바람이 잠잠했던 날, 여섯이서 함께 찍은 사진이다.");
        Inspect("복도의 긁힌 자국", 20, 21, "안쪽 벽에 길게 긁힌 자국이 있다.");
        Inspect("창고 지급 명부", 143, 145, "이번 지급 기록은 본부에 확인한 뒤 다시 보자.",
            "본부가 말한 대로 방한 장비는 다섯 세트만 지급됐다.", true);
        Inspect("관제실 출입 기록", 35, 87, "야간 출입 기록은 무전 교신 뒤에 확인할 수 있다.",
            "기록에는 출입 코드가 여섯 번 찍혔다. 등록표와 숫자가 다르다.", true);
        AddSpot("탐사대 등록 명부", 25, 189, InspectRoster, () => phase == Phase.Verify);
        AddSpot("발전 설비 손상 사진", 31, 88, InspectDamagePhoto, () => phase <= Phase.Report);
        AddSpot("본부 무전기", 88, 139, Radio, () => phase <= Phase.Verify);
        AddSpot("본관동 전체 봉쇄", 31, 88, () => OpenChoices("봉쇄 제어 장치",
            "비상 셔터를 내려 본관동 전체를 봉쇄한다.", "전체 봉쇄 실행", LockDown, "보류", null),
            () => phase == Phase.Chase);
        var key = RendererAt("관제실 출입 카드", pickupSprite,
            new Vector2Int(113, 97), 3);
        storageKey = key.gameObject.AddComponent<Item>();
    }

    private void SetupWhiteout()
    {
        blackout.color = new Color(.58f, .67f, .75f, .19f);
        Vector2Int[] lights = { new(17,9), new(19,11), new(23,14), new(27,14), new(26,16),
            new(25,17), new(25,21), new(27,23), new(29,24) };
        var lamp = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0,
            Texture2D.whiteTexture.width, Texture2D.whiteTexture.height), new Vector2(.5f,.5f), 32);
        foreach (Vector2Int cell in lights)
        {
            var light = RendererAt("야외 안내등", lamp, cell, 3);
            light.color = new Color32(251, 207, 129, 235);
            light.transform.localScale = Vector3.one * (.30f / lamp.bounds.size.x);
        }
        var hazeObject = ReportUI.Place(blackout.transform, "제한 시야", 0, 0, 1, 1);
        var haze = hazeObject.gameObject.AddComponent<RawImage>();
        haze.raycastTarget = false;
        const int size = 64;
        var mask = new Texture2D(size, size, TextureFormat.RGBA32, false);
        mask.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x - 31.5f) / 31.5f * 1.78f;
            float dy = (y - 31.5f) / 31.5f;
            float strength = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.46f, .77f, Mathf.Sqrt(dx * dx + dy * dy)));
            pixels[y * size + x] = new Color32(153, 167, 184, (byte)(strength * 252));
        }
        mask.SetPixels32(pixels);
        mask.Apply(false, true);
        haze.texture = mask;
    }

    private void SetupPowerStory()
    {
        blackout.color = new Color(.06f, .12f, .20f, .19f);
        if (!power.Contains("부품"))
        {
            var item = RendererAt("정비 부품", pickupSprite,
                new Vector2Int(144, 48), 3);
            storageKey = item.gameObject.AddComponent<Item>();
        }
        Inspect("정비 기록", 145, 47, "작성자는 D. 그런데 작성 시각은 본관동 봉쇄 이후다.");
        AddSpot("복도 시계", 29, 10, () => Speak(null, "R|초침이 멈췄다. 비상 안내등이 가리키는 방향만 따라가자."),
            () => loopStarted && !power.Contains("표식"));
        AddSpot("비상 안내등", 27, 10, () =>
        {
            power.Add("표식");
            Speak(null, "R|이쪽 표시는 아까와 다르다. 안내등을 따라가니 복도가 이어진다.");
        }, () => loopStarted && loopPasses > 0 && !power.Contains("표식"));
        AddSpot("발전기", 74, 49, () =>
        {
            if (!power.Contains("부품")) { Speak(null, "R|정비실에서 교체 부품을 가져와야 한다."); return; }
            if (!power.Contains("표식")) { Speak(null, "R|복도 안내등부터 확인해야겠다."); return; }
            power.Add("발전기");
            Speak(null, "R|부품을 장착했다. 발전기는 재가동 대기 상태다.");
        }, () => true);
        AddSpot("배전 장치", 2, 98, () =>
        {
            if (!power.Contains("발전기")) { Speak(null, "R|먼저 발전기에 교체 부품을 장착해야 한다."); return; }
            OpenChoices("배전 장치", "비상 회로에 연결할 차단기를 선택한다.",
                "비상 회로 B", () =>
                {
                    power.Add("배전");
                    Speak(null, "시스템|배전 복구. 제어실에서 보조 전력을 연결하십시오.");
                }, "주 회로 A", () => Speak(null, "시스템|과부하 감지. 비상 회로 B를 사용하십시오."));
        }, () => true);
        AddSpot("전력 제어 단말", 74, 97, () =>
        {
            if (!power.Contains("배전")) { Speak(null, "시스템|발전기 정비와 배전 복구를 먼저 완료하십시오."); return; }
            if (!power.Contains("보조 전력"))
            {
                power.Add("보조 전력");
                Speak(null, "시스템|보조 전력 연결 완료. 단말을 다시 조작해 발전기를 재가동하십시오.");
                return;
            }
            OpenChoices("전력 제어 단말", "준비 완료. 발전기를 재가동할까요?",
                "발전기 재가동", CompletePower, "보류", null);
        }, () => true);
    }

    private void CompletePower()
    {
        PlayerPrefs.DeleteKey(CheckpointKey);
        PlayerPrefs.Save();
        phase = Phase.Finished;
        locked = true;
        blackout.color = Color.clear;
        PlayEffect("shutter");
        Speak(() =>
        {
            power.Clear();
            End("STAGE 02 CLEAR", "발전동 전력 복구 완료. 연구동 출입 권한이 열렸다.");
        }, "시스템|전력 공급이 정상화되었습니다.",
            "본부|연구동으로 이동하여 탐사 기록을 확보하십시오.",
            "D|야, R. 어디 갔냐? 다들 기다리고 있어.",
            "R|……D?");
    }

    private void AddRoom(string name, int doorX, int doorY, int roomX, int roomY)
    {
        var hallway = new Vector2Int(doorX, doorY);
        var threshold = new Vector2Int(roomX + 5, roomY + 6);
        var landing = new Vector2Int(roomX + 5, roomY + 5);
        var outLanding = hallway + (doorY == 3 ? Vector2Int.up
            : doorY == 16 ? Vector2Int.down : Vector2Int.left);

        Door enter = MakeDoor("DoorIn_" + name, landing);
        Door leave = MakeDoor("DoorOut_" + name, outLanding);
        AddSpot(name + " 출입문", hallway.x, hallway.y, () =>
        {
            enter.needKeys = name == "관제실" && phase >= Phase.Verify ? 1 : 0;
            if (!Travel(enter, name))
            {
                Speak(null, "R|비상 잠금이 걸렸다. 창고에서 관제실 보조 열쇠를 찾아야 한다.");
                return;
            }
            if (name == "휴게실" && !loungeOpened) OpenLounge();
        }, () => phase != Phase.Chase || name == "관제실");
        AddSpot("복도로 나가기", threshold.x, threshold.y, () => Travel(leave, "복도"),
            () => phase != Phase.Chase);
    }

    private Door MakeDoor(string name, Vector2Int destination)
    {
        var item = new GameObject(name);
        item.transform.SetParent(transform, false);
        var target = new GameObject("착지점");
        target.transform.SetParent(item.transform, false);
        target.transform.position = MainBuildingPlan.World(destination);
        var door = item.AddComponent<Door>();
        door.target = target.transform;
        return door;
    }

    private bool Travel(Door door, string destination)
    {
        if (!door.TryUse(player.transform)) return false;
        room = destination;
        Follow(1);
        ShowObjective(player.transform.position);
        if (phase == Phase.Chase)
        {
            enemyCrossed = false;
            enemyDoorDelay = 1.25f;
        }
        return true;
    }

    private void OpenLounge()
    {
        loungeOpened = true;
        if (scenario == 1)
        {
            Vector3 ePosition = crew[3].transform.position;
            Vector3 cPosition = crew[4].transform.position;
            crew[3].transform.position = MainBuildingPlan.World(new Vector2Int(7, 146));
            crew[4].transform.position = MainBuildingPlan.World(new Vector2Int(10, 147));
            Speak(() =>
            {
                crew[3].transform.position = ePosition;
                crew[4].transform.position = cPosition;
            }, "D|늦었네. 네 잔은 늘 두던 자리에 뒀어.",
                "R|아직 교대 시간 전이야.",
                "A|새벽에 복도에서 뭔가 끄는 소리 들었어?",
                "T|바람이었다면 창문부터 흔들렸겠죠.",
                "E|음… 그 소리, 식당 앞에서 멈췄어요.",
                "C|교대 전에 관제실 사진부터 보자. 외벽에 자국이 찍혔어.");
            return;
        }
        Speak(() => OpenChoices("C", "점검표부터 확인할까?",
                "점검표 확인", () => Speak(null, "C|관제실에 발전 설비 사진이 있어. 먼저 그걸 확인해."),
                "대원들과 대화", null),
            "D|늦었네 R. 오늘도 제일 마지막이야?",
            "R|아직 교대 시간 전이야.",
            "A|아까 복도에서 이상한 소리가 들렸어.",
            "D|난방관 소리겠지. 여기선 매일 나잖아.",
            "E|음…… 아까도 들리긴 했어.",
            "R|넌 어디 있었는데?",
            "E|그냥…… 여기저기.",
            "C|잡담은 나중에 하고 점검표부터 확인하자.");
    }

    private void InspectDamagePhoto()
    {
        if (!loungeOpened)
        {
            Speak(null, "R|설비 점검 사진이다. 교대 전에 대원들을 먼저 만나자.");
            ShowDocument("generator_damage_photo");
            return;
        }
        if (phase != Phase.Survey)
        {
            Speak(null, "R|외벽에 긁힌 자국이 길게 이어져 있다.");
            ShowDocument("generator_damage_photo");
            return;
        }
        phase = Phase.Report;
        Speak(() => OpenChoices("설비 점검 사진", "사진을 확인했다. 다음으로 무엇을 할까?",
                "사진 기록", () => Speak(null, "R|손상 위치를 남겼다. 통신실에서 본부에 보고하자."),
                "통신실 안내", () => Speak(null, "C|통신실은 복도 동쪽이다. 무전기로 지원을 요청해.")),
            "R|발전 설비 외벽이 긁혀 있다. 단순한 결빙 때문일까.",
            "C|사진만으론 원인을 단정하기 어려워. 통신실에서 본부에도 보고하자.");
        ShowDocument("generator_damage_photo");
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
        var member = RendererAt(code, frame, new Vector2Int(x, y), 5);
        crew.Add(member);
        var talk = member.gameObject.AddComponent<Talk>();
        talk.lines = new[]
        {
            new DialogueLine { speaker = code, text = first },
            new DialogueLine { speaker = code, text = second }
        };
        AddSpot(code + " 대화", x, y, () =>
        {
            bool firstTalk = conversations.Add(code);
            if (firstTalk && phase != Phase.Verify) Speak(null, talk.lines);
            else Speak(null, code + "|" + (phase == Phase.Verify ? code switch
            {
                "C" => "여기 있는 기록부터 대조해. 숫자가 어디서 달라졌는지 확인하자.",
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
            Speak(null, phase == Phase.Verify ? "R|응답이 없다. 기지에 남은 기록을 대조해 보자."
                : "R|발전 설비 사진부터 확인해야 뭐라고 보고할 수 있겠다.");
            return;
        }
        phase = Phase.Verify;
        if (scenario == 1)
        {
            Speak(null, "본부|발전 설비 사진 수신했습니다. 현장 인원 다섯 명, 맞습니까?",
                "R|저를 포함하면 여섯 명입니다.",
                "본부|다시 확인했습니다. 공식 등록 인원은 다섯 명입니다.",
                "R|등록 명단을 읽어 주시겠습니까?",
                "무전기|……치직……",
                "R|본부? 응답 바랍니다. …기록실에 보관된 원본을 확인하자.");
            return;
        }
        if (storageKey) storageKey.gameObject.SetActive(true);
        Speak(null, "본부|기지 상태를 보고해 주십시오.",
            "R|발전 설비에 손상 흔적이 있습니다. 현장 확인이 필요합니다.",
            "본부|확인했습니다. 추가 자료를 전송해 주십시오.",
            "R|통신 상태가 좋지 않습니다. 반복해 주십시오.",
            "본부|……수신 상태 불안정……",
            "시스템|관제실 출입 잠금. 보조 열쇠로 접근할 수 있습니다.");
    }

    private void OpenChoices(string name, string text, string first, Action firstAction,
        string second, Action secondAction)
    {
        choosing = true;
        menuChoices.choice1 = first;
        menuChoices.choice2 = second;
        menuChoices.onChoice1.RemoveAllListeners();
        menuChoices.onChoice2.RemoveAllListeners();
        if (firstAction != null) menuChoices.onChoice1.AddListener(() => firstAction());
        if (secondAction != null) menuChoices.onChoice2.AddListener(() => secondAction());
        Present(name, text);
        choiceOneLabel.text = "1 " + menuChoices.choice1;
        choiceTwoLabel.text = "2 " + menuChoices.choice2;
        advance.text = "";
        choices.gameObject.SetActive(true);
        choices.GetComponentInChildren<Button>().Select();
    }

    private void Choose(bool first)
    {
        if (!choosing) return;
        choosing = false;
        choices.gameObject.SetActive(false);
        EventSystem.current.SetSelectedGameObject(null);
        panel.gameObject.SetActive(false);
        hint.gameObject.SetActive(true);
        (first ? menuChoices.onChoice1 : menuChoices.onChoice2).Invoke();
    }

    private void InspectRoster()
    {
        const string roster = "탐사대 등록 명부";
        if (evidence.Contains(roster))
        {
            Speak(null, "R|등록 명단은 R, C, A, T, D. E의 이름이 없다.");
            return;
        }

        void RecordRoster(params string[] lines)
        {
            evidence.Add(roster);
            Speak(CheckEvidence, lines);
        }

        Speak(() => OpenChoices(roster, "E의 이름이 없다. 누구에게 이 사실을 말할까?",
                "C에게 알리기", () => RecordRoster("R|C, 명부에 E가 없어.",
                    "C|…그 이름을 언제 처음 들었는지 기억나?"),
                "E에게 묻기", () => RecordRoster("R|E, 네 이름이 명부에 없어. 언제 합류했지?",
                    "E|음… 제가 나중에 왔나요? 계속 같이 있었던 것 같은데.")),
            "명부|탐사대 등록 인원 5명. R, C, A, T, D.",
            "R|출력된 날짜는 출발 전이다. 왜 E의 이름이 없지?");
    }

    private void CheckEvidence()
    {
        if (scenario == 1)
        {
            if (evidence.Count == 3)
            {
                phase = Phase.Blackout;
                Speak(null, "R|등록은 다섯 명. 출입 기록은 여섯 번.",
                    "R|…복도에 있던 소리가 멎었다.");
            }
            return;
        }
        if (evidence.Count != 2) return;
        Speak(() => StartCoroutine(TransformE()),
            "R|발전 설비가 긁히고 통신이 끊겼는데, 출입 기록에는 외부 문이 열린 적이 없다.",
            "D|E, 지금 뭘 보고 있는 거야?",
            "E|……응?",
            "D|아까부터 멍하니 서 있잖아.",
            "E|그냥…… 너희가 신기해서.",
            "A|뭐가 신기한데?",
            "E|다들…… 같은 걸 보고도 다르게 느끼잖아.",
            "C|E. 지금은 장난칠 때가 아니야.",
            "E|……장난은 아닌데.");
    }

    private void RevealE()
    {
        blackout.color = new Color(.04f, .02f, .04f, .65f);
        PlayEffect("scrape");
        Speak(() => StartCoroutine(TransformE()),
            "D|E, 거기서 뭐 해? 불이 나갔어.",
            "E|음… 이름이 없으면, 내가 앉던 자리도 없어지는 거예요?",
            "R|E. 거기서 움직이지 마.",
            "E|왜요? 아까까지는 옆에 있어도 괜찮았잖아요.");
    }

    private IEnumerator TransformE()
    {
        cutscene = true;
        room = "복도";
        player.transform.position = MainBuildingPlan.World(Entry);
        Follow(1);
        blackout.color = Color.black;
        PlayEffect("scrape");
        yield return new WaitForSeconds(.55f);

        enemy.transform.position = MainBuildingPlan.World(EnemyEntry);
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
        Speak(BeginChase, "E|아…… 이러면 놀라겠구나.",
            "D|뒤로 물러나!",
            "R|여기서 나가야 해…… 아니, 관제실로 가야 해.");
    }

    private void BeginChase()
    {
        phase = Phase.Chase;
        room = "복도";
        enemyCrossed = false;
        if (inventory.keys == 0) inventory.AddKey();
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

    private void LockDown()
    {
        phase = Phase.Finished;
        locked = true;
        if (shutter) shutter.enabled = true;
        PlayEffect("shutter");
        if (scenario == 1)
        {
            PlayerPrefs.SetInt(CheckpointKey, 2);
            PlayerPrefs.Save();
            enemy.enabled = false;
            Speak(() => StartCoroutine(RecoverAfterLockdown()),
                "시스템|비상 셔터 폐쇄. 본관동 전체 봉쇄 완료.",
                "E|R… 문 좀 열어 줘요. 여기, 너무 추워요.");
            return;
        }
        Speak(() =>
        {
            PlayerPrefs.DeleteKey(CheckpointKey);
            PlayerPrefs.Save();
            End("STAGE 01 종료", "본관동 전체 봉쇄 완료. R은 관제실에서 의식을 잃었다.");
        }, "시스템|비상 셔터 폐쇄. 본관동 전체 출입이 차단됐습니다.",
            "R|여섯이서 교대하던 곳인데… 갑자기 이렇게 조용할 수가 있나.");
    }

    private IEnumerator RecoverAfterLockdown()
    {
        blackout.color = Color.black;
        enemy.enabled = false;
        hud.gameObject.SetActive(false);
        var clear = ReportUI.Label(blackout.transform, "STAGE 01 CLEAR", .12f, .42f, .88f, .58f, 70, Signal);
        yield return new WaitForSeconds(1.75f);
        clear.gameObject.SetActive(false);
        Destroy(clear.gameObject);
        hud.gameObject.SetActive(true);
        ResumeAfterLockdown();
        Speak(null, "R|…얼마나 기절해 있었지?",
            "R|정문은 아직 막혀 있다. 관제실 비상 해치로 나가자.");
    }

    private void ResumeAfterLockdown()
    {
        phase = Phase.Evacuate;
        room = "관제실";
        if (inventory.keys == 0) inventory.AddKey();
        if (shutter) shutter.enabled = true;
        foreach (SpriteRenderer member in crew) member.enabled = false;
        enemy.enabled = false;
        player.transform.position = MainBuildingPlan.World(ControlEntry);
        blackout.color = new Color(.12f, .17f, .26f, .20f);
        Follow(1);
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
        choiceOneLabel = ReportUI.Button(choices, "", .35f, .052f, .60f, .265f,
            new Color32(35, 65, 75, 255), Paper, () => Choose(true)).GetComponentInChildren<Text>();
        choiceTwoLabel = ReportUI.Button(choices, "", .64f, .052f, .89f, .265f,
            new Color32(35, 65, 75, 255), Paper, () => Choose(false)).GetComponentInChildren<Text>();
        choices.gameObject.SetActive(false);
        panel.gameObject.SetActive(false);
        endPanel = ReportUI.Place(root, "Result", 0, 0, 1, 1);
        endPanel.gameObject.SetActive(false);
    }

    private void Speak(Action completion, params string[] lines)
    {
        Speak(completion, Array.ConvertAll(lines, entry =>
        {
            int divider = entry.IndexOf('|');
            return new DialogueLine { speaker = entry.Substring(0, divider), text = entry.Substring(divider + 1) };
        }));
    }

    private void Speak(Action completion, DialogueLine[] lines)
    {
        dialogue = lines;
        afterDialogue = completion;
        line = 0;
        ShowLine();
    }

    private void ShowLine()
    {
        DialogueLine entry = dialogue[line];
        Present(entry.speaker, entry.text);
        advance.text = "E / Enter";
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
            if (keys != null && (keys.digit1Key.wasPressedThisFrame || keys.numpad1Key.wasPressedThisFrame)) Choose(true);
            else if (keys != null && (keys.digit2Key.wasPressedThisFrame || keys.numpad2Key.wasPressedThisFrame)) Choose(false);
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
        if (scenario == 2 && loopStarted && !power.Contains("표식") && room == "복도" && position.x < 24)
        {
            loopPasses++;
            position = MainBuildingPlan.World(new Vector2Int(33, 10));
            Speak(null, loopPasses == 1 ? "R|시계가 멈췄다. 비상 안내등의 표시를 다시 보자."
                : "R|또 정비실 앞이다. 멈춘 시계 옆의 안내등을 조사하자.");
        }
        player.transform.position = position;
        Animate(player, playerFrames, motion, ref playerDirection);
        Follow(1 - Mathf.Exp(-9 * Time.deltaTime));

        if (storageKey && storageKey.gameObject.activeSelf
            && ((Vector2)storageKey.transform.position - position).sqrMagnitude < .45f
            && storageKey.Collect(inventory))
        {
            if (scenario == 2) power.Add("부품");
            Speak(null, scenario == 2 ? "R|발전기에 맞는 교체 부품을 챙겼다."
                : scenario == 1 ? "R|관제실 출입 카드를 챙겼다." : "R|관제실 보조 출입 열쇠를 찾았다.");
        }

        ShowObjective(position);
        FindSpot(position);
        if (dialogue == null && interact && nearest != null) nearest.Inspect();
        if (phase == Phase.Chase && dialogue == null) MoveEnemy(player.transform.position);
    }

    private static int Axis(bool positive, bool negative) => (positive ? 1 : 0) - (negative ? 1 : 0);

    private bool WalkableCell(Vector2Int cell)
    {
        var tile = new Vector3Int(cell.x, -cell.y, 0);
        return ground.HasTile(tile) && (!walls || !walls.HasTile(tile)) && (!obstacles || !obstacles.HasTile(tile));
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

        hint.text = nearest != null ? $"[E]  {nearest.Name}" : "WASD / 방향키 이동   ·   E 상호작용";
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
        if (scenario >= 2)
        {
            heading.text = scenario == 2 ? "발전동 1F  /  " + room : "야외  /  화이트아웃";
            objective.text = scenario == 3 ? "안내등을 따라 발전동으로 이동"
                : !power.Contains("부품") ? "정비실에서 교체 부품 찾기"
                : !power.Contains("표식") ? "복도의 달라진 표식 확인"
                : !power.Contains("발전기") ? "발전기실에서 부품 장착"
                : !power.Contains("배전") ? "배전실에서 비상 회로 복구"
                : "제어실에서 보조 전력 연결과 발전기 재가동";
            return;
        }
        heading.text = "본관동 1F  /  " + room;
        switch (phase)
        {
            case Phase.Evacuate:
                objective.color = Signal;
                objective.text = "관제실 북동쪽 비상 해치로 나가세요";
                break;
            case Phase.Survey:
                objective.text = loungeOpened ? "관제실에서 발전 설비 사진 확인" : scenario == 1 ? "식당에서 동료들과 만나기" : "휴게실에서 동료들과 만나기";
                break;
            case Phase.Report:
                objective.text = "동쪽 통신실에서 본부에 보고";
                break;
            case Phase.Verify:
                objective.text = scenario == 1
                    ? (inventory.keys == 0 ? "침실 D에서 출입 카드 챙기기" : $"기록실 명부 · 창고 지급표 · 관제실 출입 기록  {evidence.Count}/3")
                    : (inventory.keys == 0 ? "통신실 수신 기록 확인 · 창고에서 보조 열쇠 찾기"
                       : $"수신 기록과 관제실 상태 기록 확인  {evidence.Count}/2");
                break;
            case Phase.Blackout:
                objective.text = "방에서 나와 복도 확인";
                break;
            case Phase.Chase:
                objective.color = new Color32(255, 160, 143, 255);
                objective.text = room == "관제실" ? "봉쇄 제어 장치에서 전체 봉쇄를 실행하세요"
                    : "북동쪽 관제실까지 도망치세요";
                objective.text += $"  ·  목숨 {health.life}";
                break;
        }
    }

    private void MoveEnemy(Vector2 position)
    {
        float elapsed = Mathf.Min(Time.deltaTime, .035f);
        if (room == "관제실" && !enemyCrossed)
        {
            enemyDoorDelay -= elapsed;
            if (enemyDoorDelay > 0) return;
            enemyCrossed = true;
            enemy.transform.position = MainBuildingPlan.World(scenario == 1 ? ControlEntry : ControlEntry + Vector2Int.up);
            enemyTarget = enemy.transform.position;
        }
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
        if ((position - next).sqrMagnitude < .58f && ClearPath(next, position, true) && health.TryTakeHit())
        {
            if (health.life == 0) End("신호 소실", "E가 R을 따라잡았다.");
            else StartCoroutine(FlashDamage());
        }
    }

    private IEnumerator FlashDamage()
    {
        player.color = new Color32(255, 138, 128, 255);
        yield return new WaitForSeconds(.26f);
        player.color = Color.white;
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
