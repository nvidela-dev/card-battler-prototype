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

        // Monster keywords
        public bool Taunt;   // must be attacked before the hero / non-taunt monsters
        public bool Charge;  // can attack the turn it is summoned

        public Color Tint = Color.white;

        // Optional Resources path to a full pre-rendered card face. When set, the whole
        // image is shown as the card; otherwise the procedural placeholder is drawn.
        public string Art;

        public static CardData Monster(string name, int atk, int hp, Color tint, bool taunt = false, bool charge = false, string art = null)
            => new CardData { Name = name, Type = CardType.Monster, Attack = atk, Health = hp, Tint = tint, Taunt = taunt, Charge = charge, Art = art };

        public static CardData Magic(string name, MagicEffect effect, int amount, Color tint, string art = null)
            => new CardData { Name = name, Type = CardType.Magic, Effect = effect, Amount = amount, Tint = tint, Art = art };

        public CardData Clone() => (CardData)MemberwiseClone();
    }

    /// <summary>Runtime instance of a card (a monster on the board keeps its own HP).</summary>
    public class CardInstance
    {
        public CardData Data;
        public int CurrentHealth;
        public bool SummonedThisTurn; // summoning sickness – can't attack the turn it lands
        public bool HasAttacked;      // each monster may attack once per turn
        public CardInstance(CardData d) { Data = d; CurrentHealth = d.Health; }
    }

    /// <summary>A named collection of card definitions. A deck belongs to a duelist.</summary>
    public class Deck
    {
        public string Name;
        public Duelist Owner;                                    // the duelist this deck belongs to
        public readonly List<CardData> Cards = new List<CardData>();

        public Deck(string name) { Name = name; }

        /// <summary>Fluent helper: add <paramref name="count"/> copies of a card to the deck.</summary>
        public Deck Add(CardData card, int count = 1)
        {
            for (int i = 0; i < count; i++) Cards.Add(card);
            return this;
        }

        public int Count => Cards.Count;

        /// <summary>Create fresh runtime instances of every card, ready to shuffle into a draw pile.</summary>
        public List<CardInstance> BuildRuntime()
        {
            var r = new List<CardInstance>(Cards.Count);
            foreach (var c in Cards) r.Add(new CardInstance(c.Clone()));
            return r;
        }
    }

    /// <summary>A persistent duelist identity. The player is a duelist – the main character. Owns one or more decks.</summary>
    public class Duelist
    {
        public string Name;
        public string Title;
        public bool IsPlayer;            // the player duelist is the main character
        public int MaxHP = 30;
        public int MaxEnergy = 3;
        public Color Theme = Color.white;

        public readonly List<Deck> Decks = new List<Deck>();
        public Deck ActiveDeck;          // the deck this duelist brings into the duel

        public Duelist(string name, bool isPlayer) { Name = name; IsPlayer = isPlayer; }

        public Deck AddDeck(Deck deck)
        {
            deck.Owner = this;
            Decks.Add(deck);
            if (ActiveDeck == null) ActiveDeck = deck;
            return deck;
        }
    }

    /// <summary>Runtime battle state for a duelist in a single duel.</summary>
    public class Combatant
    {
        public Duelist Duelist;          // the identity this combatant is fighting as
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
        Duelist playerDuelist, enemyDuelist;
        Combatant player, enemy;
        int turnNumber;
        bool playerTurn;
        bool gameOver;
        bool busy;                 // true while enemy is acting
        CardInstance targetingCard;   // magic awaiting a target
        CardInstance selectedAttacker; // player monster chosen to attack, awaiting a target
        readonly List<string> log = new List<string>();

        // ----- ui refs -----
        Font font;
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

        // collection viewer (remaining deck / discard pile)
        GameObject collectionPanel;
        RectTransform collectionGrid;
        Text collectionTitle;
        Button collectionDeckTab, collectionDiscardTab;
        bool collectionShowsDiscard;

        // ================================================================
        void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

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
            playerDuelist = BuildPlayerDuelist();
            enemyDuelist  = BuildMalvyn();

            player = CreateCombatant(playerDuelist);
            enemy  = CreateCombatant(enemyDuelist);
            Shuffle(player.Deck);
            Shuffle(enemy.Deck);

            for (int i = 0; i < 5; i++) { player.Board[i] = null; enemy.Board[i] = null; }
            turnNumber = 0;
            gameOver = false;
            busy = false;
            targetingCard = null;
            selectedAttacker = null;
            log.Clear();

            for (int i = 0; i < StartingHand; i++) { Draw(player); Draw(enemy); }
            Log($"A duel begins against {enemyDuelist.Name}, {enemyDuelist.Title}.");
        }

        // Build the runtime battle state for a duelist from their active deck.
        Combatant CreateCombatant(Duelist d) => new Combatant
        {
            Duelist   = d,
            Name      = d.Name,
            IsPlayer  = d.IsPlayer,
            MaxHP     = d.MaxHP,    HP     = d.MaxHP,
            MaxEnergy = d.MaxEnergy, Energy = d.MaxEnergy,
            Deck      = d.ActiveDeck.BuildRuntime(),
        };

        // The player – the main character – and their starting deck.
        Duelist BuildPlayerDuelist()
        {
            var hero = new Duelist("You", isPlayer: true)
            { Title = "the Wandering Hero", MaxHP = StartHP, MaxEnergy = StartEnergy, Theme = ColEnergyPlayer };
            hero.AddDeck(new Deck("Adventurer's Kit")
                .Add(CardData.Monster("Stone Guardian", 2, 6, new Color(0.55f,0.65f,0.8f), taunt: true, art: "Cards/Monster - Stone Guardian"), 2)
                .Add(CardData.Monster("Dire Wolf",      3, 3, new Color(0.7f,0.5f,0.35f), art: "Cards/Monster - Dire Wolf"), 2)
                .Add(CardData.Monster("Fire Imp",       4, 2, new Color(0.9f,0.45f,0.25f), art: "Cards/Monster - Fire Imp"), 2)
                .Add(CardData.Magic ("Strike", MagicEffect.Damage, 5, new Color(0.9f,0.3f,0.3f), art: "Cards/Spell - Strike"), 3)
                .Add(CardData.Magic ("Bolt",   MagicEffect.Damage, 3, new Color(0.95f,0.8f,0.3f), art: "Cards/Spell - Bolt"), 2)
                .Add(CardData.Magic ("Mend",   MagicEffect.Heal,   5, ColHpFill, art: "Cards/Spell - Mend"), 2));
            return hero;
        }

        Duelist BuildMalvyn()
        {
            var boss = new Duelist("Malvyn", isPlayer: false)
            { Title = "the Cursed Scholar", MaxHP = StartHP, MaxEnergy = StartEnergy, Theme = ColAccent };
            boss.AddDeck(new Deck("Cursed Grimoire")
                .Add(CardData.Monster("Cursed Wretch", 2, 4, ColAccent, taunt: true, art: "Cards/Enemy Card - Cursed Wretch"), 2)
                .Add(CardData.Monster("Shade",         3, 2, new Color(0.35f,0.3f,0.45f), art: "Cards/Enemy Card - Shade"), 2)
                .Add(CardData.Magic ("Soul Rip",  MagicEffect.Damage, 4, ColAccent), 3)
                .Add(CardData.Magic ("Dark Bolt", MagicEffect.Damage, 3, new Color(0.5f,0.3f,0.7f)), 2)
                .Add(CardData.Magic ("Drain",     MagicEffect.Heal,   4, new Color(0.4f,0.7f,0.5f)), 1));
            return boss;
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
            ResetAttacks(player);
            if (!firstTurn) Draw(player);
            Log($"— Your turn ({turnNumber}) —");
            Refresh();
        }

        void OnEndTurn()
        {
            if (!playerTurn || gameOver || busy) return;
            CancelTargeting();
            CancelAttack();
            Refresh();
            StartCoroutine(EnemyTurn());
        }

        IEnumerator EnemyTurn()
        {
            playerTurn = false;
            busy = true;
            Refresh();
            yield return new WaitForSeconds(0.5f);

            enemy.Energy = enemy.MaxEnergy;
            ResetAttacks(enemy);
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
            Refresh();

            // each ready monster attacks one target, Hearthstone-style
            for (int i = 0; i < 5; i++)
            {
                var attacker = enemy.Board[i];
                if (!CanAttack(attacker)) continue;

                yield return new WaitForSeconds(0.45f);
                var target = ChooseEnemyAttackTarget(attacker);
                if (target == null)
                {
                    Log($"{attacker.Data.Name} attacks you.");
                    AttackHero(attacker, player);
                }
                else
                {
                    Log($"{attacker.Data.Name} attacks {target.Data.Name}.");
                    AttackMinion(attacker, enemy, target, player);
                }
                Refresh();
                if (CheckGameOver()) yield break;
            }

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
            card.HasAttacked = false;
            card.SummonedThisTurn = !card.Data.Charge;
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

        // ---------------- combat ----------------
        // A monster may attack if it survived since last turn and hasn't swung yet.
        bool CanAttack(CardInstance m)
            => m != null && !m.SummonedThisTurn && !m.HasAttacked && m.Data.Attack > 0;

        bool HasTaunt(Combatant c)
        {
            for (int i = 0; i < 5; i++)
                if (c.Board[i] != null && c.Board[i].Data.Taunt) return true;
            return false;
        }
        bool EnemyHasTaunt() => HasTaunt(enemy);

        // A board minion is a legal attack target only if no Taunt minion shields it.
        bool IsValidAttackTarget(Combatant defender, CardInstance m)
            => m != null && (!HasTaunt(defender) || m.Data.Taunt);

        void ResetAttacks(Combatant c)
        {
            for (int i = 0; i < 5; i++)
            {
                var m = c.Board[i];
                if (m == null) continue;
                m.HasAttacked = false;
                m.SummonedThisTurn = false;
            }
        }

        // Mutual combat: both monsters deal their Attack to each other.
        void AttackMinion(CardInstance attacker, Combatant attackerSide, CardInstance defender, Combatant defenderSide)
        {
            defender.CurrentHealth -= attacker.Data.Attack;
            attacker.CurrentHealth -= defender.Data.Attack;
            attacker.HasAttacked = true;
            CleanupDead(defenderSide);
            CleanupDead(attackerSide);
        }

        void AttackHero(CardInstance attacker, Combatant defenderSide)
        {
            defenderSide.HP = Mathf.Max(0, defenderSide.HP - attacker.Data.Attack);
            attacker.HasAttacked = true;
        }

        void CleanupDead(Combatant c)
        {
            for (int i = 0; i < 5; i++)
            {
                if (c.Board[i] != null && c.Board[i].CurrentHealth <= 0)
                {
                    Log($"{c.Board[i].Data.Name} is destroyed.");
                    c.Discard.Add(c.Board[i]);
                    c.Board[i] = null;
                }
            }
        }

        // Enemy picks an attack target: forced into Taunt, else a favourable trade, else the hero.
        CardInstance ChooseEnemyAttackTarget(CardInstance attacker)
        {
            CardInstance taunt = null, trade = null;
            for (int i = 0; i < 5; i++)
            {
                var m = player.Board[i];
                if (m == null) continue;
                if (m.Data.Taunt)
                {
                    if (taunt == null || m.CurrentHealth < taunt.CurrentHealth) taunt = m;
                    continue;
                }
                bool canKill = m.CurrentHealth <= attacker.Data.Attack;
                bool survives = m.Data.Attack < attacker.CurrentHealth;
                if (canKill && survives && (trade == null || m.Data.Attack > trade.Data.Attack)) trade = m;
            }
            if (HasTaunt(player)) return taunt;
            return trade; // null => go face
        }

        // ---------------- player input ----------------
        void OnHandCardClicked(CardInstance ci)
        {
            if (!playerTurn || gameOver || busy) return;

            CancelAttack();
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
            hintText.text = $"Casting {ci.Data.Name} — click a monster or portrait to {verb} (or the card again to cancel).";
            Refresh();
        }

        void CancelTargeting()
        {
            targetingCard = null;
            if (hintText != null && selectedAttacker == null) hintText.text = "";
        }

        void CancelAttack()
        {
            selectedAttacker = null;
            if (hintText != null && targetingCard == null) hintText.text = "";
        }

        // Click one of your own board monsters: cast a spell on it, or pick it as an attacker.
        void OnPlayerMonsterClicked(CardInstance m)
        {
            if (!playerTurn || gameOver || busy) return;

            if (targetingCard != null) { ResolveSpellOnMinion(targetingCard, m, player); return; }

            if (selectedAttacker == m) { CancelAttack(); Refresh(); return; }
            if (!CanAttack(m))
            {
                Log(m.SummonedThisTurn
                    ? $"{m.Data.Name} can't attack the turn it's summoned."
                    : $"{m.Data.Name} has already attacked.");
                Refresh();
                return;
            }
            selectedAttacker = m;
            hintText.text = EnemyHasTaunt()
                ? $"{m.Data.Name} attacks — you must strike a Taunt monster."
                : $"{m.Data.Name} attacks — click an enemy monster or Malvyn.";
            Refresh();
        }

        // Click an enemy board monster: cast a spell on it, or resolve a pending attack into it.
        void OnEnemyMonsterClicked(CardInstance m)
        {
            if (!playerTurn || gameOver || busy) return;

            if (targetingCard != null) { ResolveSpellOnMinion(targetingCard, m, enemy); return; }

            if (selectedAttacker == null) return;
            if (!IsValidAttackTarget(enemy, m)) { Log("You must attack a Taunt monster first."); Refresh(); return; }

            var attacker = selectedAttacker;
            Log($"{attacker.Data.Name} attacks {m.Data.Name}.");
            AttackMinion(attacker, player, m, enemy);
            CancelAttack();
            Refresh();
            CheckGameOver();
        }

        void ResolveSpellOnMinion(CardInstance card, CardInstance target, Combatant side)
        {
            if (card.Data.Effect == MagicEffect.Damage)
            {
                target.CurrentHealth -= card.Data.Amount;
                Log($"You cast {card.Data.Name} on {target.Data.Name} (-{card.Data.Amount}).");
                CleanupDead(side);
            }
            else
            {
                target.CurrentHealth = Mathf.Min(target.Data.Health, target.CurrentHealth + card.Data.Amount);
                Log($"You cast {card.Data.Name} on {target.Data.Name} (+{card.Data.Amount}).");
            }
            SpendToDiscard(player, card);
            CancelTargeting();
            Refresh();
            CheckGameOver();
        }

        void OnPortraitClicked(bool enemySide)
        {
            if (!playerTurn || gameOver || busy) return;

            if (targetingCard != null)
            {
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
                return;
            }

            // resolve a pending attack against the enemy hero
            if (selectedAttacker != null && enemySide)
            {
                if (EnemyHasTaunt()) { Log("You must attack a Taunt monster first."); Refresh(); return; }
                var attacker = selectedAttacker;
                Log($"{attacker.Data.Name} attacks {enemy.Name}.");
                AttackHero(attacker, enemy);
                CancelAttack();
                Refresh();
                CheckGameOver();
            }
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
            CancelAttack();
            gameOverText.text = $"{title}\n<size=22>{sub}</size>";
            gameOverPanel.SetActive(true);
            Refresh();
        }

        void Restart()
        {
            gameOverPanel.SetActive(false);
            CloseCollection();
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

            // Matching duelist frames in the top corners: enemy on the left, player on the right.
            enemyBox = BuildDuelistFrame(root, "MALVYN", "THE CURSED SCHOLAR", curseArtSprite, ColPurpleText,
                new Vector2(0,1), new Vector2(28,-24), ColEnergyOn, enemyPips, mirror: true,
                () => OnPortraitClicked(true), out enemyHpFill, out enemyHpText, out enemyEnText);
            playerBox = BuildDuelistFrame(root, "YOU", "THE WANDERING HERO", playerPortraitSprite, ColTextWarm,
                new Vector2(1,1), new Vector2(-28,-24), ColEnergyPlayer, playerPips, mirror: false,
                () => OnPortraitClicked(false), out playerHpFill, out playerHpText, out playerEnText);
            BuildDeckStack(root);

            // Turn indicator sits stacked directly above the deck/discard panel on the right.
            // (Deck panel: anchored (1,.5), right edge -42, width 132 → column centre is -108 from the right.)
            var plaque = NewImage("TurnPlaqueFrame", root, new Color(0.02f,0.016f,0.014f,0.52f));
            Place(plaque.gameObject, new Vector2(1,.5f), new Vector2(1,.5f), new Vector2(.5f,.5f), new Vector2(-108,116), new Vector2(140,56));
            AddGoldOutline(plaque, 1f);

            turnText = NewText("TurnPlaque", root, "", 14, TextAnchor.MiddleCenter, ColTextWarm);
            Place(turnText.gameObject, new Vector2(1,.5f), new Vector2(1,.5f), new Vector2(.5f,.5f), new Vector2(-108,116), new Vector2(132,50));
            turnText.fontStyle = FontStyle.Bold;
            turnText.lineSpacing = 0.92f;
            AddLetterSpacing(turnText, 3f);
            var turnOutline = turnText.gameObject.AddComponent<Outline>();
            turnOutline.effectColor = Color.black;
            turnOutline.effectDistance = new Vector2(2, -2);

            logText = NewText("Log", root, "", 12, TextAnchor.UpperCenter, new Color(0.86f,0.78f,0.92f));
            Place(logText.gameObject, new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(0,-30), new Vector2(540,52));

            var enemyRow = NewRow("EnemyBoard", root, new Vector2(0, 88));
            for (int i = 0; i < 5; i++) enemySlots[i] = BuildSlot(enemyRow, true);

            var playerRow = NewRow("PlayerBoard", root, new Vector2(0, -92));
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
            BuildCollectionViewer(root);
        }

        // A compact duelist HUD frame: portrait in the outer corner, name + subtitle,
        // an HP bar, and an energy readout (pips + count) right below it. The same builder
        // drives both duelists; `mirror` flips the layout so the enemy (left) and player
        // (right) read symmetrically toward their screen corners.
        Image BuildDuelistFrame(Transform root, string title, string subtitle, Sprite portrait, Color titleColor,
            Vector2 anchor, Vector2 pos, Color energyColor, List<Image> pips, bool mirror,
            UnityEngine.Events.UnityAction onPortraitClick,
            out RectTransform hpFill, out Text hpText, out Text enText)
        {
            var box = NewImage("DuelistFrame", root, ColPanel);
            var rt = Place(box.gameObject, anchor, anchor, anchor, pos, new Vector2(260, 104));
            AddGoldOutline(box);

            var btn = box.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(onPortraitClick);

            // portrait hugs the outer screen corner; the text column fills the inner side
            Vector2 portAnchor = mirror ? new Vector2(0,1) : new Vector2(1,1);
            Vector2 portPos    = mirror ? new Vector2(10,-10) : new Vector2(-10,-10);
            var portraitFrame = NewImage("PortraitFrame", rt, new Color(0.02f,0.018f,0.024f,0.95f));
            Place(portraitFrame.gameObject, portAnchor, portAnchor, portAnchor, portPos, new Vector2(72,72));
            AddGoldOutline(portraitFrame);
            var pimg = NewImage("Portrait", portraitFrame.rectTransform, portrait != null ? Color.white : new Color(0.1f,0.09f,0.12f));
            Stretch(pimg.rectTransform);
            pimg.rectTransform.offsetMin = new Vector2(3,3);
            pimg.rectTransform.offsetMax = new Vector2(-3,-3);
            if (portrait != null) pimg.sprite = portrait;
            pimg.preserveAspect = false;

            Vector2 txtAnchor = mirror ? new Vector2(1,1) : new Vector2(0,1);
            float dir = mirror ? -1f : 1f;   // x grows away from the outer corner
            TextAnchor align = mirror ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            const float edge = 14f;

            var name = NewText("Name", rt, title, 20, align, titleColor);
            Place(name.gameObject, txtAnchor, txtAnchor, txtAnchor, new Vector2(dir*edge, -10), new Vector2(160,26));
            name.fontStyle = FontStyle.Bold;

            if (!string.IsNullOrEmpty(subtitle))
            {
                var sub = NewText("Sub", rt, subtitle, 11, align, new Color(0.82f,0.74f,0.62f));
                Place(sub.gameObject, txtAnchor, txtAnchor, txtAnchor, new Vector2(dir*edge, -34), new Vector2(180,16));
            }

            BuildHpBar(rt, new Vector2(dir*edge, -54), new Vector2(160,18), out hpFill, out hpText, rightAnchor: mirror);

            // energy pips sit directly under the HP bar, on the same inner edge
            var pipRow = new GameObject("EnergyPips", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            pipRow.transform.SetParent(rt, false);
            Place(pipRow, txtAnchor, txtAnchor, txtAnchor, new Vector2(dir*edge, -78), new Vector2(110,16));
            var prl = pipRow.GetComponent<HorizontalLayoutGroup>();
            prl.spacing = 5; prl.childAlignment = mirror ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            prl.childControlWidth = true; prl.childControlHeight = true;
            prl.childForceExpandWidth = false; prl.childForceExpandHeight = false;
            for (int i = 0; i < StartEnergy; i++)
            {
                var pip = NewImage("Pip", pipRow.transform, energyColor);
                var le = pip.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 14; le.preferredHeight = 14;
                AddGoldOutline(pip, 0.5f, new Color(0.85f,0.62f,0.25f,0.72f));
                pips.Add(pip);
            }
            enText = NewText("EnergyText", rt, "", 13, align, ColTextWarm);
            Place(enText.gameObject, txtAnchor, txtAnchor, txtAnchor, new Vector2(dir*(edge+60), -77), new Vector2(54,16));
            enText.fontStyle = FontStyle.Bold;

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

        void BuildDeckStack(Transform root)
        {
            var panel = NewImage("DeckStack", root, new Color(0.018f,0.015f,0.018f,0.78f));
            var rt = Place(panel.gameObject, new Vector2(1,.5f), new Vector2(1,.5f), new Vector2(1,.5f), new Vector2(-42,-8), new Vector2(132,136));
            AddGoldOutline(panel);

            var icon = NewImage("DeckIcon", rt, Color.white);
            Place(icon.gameObject, new Vector2(0,1), new Vector2(0,1), new Vector2(0,1), new Vector2(14,-14), new Vector2(36,36));
            icon.sprite = curseArtSprite;
            icon.preserveAspect = true;
            var deckLabel = NewText("DeckLabel", rt, "DECK", 10, TextAnchor.UpperLeft, new Color(0.78f,0.70f,0.58f));
            Place(deckLabel.gameObject, new Vector2(0,1), new Vector2(0,1), new Vector2(0,1), new Vector2(58,-16), new Vector2(60,18));
            AddLetterSpacing(deckLabel, 2.5f);
            deckText = NewText("DeckCount", rt, "", 20, TextAnchor.UpperLeft, ColTextWarm);
            Place(deckText.gameObject, new Vector2(0,1), new Vector2(0,1), new Vector2(0,1), new Vector2(58,-36), new Vector2(54,30));
            deckText.fontStyle = FontStyle.Bold;

            var line = NewImage("Divider", rt, new Color(0.67f,0.49f,0.25f,0.45f));
            Place(line.gameObject, new Vector2(0,1), new Vector2(0,1), new Vector2(0,1), new Vector2(14,-70), new Vector2(104,1));

            var discardLabel = NewText("DiscardLabel", rt, "DISCARD", 10, TextAnchor.UpperCenter, new Color(0.78f,0.70f,0.58f));
            Place(discardLabel.gameObject, new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(0,-80), new Vector2(104,18));
            AddLetterSpacing(discardLabel, 2.5f);
            discardText = NewText("DiscardCount", rt, "", 20, TextAnchor.UpperCenter, ColTextWarm);
            Place(discardText.gameObject, new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(0,-100), new Vector2(54,30));
            discardText.fontStyle = FontStyle.Bold;

            // click the deck or discard half to inspect the cards inside
            AddPileButton(rt, new Vector2(0,-36), new Vector2(124,62), () => OpenCollection(false));
            AddPileButton(rt, new Vector2(0,-100), new Vector2(124,62), () => OpenCollection(true));
        }

        // A near-invisible hotspot over part of the deck stack that opens the collection viewer.
        void AddPileButton(Transform parent, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var hot = NewImage("PileHotspot", parent, new Color(1f,1f,1f,0.001f));
            Place(hot.gameObject, new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(.5f,1), pos, size);
            var btn = hot.gameObject.AddComponent<Button>();
            btn.targetGraphic = hot;
            var colors = btn.colors;
            colors.normalColor = new Color(1f,1f,1f,0.001f);
            colors.highlightedColor = new Color(0.85f,0.7f,0.35f,0.16f);
            colors.pressedColor = new Color(0.85f,0.7f,0.35f,0.28f);
            btn.colors = colors;
            btn.onClick.AddListener(onClick);
        }

        RectTransform NewRow(string name, Transform root, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(root, false);
            var rt = Place(go, new Vector2(.5f,.5f), new Vector2(.5f,.5f), new Vector2(.5f,.5f), pos, new Vector2(640, 172));
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
            le.preferredWidth = 116; le.preferredHeight = 164;   // match the hand-card size
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

        // Overlay that lists the cards still in your deck or in your discard pile.
        void BuildCollectionViewer(Transform root)
        {
            // fully opaque backdrop – a modal screen that hides the battlefield until closed
            var overlay = NewImage("CollectionOverlay", root, ColBG);
            Stretch(overlay.rectTransform);
            collectionPanel = overlay.gameObject;
            if (backgroundSprite != null)
            {
                overlay.sprite = backgroundSprite;
                overlay.color = new Color(0.18f, 0.16f, 0.20f); // darkened so card art stays readable
                overlay.type = Image.Type.Simple;
            }
            // the backdrop captures every click: outside the frame it closes, and nothing leaks through to the board
            var closeBg = overlay.gameObject.AddComponent<Button>();
            closeBg.transition = Selectable.Transition.None;
            closeBg.onClick.AddListener(CloseCollection);

            var frame = NewImage("CollectionFrame", overlay.rectTransform, ColPanel);
            var frt = Place(frame.gameObject, new Vector2(.5f,.5f), new Vector2(.5f,.5f), new Vector2(.5f,.5f), Vector2.zero, new Vector2(640, 660));
            AddGoldOutline(frame, 1.6f, ColGold);
            frame.raycastTarget = true; // absorbs clicks so they don't fall through to the close hotspot

            collectionTitle = NewText("CollectionTitle", frt, "", 24, TextAnchor.MiddleCenter, ColTextWarm);
            Place(collectionTitle.gameObject, new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(0,-24), new Vector2(580,34));
            collectionTitle.fontStyle = FontStyle.Bold;

            collectionDeckTab    = BuildButton(frt, "DECK",    new Vector2(.5f,1), new Vector2(-84,-66), new Vector2(156,40), () => SwitchCollection(false));
            collectionDiscardTab = BuildButton(frt, "DISCARD", new Vector2(.5f,1), new Vector2( 84,-66), new Vector2(156,40), () => SwitchCollection(true));

            var gridGO = new GameObject("CollectionGrid", typeof(RectTransform), typeof(GridLayoutGroup));
            gridGO.transform.SetParent(frt, false);
            collectionGrid = (RectTransform)gridGO.transform;
            Place(gridGO, new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(0,-114), new Vector2(584,470));
            var grid = gridGO.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(108, 150);
            grid.spacing = new Vector2(8, 8);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;

            BuildButton(frt, "CLOSE", new Vector2(.5f,0), new Vector2(0,22), new Vector2(176,46), CloseCollection);

            collectionPanel.SetActive(false);
        }

        void OpenCollection(bool discard)
        {
            collectionShowsDiscard = discard;
            collectionPanel.SetActive(true);
            RefreshCollection();
        }

        void SwitchCollection(bool discard)
        {
            collectionShowsDiscard = discard;
            RefreshCollection();
        }

        void CloseCollection()
        {
            if (collectionPanel != null) collectionPanel.SetActive(false);
        }

        void RefreshCollection()
        {
            if (collectionPanel == null || !collectionPanel.activeSelf) return;

            var pile = collectionShowsDiscard ? player.Discard : player.Deck;
            collectionTitle.text = collectionShowsDiscard
                ? $"DISCARD PILE — {pile.Count} CARD{(pile.Count == 1 ? "" : "S")}"
                : $"{playerDuelist.ActiveDeck.Name.ToUpperInvariant()} — {pile.Count} CARD{(pile.Count == 1 ? "" : "S")} LEFT";

            // the active tab is shown as non-interactable (greyed) to mark the current pile
            collectionDeckTab.interactable    = collectionShowsDiscard;
            collectionDiscardTab.interactable = !collectionShowsDiscard;

            for (int k = collectionGrid.childCount - 1; k >= 0; k--) Destroy(collectionGrid.GetChild(k).gameObject);

            // sort by cost then name so the draw order of the deck stays hidden
            var cards = new List<CardInstance>(pile);
            cards.Sort((a, b) =>
            {
                int c = a.Data.Cost.CompareTo(b.Data.Cost);
                return c != 0 ? c : string.Compare(a.Data.Name, b.Data.Name, System.StringComparison.Ordinal);
            });

            foreach (var ci in cards)
                BuildCardVisual(collectionGrid, ci, fill: false, showCurrentHp: false,
                    onClick: null, selected: false, exhausted: false, taunt: ci.Data.Taunt, validTarget: false);

            if (cards.Count == 0)
            {
                var empty = NewText("CollectionEmpty", collectionGrid, "(empty)", 18, TextAnchor.MiddleCenter, new Color(0.7f,0.64f,0.56f));
                var le = empty.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 200; le.preferredHeight = 40;
            }
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
            RefreshCollection(); // keep an open viewer in sync as the piles change
            SetPips(enemyPips, enemy.Energy, ColEnergyOn);
            SetPips(playerPips, player.Energy, ColEnergyPlayer);

            turnText.text = playerTurn ? $"TURN {turnNumber}\nYOUR TURN" : "MALVYN\nTHINKING";
            logText.text = string.Join("\n", log);

            // highlight portraits: purple while a spell is targeting, red when the hero can be attacked
            bool spellTargeting = targetingCard != null;
            bool enemyFaceAttackable = selectedAttacker != null && !EnemyHasTaunt();
            enemyBox.color  = spellTargeting ? new Color(0.22f,0.09f,0.26f,0.92f)
                              : enemyFaceAttackable ? new Color(0.34f,0.08f,0.08f,0.92f) : ColPanel;
            playerBox.color = spellTargeting ? new Color(0.06f,0.16f,0.24f,0.92f) : ColPanel;

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

        void RebuildBoard(RectTransform[] slots, Combatant c, bool isEnemy)
        {
            for (int i = 0; i < 5; i++)
            {
                var slot = slots[i];
                for (int k = slot.childCount - 1; k >= 0; k--) Destroy(slot.GetChild(k).gameObject);
                var m = c.Board[i];
                if (m == null) continue;

                bool selected = !isEnemy && selectedAttacker == m;
                bool exhausted = !isEnemy && playerTurn && !CanAttack(m);
                bool validTarget = targetingCard != null
                                   || (isEnemy && selectedAttacker != null && IsValidAttackTarget(enemy, m));
                System.Action onClick = isEnemy
                    ? (System.Action)(() => OnEnemyMonsterClicked(m))
                    : (() => OnPlayerMonsterClicked(m));

                BuildCardVisual(slot, m, fill: true, showCurrentHp: true,
                    onClick: onClick, selected: selected, exhausted: exhausted,
                    taunt: m.Data.Taunt, validTarget: validTarget);
            }
        }

        void RebuildHand()
        {
            for (int k = handPanel.childCount - 1; k >= 0; k--) Destroy(handPanel.GetChild(k).gameObject);
            foreach (var ci in player.Hand)
            {
                var captured = ci;
                bool affordable = player.Energy >= ci.Data.Cost && playerTurn && !busy && !gameOver;
                BuildCardVisual(handPanel, ci, fill: false, showCurrentHp: false,
                    onClick: () => OnHandCardClicked(captured), selected: targetingCard == ci,
                    exhausted: !affordable, taunt: ci.Data.Taunt, validTarget: false);
            }
        }

        // ----- card visual -----
        void BuildCardVisual(Transform parent, CardInstance ci, bool fill, bool showCurrentHp,
            System.Action onClick, bool selected, bool exhausted, bool taunt, bool validTarget)
        {
            bool interactive = onClick != null;
            bool isMonster = ci.Data.Type == CardType.Monster;
            var fullArt = FullArtFor(ci.Data);
            var card = NewImage("Card_" + ci.Data.Name, parent,
                fullArt != null ? Color.white : (isMonster ? ColCardMonster : ColCardMagic));
            card.raycastTarget = interactive;
            if (fullArt != null) { card.sprite = fullArt; card.preserveAspect = false; }

            if (fill)
            {
                card.rectTransform.anchorMin = new Vector2(0,0); card.rectTransform.anchorMax = new Vector2(1,1);
                card.rectTransform.offsetMin = new Vector2(5,5); card.rectTransform.offsetMax = new Vector2(-5,-5);
            }
            else
            {
                var le = card.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 116; le.preferredHeight = 164;
            }

            float outlineW; Color outlineCol;
            if (selected)         { outlineW = 2.6f; outlineCol = new Color(1f,0.88f,0.32f); }   // chosen attacker / casting
            else if (validTarget) { outlineW = 2.2f; outlineCol = new Color(1f,0.40f,0.35f); }   // legal target
            else if (taunt)       { outlineW = 2.2f; outlineCol = new Color(0.72f,0.80f,0.88f); } // Taunt shield
            else                  { outlineW = 1.2f; outlineCol = ColGold; }
            AddGoldOutline(card, outlineW, outlineCol);

            if (fullArt == null)
            {
            var inner = NewImage("Inner", card.rectTransform, new Color(1f,1f,1f,0.025f));
            Stretch(inner.rectTransform);
            inner.rectTransform.offsetMin = new Vector2(5,5);
            inner.rectTransform.offsetMax = new Vector2(-5,-5);

            var titleBar = NewImage("TitleBar", card.rectTransform, new Color(0.015f,0.012f,0.016f,0.82f));
            Place(titleBar.gameObject, new Vector2(0,1), new Vector2(1,1), new Vector2(.5f,1), new Vector2(0,-6), new Vector2(-12,26));

            var name = NewText("Name", titleBar.rectTransform, ci.Data.Name.ToUpperInvariant(), fill ? 10 : 12, TextAnchor.MiddleCenter, ColTextWarm);
            Stretch(name.rectTransform);
            name.rectTransform.offsetMin = new Vector2(8, 0);
            name.rectTransform.offsetMax = new Vector2(fill ? -24 : -32, 0);
            name.alignment = TextAnchor.MiddleLeft;
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
            Place(artFrame.gameObject, new Vector2(0,1), new Vector2(1,1), new Vector2(.5f,1), new Vector2(0,-36), new Vector2(-14, fill ? 44 : 62));
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

            string typeLabel = isMonster
                ? (ci.Data.Taunt ? "MONSTER - TAUNT" : "MONSTER")
                : (ci.Data.Effect == MagicEffect.Damage ? "SPELL - DAMAGE" : "SPELL - HEAL");
            var type = NewText("Type", card.rectTransform, typeLabel,
                               fill ? 8 : 9, TextAnchor.MiddleCenter,
                               isMonster && ci.Data.Taunt ? new Color(0.78f,0.86f,0.95f) : new Color(0.78f,0.70f,0.58f));
            Place(type.gameObject, new Vector2(0,1), new Vector2(1,1), new Vector2(.5f,1), new Vector2(0, fill ? -82 : -104), new Vector2(-10,14));

            string rulesText = isMonster
                ? $"{ci.Data.Attack} ATK / {(showCurrentHp ? ci.CurrentHealth : ci.Data.Health)} HP"
                : ci.Data.Effect == MagicEffect.Damage
                    ? $"Deal {ci.Data.Amount} damage."
                    : $"Restore {ci.Data.Amount} HP.";
            var rules = NewText("Rules", card.rectTransform, rulesText, fill ? 8 : 9, TextAnchor.UpperCenter, new Color(0.88f,0.82f,0.72f));
            Place(rules.gameObject, new Vector2(0,0), new Vector2(1,0), new Vector2(.5f,0), new Vector2(0, fill ? 26 : 36), new Vector2(-14, fill ? 22 : 20));
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
            }
            else if (isMonster && showCurrentHp)
            {
                // Pre-rendered face on the board: overlay live combat stats over the
                // baked-in numbers so damage is readable as the minion trades blows.
                int hp = ci.CurrentHealth;
                BuildStatChip(card.rectTransform, ci.Data.Attack.ToString(), left: true,  new Color(1f,0.45f,0.35f));
                BuildStatChip(card.rectTransform, hp.ToString(), left: false,
                    hp < ci.Data.Health ? new Color(1f,0.72f,0.25f) : new Color(0.48f,0.92f,0.58f));
            }

            if (interactive)
            {
                if (exhausted) card.color = Color.Lerp(card.color, Color.black, 0.42f);
                var btn = card.gameObject.AddComponent<Button>();
                btn.targetGraphic = card;
                var cb = onClick;
                btn.onClick.AddListener(() => cb());
            }
        }

        // A small stat badge pinned to a bottom corner of a card, used to show live
        // ATK / current HP over a pre-rendered card face.
        void BuildStatChip(Transform card, string value, bool left, Color textColor)
        {
            var chip = NewImage("StatChip", card, new Color(0.02f,0.015f,0.02f,0.9f));
            Vector2 corner = left ? new Vector2(0,0) : new Vector2(1,0);
            Place(chip.gameObject, corner, corner, corner, new Vector2(left ? 4 : -4, 4), new Vector2(24,20));
            AddGoldOutline(chip, 0.6f, ColGold);
            var t = NewText("V", chip.rectTransform, value, 13, TextAnchor.MiddleCenter, textColor);
            Stretch(t.rectTransform); t.fontStyle = FontStyle.Bold;
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

        Sprite LoadSprite(string resourceName) => LoadSpriteAt("CardQuest/" + resourceName);

        Sprite LoadSpriteAt(string resourcePath)
        {
            var tex = Resources.Load<Texture2D>(resourcePath);
            if (tex == null) return null;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }

        // Lazily load and cache a card's full pre-rendered face, if it declares one.
        readonly Dictionary<string, Sprite> fullArtCache = new Dictionary<string, Sprite>();
        Sprite FullArtFor(CardData d)
        {
            if (string.IsNullOrEmpty(d.Art)) return null;
            if (!fullArtCache.TryGetValue(d.Art, out var sprite))
                fullArtCache[d.Art] = sprite = LoadSpriteAt(d.Art);
            return sprite;
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

        // Legacy uGUI Text has no tracking property; attach a mesh effect to add per-letter spacing (in pixels).
        static void AddLetterSpacing(Text t, float pixels)
        {
            t.gameObject.AddComponent<LetterSpacing>().Spacing = pixels;
        }
    }

    /// <summary>Adds uniform per-character spacing (tracking, in pixels) to a legacy uGUI Text.</summary>
    [DisallowMultipleComponent]
    public class LetterSpacing : BaseMeshEffect
    {
        public float Spacing;

        protected LetterSpacing() { }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || Spacing == 0f || vh.currentVertCount == 0) return;
            var text = GetComponent<Text>();
            if (text == null) return;

            var verts = new List<UIVertex>();
            vh.GetUIVertexStream(verts);

            string[] lines = text.text.Split('\n');
            float align;
            switch (text.alignment)
            {
                case TextAnchor.UpperCenter:
                case TextAnchor.MiddleCenter:
                case TextAnchor.LowerCenter: align = 0.5f; break;
                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:  align = 1f;   break;
                default:                     align = 0f;   break;
            }

            // Some Unity versions emit quads for whitespace, some don't — detect which.
            int visibleChars = 0;
            foreach (var ln in lines)
                foreach (char c in ln) if (!char.IsWhiteSpace(c)) visibleChars++;
            bool whitespaceHasVerts = verts.Count / 6 > visibleChars;

            int glyph = 0;
            foreach (var line in lines)
            {
                int len = line.Length;
                float lineShift = (len - 1) * Spacing * align;
                for (int ci = 0; ci < len; ci++)
                {
                    if (char.IsWhiteSpace(line[ci]) && !whitespaceHasVerts) continue;
                    int b = glyph * 6;
                    if (b + 5 >= verts.Count) { vh.Clear(); vh.AddUIVertexTriangleStream(verts); return; }
                    Vector3 shift = Vector3.right * (Spacing * ci - lineShift);
                    for (int k = 0; k < 6; k++) { var v = verts[b + k]; v.position += shift; verts[b + k] = v; }
                    glyph++;
                }
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(verts);
        }
    }
}
