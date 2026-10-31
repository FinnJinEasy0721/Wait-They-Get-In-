// 语料管理器：书籍注册表与当前选中状态管理，书籍数据由 GameDataManager 构建后拉取（本类不做加载）。
using System.Collections.Generic;
using UnityEngine;

namespace SecurityGuardSimulator
{
    /// <summary>
    /// 语料管理器 - 管理所有书籍/语料
    /// 书籍数据来源：GameDataManager 从策划JSON配置构建，本类只做注册表与选中状态管理
    /// </summary>
    public class BookManager : MonoBehaviour
    {
        public static BookManager Instance { get; private set; }

        private readonly List<Book> books = new List<Book>();
        private Book currentSelectedBook;
        private bool isInitialized;

        /// <summary>书籍加载完成事件（首次 GetAllBooks 时触发）</summary>
        public event System.Action OnBooksLoaded;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// 获取所有书籍（首次访问时从GameDataManager拉取并初始化运行时状态）
        /// </summary>
        public List<Book> GetAllBooks()
        {
            EnsureInitialized();
            return books;
        }

        /// <summary>从GameDataManager拉取书籍并重置运行时状态（重新选书时调用）</summary>
        public void InitializeBooks()
        {
            if (GameDataManager.Instance == null)
            {
                Debug.LogError("[BookManager] 场景中缺少 GameDataManager，无法加载书籍数据");
                return;
            }

            books.Clear();
            books.AddRange(GameDataManager.Instance.Books);
            foreach (var book in books)
            {
                book.Initialize();
            }
            currentSelectedBook = null;
            isInitialized = true;

            Debug.Log($"[BookManager] 已加载 {books.Count} 本书籍");
            OnBooksLoaded?.Invoke();
        }

        /// <summary>重置所有书籍状态（用于重新选书）</summary>
        public void ResetAllBooks()
        {
            foreach (var book in books)
            {
                book.Initialize();
            }
            currentSelectedBook = null;
        }

        /// <summary>选中书籍（由流程控制器在点击书本时调用）</summary>
        public void SelectBook(Book book)
        {
            currentSelectedBook = book;
            Debug.Log($"[BookManager] 当前选择书籍: {book.DisplayName}");
        }

        public Book GetCurrentSelectedBook()
        {
            return currentSelectedBook;
        }

        public Book GetBookById(string id)
        {
            return books.Find(b => b.ID == id);
        }

        private void EnsureInitialized()
        {
            if (!isInitialized)
            {
                InitializeBooks();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
