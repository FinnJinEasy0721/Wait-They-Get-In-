// 选书流程控制器：编排选书→规则面板→确认装配，全部装配完成或玩家点「开始上班」提前结束后切换值班场景。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 选书流程控制器
    /// 编排：进入场景 → 展示书籍 → 玩家点击选书 → 打开规则书面板 → 选规则/单条刷新 → 确认提交
    ///       → 逐本装配 → 全部书籍确认或玩家点「开始上班」后进入下一阶段（可不装配任何书）
    ///
    /// 交互决策集中在控制器：书本点击只上报，由控制器统一维护"当前选中"与面板开关，
    /// 避免多本书本各自为政导致的重复开面板问题
    /// </summary>
    public class SelectionFlowController : MonoBehaviour
    {
        public static SelectionFlowController Instance { get; private set; }

        /// <summary>流程状态（对外公开，供其他系统查询阶段）</summary>
        public enum FlowState
        {
            Intro,            // 开场
            SelectingBook,    // 选书阶段
            Completed         // 选书阶段结束（全部装配或点击「开始上班」），进入下一阶段
        }

        [Header("场景引用")]
        [SerializeField] private RectTransform bookSpawnPoint;       // 书籍生成点（水平布局）
        [SerializeField] private BookGameObject bookPrefab;          // 书本Prefab
        [SerializeField] private RuleBookPanel ruleBookPanelPrefab;   // 规则书面板Prefab
        [SerializeField] private RectTransform panelParent;          // 面板父节点（留空时挂到画布根）

        [Header("UI文本")]
        [SerializeField] private Text flowTitleText;                 // 流程标题/进度提示

        [Header("脑容量图标（右上角）")]
        [Tooltip("脑容量槽位图标（左起），每消耗一点容量对应槽位图标变灰")]
        [SerializeField] private Image[] capacityIcons = new Image[3];
        [SerializeField] private Sprite brainSprite;                // 未消耗槽位：彩色脑
        [SerializeField] private Sprite greyBrainSprite;             // 已消耗槽位：灰色脑

        [Header("开始上班")]
        [Tooltip("开始上班按钮：可不装配任何书籍直接进入值班（留空则场景中无此入口）")]
        [SerializeField] private Button startWorkButton;

        [Header("完成后切换场景")]
        [Tooltip("全部书籍装配完成后是否自动切换到下一场景（正式选书场景开启，测试场景可关闭）")]
        [SerializeField] private bool switchSceneOnComplete = false;
        [Tooltip("切换的目标场景名（需已加入Build Settings）")]
        [SerializeField] private string nextSceneName = "MainGameScene";

        private FlowState currentState = FlowState.Intro;
        private readonly List<BookGameObject> spawnedBooks = new List<BookGameObject>();
        private RuleBookPanel panelInstance;
        private BookGameObject selectedBook;

        public FlowState CurrentState => currentState;
        public IReadOnlyList<BookGameObject> SpawnedBooks => spawnedBooks;
        public RuleBookPanel Panel => panelInstance;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // 数据与管理器兜底：任意场景只要有本控制器即可运行（方便其他成员集成）
            if (GameDataManager.Instance == null)
            {
                new GameObject("GameDataManager").AddComponent<GameDataManager>();
            }
            if (BookManager.Instance == null)
            {
                new GameObject("BookManager").AddComponent<BookManager>();
            }

            // 开始上班按钮接线（运行时绑定，场景未配置按钮则无此入口）
            if (startWorkButton != null)
            {
                startWorkButton.onClick.RemoveAllListeners();
                startWorkButton.onClick.AddListener(StartWork);
            }
        }

        private void Start()
        {
            StartSelectionFlow();
        }

        /// <summary>
        /// 启动选书流程
        /// </summary>
        private void StartSelectionFlow()
        {
            currentState = FlowState.SelectingBook;
            SpawnBooks();
            UpdateCapacityIcons();
            int day = GameDataManager.Instance != null ? GameDataManager.Instance.CurrentDay : 1;
            SetFlowTitle($"第 {day} 天 · 为新保安装配规则书：点击书本查看其规则，选好后点「开始上班」上岗");
            Debug.Log($"[Flow] 进入选书阶段 - 共 {spawnedBooks.Count} 本书籍待装配");
        }

        /// <summary>
        /// 生成书籍物体：数量取 常量"初始书本数量" 与 配置书籍数 的较小值，
        /// 排布交给 BookSpawnPoint 上的 HorizontalLayoutGroup
        /// </summary>
        private void SpawnBooks()
        {
            var allBooks = BookManager.Instance.GetAllBooks();
            int count = Mathf.Min(GameConstants.InitialBookCount, allBooks.Count);

            if (count == 0)
            {
                Debug.LogError("[Flow] 没有可用的书籍数据：请检查 书籍数据.xlsx / BookConfig.json");
                SetFlowTitle("配置数据缺失：没有可用书籍");
                return;
            }

            // 应用策划配置的间距
            var layout = bookSpawnPoint.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
            {
                layout.spacing = GameConstants.BookSpawnSpacing;
            }

            for (int i = 0; i < count; i++)
            {
                BookGameObject bookObj = Instantiate(bookPrefab, bookSpawnPoint);
                bookObj.Initialize(allBooks[i], this);
                spawnedBooks.Add(bookObj);
            }

            if (count < GameConstants.InitialBookCount)
                Debug.LogWarning($"[Flow] 配置书籍数({allBooks.Count})少于 常量\"初始书本数量\"({GameConstants.InitialBookCount})，实际生成 {count} 本");
        }

        /// <summary>
        /// 书本被点击（由 BookGameObject 转发）
        /// 规则：点击未选中的书 → 选中并打开规则书面板；
        ///       再次点击当前选中的书 → 取消选中并关闭面板
        /// </summary>
        public void OnBookClicked(BookGameObject bookGO)
        {
            if (currentState == FlowState.Completed && !bookGO.BookData.IsConfirmed)
            {
                // 流程已完成（正常不应发生：完成时所有书都已确认）
                return;
            }

            if (selectedBook == bookGO)
            {
                DeselectBook();
                return;
            }

            SelectBook(bookGO);
        }

        /// <summary>选中书本并打开该书的规则书面板</summary>
        private void SelectBook(BookGameObject bookGO)
        {
            // 单选：取消之前的选中
            if (selectedBook != null && selectedBook != bookGO)
            {
                selectedBook.SetSelectedVisual(false);
            }

            selectedBook = bookGO;
            bookGO.SetSelectedVisual(true);
            BookManager.Instance.SelectBook(bookGO.BookData);

            OpenPanelFor(bookGO);
        }

        /// <summary>取消选中当前书并关闭面板（保留该书已确认的装配结果）</summary>
        private void DeselectBook()
        {
            if (selectedBook != null)
            {
                selectedBook.SetSelectedVisual(false);
            }
            selectedBook = null;

            if (panelInstance != null && panelInstance.IsOpen)
            {
                panelInstance.Close();
            }
        }

        /// <summary>
        /// 打开规则书面板（单实例复用，避免重复选书导致面板堆叠）
        /// 脑容量校验：面板内选中规则时，其他书已占用槽位 + 本次选择 不得超过总槽位
        /// </summary>
        private void OpenPanelFor(BookGameObject bookGO)
        {
            panelInstance = GetOrCreatePanel();
            int usedElsewhere = GetUsedSlotsTotal() - bookGO.BookData.UsedSlots;
            panelInstance.Open(bookGO.BookData, usedElsewhere, GameConstants.InitialBrainSlots);
        }

        private RuleBookPanel GetOrCreatePanel()
        {
            if (panelInstance != null) return panelInstance;

            RectTransform parent = panelParent;
            if (parent == null)
            {
                Canvas canvas = bookSpawnPoint != null
                    ? bookSpawnPoint.GetComponentInParent<Canvas>()
                    : FindObjectOfType<Canvas>();
                parent = canvas != null ? canvas.transform as RectTransform : null;
            }

            panelInstance = Instantiate(ruleBookPanelPrefab, parent);
            panelInstance.OnConfirmed += OnPanelConfirmed;
            panelInstance.OnCloseRequested += DeselectBook;
            return panelInstance;
        }

        /// <summary>规则书面板确认回调：装配该书规则（空列表=取消该书装配），检查是否全部完成</summary>
        private void OnPanelConfirmed(Book book, List<Rule> rules)
        {
            // 更新书本来体的装配视觉
            BookGameObject bookGO = spawnedBooks.Find(b => b.BookData == book);
            if (bookGO != null)
            {
                if (rules.Count == 0)
                {
                    bookGO.SetUnconfirmedVisual();
                }
                else
                {
                    var names = new List<string>();
                    foreach (var rule in rules) names.Add(rule.Name);
                    bookGO.SetConfirmedVisual(string.Join("、", names));
                }
            }

            // 关闭面板并取消选中
            DeselectBook();

            UpdateCapacityIcons();

            int confirmedCount = 0;
            foreach (var b in spawnedBooks)
            {
                if (b.BookData.IsConfirmed) confirmedCount++;
            }

            if (confirmedCount >= spawnedBooks.Count && spawnedBooks.Count > 0)
            {
                CompleteFlow("规则书装配完成！新的一天开始，保安上岗值班……");
            }
            else
            {
                SetFlowTitle($"第 {GameDataManager.Instance.CurrentDay} 天 · 规则书装配中：已装配 {confirmedCount}/{spawnedBooks.Count} 本书，继续点击书本或点「开始上班」");
                Debug.Log(rules.Count == 0
                    ? $"[Flow] 书籍 {book.DisplayName} 取消装配（{confirmedCount}/{spawnedBooks.Count}）"
                    : $"[Flow] 书籍 {book.DisplayName} 装配完成（{confirmedCount}/{spawnedBooks.Count}）");
            }
        }

        /// <summary>
        /// 开始上班：提前结束选书阶段（可不装配、装配部分或全部书籍）
        /// 合上打开中的规则书面板，未确认的临时选择不生效，以当前已确认装配进入值班
        /// </summary>
        public void StartWork()
        {
            if (currentState == FlowState.Completed) return;

            DeselectBook();

            int confirmedCount = 0;
            foreach (var b in spawnedBooks)
            {
                if (b.BookData.IsConfirmed) confirmedCount++;
            }
            Debug.Log($"[Flow] 提前开始上班：已装配 {confirmedCount}/{spawnedBooks.Count} 本规则书");

            CompleteFlow("选书结束！新的一天开始，保安上岗值班……");
        }

        /// <summary>选书阶段结束（全部装配完成或点击开始上班），进入下一阶段</summary>
        private void CompleteFlow(string message)
        {
            currentState = FlowState.Completed;
            SetFlowTitle(message);
            Debug.Log("[Flow] 选书阶段结束，进入下一阶段: 值班");

            // 装配结果保留在 GameDataManager（DontDestroyOnLoad）持有的 Book 对象上，
            // 切场景后由主场景读取，无需额外传参
            if (switchSceneOnComplete)
            {
                Debug.Log($"[Flow] 自动切换场景: {nextSceneName}");
                SceneFadeTransition.Switch(nextSceneName);
            }
        }

        /// <summary>当前所有书已装配规则占用的槽位总数</summary>
        public int GetUsedSlotsTotal()
        {
            int total = 0;
            foreach (var bookGO in spawnedBooks)
            {
                total += bookGO.BookData.UsedSlots;
            }
            return total;
        }

        /// <summary>刷新右上角脑容量图标：已消耗槽位变灰，剩余保持彩色</summary>
        private void UpdateCapacityIcons()
        {
            int used = Mathf.Min(GetUsedSlotsTotal(), capacityIcons.Length);
            for (int i = 0; i < capacityIcons.Length; i++)
            {
                if (capacityIcons[i] == null) continue;
                capacityIcons[i].sprite = i < used ? greyBrainSprite : brainSprite;
            }
        }

        private void SetFlowTitle(string text)
        {
            if (flowTitleText != null)
            {
                flowTitleText.text = text;
            }
        }

        /// <summary>清理生成的书籍与面板（场景切换/销毁时）</summary>
        public void Cleanup()
        {
            foreach (var book in spawnedBooks)
            {
                if (book != null)
                    Destroy(book.gameObject);
            }
            spawnedBooks.Clear();

            if (panelInstance != null)
            {
                panelInstance.OnConfirmed -= OnPanelConfirmed;
                panelInstance.OnCloseRequested -= DeselectBook;
                Destroy(panelInstance.gameObject);
                panelInstance = null;
            }
            selectedBook = null;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            Cleanup();
        }
    }
}
