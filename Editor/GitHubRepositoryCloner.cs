using UnityEditor;
using UnityEngine;
using System.Net.Http;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

#if UNITY_EDITOR
namespace UnityEssentials
{
    public class GitHubRepositoryCloner : EditorWindow
    {
        private const string TokenKey = "GitToken";
        private static string s_token;
        private static List<string> s_repositoryNames = new();
        private static List<bool> s_repositorySelected = new();
        private bool _isFetching = false;

        private Vector2 _scrollPosition;

        private bool _createAssemblyDefinition = false;
        private bool _useTemplateFiles = false;
        private string _templateFolder = "Assets/_Templates"; // Template files folder

        [MenuItem("Tools/GitHub Repository Cloner")]
        public static void ShowWindow()
        {
            var window = GetWindow<GitHubRepositoryCloner>();
            window.minSize = new Vector2(400, 300);
        }

        public void OnGUI()
        {
            if (string.IsNullOrEmpty(s_token))
                s_token = EditorPrefs.GetString(TokenKey, "");

            if (string.IsNullOrEmpty(s_token))
            {
                GUILayout.Label("Enter your GitHub token:");
                s_token = EditorGUILayout.TextField(s_token);

                if (GUILayout.Button("Save Token"))
                {
                    EditorPrefs.SetString(TokenKey, s_token);
                    FetchRepos();
                }
                return; // Early return since no repositories to show yet
            }
            else
            {
                if (GUILayout.Button("Change Token"))
                {
                    s_token = "";
                    EditorPrefs.DeleteKey(TokenKey);
                    s_repositoryNames.Clear();
                    s_repositorySelected.Clear();
                    return;
                }

                if (s_repositoryNames.Count == 0 && !_isFetching)
                {
                    if (GUILayout.Button("Fetch Repositories"))
                        FetchRepos();

                    return; // Early return because no repositories to show yet
                }
                else if (s_repositoryNames.Count > 0)
                {
                    GUILayout.Label("Select repositories to clone:");

                    // Calculate space for the scroll view dynamically
                    // Get the total available height of the window's client area
                    float totalHeight = position.height;

                    // Calculate fixed heights for elements above and below the scroll view
                    float headerHeight = EditorGUIUtility.singleLineHeight * 3 + 20; // estimate labels & toggles
                    float toggleTemplateHeight = EditorGUIUtility.singleLineHeight + 6; // checkbox toggle
                    float buttonHeight = 30 + 10; // button + padding
                    float padding = 20;

                    // Calculate remaining height for scroll view
                    float scrollHeight = totalHeight - (headerHeight + toggleTemplateHeight + buttonHeight + padding);
                    scrollHeight = Mathf.Max(scrollHeight, 100); // minimum height

                    using (new GUILayout.VerticalScope("box"))
                    {
                        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(scrollHeight));
                        for (int i = 0; i < s_repositoryNames.Count; i++)
                        {
                            if (s_repositorySelected.Count < s_repositoryNames.Count)
                                s_repositorySelected.Add(false);

                            s_repositorySelected[i] = EditorGUILayout.ToggleLeft(s_repositoryNames[i], s_repositorySelected[i]);
                        }
                        EditorGUILayout.EndScrollView();
                    }

                    GUILayout.Space(10);

                    _createAssemblyDefinition = EditorGUILayout.ToggleLeft("Create Assembly Definitions into cloned repositories", _createAssemblyDefinition);
                    _useTemplateFiles = EditorGUILayout.ToggleLeft("Copy Template Files into cloned repositories", _useTemplateFiles);

                    GUILayout.FlexibleSpace(); // push button to bottom

                    if (GUILayout.Button("Clone Selected Repositories", GUILayout.Height(30)))
                    {
                        // Clone directly into Assets folder
                        string targetPath = Application.dataPath;
                        CloneSelectedRepos(targetPath);
                    }
                }
            }
        }

