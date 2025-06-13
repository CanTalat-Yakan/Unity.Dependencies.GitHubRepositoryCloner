using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityEssentials
{
    public class GitHubRepositoryClonerEditor
    {
        public GitHubRepositoryCloner Cloner = new();

        public EditorWindowDrawer window;
        public Action Repaint;
        public Action Close;

        public const string TokenKey = "GitToken";

        [MenuItem("Tools/GitHub Repository Cloner")]
        public static void ShowWindow()
        {
            var editor = new GitHubRepositoryClonerEditor();
            editor.window = new EditorWindowDrawer("GitHub Repository Cloner", new(700, 800), new(400, 500))
                .SetHeader(editor.Header)
                .SetBody(editor.Body)
                .SetFooter(editor.Footer)
                .GetRepaintEvent(out editor.Repaint)
                .GetCloseEvent(out editor.Close)
                .ShowUtility();
        }

        public void Header()
        {
            if (string.IsNullOrEmpty(Cloner.Token))
            {
                GUILayout.Label("Enter your GitHub token:");

                Cloner.TokenPlaceholder = EditorGUILayout.TextField(Cloner.TokenPlaceholder);

                if (GUILayout.Button("Save Token"))
                {
                    Cloner.Token = Cloner.TokenPlaceholder;
                    EditorPrefs.SetString(TokenKey, Cloner.Token);

                    Cloner.FetchRepositories(Repaint);
                }
                return; // Early return since no repositories to show yet
            }

            if (GUILayout.Button("Change Token"))
            {
                Cloner.Token = "";
                EditorPrefs.DeleteKey(TokenKey);
                Cloner.RepositoryNames.Clear();
                Cloner.AllRepositoryNames.Clear();
                Cloner.RepositorySelected.Clear();
                return;
            }

            if (Cloner.IsFetching)
            {
                GUILayout.Label("Fetching...");
                return;
            }

            GUILayout.BeginVertical();
            {
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Label("Filter repositories by name:");
                    if (GUILayout.Button("Refresh", GUILayout.Width(100), GUILayout.Height(18)))
                    {
                        Cloner.FetchRepositories(Repaint);
                        GUI.FocusControl(null);
                    }
                }
                GUILayout.EndHorizontal();

                string oldFilter = Cloner.RepositoryNameFilter;
                Cloner.RepositoryNameFilter = EditorGUILayout.TextField(Cloner.RepositoryNameFilter);
                if (Cloner.RepositoryNameFilter != oldFilter)
                {
                    // Only filter locally, do not fetch
                    Cloner.RepositoryNames = Cloner.FilterByName(Cloner.AllRepositoryNames, Cloner.RepositoryNameFilter);
                    Cloner.RepositorySelected = new List<bool>(new bool[Cloner.RepositoryNames.Count]);
                }
            }
            GUILayout.EndVertical();
        }

        public void Body()
        {
            if (string.IsNullOrEmpty(Cloner.Token))
                return; // Early return since no repositories to show yet

            if (Cloner.IsFetching)
                return;

            if (Cloner.RepositoryNames.Count == 0)
            {
                GUILayout.Label("No repositories found matching the filter.");
                return;
            }
            else if (Cloner.RepositoryNames.Count > 0)
            {
                // Calculate space for the scroll view dynamically
                float totalHeight = window.position.height;
                float headerHeight = EditorGUIUtility.singleLineHeight * 3 + 30;
                float toggleTemplateHeight = EditorGUIUtility.singleLineHeight + 6;
                float buttonHeight = 90;
                float scrollHeight = totalHeight - (headerHeight + toggleTemplateHeight + buttonHeight);
                scrollHeight = Mathf.Max(scrollHeight, 100);

                for (int i = 0; i < Cloner.RepositoryNames.Count; i++)
                {
                    if (Cloner.RepositorySelected.Count < Cloner.RepositoryNames.Count)
                        Cloner.RepositorySelected.Add(false);

                    Cloner.RepositorySelected[i] = EditorGUILayout.ToggleLeft(Cloner.RepositoryNames[i], Cloner.RepositorySelected[i]);
                }
            }
        }

        public void Footer()
        {
            if (Cloner.IsFetching)
                return;

            if (string.IsNullOrEmpty(Cloner.Token))
                return; // Early return since no repositories to show yet

            Cloner.ShouldCreateAssemblyDefinition = EditorGUILayout.ToggleLeft("Create Assembly Definitions", Cloner.ShouldCreateAssemblyDefinition);
            Cloner.ShouldCreatePackageManifests = EditorGUILayout.ToggleLeft("Create Package Manifests", Cloner.ShouldCreatePackageManifests);
            Cloner.ShouldUseTemplateFiles = EditorGUILayout.ToggleLeft("Copy Template Files", Cloner.ShouldUseTemplateFiles);

            GUI.enabled = Cloner.RepositorySelected.Any(selected => selected);
            if (GUILayout.Button("Clone Selected Repositories", GUILayout.Height(24)))
            {
                string targetPath = Application.dataPath;
                Cloner.CloneSelectedRepositories(targetPath);

                Cloner.FetchRepositories(Repaint);
                GUI.FocusControl(null);
            }
            GUI.enabled = true;
        }
    }
}
