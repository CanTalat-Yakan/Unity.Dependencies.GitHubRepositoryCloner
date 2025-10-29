#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityEssentials
{
    public partial class GitHubRepositoryCloner
    {
        public EditorWindowDrawer Window;
        public Action Repaint;
        public Action Close;

        [MenuItem("Assets/GitHub Repository Cloner", true)]
        public static bool ValidateGitHubRepositoryCloner()
        {
            string path = GetSelectedPath();
            return !string.IsNullOrEmpty(path) && Directory.Exists(path);
        }

        [MenuItem("Assets/GitHub Repository Cloner", priority = -80)]
        public static void ShowUtility()
        {
            var editor = new GitHubRepositoryCloner();
            editor.Window = EditorWindowDrawer
                .CreateInstance("GitHub Repository Cloner", new(400, 500))
                .SetHeader(editor.Header, EditorWindowStyle.Toolbar)
                .SetBody(editor.Body, EditorWindowStyle.Margin)
                .SetFooter(editor.Footer)
                .GetRepaintEvent(out editor.Repaint)
                .GetCloseEvent(out editor.Close)
                .ShowAsUtility();
        }

        public void Header()
        {
            if (string.IsNullOrEmpty(Token))
            {
                GUILayout.Label("Enter your Git Token:");

                _tokenPlaceholder = EditorGUILayout.PasswordField(_tokenPlaceholder, EditorStyles.toolbarTextField);

                if (GUILayout.Button("Save Token", EditorStyles.toolbarButton))
                {
                    Token = _tokenPlaceholder;
                    EditorPrefs.SetString(TokenKey, Token);

                    FetchRepositories(Repaint);
                }
                return;
            }

            GUILayout.Space(4);

            string oldFilter = _repositoryNameFilter;
            _repositoryNameFilter = EditorGUILayout.TextField(_repositoryNameFilter, EditorStyles.toolbarSearchField);
            if (_repositoryNameFilter != oldFilter)
            {
                // Only filter locally, do not fetch
                RepositoryNames = FilterByName(AllRepositoryNames, _repositoryNameFilter);
                RepositorySelected = new(new bool[RepositoryNames.Count]);
            }

            GUILayout.Space(4);

            if (GUILayout.Button("Refresh Repositories", EditorStyles.toolbarButton))
            {
                FetchRepositories(Repaint);
                GUI.FocusControl(null);
            }

            if (GUILayout.Button("Change Token", EditorStyles.toolbarButton))
            {
                Token = "";
                EditorPrefs.DeleteKey(TokenKey);
                RepositoryNames.Clear();
                AllRepositoryNames.Clear();
                RepositorySelected.Clear();
                _tokenPlaceholder = string.Empty;
            }
        }

        public void Body()
        {
            if (string.IsNullOrEmpty(Token))
            {
                EditorGUILayout.HelpBox(
                    "To use the GitHub Repository Cloner, you need a Personal Access Token (PAT).",
                    MessageType.Info);
                EditorGUILayout.LabelField(
                    "You can create either a Fine-grained or Classic token. When creating the token:\n\n" +
                    "• Set access to 'All repositories' (applies to all current and future repositories you own, including public repositories).\n\n" +
                    "• Under 'Repository permissions', enable 'Contents' with 'Read and write' access. This allows access to repository contents, commits, branches, downloads, releases, and merges.\n\n" +
                    "After generating the token, copy it and paste it above.", EditorStyles.wordWrappedLabel);
                if (EditorGUILayout.LinkButton("Create Personal Access Tokens"))
                    Application.OpenURL("https://github.com/settings/personal-access-tokens");
                return;
            }

            if (IsFetching)
            {
                GUILayout.Label("Fetching...");
                return;
            }

            if (RepositoryNames.Count == 0)
            {
                GUILayout.Label("No repositories found matching the filter.");
                return;
            }

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("All", GUILayout.Width(30)))
                    for (int i = 0; i < RepositorySelected.Count; i++)
                        RepositorySelected[i] = true;

                if (GUILayout.Button("None", GUILayout.Width(40)))
                    for (int i = 0; i < RepositorySelected.Count; i++)
                        RepositorySelected[i] = false;
            }

            if (RepositoryNames.Count > 0)
                for (int i = 0; i < RepositoryNames.Count; i++)
                {
                    if (RepositorySelected.Count < RepositoryNames.Count)
                        RepositorySelected.Add(false);

                    RepositorySelected[i] = EditorGUILayout.ToggleLeft(RepositoryNames[i], RepositorySelected[i]);
                }
        }

        public void Footer()
        {
            if (string.IsNullOrEmpty(Token) || IsFetching)
                return;

            if (!RepositorySelected.Any(selected => selected))
                return;

            GUILayout.Space(-1);
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                ShouldCreateAssemblyDefinition = EditorGUILayout.ToggleLeft("Create Assembly Definitions", ShouldCreateAssemblyDefinition);
                ShouldCreatePackageManifests = EditorGUILayout.ToggleLeft("Create Package Manifests", ShouldCreatePackageManifests);
                ShouldUseTemplateFiles = EditorGUILayout.ToggleLeft("Copy Template Files", ShouldUseTemplateFiles);
                
                string repositryString = RepositorySelected.Count(selected => selected) == 1
                    ? "Repository"
                    : "Repositories";
                if (GUILayout.Button($"Clone Selected {repositryString}", GUILayout.Height(24)))
                {
                    CloneSelectedRepositories();

                    FetchRepositories(Repaint);
                    GUI.FocusControl(null);
                }
            }
        }
    }
}
#endif