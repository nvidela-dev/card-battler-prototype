using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace CardBattle
{
    // ----------------------------------------------------------------------------
    //  DATA MODEL
    // ----------------------------------------------------------------------------

    public enum CardType { Monster, Magic }
    public enum MagicEffect { Damage, Heal }

    /// <summary>Static definition of a card.</summary>
    public class CardData
    {
        public string Name;
        public CardType Type;
        public int Cost = 1;

        // Monster stats
        public int Attack;
        public int Health;

        // Magic stats
        public MagicEffect Effect;
        public int Amount;

        public Color Tint = Color.white;

        public static CardData Monster(string name, int atk, int hp, Color tint)
            => new CardData { Name = name, Type = CardType.Monster, Attack = atk, Health = hp, Tint = tint };

        public static CardData Magic(string name, MagicEffect effect, int amount, Color tint)
            => new CardData { Name = name, Type = CardType.Magic, Effect = effect, Amount = amount, Tint = tint };

        public CardData Clone() => (CardData)MemberwiseClone();
    }

    /// <summary>Runtime instance of a card (a monster on the board keeps its own HP).</summary>
    public class CardInstance
    {
        public CardData Data;
        public int CurrentHealth;
        public CardInstance(CardData d) { Data = d; CurrentHealth = d.Health; }
    }

    /// <summary>A duelist: player or enemy.</summary>
    public class Combatant
    {
        public string Name;
        public bool IsPlayer;
        public int MaxHP, HP;
        public int MaxEnergy = 3, Energy = 3;

        public List<CardInstance> Deck = new List<CardInstance>();
        public List<CardInstance> Hand = new List<CardInstance>();
        public List<CardInstance> Discard = new List<CardInstance>();
        public CardInstance[] Board = new CardInstance[5];

        public bool HasEmptySlot()
        {
            for (int i = 0; i < Board.Length; i++) if (Board[i] == null) return true;
            return false;
        }
        public int FirstEmptySlot()
        {
            for (int i = 0; i < Board.Length; i++) if (Board[i] == null) return i;
            return -1;
        }
    }

    // ----------------------------------------------------------------------------
    //  BOOTSTRAP – auto-creates the game when you press Play, no scene wiring needed.
    // ----------------------------------------------------------------------------

    public static class CardBattleBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            if (Object.FindAnyObjectByType<CardBattleGame>() != null) return;
            var go = new GameObject("CardBattleGame");
            go.AddComponent<CardBattleGame>();
        }
    }

    // ----------------------------------------------------------------------------
    //  GAME – logic + runtime-built uGUI board.
    // ----------------------------------------------------------------------------

    public class CardBattleGame : MonoBehaviour
    {
        // ----- tunables -----
        const int StartHP = 30;
        const int StartEnergy = 3;
        const int StartingHand = 5;
        const int MaxHandSize = 8;

        // ----- palette -----
        static readonly Color ColBG          = new Color(0.03f, 0.02f, 0.03f);
        static readonly Color ColPanel       = new Color(0.025f, 0.022f, 0.026f, 0.84f);
        static readonly Color ColSlot        = new Color(0.025f, 0.018f, 0.016f, 0.16f);
        static readonly Color ColSlotEnemy   = new Color(0.18f, 0.05f, 0.17f, 0.18f);
        static readonly Color ColCardMonster = new Color(0.045f, 0.055f, 0.072f, 0.98f);
        static readonly Color ColCardMagic   = new Color(0.07f, 0.045f, 0.09f, 0.98f);
        static readonly Color ColHpFill      = new Color(0.08f, 0.48f, 0.95f);
        static readonly Color ColHpBack      = new Color(0.015f, 0.012f, 0.018f, 0.96f);
        static readonly Color ColEnergyOn    = new Color(0.56f, 0.22f, 0.95f);
        static readonly Color ColEnergyPlayer= new Color(0.08f, 0.55f, 1f);
        static readonly Color ColEnergyOff   = new Color(0.78f, 0.62f, 0.36f, 0.22f);
        static readonly Color ColAccent      = new Color(0.57f, 0.23f, 0.86f);
        static readonly Color ColGold        = new Color(0.83f, 0.62f, 0.34f);
        static readonly Color ColGoldDim     = new Color(0.43f, 0.29f, 0.14f);
        static readonly Color ColTextWarm    = new Color(0.92f, 0.84f, 0.70f);
        static readonly Color ColPurpleText  = new Color(0.78f, 0.52f, 1f);

        // ----- state -----
        Combatant player, enemy;
        int turnNumber;
        bool playerTurn;
        bool gameOver;
        bool busy;                 // true while enemy is acting
        CardInstance targetingCard; // magic awaiting a target
        readonly List<string> log = new List<string>();

        // ----- ui refs -----
        Font font, displayFont;
        Sprite backgroundSprite, playerPortraitSprite, strikeArtSprite, guardArtSprite, curseArtSprite;
        RectTransform handPanel;
        RectTransform[] playerSlots = new RectTransform[5];
        RectTransform[] enemySlots  = new RectTransform[5];
        RectTransform playerHpFill, enemyHpFill;
        Text playerHpText, enemyHpText, playerEnText, enemyEnText, turnText, logText, hintText;
        Text deckText, discardText;
        Image playerBox, enemyBox;     // info boxes (highlighted while targeting)
        readonly List<Image> playerPips = new List<Image>();
        readonly List<Image> enemyPips  = new List<Image>();
        Button endTurnButton;
        GameObject gameOverPanel;
        Text gameOverText;

        // ================================================================
        void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            displayFont = Font.CreateDynamicFontFromOSFont(new[] { "Georgia", "Times New Roman", "Times" }, 48);
            if (displayFont == null) displayFont = font;

            EnsureEventSystem();
            LoadArt();
            BuildUI();
            SetupGame();
            StartPlayerTurn(firstTurn: true);
        }

        void LoadArt()
        {
            backgroundSprite = LoadSprite("duel_background");
            playerPortraitSprite = LoadSprite("player_portrait");
            strikeArtSprite = LoadSprite("card_strike");
            guardArtSprite = LoadSprite("card_guard");
            curseArtSprite = LoadSprite("card_curse");
        }

        // ---------------- setup ----------------
        void SetupGame()
        {
            player = new Combatant { Name = "You",   IsPlayer = true,  MaxHP = StartHP, HP = StartHP, MaxEnergy = StartEnergy, Energy = StartEnergy };
            enemy  = new Combatant { Name = "Malvyn", IsPlayer = false, MaxHP = StartHP, HP = StartHP, MaxEnergy = StartEnergy, Energy = StartEnergy };

            player.Deck = BuildPlayerDeck();
            enemy.Deck  = BuildEnemyDeck();
            Shuffle(player.Deck);
            Shuffle(enemy.Deck);

            for (int i = 0; i < 5; i++) { player.Board[i] = null; enemy.Board[i] = null; }
            turnNumber = 0;
            gameOver = false;
            busy = false;
            targetingCard = null;
            log.Clear();

            for (int i = 0; i < StartingHand; i++) { Draw(player); Draw(enemy); }
            Log("A duel begins against Malvyn, the Cursed Scholar.");
        }

        List<CardInstance> BuildPlayerDeck()
        {
            var list = new List<CardData>();
            Add(list, CardData.Monster("Stone Guardian", 2, 6, new Color(0.55f,0.65f,0.8f)), 2);
            Add(list, CardData.Monster("Dire Wolf",      3, 3, new Color(0.7f,0.5f,0.35f)), 2);
            Add(list, CardData.Monster("Fire Imp",       4, 2, new Color(0.9f,0.45f,0.25f)), 2);
            Add(list, CardData.Magic ("Strike", MagicEffect.Damage, 5, new Color(0.9f,0.3f,0.3f)), 3);
            Add(list, CardData.Magic ("Bolt",   MagicEffect.Damage, 3, new Color(0.95f,0.8f,0.3f)), 2);
            Add(list, CardData.Magic ("Mend",   MagicEffect.Heal,   5, ColHpFill), 2);
            return Instantiate(list);
        }

        List<CardInstance> BuildEnemyDeck()
        {
            var list = new List<CardData>();
            Add(list, CardData.Monster("Cursed Wretch", 2, 4, ColAccent), 2);
            Add(list, CardData.Monster("Shade",         3, 2, new Color(0.35f,0.3f,0.45f)), 2);
            Add(list, CardData.Magic ("Soul Rip",  MagicEffect.Damage, 4, ColAccent), 3);
            Add(list, CardData.Magic ("Dark Bolt", MagicEffect.Damage, 3, new Color(0.5f,0.3f,0.7f)), 2);
            Add(list, CardData.Magic ("Drain",     MagicEffect.Heal,   4, new Color(0.4f,0.7f,0.5f)), 1);
            return Instantiate(list);
        }

        static void Add(List<CardData> list, CardData d, int count) { for (int i = 0; i < count; i++) list.Add(d); }
        static List<CardInstance> Instantiate(List<CardData> defs)
        {
            var r = new List<CardInstance>();
            foreach (var d in defs) r.Add(new CardInstance(d.Clone()));
            return r;
        }
        static void Shuffle<T>(List<T> l)
        {
            for (int i = l.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (l[i], l[j]) = (l[j], l[i]); }
        }

        // ---------------- turn flow ----------------
        void StartPlayerTurn(bool firstTurn = false)
        {
            playerTurn = true;
            busy = false;
            turnNumber++;
            player.Energy = player.MaxEnergy;
            if (!firstTurn) Draw(player);
            Log($"— Your turn ({turnNumber}) —");
            Refresh();
        }

        void OnEndTurn()
        {
            if (!playerTurn || gameOver || busy) return;
            CancelTargeting();
            Log("Your monsters attack!");
            ResolveAttacks(player, enemy);
            Refresh();
            if (CheckGameOver()) return;
            StartCoroutine(EnemyTurn());
        }

        IEnumerator EnemyTurn()
        {
            playerTurn = false;
            busy = true;
            Refresh();
            yield return new WaitForSeconds(0.5f);

            enemy.Energy = enemy.MaxEnergy;
            Draw(enemy);
            Log($"— Malvyn's turn —");
            Refresh();
            yield return new WaitForSeconds(0.5f);

            // greedy play loop
            bool acted = true;
            while (acted && !gameOver)
            {
                acted = false;

                // 1) summon a monster if a slot is free
                var monster = enemy.Hand.Find(c => c.Data.Type == CardType.Monster
                                                   && enemy.Energy >= c.Data.Cost && enemy.HasEmptySlot());
                if (monster != null)
                {
                    PlayMonster(enemy, monster);
                    Log($"Malvyn summons {monster.Data.Name}.");
                    acted = true; Refresh();
                    yield return new WaitForSeconds(0.55f);
                    continue;
                }

                // 2) heal when wounded
                if (enemy.HP < enemy.MaxHP * 0.45f)
                {
                    var heal = enemy.Hand.Find(c => c.Data.Type == CardType.Magic
                                                    && c.Data.Effect == MagicEffect.Heal && enemy.Energy >= c.Data.Cost);
                    if (heal != null)
                    {
                        enemy.HP = Mathf.Min(enemy.MaxHP, enemy.HP + heal.Data.Amount);
                        SpendToDiscard(enemy, heal);
                        Log($"Malvyn casts {heal.Data.Name} (+{heal.Data.Amount} HP).");
                        acted = true; Refresh();
                        yield return new WaitForSeconds(0.55f);
                        continue;
                    }
                }

                // 3) burn the player's face
                var dmg = enemy.Hand.Find(c => c.Data.Type == CardType.Magic
                                               && c.Data.Effect == MagicEffect.Damage && enemy.Energy >= c.Data.Cost);
                if (dmg != null)
                {
                    player.HP = Mathf.Max(0, player.HP - dmg.Data.Amount);
                    SpendToDiscard(enemy, dmg);
                    Log($"Malvyn casts {dmg.Data.Name} on you (-{dmg.Data.Amount} HP).");
                    acted = true; Refresh();
                    if (CheckGameOver()) yield break;
                    yield return new WaitForSeconds(0.55f);
                    continue;
                }
            }

            yield return new WaitForSeconds(0.25f);
            Log("Malvyn's monsters attack!");
            ResolveAttacks(enemy, player);
            Refresh();
            if (CheckGameOver()) yield break;

            yield return new WaitForSeconds(0.4f);
            StartPlayerTurn();
        }

        // ---------------- actions ----------------
        void Draw(Combatant c)
        {
            if (c.Hand.Count >= MaxHandSize) return;
            if (c.Deck.Count == 0)
            {
                if (c.Discard.Count == 0) return;
                c.Deck.AddRange(c.Discard);
                c.Discard.Clear();
                Shuffle(c.Deck);
            }
            var card = c.Deck[c.Deck.Count - 1];
            c.Deck.RemoveAt(c.Deck.Count - 1);
            c.Hand.Add(card);
        }

        void PlayMonster(Combatant c, CardInstance card)
        {
            int slot = c.FirstEmptySlot();
            if (slot < 0) return;
            card.CurrentHealth = card.Data.Health;
            c.Board[slot] = card;
            c.Hand.Remove(card);
            c.Energy -= card.Data.Cost;
        }

        void SpendToDiscard(Combatant c, CardInstance card)
        {
            c.Energy -= card.Data.Cost;
            c.Hand.Remove(card);
            c.Discard.Add(card);
        }

        // Active side's monsters strike the slot opposite them, or the face if unblocked.
        void ResolveAttacks(Combatant attacker, Combatant defender)
        {
            for (int i = 0; i < 5; i++)
            {
                var m = attacker.Board[i];
                if (m == null) continue;
                var blocker = defender.Board[i];
                if (blocker != null) blocker.CurrentHealth -= m.Data.Attack;
                else defender.HP = Mathf.Max(0, defender.HP - m.Data.Attack);
            }
            for (int i = 0; i < 5; i++)
            {
                if (defender.Board[i] != null && defender.Board[i].CurrentHealth <= 0)
                {
                    defender.Discard.Add(defender.Board[i]);
                    defender.Board[i] = null;
                }
            }
        }

        // ---------------- player input ----------------
        void OnHandCardClicked(CardInstance ci)
        {
            if (!playerTurn || gameOver || busy) return;

            if (targetingCard != null && targetingCard != ci) CancelTargeting();

            if (player.Energy < ci.Data.Cost) { Log("Not enough energy."); Refresh(); return; }

            if (ci.Data.Type == CardType.Monster)
            {
                if (!player.HasEmptySlot()) { Log("Your board is full."); Refresh(); return; }
                PlayMonster(player, ci);
                Log($"You summon {ci.Data.Name}.");
                Refresh();
            }
            else // Magic – needs a target
            {
                if (targetingCard == ci) { CancelTargeting(); Refresh(); return; }
                BeginTargeting(ci);
            }
        }

        void BeginTargeting(CardInstance ci)
        {
            targetingCard = ci;
            string verb = ci.Data.Effect == MagicEffect.Damage ? "damage" : "heal";
            hintText.text = $"Casting {ci.Data.Name} — click a portrait to {verb} (or the card again to cancel).";
            Refresh();
        }

        void CancelTargeting()
        {
            targetingCard = null;
            if (hintText != null) hintText.text = "";
        }

        void OnPortraitClicked(bool enemySide)
        {
            if (targetingCard == null || !playerTurn || gameOver) return;
            var card = targetingCard;
            var target = enemySide ? enemy : player;

            if (card.Data.Effect == MagicEffect.Damage)
            {
                target.HP = Mathf.Max(0, target.HP - card.Data.Amount);
                Log($"You cast {card.Data.Name} on {target.Name} (-{card.Data.Amount} HP).");
            }
            else
            {
                target.HP = Mathf.Min(target.MaxHP, target.HP + card.Data.Amount);
                Log($"You cast {card.Data.Name} on {target.Name} (+{card.Data.Amount} HP).");
            }
            SpendToDiscard(player, card);
            CancelTargeting();
            Refresh();
            CheckGameOver();
        }

        bool CheckGameOver()
        {
            if (gameOver) return true;
            if (enemy.HP <= 0) { EndGame("VICTORY", "Malvyn is defeated."); return true; }
            if (player.HP <= 0) { EndGame("DEFEAT", "You have fallen."); return true; }
            return false;
        }

        void EndGame(string title, string sub)
        {
            gameOver = true;
            busy = false;
            CancelTargeting();
            gameOverText.text = $"{title}\n<size=22>{sub}</size>";
            gameOverPanel.SetActive(true);
            Refresh();
        }

        void Restart()
        {
            gameOverPanel.SetActive(false);
            SetupGame();
            StartPlayerTurn(firstTurn: true);
        }

        void Log(string s)
        {
            log.Add(s);
            while (log.Count > 4) log.RemoveAt(0);
        }

        // ================================================================
        //  UI BUILD
        // ================================================================
        void BuildUI()
        {
            var canvasGO = new GameObject("CardBattleCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            Transform root = canvasGO.transform;

            var bg = NewImage("PaintedBackground", root, backgroundSprite != null ? Color.white : ColBG);
            Stretch(bg.rectTransform);
            if (backgroundSprite != null) bg.sprite = backgroundSprite;

            var shade = NewImage("ReadableShade", root, new Color(0f, 0f, 0f, 0.16f));
            Stretch(shade.rectTransform);
            var lowerShade = NewImage("TableReadabilityShade", root, new Color(0f, 0f, 0f, 0.14f));
            Place(lowerShade.gameObject, new Vector2(0,0), new Vector2(1,0.45f), new Vector2(.5f,0), Vector2.zero, Vector2.zero);

            var boardFrame = NewImage("BoardFrame", root, new Color(0.02f, 0.015f, 0.013f, 0.10f));
            Place(boardFrame.gameObject, new Vector2(.5f,.5f), new Vector2(.5f,.5f), new Vector2(.5f,.5f), new Vector2(74,-20), new Vector2(760,296));
            AddGoldOutline(boardFrame, 0.8f, new Color(0.45f,0.30f,0.14f,0.36f));

            BuildLogo(root);

            enemyBox = BuildBossPanel(root, out enemyHpFill, out enemyHpText, out enemyEnText, enemyPips);
            playerBox = BuildPlayerStatus(root, out playerHpFill, out playerHpText);
            BuildEnergyDial(root, out playerEnText, playerPips);
            BuildDeckStack(root);
            BuildPhaseList(root);

            turnText = NewText("TurnPlaque", root, "", 20, TextAnchor.MiddleCenter, ColTextWarm);
            Place(turnText.gameObject, new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(0,-22), new Vector2(220,42));
            turnText.fontStyle = FontStyle.Bold;
            var turnOutline = turnText.gameObject.AddComponent<Outline>();
            turnOutline.effectColor = Color.black;
            turnOutline.effectDistance = new Vector2(2, -2);

            var plaque = NewImage("TurnPlaqueFrame", root, new Color(0.02f,0.016f,0.014f,0.58f));
            Place(plaque.gameObject, new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(0,-24), new Vector2(238,48));
            plaque.transform.SetSiblingIndex(turnText.transform.GetSiblingIndex());
            AddGoldOutline(plaque, 1f);

            logText = NewText("Log", root, "", 13, TextAnchor.UpperCenter, new Color(0.86f,0.78f,0.92f));
            Place(logText.gameObject, new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(0,-66), new Vector2(560,60));

            var enemyRow = NewRow("EnemyBoard", root, new Vector2(74, 82));
            for (int i = 0; i < 5; i++) enemySlots[i] = BuildSlot(enemyRow, true);

            var playerRow = NewRow("PlayerBoard", root, new Vector2(74, -72));
            for (int i = 0; i < 5; i++) playerSlots[i] = BuildSlot(playerRow, false);

            var handGO = new GameObject("Hand", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            handGO.transform.SetParent(root, false);
            handPanel = (RectTransform)handGO.transform;
            Place(handGO, new Vector2(.5f,0), new Vector2(.5f,0), new Vector2(.5f,0), new Vector2(0,14), new Vector2(680,164));
            var hlg = handGO.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10; hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

            hintText = NewText("Hint", root, "", 15, TextAnchor.MiddleCenter, new Color(1f,0.88f,0.42f));
            Place(hintText.gameObject, new Vector2(.5f,0), new Vector2(.5f,0), new Vector2(.5f,0), new Vector2(0,180), new Vector2(700,24));
            hintText.fontStyle = FontStyle.Bold;

            endTurnButton = BuildButton(root, "END\nTURN", new Vector2(1,0), new Vector2(-42,116), new Vector2(124,86), OnEndTurn);

            BuildGameOver(root);
        }

        void BuildLogo(Transform root)
        {
            var group = NewRect("Brand", root);
            Place(group.gameObject, new Vector2(0,1), new Vector2(0,1), new Vector2(0,1), new Vector2(28,-24), new Vector2(318,90));

            var mark = NewImage("EyeMark", group, Color.white);
            Place(mark.gameObject, new Vector2(0,1), new Vector2(0,1), new Vector2(0,1), new Vector2(0,0), new Vector2(58,58));
            mark.sprite = curseArtSprite;
            mark.preserveAspect = true;

            var word = NewText("Wordmark", group, "CARD QUEST", 36, TextAnchor.UpperLeft, ColTextWarm);
            word.font = displayFont;
            Place(word.gameObject, new Vector2(0,1), new Vector2(0,1), new Vector2(0,1), new Vector2(66,-2), new Vector2(240,46));
            word.fontStyle = FontStyle.Bold;
            var wordOutline = word.gameObject.AddComponent<Outline>();
            wordOutline.effectColor = Color.black;
            wordOutline.effectDistance = new Vector2(3, -3);

            var tag = NewText("Tagline", group, "DUEL - EXPLORE - COLLECT", 12, TextAnchor.UpperLeft, new Color(0.86f,0.70f,0.45f));
            tag.font = displayFont;
            Place(tag.gameObject, new Vector2(0,1), new Vector2(0,1), new Vector2(0,1), new Vector2(74,-48), new Vector2(216,20));
        }

        Image BuildBossPanel(Transform root, out RectTransform hpFill, out Text hpText, out Text enText, List<Image> pips)
        {
            var box = NewImage("BossDossier", root, ColPanel);
            var rt = Place(box.gameObject, new Vector2(0,1), new Vector2(0,1), new Vector2(0,1), new Vector2(28,-138), new Vector2(342,214));
            AddGoldOutline(box);

            var btn = box.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(() => OnPortraitClicked(true));

            var sigil = NewImage("BossSigil", rt, Color.white);
            Place(sigil.gameObject, new Vector2(0,1), new Vector2(0,1), new Vector2(0,1), new Vector2(16,-16), new Vector2(78,78));
            sigil.sprite = curseArtSprite;
            sigil.preserveAspect = true;

            var eyebrow = NewText("BossEyebrow", rt, "BOSS DUELIST", 14, TextAnchor.UpperLeft, ColTextWarm);
            Place(eyebrow.gameObject, new Vector2(0,1), new Vector2(0,1), new Vector2(0,1), new Vector2(112,-18), new Vector2(174,22));
            var name = NewText("BossName", rt, "MALVYN", 28, TextAnchor.UpperLeft, ColPurpleText);
            Place(name.gameObject, new Vector2(0,1), new Vector2(0,1), new Vector2(0,1), new Vector2(112,-40), new Vector2(184,36));
            name.fontStyle = FontStyle.Bold;
            var sub = NewText("BossSub", rt, "THE CURSED SCHOLAR", 13, TextAnchor.UpperLeft, new Color(0.82f,0.66f,0.95f));
            Place(sub.gameObject, new Vector2(0,1), new Vector2(0,1), new Vector2(0,1), new Vector2(112,-76), new Vector2(198,20));

            BuildHpBar(rt, new Vector2(16,-108), new Vector2(310,18), out hpFill, out hpText);

            var quote = NewText("BossQuote", rt, "\"Heh heh heh...\nThe weak don't deserve\nthe right to draw.\"", 14, TextAnchor.UpperLeft, new Color(0.84f,0.67f,1f));
            Place(quote.gameObject, new Vector2(0,1), new Vector2(0,1), new Vector2(0,1), new Vector2(26,-154), new Vector2(200,58));

            var pipRow = new GameObject("BossEnergy", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            pipRow.transform.SetParent(rt, false);
            Place(pipRow, new Vector2(1,1), new Vector2(1,1), new Vector2(1,1), new Vector2(-128,-132), new Vector2(92,16));
            var prl = pipRow.GetComponent<HorizontalLayoutGroup>();
            prl.spacing = 4; prl.childAlignment = TextAnchor.MiddleLeft;
            prl.childControlWidth = true; prl.childControlHeight = true;
            prl.childForceExpandWidth = false; prl.childForceExpandHeight = false;
            for (int i = 0; i < StartEnergy; i++)
            {
                var pip = NewImage("Pip", pipRow.transform, ColEnergyOn);
                var le = pip.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 15; le.preferredHeight = 15;
                AddGoldOutline(pip, 0.5f, new Color(0.85f,0.62f,0.25f,0.7f));
                pips.Add(pip);
            }
            enText = NewText("BossEnergyText", rt, "", 13, TextAnchor.UpperRight, ColTextWarm);
            Place(enText.gameObject, new Vector2(1,1), new Vector2(1,1), new Vector2(1,1), new Vector2(-18,-130), new Vector2(92,18));
            enText.fontStyle = FontStyle.Bold;

            return box;
        }

        Image BuildPlayerStatus(Transform root, out RectTransform hpFill, out Text hpText)
        {
            var box = NewImage("PlayerStatus", root, ColPanel);
            var rt = Place(box.gameObject, new Vector2(1,1), new Vector2(1,1), new Vector2(1,1), new Vector2(-28,-24), new Vector2(264,96));
            AddGoldOutline(box);

            var btn = box.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(() => OnPortraitClicked(false));

            var portraitFrame = NewImage("PortraitFrame", rt, new Color(0.02f,0.018f,0.024f,0.95f));
            Place(portraitFrame.gameObject, new Vector2(1,1), new Vector2(1,1), new Vector2(1,1), new Vector2(-12,-12), new Vector2(72,72));
            AddGoldOutline(portraitFrame);
            var portrait = NewImage("Portrait", portraitFrame.rectTransform, Color.white);
            Stretch(portrait.rectTransform);
            portrait.rectTransform.offsetMin = new Vector2(4,4);
            portrait.rectTransform.offsetMax = new Vector2(-4,-4);
            portrait.sprite = playerPortraitSprite;
            portrait.preserveAspect = false;

            var name = NewText("PlayerName", rt, "YOU", 20, TextAnchor.UpperRight, ColTextWarm);
            Place(name.gameObject, new Vector2(1,1), new Vector2(1,1), new Vector2(1,1), new Vector2(-96,-16), new Vector2(150,28));
            name.fontStyle = FontStyle.Bold;
            BuildHpBar(rt, new Vector2(16,-54), new Vector2(168,18), out hpFill, out hpText);

            return box;
        }

        void BuildHpBar(Transform root, Vector2 pos, Vector2 size, out RectTransform hpFill, out Text hpText, bool rightAnchor = false)
        {
            Vector2 anchor = rightAnchor ? Vector2.one : new Vector2(0,1);
            Vector2 pivot = rightAnchor ? Vector2.one : new Vector2(0,1);
            var hpBack = NewImage("HpBack", root, ColHpBack);
            Place(hpBack.gameObject, anchor, anchor, pivot, pos, size);
            AddGoldOutline(hpBack, 0.5f, new Color(0.75f,0.55f,0.28f,0.55f));
            var fillGO = NewImage("HpFill", hpBack.rectTransform, ColHpFill);
            fillGO.rectTransform.anchorMin = new Vector2(0,0);
            fillGO.rectTransform.anchorMax = new Vector2(1,1);
            fillGO.rectTransform.offsetMin = new Vector2(2,2);
            fillGO.rectTransform.offsetMax = new Vector2(-2,-2);
            hpFill = fillGO.rectTransform;
            hpText = NewText("HpTxt", hpBack.rectTransform, "", 15, TextAnchor.MiddleCenter, Color.white);
            Stretch(hpText.rectTransform); hpText.fontStyle = FontStyle.Bold;
        }

        void BuildEnergyDial(Transform root, out Text enText, List<Image> pips)
        {
            var dial = NewImage("EnergyDial", root, new Color(0.02f,0.018f,0.024f,0.88f));
            var rt = Place(dial.gameObject, new Vector2(0,0), new Vector2(0,0), new Vector2(0,0), new Vector2(28,24), new Vector2(156,104));
            AddGoldOutline(dial);

            enText = NewText("EnergyText", rt, "", 30, TextAnchor.MiddleCenter, ColTextWarm);
            Place(enText.gameObject, new Vector2(.5f,.5f), new Vector2(.5f,.5f), new Vector2(.5f,.5f), new Vector2(0,9), new Vector2(112,56));
            enText.fontStyle = FontStyle.Bold;

            var label = NewText("EnergyLabel", rt, "ENERGY", 14, TextAnchor.MiddleCenter, new Color(0.78f,0.68f,0.52f));
            Place(label.gameObject, new Vector2(.5f,0), new Vector2(.5f,0), new Vector2(.5f,0), new Vector2(0,22), new Vector2(110,22));

            var pipRow = new GameObject("PlayerEnergyPips", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            pipRow.transform.SetParent(rt, false);
            Place(pipRow, new Vector2(.5f,0), new Vector2(.5f,0), new Vector2(.5f,0), new Vector2(0,8), new Vector2(116,20));
            var prl = pipRow.GetComponent<HorizontalLayoutGroup>();
            prl.spacing = 6; prl.childAlignment = TextAnchor.MiddleCenter;
            prl.childControlWidth = true; prl.childControlHeight = true;
            prl.childForceExpandWidth = false; prl.childForceExpandHeight = false;
            for (int i = 0; i < StartEnergy; i++)
            {
                var pip = NewImage("Pip", pipRow.transform, ColEnergyPlayer);
                var le = pip.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 20; le.preferredHeight = 20;
                AddGoldOutline(pip, 0.5f, new Color(0.85f,0.62f,0.25f,0.72f));
                pips.Add(pip);
            }
        }

        void BuildDeckStack(Transform root)
        {
            var panel = NewImage("DeckStack", root, new Color(0.018f,0.015f,0.018f,0.78f));
            var rt = Place(panel.gameObject, new Vector2(1,.5f), new Vector2(1,.5f), new Vector2(1,.5f), new Vector2(-42,-8), new Vector2(132,124));
            AddGoldOutline(panel);

            var icon = NewImage("DeckIcon", rt, Color.white);
            Place(icon.gameObject, new Vector2(0,1), new Vector2(0,1), new Vector2(0,1), new Vector2(14,-14), new Vector2(38,38));
            icon.sprite = curseArtSprite;
            icon.preserveAspect = true;
            var deckLabel = NewText("DeckLabel", rt, "DECK", 12, TextAnchor.UpperLeft, new Color(0.78f,0.70f,0.58f));
            Place(deckLabel.gameObject, new Vector2(0,1), new Vector2(0,1), new Vector2(0,1), new Vector2(60,-18), new Vector2(62,18));
            deckText = NewText("DeckCount", rt, "", 24, TextAnchor.UpperLeft, ColTextWarm);
            Place(deckText.gameObject, new Vector2(0,1), new Vector2(0,1), new Vector2(0,1), new Vector2(60,-36), new Vector2(62,30));
            deckText.fontStyle = FontStyle.Bold;

            var line = NewImage("Divider", rt, new Color(0.67f,0.49f,0.25f,0.45f));
            Place(line.gameObject, new Vector2(.5f,.5f), new Vector2(.5f,.5f), new Vector2(.5f,.5f), new Vector2(0,-2), new Vector2(104,1));

            var discardLabel = NewText("DiscardLabel", rt, "DISCARD", 12, TextAnchor.UpperLeft, new Color(0.78f,0.70f,0.58f));
            Place(discardLabel.gameObject, new Vector2(0,0), new Vector2(0,0), new Vector2(0,0), new Vector2(60,44), new Vector2(68,18));
            discardText = NewText("DiscardCount", rt, "", 24, TextAnchor.UpperLeft, ColTextWarm);
            Place(discardText.gameObject, new Vector2(0,0), new Vector2(0,0), new Vector2(0,0), new Vector2(60,16), new Vector2(62,30));
            discardText.fontStyle = FontStyle.Bold;
        }

        void BuildPhaseList(Transform root)
        {
            var panel = NewImage("PhaseList", root, new Color(0.018f,0.016f,0.018f,0.78f));
            var rt = Place(panel.gameObject, new Vector2(0,.5f), new Vector2(0,.5f), new Vector2(0,.5f), new Vector2(28,-112), new Vector2(156,170));
            AddGoldOutline(panel, 0.8f, new Color(0.55f,0.38f,0.18f,0.68f));

            var title = NewText("PhaseTitle", rt, "TURN PHASE", 13, TextAnchor.MiddleCenter, ColTextWarm);
            Place(title.gameObject, new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(0,-19), new Vector2(130,24));

            string[] rows = { "1  DRAW CARD", "2  GAIN ENERGY", "3  PLAY CARDS", "4  ATTACK", "5  END TURN" };
            for (int i = 0; i < rows.Length; i++)
            {
                var row = NewImage("PhaseRow", rt, i == 2 ? new Color(0.32f,0.12f,0.68f,0.72f) : new Color(1f,1f,1f,0.035f));
                Place(row.gameObject, new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(0,-48 - i * 24), new Vector2(132,22));
                if (i == 2) AddGoldOutline(row, 0.6f, new Color(0.75f,0.42f,1f,0.74f));
                var txt = NewText("PhaseText", row.rectTransform, rows[i], 12, TextAnchor.MiddleLeft, i == 2 ? Color.white : new Color(0.70f,0.66f,0.60f));
                Place(txt.gameObject, new Vector2(0,0), new Vector2(1,1), new Vector2(.5f,.5f), new Vector2(10,0), new Vector2(-18,0));
                txt.fontStyle = i == 2 ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        RectTransform NewRow(string name, Transform root, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(root, false);
            var rt = Place(go, new Vector2(.5f,.5f), new Vector2(.5f,.5f), new Vector2(.5f,.5f), pos, new Vector2(580, 128));
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 12; h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            return rt;
        }

        RectTransform BuildSlot(Transform row, bool enemy)
        {
            var slot = NewImage("Slot", row, enemy ? ColSlotEnemy : ColSlot);
            var le = slot.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 96; le.preferredHeight = 126;
            AddGoldOutline(slot, 0.6f, new Color(0.57f,0.39f,0.18f,0.42f));
            return slot.rectTransform;
        }

        void BuildGameOver(Transform root)
        {
            gameOverPanel = NewImage("GameOver", root, new Color(0,0,0,0.82f)).gameObject;
            Stretch((RectTransform)gameOverPanel.transform);
            gameOverText = NewText("GOText", gameOverPanel.transform, "", 60, TextAnchor.MiddleCenter, ColTextWarm);
            Place(gameOverText.gameObject, new Vector2(.5f,.5f), new Vector2(.5f,.5f), new Vector2(.5f,.5f), new Vector2(0,60), new Vector2(800,200));
            gameOverText.fontStyle = FontStyle.Bold;
            gameOverText.supportRichText = true;
            BuildButton(gameOverPanel.transform, "PLAY AGAIN", new Vector2(.5f,.5f), new Vector2(0,-60), new Vector2(230,78), Restart);
            gameOverPanel.SetActive(false);
        }

        Button BuildButton(Transform parent, string label, Vector2 anchor, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var img = NewImage("Btn_" + label.Replace("\n", "_"), parent, new Color(0.055f,0.035f,0.025f,0.92f));
            Place(img.gameObject, anchor, anchor, anchor, pos, size);
            AddGoldOutline(img, 2f, ColGold);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = new Color(0.055f,0.035f,0.025f,0.92f);
            colors.highlightedColor = new Color(0.14f,0.08f,0.055f,0.98f);
            colors.pressedColor = new Color(0.28f,0.15f,0.09f,0.98f);
            colors.disabledColor = new Color(0.04f,0.035f,0.04f,0.42f);
            btn.colors = colors;
            btn.onClick.AddListener(onClick);
            var txt = NewText("Label", img.rectTransform, label, 23, TextAnchor.MiddleCenter, ColTextWarm);
            Stretch(txt.rectTransform); txt.fontStyle = FontStyle.Bold;
            return btn;
        }

        // ================================================================
        //  UI REFRESH
        // ================================================================
        void Refresh()
        {
            SetBar(enemyHpFill, (float)enemy.HP / enemy.MaxHP);
            SetBar(playerHpFill, (float)player.HP / player.MaxHP);
            enemyHpText.text  = $"{Mathf.Max(0,enemy.HP)} / {enemy.MaxHP}";
            playerHpText.text = $"{Mathf.Max(0,player.HP)} / {player.MaxHP}";
            enemyEnText.text  = $"{enemy.Energy}/{enemy.MaxEnergy}";
            playerEnText.text = $"{player.Energy}/{player.MaxEnergy}";
            deckText.text = player.Deck.Count.ToString();
            discardText.text = player.Discard.Count.ToString();
            SetPips(enemyPips, enemy.Energy, ColEnergyOn);
            SetPips(playerPips, player.Energy, ColEnergyPlayer);

            turnText.text = playerTurn ? $"TURN {turnNumber}\nYOUR TURN" : "MALVYN\nTHINKING";
            logText.text = string.Join("\n", log);

            // targeting highlight on portraits
            bool t = targetingCard != null;
            enemyBox.color  = t ? new Color(0.22f,0.09f,0.26f,0.92f) : ColPanel;
            playerBox.color = t ? new Color(0.06f,0.16f,0.24f,0.92f) : ColPanel;

            RebuildBoard(enemySlots, enemy, true);
            RebuildBoard(playerSlots, player, false);
            RebuildHand();

            endTurnButton.interactable = playerTurn && !gameOver && !busy;
        }

        void SetBar(RectTransform fill, float frac)
        {
            frac = Mathf.Clamp01(frac);
            fill.anchorMin = new Vector2(0,0);
            fill.anchorMax = new Vector2(frac,1);
            fill.offsetMin = new Vector2(2, 2);
            fill.offsetMax = new Vector2(-2, -2);
            var img = fill.GetComponent<Image>();
            img.color = frac > 0.35f ? ColHpFill : (frac > 0.15f ? new Color(0.9f,0.7f,0.2f) : new Color(0.85f,0.25f,0.25f));
        }

        void SetPips(List<Image> pips, int energy, Color onColor)
        {
            for (int i = 0; i < pips.Count; i++)
                pips[i].color = i < energy ? onColor : ColEnergyOff;
        }

        void RebuildBoard(RectTransform[] slots, Combatant c, bool enemy)
        {
            for (int i = 0; i < 5; i++)
            {
                var slot = slots[i];
                for (int k = slot.childCount - 1; k >= 0; k--) Destroy(slot.GetChild(k).gameObject);
                if (c.Board[i] != null) BuildCardVisual(slot, c.Board[i], fill: true, interactive: false, showCurrentHp: true);
            }
        }

        void RebuildHand()
        {
            for (int k = handPanel.childCount - 1; k >= 0; k--) Destroy(handPanel.GetChild(k).gameObject);
            foreach (var ci in player.Hand)
                BuildCardVisual(handPanel, ci, fill: false, interactive: true, showCurrentHp: false);
        }

        // ----- card visual -----
        void BuildCardVisual(Transform parent, CardInstance ci, bool fill, bool interactive, bool showCurrentHp)
        {
            bool isMonster = ci.Data.Type == CardType.Monster;
            bool highlighted = interactive && targetingCard == ci;
            var card = NewImage("Card_" + ci.Data.Name, parent, isMonster ? ColCardMonster : ColCardMagic);
            card.raycastTarget = interactive;

            if (fill)
            {
                card.rectTransform.anchorMin = new Vector2(0,0); card.rectTransform.anchorMax = new Vector2(1,1);
                card.rectTransform.offsetMin = new Vector2(5,5); card.rectTransform.offsetMax = new Vector2(-5,-5);
            }
            else
            {
                var le = card.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 112; le.preferredHeight = 154;
            }

            AddGoldOutline(card, highlighted ? 2.4f : 1.2f, highlighted ? new Color(1f,0.88f,0.32f) : ColGold);

            var inner = NewImage("Inner", card.rectTransform, new Color(1f,1f,1f,0.025f));
            Stretch(inner.rectTransform);
            inner.rectTransform.offsetMin = new Vector2(5,5);
            inner.rectTransform.offsetMax = new Vector2(-5,-5);

            var titleBar = NewImage("TitleBar", card.rectTransform, new Color(0.015f,0.012f,0.016f,0.82f));
            Place(titleBar.gameObject, new Vector2(0,1), new Vector2(1,1), new Vector2(.5f,1), new Vector2(0,-6), new Vector2(-12,26));

            var name = NewText("Name", titleBar.rectTransform, ci.Data.Name.ToUpperInvariant(), fill ? 10 : 12, TextAnchor.MiddleCenter, ColTextWarm);
            Stretch(name.rectTransform);
            name.fontStyle = FontStyle.Bold;
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 7;
            name.resizeTextMaxSize = fill ? 10 : 12;

            var cost = NewImage("Cost", card.rectTransform, ci.Data.Type == CardType.Magic ? ColAccent : new Color(0.09f,0.47f,0.88f));
            Place(cost.gameObject, new Vector2(1,1), new Vector2(1,1), new Vector2(1,1), new Vector2(-6,-6), new Vector2(fill ? 18 : 24, fill ? 18 : 24));
            AddGoldOutline(cost, 0.7f, ColGold);
            var costTxt = NewText("CostTxt", cost.rectTransform, ci.Data.Cost.ToString(), fill ? 11 : 15, TextAnchor.MiddleCenter, Color.white);
            Stretch(costTxt.rectTransform); costTxt.fontStyle = FontStyle.Bold;

            var artFrame = NewImage("ArtFrame", card.rectTransform, new Color(0f,0f,0f,0.62f));
            Place(artFrame.gameObject, new Vector2(0,1), new Vector2(1,1), new Vector2(.5f,1), new Vector2(0,-36), new Vector2(-14, fill ? 48 : 74));
            AddGoldOutline(artFrame, 0.5f, new Color(0.50f,0.34f,0.16f,0.7f));

            var art = NewImage("Art", artFrame.rectTransform, ci.Data.Tint);
            Stretch(art.rectTransform);
            art.rectTransform.offsetMin = new Vector2(2,2);
            art.rectTransform.offsetMax = new Vector2(-2,-2);
            var sprite = CardArtFor(ci);
            if (sprite != null)
            {
                art.sprite = sprite;
                art.color = Color.white;
            }
            art.preserveAspect = false;

            var type = NewText("Type", card.rectTransform, isMonster ? "MONSTER" : (ci.Data.Effect == MagicEffect.Damage ? "SPELL - DAMAGE" : "SPELL - HEAL"),
                               fill ? 8 : 9, TextAnchor.MiddleCenter, new Color(0.78f,0.70f,0.58f));
            Place(type.gameObject, new Vector2(0,1), new Vector2(1,1), new Vector2(.5f,1), new Vector2(0, fill ? -88 : -118), new Vector2(-10,14));

            string rulesText = isMonster
                ? $"{ci.Data.Attack} ATK / {(showCurrentHp ? ci.CurrentHealth : ci.Data.Health)} HP"
                : ci.Data.Effect == MagicEffect.Damage
                    ? $"Deal {ci.Data.Amount} damage."
                    : $"Restore {ci.Data.Amount} HP.";
            var rules = NewText("Rules", card.rectTransform, rulesText, fill ? 9 : 11, TextAnchor.UpperCenter, new Color(0.88f,0.82f,0.72f));
            Place(rules.gameObject, new Vector2(0,0), new Vector2(1,0), new Vector2(.5f,0), new Vector2(0, fill ? 28 : 34), new Vector2(-14, fill ? 26 : 42));
            rules.horizontalOverflow = HorizontalWrapMode.Wrap;

            if (isMonster)
            {
                var atk = NewText("Atk", card.rectTransform, ci.Data.Attack.ToString(), fill ? 15 : 18, TextAnchor.LowerLeft, new Color(1f,0.45f,0.35f));
                Place(atk.gameObject, new Vector2(0,0), new Vector2(0,0), new Vector2(0,0), new Vector2(8,5), new Vector2(40,24));
                atk.fontStyle = FontStyle.Bold;

                int hp = showCurrentHp ? ci.CurrentHealth : ci.Data.Health;
                var hpt = NewText("Hp", card.rectTransform, hp.ToString(), fill ? 15 : 18, TextAnchor.LowerRight,
                                  (showCurrentHp && hp < ci.Data.Health) ? new Color(1f,0.72f,0.25f) : new Color(0.48f,0.92f,0.58f));
                Place(hpt.gameObject, new Vector2(1,0), new Vector2(1,0), new Vector2(1,0), new Vector2(-8,5), new Vector2(40,24));
                hpt.fontStyle = FontStyle.Bold;
            }
            else
            {
                var amount = NewText("Amount", card.rectTransform, ci.Data.Amount.ToString(), fill ? 17 : 20, TextAnchor.LowerRight,
                                     ci.Data.Effect == MagicEffect.Damage ? new Color(1f,0.52f,0.42f) : new Color(0.55f,1f,0.66f));
                Place(amount.gameObject, new Vector2(1,0), new Vector2(1,0), new Vector2(1,0), new Vector2(-8,5), new Vector2(38,24));
                amount.fontStyle = FontStyle.Bold;
            }

            if (interactive)
            {
                bool affordable = player.Energy >= ci.Data.Cost && playerTurn && !busy && !gameOver;
                if (!affordable) card.color = Color.Lerp(card.color, Color.black, 0.42f);
                var btn = card.gameObject.AddComponent<Button>();
                btn.targetGraphic = card;
                var captured = ci;
                btn.onClick.AddListener(() => OnHandCardClicked(captured));
            }
        }

        Sprite CardArtFor(CardInstance ci)
        {
            if (ci.Data.Type == CardType.Magic)
            {
                if (ci.Data.Effect == MagicEffect.Heal) return guardArtSprite;
                return ci.Data.Name.Contains("Soul") || ci.Data.Name.Contains("Drain") || ci.Data.Name.Contains("Dark")
                    ? curseArtSprite
                    : strikeArtSprite;
            }

            if (ci.Data.Name.Contains("Guardian")) return guardArtSprite;
            if (ci.Data.Name.Contains("Wretch") || ci.Data.Name.Contains("Shade")) return curseArtSprite;
            return strikeArtSprite;
        }

        // ================================================================
        //  UI HELPERS
        // ================================================================
        void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var module = es.GetComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        Image NewImage(string name, Transform parent, Color c)
        {
            var rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            return img;
        }

        Sprite LoadSprite(string resourceName)
        {
            var tex = Resources.Load<Texture2D>("CardQuest/" + resourceName);
            if (tex == null) return null;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }

        void AddGoldOutline(Image img)
        {
            AddGoldOutline(img, 1f, ColGoldDim);
        }

        void AddGoldOutline(Image img, float distance)
        {
            AddGoldOutline(img, distance, ColGoldDim);
        }

        void AddGoldOutline(Image img, float distance, Color color)
        {
            var outline = img.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(distance, -distance);
        }

        Text NewText(string name, Transform parent, string s, int size, TextAnchor anchor, Color c)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.text = s; t.fontSize = size; t.alignment = anchor; t.color = c;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        static RectTransform Place(GameObject go, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
    }
}
