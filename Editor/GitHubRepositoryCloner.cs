#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Net.Http;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

namespace UnityEssentials
{
    /// <summary>
    /// Provides a Unity Editor window for cloning GitHub repositories into a Unity project.
    /// </summary>
    /// <remarks>This tool allows users to authenticate with a GitHub personal access token, fetch a list of
    /// repositories associated with the token, and select repositories to clone directly into the Unity project's
    /// Assets folder. Additional options include creating assembly definition files, package manifests, and copying
    /// template files for the cloned repositories.  To use this tool, navigate to the "Tools" menu in the Unity Editor
    /// and select "GitHub Repository Cloner".</remarks>
    public class GitHubRepositoryCloner : EditorWindow
    {
        private const string TemplateFolder = "Assets/Templates";
        private const string AuthorName = "Unity Essentials";
        private const string ProjectName = "UnityEssentials";
        private const string ExcludeString = "Unity";
        private const string TokenKey = "GitToken";

        private static string s_token;
        private static List<string> s_repositoryNames = new();
        private static List<bool> s_repositorySelected = new();
        private bool _isFetching = false;

        private Vector2 _scrollPosition;

        private bool _createAssemblyDefinition = true;
        private bool _createPackageManifests = true;
        private bool _useTemplateFiles = true;

        private string _tokenPlaceholder = string.Empty;

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

            GUILayout.Space(10);