        private async void FetchRepos()
        {
            _isFetching = true;
            Repaint();

            if (string.IsNullOrEmpty(s_token))
            {
                Debug.LogWarning("[Git] Token is empty.");
                _isFetching = false;
                Repaint();
                return;
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("UnityGitClient");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", s_token);

            var response = await client.GetAsync("https://api.github.com/user/repos?per_page=100");

            if (!response.IsSuccessStatusCode)
            {
                Debug.LogError("[Git] Invalid token or failed to fetch repos. Clearing token.");
                EditorPrefs.DeleteKey(TokenKey);
                s_token = "";
                s_repositoryNames.Clear();
                s_repositorySelected.Clear();
                _isFetching = false;
                Repaint();
                return;
            }

            string json = await response.Content.ReadAsStringAsync();
            s_repositoryNames = ExtractRepoNames(json);

            // Reset selection list
            s_repositorySelected = new List<bool>(new bool[s_repositoryNames.Count]);

            _isFetching = false;
            Repaint();
        }

        private List<string> ExtractRepoNames(string json)
        {
            List<string> names = new();

            int index = 0;
            while ((index = json.IndexOf("\"full_name\":\"", index)) != -1)
            {
                index += "\"full_name\":\"".Length;
                int end = json.IndexOf("\"", index);
                string name = json.Substring(index, end - index);
                names.Add(name);
                index = end;
            }

            return names;
        }

        private async void CloneSelectedRepos(string targetFolder)
        {
            List<string> selectedRepos = new();

            for (int i = 0; i < s_repositoryNames.Count; i++)
                if (s_repositorySelected[i])
                    selectedRepos.Add(s_repositoryNames[i]);

            if (selectedRepos.Count == 0)
            {
                EditorUtility.DisplayDialog("No selection", "Please select at least one repository to clone.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Confirm Clone", $"Clone {selectedRepos.Count} repositories into:\nAssets/ ?", "Yes", "Cancel"))
                return;

            foreach (var repoFullName in selectedRepos)
            {
                string cloneUrl = $"https://github.com/{repoFullName}.git";
                string repoFolderName = repoFullName.Split('/')[1]; // repository name after owner/
                string localPath = Path.Combine(targetFolder, repoFolderName);

                if (Directory.Exists(localPath))
                {
                    Debug.LogWarning($"Repository folder already exists, skipping: {localPath}");
                    continue;
                }

                EditorUtility.DisplayProgressBar("Cloning Repositories", $"Cloning {repoFullName}...", 0);

                bool success = await CloneGitRepo(cloneUrl, localPath);
                if (success)
                {
                    Debug.Log($"Cloned {repoFullName} into {localPath}");

                    if (_createAssemblyDefinition)
                    {
                        CreateAssemblyDefinition(localPath, repoFolderName);
                        Debug.Log($"Created Assembly Definition at {localPath}");
                    }

                    if (_useTemplateFiles)
                    {
                        CopyTemplateFiles(_templateFolder, localPath);
                        Debug.Log($"Copied template files into {localPath}");
                    }
                }
                else Debug.LogError($"Failed to clone repository: {repoFullName}");

                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Clone Complete", "Selected repositories cloned successfully.", "OK");
        }

        private Task<bool> CloneGitRepo(string cloneUrl, string localPath)
        {
            return Task.Run(() =>
            {
                try
                {
                    ProcessStartInfo psi = new()
                    {
                        FileName = "git",
                        Arguments = $"clone {cloneUrl} \"{localPath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };

                    using Process process = Process.Start(psi);
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                        return true;
                    else
                    {
                        string error = process.StandardError.ReadToEnd();
                        Debug.LogError($"Git clone error: {error}");
                        return false;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Exception cloning repo: {ex.Message}");
                    return false;
                }
            });
        }

        private void CreateAssemblyDefinition(string localPath, string fileName)
        {
            var asmdefData = new AssemblyDefinitionData
            {
                name = fileName
            };

            string asmdefPath = Path.Combine(localPath, $"{fileName}.asmdef");
            string json = JsonUtility.ToJson(asmdefData, true);
            File.WriteAllText(asmdefPath, json);
        }

        private void CopyTemplateFiles(string sourceFolder, string destinationFolder)
        {
            if (!Directory.Exists(sourceFolder))
            {
                Debug.LogWarning($"Template folder not found: {sourceFolder}");
                return;
            }

            foreach (string dirPath in Directory.GetDirectories(sourceFolder, "*", SearchOption.AllDirectories))
            {
                string newDirPath = dirPath.Replace(sourceFolder, destinationFolder);
                if (!Directory.Exists(newDirPath))
                    Directory.CreateDirectory(newDirPath);
            }

            foreach (string filePath in Directory.GetFiles(sourceFolder, "*.*", SearchOption.AllDirectories))
            {
                // Ignore .meta files
                if (filePath.EndsWith(".meta"))
                    continue;

                string newFilePath = filePath.Replace(sourceFolder, destinationFolder);
                File.Copy(filePath, newFilePath, overwrite: true);
            }
        }

        [System.Serializable]
        public class AssemblyDefinitionData
        {
            public string name;
            public bool allowUnsafeCode = false;
            public bool noEngineReferences = false;
            public bool overrideReferences = false;
            public bool autoReferenced = true;
            public string rootNamespace = "UnityEssentials";
            public string[] references = new string[] { };
            public string[] precompiledReferences = new string[] { };
            public string[] includePlatforms = new string[] { };
            public string[] excludePlatforms = new string[] { };
            public string[] defineConstraints = new string[] { };
            public object[] versionDefines = new object[] { };
        }
    }
}
#endif
