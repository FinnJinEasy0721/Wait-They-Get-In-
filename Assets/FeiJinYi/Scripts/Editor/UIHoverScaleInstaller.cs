#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class UIHoverScaleInstaller
{
    private const string Root = "Tools/UI Hover Scale/";

    // ① 当前打开的场景里所有 Button
    [MenuItem(Root + "① 添加到当前场景所有 Button", false, 1)]
    public static void AddToAllButtonsInScene()
    {
#if UNITY_2022_2_OR_NEWER
        var buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        var buttons = Object.FindObjectsOfType<Button>(true);
#endif
        int n = Attach(buttons.Select(b => b.gameObject));
        Debug.Log($"[UIHoverScale] 已为 {n} 个按钮添加悬停缩放组件。");
    }

    // ② 选中对象及其所有子物体里的 Button
    [MenuItem(Root + "② 添加到选中对象及子物体中的 Button", false, 2)]
    public static void AddToSelection()
    {
        var targets = new List<GameObject>();
        foreach (var go in Selection.gameObjects)
            targets.AddRange(go.GetComponentsInChildren<Button>(true).Select(b => b.gameObject));

        if (targets.Count == 0)
        {
            Debug.LogWarning("[UIHoverScale] 选中对象里没有 Button。");
            return;
        }
        int n = Attach(targets);
        Debug.Log($"[UIHoverScale] 已为 {n} 个按钮添加悬停缩放组件。");
    }

    // ③ 直接改 Prefab 资源（推荐：按钮都是同一个/几个 Prefab 时用这个）
    [MenuItem(Root + "③ 添加到选中的 Prefab 资源（或文件夹）", false, 3)]
    public static void AddToSelectedPrefabs()
    {
        var paths = CollectPrefabPaths();
        if (paths.Count == 0)
        {
            Debug.LogWarning("[UIHoverScale] 请在 Project 窗口选中 Prefab 或文件夹。");
            return;
        }

        int total = 0;
        for (int i = 0; i < paths.Count; i++)
        {
            EditorUtility.DisplayProgressBar("添加 UIHoverScale", paths[i], (float)i / paths.Count);
            try
            {
                var root = PrefabUtility.LoadPrefabContents(paths[i]);
                int n = 0;
                foreach (var btn in root.GetComponentsInChildren<Button>(true))
                {
                    // 跳过嵌套 Prefab 内部的按钮（改了也存不进去）
                    if (PrefabUtility.IsPartOfPrefabInstance(btn)) continue;
                    if (btn.GetComponent<UIHoverScale>() != null) continue;

                    btn.gameObject.AddComponent<UIHoverScale>();
                    n++;
                }

                if (n > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, paths[i]);
                    total += n;
                }
                PrefabUtility.UnloadPrefabContents(root);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UIHoverScale] 处理 {paths[i]} 失败: {e.Message}");
                PrefabUtility.UnloadPrefabContents(PrefabUtility.LoadPrefabContents(paths[i]));
            }
        }

        EditorUtility.ClearProgressBar();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[UIHoverScale] 共处理 {paths.Count} 个 Prefab，为 {total} 个按钮添加了组件。");
    }

    // ---------------- 内部工具 ----------------

    private static int Attach(IEnumerable<GameObject> buttons)
    {
        int count = 0;
        var dirtyScenes = new HashSet<UnityEngine.SceneManagement.Scene>();

        Undo.SetCurrentGroupName("Add UIHoverScale");
        int group = Undo.GetCurrentGroup();

        foreach (var go in buttons)
        {
            if (go == null) continue;
            if (go.GetComponent<UIHoverScale>() != null) continue; // 已存在，跳过

            if (Application.isPlaying)
            {
                go.AddComponent<UIHoverScale>();
            }
            else
            {
                Undo.AddComponent<UIHoverScale>(go);
                if (go.scene.IsValid()) dirtyScenes.Add(go.scene);
            }
            count++;
        }

        foreach (var s in dirtyScenes) EditorSceneManager.MarkSceneDirty(s);
        Undo.CollapseUndoOperations(group);
        return count;
    }

    private static List<string> CollectPrefabPaths()
    {
        var paths = new List<string>();
        foreach (var obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) continue;

            if (AssetDatabase.IsValidFolder(path))
            {
                paths.AddRange(AssetDatabase.FindAssets("t:Prefab", new[] { path })
                                           .Select(AssetDatabase.GUIDToAssetPath));
            }
            else if (path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(path);
            }
        }
        return paths.Distinct().ToList();
    }
}
#endif