            if (string.IsNullOrEmpty(s_token))
            {
                GUILayout.Label("Enter your GitHub token:");

                _tokenPlaceholder = EditorGUILayout.TextField(_tokenPlaceholder);

                if (GUILayout.Button("Save Token"))
                {
                    s_token = _tokenPlaceholder;
                    EditorPrefs.SetString(TokenKey, s_token);

                    FetchRepositories();
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
                    FetchRepositories();

                    return; // Early return because no repositories to show yet
                }
                else if (s_repositoryNames.Count > 0)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Select repositories to clone:");
                    if (GUILayout.Button("Refresh", GUILayout.Width(100), GUILayout.Height(18)))
                    {
                        FetchRepositories();
                        GUI.FocusControl(null); // Remove focus from button to avoid accidental double-clicks
                    }
                    GUILayout.EndHorizontal();

                    // Calculate space for the scroll view dynamically
                    float totalHeight = position.height;
                    float headerHeight = EditorGUIUtility.singleLineHeight * 3 + 30;
                    float toggleTemplateHeight = EditorGUIUtility.singleLineHeight + 6;
                    float buttonHeight = 30 + 20;
                    float padding = 20;
                    float scrollHeight = totalHeight - (headerHeight + toggleTemplateHeight + buttonHeight + padding);
                    scrollHeight = Mathf.Max(scrollHeight, 100);

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

                    _createAssemblyDefinition = EditorGUILayout.ToggleLeft("Create Assembly Definitions", _createAssemblyDefinition);
                    _createPackageManifests = EditorGUILayout.ToggleLeft("Create Package Manifests", _createPackageManifests);
                    _useTemplateFiles = EditorGUILayout.ToggleLeft("Copy Template Files", _useTemplateFiles);

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Clone Selected Repositories", GUILayout.Height(30)))
                    {
                        string targetPath = Application.dataPath;
                        CloneSelectedRepositories(targetPath);
                    }

                    GUILayout.Space(10);
                }
            }
        }

        /// <summary>
        /// Fetches the list of repositories for the authenticated GitHub user.
        /// </summary>
        /// <remarks>This method retrieves the repositories associated with the GitHub account linked to
        /// the provided token. If the token is invalid or empty, the method logs a warning or error, clears the token,
        /// and resets the repository lists. The method updates the repository names and selection states upon
        /// successful retrieval.</remarks>
        private async void FetchRepositories()
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
                Debug.LogError("[Git] Invalid token or failed to fetch repositories. Clearing token.");
                EditorPrefs.DeleteKey(TokenKey);
                s_token = "";
                s_repositoryNames.Clear();
                s_repositorySelected.Clear();
                _isFetching = false;
                Repaint();
                return;
            }

            string json = await response.Content.ReadAsStringAsync();
            s_repositoryNames = ExtractRepositoryNames(json);

            // Reset selection list
            s_repositorySelected = new List<bool>(new bool[s_repositoryNames.Count]);

            _isFetching = false;
            Repaint();
        }

        /// <summary>
        /// Extracts a list of repository names from a JSON string.
        /// </summary>
        /// <remarks>This method searches for occurrences of the "full_name" field in the provided JSON
        /// string and extracts their values. The method does not validate the overall structure of the JSON string and
        /// assumes that "full_name" fields are properly formatted.</remarks>
        /// <param name="json">A JSON-formatted string containing repository data. The string must include "full_name" fields for
        /// repositories.</param>
        /// <returns>A list of repository names extracted from the JSON string. If no repository names are found, returns an
        /// empty list.</returns>
        private List<string> ExtractRepositoryNames(string json)
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

        /// <summary>
        /// Clones the selected repositories into the specified target folder.
        /// </summary>
        /// <remarks>This method clones all repositories that are marked as selected. If no repositories
        /// are selected,  the operation is canceled, and a dialog is displayed to inform the user. Before cloning, the
        /// user  is prompted to confirm the operation. If a repository folder already exists in the target location, 
        /// it is skipped, and a warning is logged.  After cloning, additional operations such as renaming license
        /// files, creating assembly definitions,  generating package manifests, and copying template files may be
        /// performed based on the current settings.  The method ensures that all file operations and UI updates are
        /// performed on the main thread.  A progress bar is displayed during the cloning process, and the Unity asset
        /// database is refreshed  upon completion.</remarks>
        /// <param name="targetFolder">The path to the target folder where the repositories will be cloned. This must be a valid directory path.</param>
        private async void CloneSelectedRepositories(string targetFolder)
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

            foreach (var repositoryFullName in selectedRepos)
            {
                string cloneUrl = $"https://{s_token}@github.com/{repositoryFullName}.git"; // Include token in URL
                string repositoryFolderName = repositoryFullName.Split('/')[1];
                string packageName = repositoryFolderName.Replace(ExcludeString, "");
                string localPath = Path.Combine(targetFolder, repositoryFolderName);

                if (Directory.Exists(localPath))
                {
                    Debug.LogWarning($"Repository folder already exists, skipping: {localPath}");
                    continue;
                }

                EditorUtility.DisplayProgressBar("Cloning Repositories", $"Cloning {repositoryFullName}...", 0);

                bool success = await CloneGitRepository(cloneUrl, localPath).ConfigureAwait(true); // Ensure main thread continuation

                if (success)
                {
                    Debug.Log($"Cloned {repositoryFullName} into {localPath}");

                    // Ensure all file operations are on the main thread
                    await Task.Run(() => { }).ConfigureAwait(true); // Explicitly switch context if needed

                    RenameLicenseFile(localPath, repositoryFullName);

                    if (_createAssemblyDefinition)
                        CreateAssemblyDefinition(localPath, packageName);

                    if (_createPackageManifests)
                        CreatePackageManifest(localPath, packageName);

                    if (_useTemplateFiles)
                        CopyTemplateFiles(TemplateFolder, localPath);
                }
                else
                {
                    Debug.LogError($"Failed to clone repository: {repositoryFullName}");
                }

                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Clone Complete", "Selected repositories cloned successfully.", "OK");
        }

        /// <summary>
        /// Clones a Git repository from the specified URL to the specified local path.
        /// </summary>
        /// <remarks>This method uses the Git command-line tool to perform the clone operation. Ensure
        /// that Git is installed and available in the system's PATH environment variable. If the operation fails, error
        /// details will be logged.</remarks>
        /// <param name="cloneUrl">The URL of the Git repository to clone. This must be a valid Git repository URL.</param>
        /// <param name="localPath">The local file system path where the repository will be cloned. This path must be writable.</param>
        /// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the
        /// repository was successfully cloned; otherwise, <see langword="false"/>.</returns>
        private Task<bool> CloneGitRepository(string cloneUrl, string localPath) =>
            Task.Run(() =>
            {
                try
                {
                    // Step 1: Clone the repository
                    ProcessStartInfo psi = new()
                    {
                        FileName = "git",
                        Arguments = $"clone {cloneUrl} \"{localPath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };

                    using (Process process = Process.Start(psi))
                    {
                        process.WaitForExit();

                        if (process.ExitCode != 0)
                        {
                            string error = process.StandardError.ReadToEnd();
                            Debug.LogError($"Git clone error: {error}");
                            return false;
                        }
                    }

                    // Step 2: Check for LFS usage
                    string gitattributesPath = Path.Combine(localPath, ".gitattributes");
                    if (File.Exists(gitattributesPath))
                    {
                        string[] lines = File.ReadAllLines(gitattributesPath);
                        bool usesLfs = false;
                        foreach (var line in lines)
                            if (line.Contains("filter=lfs"))
                            {
                                usesLfs = true;
                                break;
                            }

                        // Step 3: If LFS is used, run 'git lfs pull'
                        if (usesLfs)
                        {
                            ProcessStartInfo lfsPsi = new()
                            {
                                FileName = "git",
                                Arguments = "lfs pull",
                                WorkingDirectory = localPath,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                            };

                            using (Process lfsProcess = Process.Start(lfsPsi))
                            {
                                lfsProcess.WaitForExit();

                                if (lfsProcess.ExitCode != 0)
                                {
                                    string lfsError = lfsProcess.StandardError.ReadToEnd();
                                    if (lfsError.Contains("git-lfs: command not found") || lfsError.Contains("'lfs' is not a git command"))
                                        Debug.LogWarning("Git LFS is required but not installed. LFS files were not pulled.");
                                    else
                                    {
                                        Debug.LogError($"Git LFS pull error: {lfsError}");
                                        return false;
                                    }
                                }
                            }
                        }
                    }

                    return true;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Exception cloning repository: {ex.Message}");
                    return false;
                }
            });

        /// <summary>
        /// Renames the LICENSE file in the specified local repository path to LICENSE.md if it exists and does not
        /// already have a file extension.
        /// </summary>
        /// <remarks>This method checks for the existence of a LICENSE file in the specified directory. If
        /// the file exists and does not have an extension, it renames the file to LICENSE.md. Any errors encountered
        /// during the renaming process are logged.</remarks>
        /// <param name="localPath">The local file system path to the repository where the LICENSE file is located.</param>
        /// <param name="repositoryFullName">The full name of the repository, used for logging purposes.</param>
        private void RenameLicenseFile(string localPath, string repositoryFullName)
        {
            string licensePath = Path.Combine(localPath, "LICENSE");
            if (File.Exists(licensePath) && string.IsNullOrEmpty(Path.GetExtension(licensePath)))
            {
                string newLicensePath = licensePath + ".md";
                try
                {
                    File.Move(licensePath, newLicensePath);
                    Debug.Log($"Renamed LICENSE to LICENSE.md in {repositoryFullName}");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Failed to rename LICENSE file: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Creates a new assembly definition file (.asmdef) for the specified package at the given local path.
        /// </summary>
        /// <remarks>If any existing .asmdef files are found in the specified <paramref
        /// name="localPath"/>, they will be deleted to avoid duplicates. The new assembly definition file will be named
        /// using the format "<c>{ProjectName}.{packageName}.asmdef</c>", where <c>ProjectName</c> is a predefined
        /// project-level identifier.</remarks>
        /// <param name="localPath">The local file system path where the assembly definition file will be created. This path must be writable.</param>
        /// <param name="packageName">The name of the package for which the assembly definition file is being created. This will be used to
        /// generate the assembly name.</param>
        private void CreateAssemblyDefinition(string localPath, string packageName)
        {
            // Delete existing .asmdef files to avoid duplicates
            string[] existingAsmdefs = Directory.GetFiles(localPath, "*.asmdef");
            foreach (string asmdef in existingAsmdefs)
            {
                File.Delete(asmdef);
                Debug.Log($"Deleted existing assembly definition: {asmdef}");
            }

            // Create new .asmdef with the correct name
            var asmdefData = new AssemblyDefinitionData
            {
                name = $"{ProjectName}.{packageName}"
            };

            string asmdefPath = Path.Combine(localPath, $"{ProjectName}.{packageName}.asmdef");
            string json = JsonUtility.ToJson(asmdefData, true);
            File.WriteAllText(asmdefPath, json);
        }

        /// <summary>
        /// Creates a package manifest file in the specified local path with the given package name.
        /// </summary>
        /// <remarks>The method generates a JSON file named <c>package.json</c> in the specified <paramref
        /// name="localPath"/>.  The file contains metadata about the package, including its unique identifier and
        /// display name,  which are derived from the project name and the provided <paramref
        /// name="packageName"/>.</remarks>
        /// <param name="localPath">The local directory where the package manifest file will be created. This path must be valid and writable.</param>
        /// <param name="packageName">The name of the package to include in the manifest. This value is used to generate the package's unique
        /// identifier and display name.</param>
        private void CreatePackageManifest(string localPath, string packageName)
        {
            var packageData = new PackageData
            {
                name = $"com.{ProjectName.ToLower()}.{packageName.ToLower()}",
                displayName = $"{ProjectName} {packageName}"
            };

            string json = JsonUtility.ToJson(packageData, true);
            json = json.Replace("\\", "");

            // Write to package.json file
            string packageJsonPath = Path.Combine(localPath, "package.json");
            File.WriteAllText(packageJsonPath, json);
        }

        /// <summary>
        /// Copies all files and subdirectories from the specified source folder to the destination folder.
        /// </summary>
        /// <remarks>This method recursively copies all files and subdirectories from the source folder to
        /// the destination folder, preserving the directory structure. Files with a <c>.meta</c> extension are ignored
        /// during the copy process.</remarks>
        /// <param name="sourceFolder">The path of the source folder containing the files and directories to copy. Must exist.</param>
        /// <param name="destinationFolder">The path of the destination folder where the files and directories will be copied. If the folder does not
        /// exist, it will be created.</param>
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

        /// <summary>
        /// Represents the configuration data for an assembly definition, including metadata and build settings.
        /// </summary>
        /// <remarks>This class is used to define the properties and settings of an assembly definition
        /// file, such as its name,  references, platform constraints, and other build-related options. It is typically
        /// used in the context of  managing assemblies in a project, such as Unity projects.</remarks>
        [System.Serializable]
        public class AssemblyDefinitionData
        {
            public string name;
            public bool allowUnsafeCode = false;
            public bool noEngineReferences = false;
            public bool overrideReferences = false;
            public bool autoReferenced = true;
            public string rootNamespace = ProjectName;
            public string[] references = new string[] { };
            public string[] precompiledReferences = new string[] { };
            public string[] includePlatforms = new string[] { };
            public string[] excludePlatforms = new string[] { };
            public string[] defineConstraints = new string[] { };
            public object[] versionDefines = new object[] { };
        }

        /// <summary>
        /// Represents metadata and configuration information for a Unity package.
        /// </summary>
        /// <remarks>This class is used to define the essential details of a Unity package, including its
        /// name, version,  display name, description, Unity version compatibility, and associated URLs for
        /// documentation,  changelogs, and licenses. It also includes dependency information and keywords for
        /// categorization.</remarks>
        [System.Serializable]
        public class PackageData
        {
            public string name;
            public string version = "1.0.0";
            public string displayName;
            public string description = $"This is a part of the {ProjectName} Ecosystem";
            public string unity = "2022.1";
            public string documentationUrl = "";
            public string changelogUrl = "";
            public string licensesUrl = "";
            public string dependencies = " { \"com.unityessentials.core\": \"1.0.0\" } ";
            public string[] keywords = new string[] { };
            public AuthorInfo author = new();
        }

        /// <summary>
        /// Represents information about an author, including their name, email, and website URL.
        /// </summary>
        /// <remarks>This class is used to store and manage basic details about an author.  All fields are
        /// publicly accessible and can be modified directly.</remarks>
        [System.Serializable]
        public class AuthorInfo
        {
            public string name = AuthorName;
            public string email = "";
            public string url = "";
        }
    }
}
#endif