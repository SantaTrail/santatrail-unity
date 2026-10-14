using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// Runtime controller for one of the five dedicated visual-novel scenes.
public sealed class SantaTrailOpeningStory : MonoBehaviour
{
    private const string MainPage = "MainPageScene";
    private const string DefaultName = "Trainee";
    public static bool IsShowing { get; private set; }
    private static string playerName = DefaultName;

    [Serializable]
    public sealed class DialogueLine
    {
        public string speaker;
        [TextArea(2, 6)] public string text;
        [Tooltip("Optional portrait for this exact conversation cloud.")] public Sprite portrait;
        [Tooltip("Optional voice/SFX played when this cloud appears.")] public AudioClip sound;
        public DialogueLine(string speaker, string text) { this.speaker = speaker; this.text = text; }
    }

    private sealed class Definition
    {
        public string title, next; public List<DialogueLine> lines = new List<DialogueLine>();
    }

    private Definition definition;
    private int sceneNumber, lineIndex;
    private Canvas canvas;
    private Image portrait;
    private TextMeshProUGUI chapter, speaker, body, hint;
    private Button advance, skip;
    private Button[] choices;
    private TextMeshProUGUI[] choiceLabels;
    private AudioSource lineAudio;
    private Coroutine typer;
    private bool finished, choiceOpen, lineComplete;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!TrySceneNumber(SceneManager.GetActiveScene().name, out _)) return;
        if (FindFirstObjectByType<SantaTrailOpeningStory>() != null) return;
        GameObject obj = new GameObject("SantaTrail Visual Novel Controller");
        SceneManager.MoveGameObjectToScene(obj, SceneManager.GetActiveScene());
        obj.AddComponent<SantaTrailOpeningStory>();
    }

    private void Awake()
    {
        if (!TrySceneNumber(gameObject.scene.name, out sceneNumber)) { Destroy(gameObject); return; }
        IsShowing = true;
        definition = Fallback(sceneNumber);
        CreateUi();
        if (sceneNumber == 1) ShowNameCard(); else StartStory();
    }

    private void Update()
    {
        if (finished || choiceOpen || UnityEngine.InputSystem.Keyboard.current == null) return;
        if (UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame || UnityEngine.InputSystem.Keyboard.current.enterKey.wasPressedThisFrame) Advance();
    }

    private Definition Fallback(int number)
    {
        Definition d = new Definition { next = number == 5 ? MainPage : "VisualNovelScene" + (number + 1) };
        if (number == 1)
        {
            d.title = "SCENE 1  •  THE PACKING HALL";
            Add(d,"NARRATOR","Christmas Eve morning at the North Pole. In twelve hours, the biggest delivery of the year begins.");
            Add(d,"TINSEL","Box forty-two, check! Ribbon on the blue ones, please! Bows go on top, people!");
            Add(d,"YOU","Where should I put the teddy bears?"); Add(d,"TINSEL","Belt three, " + playerName + ". Gently. Every child will hug that bear tonight.");
            Add(d,"TINSEL","Everything is on schedule. This is going to be our smoothest Christmas Eve ever."); Add(d,"NARRATOR","The packing-hall door bangs open. Pip rushes in, hat crooked.");
            Add(d,"PIP","Tinsel! Everyone! Emergency!"); Add(d,"TINSEL","Pip, what happened? Did the glue machine explode again?");
            Add(d,"PIP","Worse! It's the reindeer. They're sick. All of them. They can't fly!"); Add(d,"NARRATOR","Everything stops. A single present drops and bounces.");
            Add(d,"TINSEL","That has never happened. Not once in all the years of Christmas."); Add(d,"PIP","We have to tell Santa. Right now!");
        }
        else if (number == 2)
        {
            d.title = "SCENE 2  •  SANTA'S STUDY";
            Add(d,"PIP","Santa! The reindeer are sick. Dasher can't stand up straight, and Comet sneezed a snowflake the size of my hand!"); Add(d,"SANTA","All of them?");
            Add(d,"PIP","All eight. And Rudolph's nose is flickering like a tired candle."); Add(d,"SANTA","Hmm. Reindeer have never been sick on Christmas Eve.");
            Add(d,"YOU","What should we do, Santa?"); Add(d,"SANTA","I wish I knew. In all my years, I have never faced this. But let's not guess.");
            Add(d,"SANTA","Something is making them weak, and I have a feeling I know where to look. Come with me—to the Christmas Spirit Tank.");
        }
        else if (number == 3)
        {
            d.title = "SCENE 3  •  THE CHRISTMAS SPIRIT TANK";
            Add(d,"NARRATOR","The Spirit Tank is the heart of Christmas. It powers the sleigh, the lights, and the reindeer."); Add(d,"PIP","It's almost empty.");
            Add(d,"SANTA","Eight percent. It has never been lower than sixty."); Add(d,"YOU","Why is it so low?");
            Add(d,"SANTA","The Tank fills when children believe in Christmas—when they wonder, hope, and wait for morning."); Add(d,"SANTA","Every year a few less of them wait. This year, many less.");
            Add(d,"TINSEL","And the reindeer fly on Christmas Spirit."); Add(d,"SANTA","Exactly. When the Tank runs low, they grow weak. That is why they are sick.");
            Add(d,"PIP","So no reindeer, no sleigh, and no presents?"); Add(d,"SANTA","The presents are wrapped, and the children are waiting. We just have no way to carry them.");
            Add(d,"SANTA","Is there anyone here with an idea? Any way to deliver Christmas without the sleigh—and help the Spirit grow again?");
        }
        else if (number == 4)
        {
            d.title = "SCENE 4  •  QUINN'S IDEA";
            Add(d,"CAPTAIN QUINN","Santa! I have an idea. I've been testing it for months in the workshop."); Add(d,"SANTA","Quinn, the inventor from the lab. Go on."); Add(d,"CAPTAIN QUINN","Drones."); Add(d,"TINSEL","Drones?");
            Add(d,"CAPTAIN QUINN","Flying machines. They run on batteries, not Spirit. That means they can fly tonight, even with the Tank this low."); Add(d,"PIP","It's so small!");
            Add(d,"CAPTAIN QUINN","Small, quiet, and fast. Each drone carries one present to one home. We send hundreds at once."); Add(d,"SANTA","Hundreds at once. Go on.");
            Add(d,"CAPTAIN QUINN","Every child who wakes up to a present feels wonder. That wonder flows back to the Tank."); Add(d,"CAPTAIN QUINN","The more we deliver, the more it fills. When it fills, the reindeer recover.");
            Add(d,"SANTA","It is a very good idea. But the drones must be fast, precise, and unseen."); Add(d,"YOU","Why unseen?"); Add(d,"SANTA","If a child sees a flying machine, they see a gadget, not magic. Then belief would shrink even more.");
            Add(d,"CAPTAIN QUINN","So flight plans must stay on rooftops, away from windows, streets, and people. That takes careful planning.");
        }
        else
        {
            d.title = "SCENE 5  •  THE PLAN";
            Add(d,"SANTA","Quinn, is this truly the best choice?"); Add(d,"CAPTAIN QUINN","It is the only choice we have tonight, Santa. And it is a good one."); Add(d,"CAPTAIN QUINN","I can't plan every route alone. Each drone needs a flight plan that is fast and exact.");
            Add(d,"TINSEL","The packing team will load drones."); Add(d,"PIP","I'll run the batteries! I'm very fast!"); Add(d,"SANTA","You are, Pip."); Add(d,"CAPTAIN QUINN","And I need someone to learn flight planning—fast. Today. You.");
            Add(d,"YOU","Me? I'm only a trainee."); Add(d,"CAPTAIN QUINN","Everyone starts as a trainee. I'll coach you through every step."); Add(d,"CAPTAIN QUINN","By tonight, you'll plan flights that save time and land exactly where they should.");
            Add(d,"SANTA","Christmas is counting on you, little elf."); Add(d,"NARRATOR","The reindeer rest. The drones wait. And a new kind of Christmas Eve begins.");
        }
        return d;
    }

    private void CreateUi()
    {
        if (TryBindSceneUi())
        {
            lineAudio = gameObject.GetComponent<AudioSource>();
            if (lineAudio == null) lineAudio = gameObject.AddComponent<AudioSource>();
            return;
        }
        if (FindFirstObjectByType<EventSystem>() == null) DontDestroyOnLoad(new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)));
        canvas = new GameObject("Visual Novel Canvas").AddComponent<Canvas>(); canvas.transform.SetParent(transform, false); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2000;
        CanvasScaler scale = canvas.gameObject.AddComponent<CanvasScaler>(); scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scale.referenceResolution = new Vector2(1920,1080); canvas.gameObject.AddComponent<GraphicRaycaster>();
        Image black = Image("Background (black by default)", canvas.transform, Color.black); Stretch(black.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        portrait = Image("Current Speaker Portrait", canvas.transform, Color.white); Rect(portrait.rectTransform, new Vector2(0,0),new Vector2(0,0),new Vector2(390,330),new Vector2(430,620)); portrait.preserveAspect=true; portrait.gameObject.SetActive(false);
        chapter=Text("Chapter",canvas.transform,34,TextAlignmentOptions.Center,new Color(1,.88f,.36f)); Rect(chapter.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-72),new Vector2(1100,60));
        GameObject bubble=Panel("Comic Dialogue Bubble",canvas.transform,new Color(1,.98f,.89f)); Rect(bubble.GetComponent<RectTransform>(),new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,215),new Vector2(1500,390)); Outline(bubble,new Color(.1f,.24f,.22f),9);
        Image plate=Image("Speaker Plate",bubble.transform,new Color(.12f,.45f,.34f)); Rect(plate.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(70,-48),new Vector2(390,68)); speaker=Text("Speaker",plate.transform,30,TextAlignmentOptions.Center,Color.white); Stretch(speaker.rectTransform,Vector2.zero,Vector2.one,new Vector2(10,4),new Vector2(-10,-4));
        body=Text("Dialogue",bubble.transform,38,TextAlignmentOptions.TopLeft,new Color(.08f,.16f,.18f)); Rect(body.rectTransform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(0,-10),new Vector2(1300,220));
        hint=Text("Hint",bubble.transform,23,TextAlignmentOptions.BottomRight,new Color(.12f,.45f,.34f)); hint.text="CLICK / SPACE TO CONTINUE  ▸"; Rect(hint.rectTransform,new Vector2(1,0),new Vector2(1,0),new Vector2(-70,34),new Vector2(460,40));
        advance=bubble.AddComponent<Button>(); advance.targetGraphic=bubble.GetComponent<Image>(); advance.onClick.AddListener(Advance);
        choices=new Button[2]; choiceLabels=new TextMeshProUGUI[2]; for(int i=0;i<2;i++){choices[i]=Button("Choice",canvas.transform,"",i==0?new Color(.17f,.49f,.36f):new Color(.22f,.36f,.64f)); choiceLabels[i]=choices[i].GetComponentInChildren<TextMeshProUGUI>(true); Rect(choices[i].GetComponent<RectTransform>(),new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,i==0?515:425),new Vector2(940,70));choices[i].gameObject.SetActive(false);}
        skip=Button("Skip",canvas.transform,"SKIP OPENING",new Color(.17f,.29f,.34f));Rect(skip.GetComponent<RectTransform>(),new Vector2(1,1),new Vector2(1,1),new Vector2(-145,-70),new Vector2(220,52));skip.onClick.AddListener(()=>Load(MainPage));lineAudio=gameObject.AddComponent<AudioSource>();
    }

    private bool TryBindSceneUi()
    {
        Transform root = transform.Find("Visual Novel Canvas");
        if (root == null) return false;
        canvas = root.GetComponent<Canvas>();
        Transform bubble = root.Find("Comic Dialogue Bubble");
        portrait = root.Find("Current Speaker Portrait")?.GetComponent<Image>();
        chapter = root.Find("Chapter")?.GetComponent<TextMeshProUGUI>();
        speaker = bubble?.Find("Speaker Plate/Speaker")?.GetComponent<TextMeshProUGUI>();
        body = bubble?.Find("Dialogue")?.GetComponent<TextMeshProUGUI>();
        hint = bubble?.Find("Hint")?.GetComponent<TextMeshProUGUI>();
        advance = bubble?.GetComponent<Button>();
        skip = root.Find("Skip")?.GetComponent<Button>();
        choices = new[] { root.Find("Choice 1")?.GetComponent<Button>(), root.Find("Choice 2")?.GetComponent<Button>() };
        choiceLabels = new[] { root.Find("Choice 1/Label")?.GetComponent<TextMeshProUGUI>(), root.Find("Choice 2/Label")?.GetComponent<TextMeshProUGUI>() };
        return canvas != null && portrait != null && chapter != null && speaker != null && body != null && hint != null && advance != null && skip != null && choices[0] != null && choices[1] != null;
    }

    private void ShowNameCard()
    {
        GameObject card=Panel("Elf Registration",canvas.transform,new Color(.14f,.38f,.3f));Rect(card.GetComponent<RectTransform>(),new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(730,450)); TextMeshProUGUI title=Text("Title",card.transform,52,TextAlignmentOptions.Center,new Color(1,.92f,.54f));title.text="WELCOME, NEW ELF!";Rect(title.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-72),new Vector2(640,75));
        TMP_InputField input=Input(card.transform);input.text=playerName;Button start=Button("Start",card.transform,"START CHRISTMAS EVE",new Color(.78f,.18f,.16f));Rect(start.GetComponent<RectTransform>(),new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,65),new Vector2(420,78));start.onClick.AddListener(()=>{playerName=string.IsNullOrWhiteSpace(input.text)?DefaultName:input.text.Trim();Destroy(card);definition=Fallback(1);StartStory();});
    }

    private void StartStory(){chapter.text=definition.title;Show();}
    private void Show(){if(lineIndex>=definition.lines.Count){Finish();return;}DialogueLine l=definition.lines[lineIndex];speaker.text=l.speaker=="YOU"?playerName.ToUpperInvariant():l.speaker;portrait.sprite=l.portrait;portrait.gameObject.SetActive(l.portrait!=null);if(l.sound!=null)lineAudio.PlayOneShot(l.sound);if(typer!=null)StopCoroutine(typer);typer=StartCoroutine(Type(l.text));}
    private IEnumerator Type(string value){lineComplete=false;body.text="";hint.gameObject.SetActive(false);for(int i=0;i<value.Length;i++){body.text=value.Substring(0,i+1);yield return new WaitForSecondsRealtime(.012f);}lineComplete=true;hint.gameObject.SetActive(true);}
    private void Advance(){if(finished||choiceOpen)return;if(!lineComplete){StopCoroutine(typer);body.text=definition.lines[lineIndex].text;lineComplete=true;hint.gameObject.SetActive(true);return;}if((sceneNumber==1||sceneNumber==4)&&lineIndex==definition.lines.Count-1){Choose();return;}lineIndex++;Show();}
    private void Choose(){choiceOpen=true;body.text=sceneNumber==1?"What do you do?":"What do you say?";hint.gameObject.SetActive(false);string[] words=sceneNumber==1?new[]{"I'll come with you, Pip!","Should we check the reindeer ourselves first?","Thanks, I was hoping you'd say that.","I did! They're coughing little snowflakes. Come see!"}:new[]{"That sounds like a lot of work.","That sounds like a puzzle. I like puzzles.","A lot of work shared among a lot of elves.","Then you're going to love this job."};for(int i=0;i<2;i++){choiceLabels[i].text=words[i];choices[i].gameObject.SetActive(true);choices[i].onClick.RemoveAllListeners();int n=i;choices[i].onClick.AddListener(()=>{definition.lines.Add(new DialogueLine(sceneNumber==1?"PIP":"CAPTAIN QUINN",words[n+2]));choiceOpen=false;choices[0].gameObject.SetActive(false);choices[1].gameObject.SetActive(false);lineIndex++;Show();});}}
    private void Finish(){finished=true;body.text=sceneNumber==5?"Your mission desk is ready. Plan every route carefully—and keep Christmas magical.":"This part of the story is complete.";speaker.text=sceneNumber==5?"READY TO FLY":"NEXT SCENE";hint.gameObject.SetActive(false);advance.interactable=false;skip.gameObject.SetActive(false);Button next=Button("Continue",canvas.transform,sceneNumber==5?"CONTINUE TO MISSION DESK":"CONTINUE TO NEXT SCENE",new Color(.76f,.18f,.15f));Rect(next.GetComponent<RectTransform>(),new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,92),new Vector2(520,72));next.onClick.AddListener(()=>Load(definition.next));}
    private void Load(string name){IsShowing=false;SceneManager.LoadSceneAsync(name);}
    private static bool TrySceneNumber(string name,out int number){const string p="VisualNovelScene";number=0;return name.StartsWith(p)&&int.TryParse(name.Substring(p.Length),out number)&&number>=1&&number<=5;}
    private static void Add(Definition d,string s,string t){d.lines.Add(new DialogueLine(s,t));}
    private static Image Image(string n,Transform p,Color c){GameObject o=new GameObject(n,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));o.transform.SetParent(p,false);Image i=o.GetComponent<Image>();i.color=c;return i;}
    private static GameObject Panel(string n,Transform p,Color c){return Image(n,p,c).gameObject;}
    private static TextMeshProUGUI Text(string n,Transform p,float z,TextAlignmentOptions a,Color c){GameObject o=new GameObject(n,typeof(RectTransform),typeof(CanvasRenderer),typeof(TextMeshProUGUI));o.transform.SetParent(p,false);TextMeshProUGUI t=o.GetComponent<TextMeshProUGUI>();t.font=TMP_Settings.defaultFontAsset;t.fontSize=z;t.alignment=a;t.color=c;t.raycastTarget=false;return t;}
    private static Button Button(string n,Transform p,string l,Color c){GameObject o=Panel(n,p,c);Button b=o.AddComponent<Button>();TextMeshProUGUI t=Text("Label",o.transform,27,TextAlignmentOptions.Center,Color.white);t.text=l;Stretch(t.rectTransform,Vector2.zero,Vector2.one,new Vector2(12,4),new Vector2(-12,-4));return b;}
    private static TMP_InputField Input(Transform p){GameObject o=Panel("Elf Name",p,Color.white);Rect(o.GetComponent<RectTransform>(),new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(0,-8),new Vector2(560,72));TMP_InputField f=o.AddComponent<TMP_InputField>();TextMeshProUGUI t=Text("Text",o.transform,32,TextAlignmentOptions.Left,new Color(.08f,.16f,.18f));Stretch(t.rectTransform,Vector2.zero,Vector2.one,new Vector2(20,4),new Vector2(-20,-4));f.textComponent=t;f.characterLimit=18;return f;}
    private static void Rect(RectTransform r,Vector2 min,Vector2 max,Vector2 pos,Vector2 size){r.anchorMin=min;r.anchorMax=max;r.anchoredPosition=pos;r.sizeDelta=size;}
    private static void Stretch(RectTransform r,Vector2 min,Vector2 max,Vector2 amin,Vector2 amax){r.anchorMin=min;r.anchorMax=max;r.offsetMin=amin;r.offsetMax=amax;}
    private static void Outline(GameObject o,Color c,float d){Outline x=o.AddComponent<Outline>();x.effectColor=c;x.effectDistance=new Vector2(d,-d);}
    private void OnDestroy(){if(IsShowing)IsShowing=false;}
}
