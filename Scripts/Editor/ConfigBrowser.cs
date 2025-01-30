using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class ConfigBrowser : EditorWindow
{
    private const string DefaultPath = "Assets/_MainProject/ScriptableObjects";
    private Dictionary<string, FolderNode> _folderHierarchy;
    private string _currentFolder;
    private List<ScriptableObject> _scriptableObjects;
    private Dictionary<ScriptableObject, Editor> _editorCache;
    private Dictionary<ScriptableObject, bool> _objectFoldoutStates;
    private Dictionary<string, bool> _folderFoldoutStates;
    private Vector2 _scrollPosition;

    private Vector2 _leftScrollPosition;
    private float _leftPanelWidth = 200f;
    private const float MinLeftPanelWidth = 200f;
    private const float MaxLeftPanelWidth = 400f;

    private bool _isResizing = false;
    private Rect _resizeHandleRect;

    private string _searchQuery = "";

    [MenuItem("Tools/Config Browser")]
    public static void ShowWindow()
    {
        var window = GetWindow<ConfigBrowser>("Config Browser");
        window.minSize = new Vector2(600, 400);
    }

    private void OnEnable()
    {
        _editorCache = new Dictionary<ScriptableObject, Editor>();
        _objectFoldoutStates = new Dictionary<ScriptableObject, bool>();
        _folderFoldoutStates = new Dictionary<string, bool>();
        LoadFolderHierarchy();
    }

    private void OnDisable()
    {
        foreach (var editor in _editorCache.Values)
        {
            DestroyImmediate(editor);
        }
        _editorCache.Clear();
        _objectFoldoutStates.Clear();
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();

        // Левое меню (папки) с поиском и прокруткой
        EditorGUILayout.BeginVertical(GUILayout.Width(_leftPanelWidth));
        EditorGUILayout.LabelField("Folders", EditorStyles.boldLabel);

        // Поле ввода для поиска с иконкой
        DrawSearchFieldWithIcon();
        EditorGUILayout.Space(10); // Отступ после поля ввода

        // Прокрутка для списка папок
        _leftScrollPosition = EditorGUILayout.BeginScrollView(_leftScrollPosition);

        // Фильтрация папок
        if (string.IsNullOrEmpty(_searchQuery))
        {
            // Отображаем все папки
            DrawFolderHierarchy(_folderHierarchy, "");
        }
        else
        {
            // Отображаем только папки, которые соответствуют поисковому запросу
            DrawFilteredFolderHierarchy(_folderHierarchy, "", _searchQuery.ToLower());
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        // Граница для ресайза
        DrawResizeHandle();

        // Правое меню (объекты)
        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField($"Browsing Path: {_currentFolder ?? "None"}", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

        if (!string.IsNullOrEmpty(_currentFolder))
        {
            if (_scriptableObjects != null && _scriptableObjects.Count > 0)
            {
                foreach (var so in _scriptableObjects)
                {
                    DrawScriptableObjectEditor(so);
                }
            }
            else
            {
                EditorGUILayout.LabelField("No ScriptableObjects found.", EditorStyles.centeredGreyMiniLabel);
            }
        }
        else
        {
            EditorGUILayout.LabelField("Select a folder to browse.", EditorStyles.centeredGreyMiniLabel);
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawSearchFieldWithIcon()
    {
        EditorGUILayout.BeginHorizontal();

        // Отображаем иконку лупы
        GUIContent searchIcon = EditorGUIUtility.IconContent("Search Icon");
        GUILayout.Label(searchIcon, GUILayout.Width(20), GUILayout.Height(20));

        // Поле ввода для поиска
        _searchQuery = EditorGUILayout.TextField(_searchQuery, GUILayout.Width(_leftPanelWidth - 40));

        EditorGUILayout.EndHorizontal();
    }

    private void LoadFolderHierarchy()
    {
        _folderHierarchy = new Dictionary<string, FolderNode>();

        if (!Directory.Exists(DefaultPath))
        {
            Debug.LogWarning($"Path '{DefaultPath}' does not exist. Please create it and add ScriptableObjects.");
            return;
        }

        string[] allFolders = Directory.GetDirectories(DefaultPath, "*", SearchOption.AllDirectories);

        foreach (var folder in allFolders)
        {
            string relativePath = folder.Replace("\\", "/").Replace(DefaultPath, "").Trim('/');
            string[] parts = relativePath.Split('/');
            AddToHierarchy(_folderHierarchy, parts);
        }
    }

    private void AddToHierarchy(Dictionary<string, FolderNode> hierarchy, string[] parts)
    {
        var currentLevel = hierarchy;
        for (int i = 0; i < parts.Length; i++)
        {
            if (!currentLevel.ContainsKey(parts[i]))
            {
                currentLevel[parts[i]] = new FolderNode(parts[i]);
            }

            if (i == parts.Length - 1)
                return;

            currentLevel = currentLevel[parts[i]].SubFolders;
        }
    }

    private void DrawFolderHierarchy(Dictionary<string, FolderNode> hierarchy, string parentPath, int depth = 0)
    {
        foreach (var entry in hierarchy)
        {
            string currentPath = string.IsNullOrEmpty(parentPath) ? entry.Key : $"{parentPath}/{entry.Key}";

            if (!_folderFoldoutStates.ContainsKey(currentPath))
            {
                _folderFoldoutStates[currentPath] = false;
            }

            EditorGUILayout.BeginHorizontal();

            GUILayout.Space(15 + depth * 15);

            if (entry.Value.SubFolders.Count > 0)
            {
                bool previousState = _folderFoldoutStates[currentPath];
                _folderFoldoutStates[currentPath] = EditorGUILayout.Foldout(previousState, $"{entry.Key}");

                if (_folderFoldoutStates[currentPath] && !previousState)
                {
                    _currentFolder = $"{DefaultPath}/{currentPath}";
                    LoadScriptableObjects();
                }
            }
            else
            {
                GUIContent folderIcon = EditorGUIUtility.IconContent("Folder Icon");
                if (GUILayout.Button(folderIcon, GUIStyle.none, GUILayout.Width(20), GUILayout.Height(20)))
                {
                    _currentFolder = $"{DefaultPath}/{currentPath}";
                    LoadScriptableObjects();
                }
            }

            string folderName = entry.Value.SubFolders.Count == 0 ? entry.Key : "";
            if (GUILayout.Button(folderName, EditorStyles.label, GUILayout.ExpandWidth(false)))
            {
                _currentFolder = $"{DefaultPath}/{currentPath}";
                LoadScriptableObjects();
            }

            EditorGUILayout.EndHorizontal();

            if (entry.Value.SubFolders.Count > 0 && _folderFoldoutStates[currentPath])
            {
                DrawFolderHierarchy(entry.Value.SubFolders, currentPath, depth + 1);
            }
        }
    }

    private void DrawFilteredFolderHierarchy(Dictionary<string, FolderNode> hierarchy, string parentPath, string searchQuery)
    {
        foreach (var entry in hierarchy)
        {
            string currentPath = string.IsNullOrEmpty(parentPath) ? entry.Key : $"{parentPath}/{entry.Key}";

            // Проверяем, содержит ли имя папки поисковую строку
            if (entry.Key.ToLower().Contains(searchQuery))
            {
                EditorGUILayout.BeginHorizontal();

                // Отображаем иконку папки
                GUIContent folderIcon = EditorGUIUtility.IconContent("Folder Icon");
                GUILayout.Label(folderIcon, GUILayout.Width(20), GUILayout.Height(20));

                // Отображаем название папки
                if (GUILayout.Button(entry.Key, EditorStyles.label, GUILayout.ExpandWidth(false)))
                {
                    _currentFolder = $"{DefaultPath}/{currentPath}";
                    LoadScriptableObjects();
                }

                EditorGUILayout.EndHorizontal();
            }

            // Рекурсивный вызов для подпапок
            if (entry.Value.SubFolders.Count > 0)
            {
                DrawFilteredFolderHierarchy(entry.Value.SubFolders, currentPath, searchQuery);
            }
        }
    }

    private void LoadScriptableObjects()
    {
        foreach (var editor in _editorCache.Values)
        {
            DestroyImmediate(editor);
        }
        _editorCache.Clear();
        _objectFoldoutStates.Clear();

        _scriptableObjects = new List<ScriptableObject>();

        if (string.IsNullOrEmpty(_currentFolder) || !Directory.Exists(_currentFolder))
        {
            Debug.LogWarning("Selected folder does not exist or is empty.");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { _currentFolder });

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            ScriptableObject so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (so != null)
            {
                _scriptableObjects.Add(so);
                _editorCache[so] = Editor.CreateEditor(so);
                _objectFoldoutStates[so] = false;
            }
        }
    }

    private void DrawScriptableObjectEditor(ScriptableObject so)
    {
        EditorGUILayout.BeginHorizontal();
        GUIContent objectIcon = EditorGUIUtility.ObjectContent(so, so.GetType());
        GUILayout.Label(objectIcon.image, GUILayout.Width(20), GUILayout.Height(20));
        _objectFoldoutStates[so] = EditorGUILayout.Foldout(_objectFoldoutStates[so], so.name, true);
        EditorGUILayout.EndHorizontal();

        if (_objectFoldoutStates[so] && _editorCache.TryGetValue(so, out Editor editor))
        {
            EditorGUILayout.BeginVertical("box");
            Undo.RecordObject(so, "Modify ScriptableObject");
            editor.OnInspectorGUI();
            if (GUI.changed)
            {
                EditorUtility.SetDirty(so);
            }
            EditorGUILayout.EndVertical();
        }
    }

    private void DrawResizeHandle()
    {
        const float resizeHandleWidth = 2.5f;

        _resizeHandleRect = new Rect(_leftPanelWidth + 10, 0, resizeHandleWidth, position.height);
        EditorGUIUtility.AddCursorRect(_resizeHandleRect, MouseCursor.ResizeHorizontal);

        if (Event.current.type == EventType.MouseDown && _resizeHandleRect.Contains(Event.current.mousePosition))
        {
            _isResizing = true;
        }
        else if (Event.current.type == EventType.MouseUp)
        {
            _isResizing = false;
        }

        if (_isResizing && Event.current.type == EventType.MouseDrag)
        {
            _leftPanelWidth = Mathf.Clamp(_leftPanelWidth + Event.current.delta.x, MinLeftPanelWidth, MaxLeftPanelWidth);
            Repaint();
        }

        EditorGUI.DrawRect(_resizeHandleRect, Color.black);
    }

    private class FolderNode
    {
        public string Name { get; }
        public Dictionary<string, FolderNode> SubFolders { get; }

        public FolderNode(string name)
        {
            Name = name;
            SubFolders = new Dictionary<string, FolderNode>();
        }
    }
}