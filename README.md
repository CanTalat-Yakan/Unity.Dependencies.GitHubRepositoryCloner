# Unity Essentials

**Unity Essentials** is a lightweight, modular utility namespace designed to streamline development in Unity.
It provides a collection of foundational tools, extensions, and helpers to enhance productivity and maintain clean code architecture.

## Installation

Install the Unity Essentials entry package via Unity's Package Manager, then install all modules from the Tools menu:

1) Add the entry package (via Git URL)
- Open: Window → Package Manager
- Click the + button → "Add package from git URL…"
- Paste this URL and click Add:
  
```
https://github.com/CanTalat-Yakan/UnityEssentials.git
```

2) Install or update all Unity Essentials packages
- In Unity, go to: Tools → Install & Update UnityEssentials
- Use the installer to install all packages (or select only the ones you need)
- Re-run the same menu at any time to update packages to the latest versions

## 📦 This Package

This package is part of the **Unity Essentials** ecosystem.  
It integrates seamlessly with other Unity Essentials modules and follows the same lightweight, dependency-free philosophy.

## 🌐 Namespace

All utilities are under the `UnityEssentials` namespace. This keeps your project clean, consistent, and conflict-free.

```csharp
using UnityEssentials;
```

---

# GitHub Repository Cloner

Clone multiple GitHub repositories directly into your Unity project from a simple editor window. Authenticate with a GitHub Personal Access Token (PAT), filter and select repos, and optionally scaffold each repo with an assembly definition, a package manifest, and your own templates - all in one go.

This is an editor-only utility that ships as part of the Unity Essentials ecosystem.

![screenshot](Documentation/Screenshot.png)

## Highlights

- Authenticate with a GitHub PAT and fetch up to 100 repositories tied to your account
- Fast filtering and multi-select (All/None)
- Clone into a folder you select in the Project window (under Assets)
- Optional post-clone steps per repo:
  - Create .asmdef: `UnityEssentials.<PackageName>`
  - Create package.json with sensible defaults
  - Copy files from `Assets/Templates` into each cloned repo
- Git LFS awareness: runs `git lfs pull` automatically when needed

## Requirements

- Unity Editor (Editor-only; no runtime code)
- Git installed and available on your PATH
- Optional: Git LFS if you clone repositories that use LFS
- A GitHub Personal Access Token (PAT) with permission to read your repositories

Tip: If the tool can’t find Git/LFS, install them and restart Unity so PATH updates are picked up.

## Usage

1) Select a target folder in the Project window
- Select any folder under `Assets/` where you want the repositories to be cloned.
- The menu item is enabled only when a folder (or an asset inside a folder) is selected.

2) Open the tool
- Menu: Assets → GitHub Repository Cloner

3) Enter and save your GitHub token
- Paste your GitHub PAT and click Save Token.
- The tool stores it in Unity’s EditorPrefs and immediately fetches your repositories.

4) Filter and select repositories
- Use the search field to filter by full name (e.g., `owner/name`).
- Use All or None for bulk selection, then check the repos you want to clone.

5) Choose post-clone options
- Create Assembly Definitions
- Create Package Manifests
- Copy Template Files (from `Assets/Templates`)

6) Clone
- Click Clone Selected Repository/Repositories.
- A progress bar appears; the tool will:
  - Clone each repo to the selected folder
  - If `.gitattributes` indicates LFS, run `git lfs pull`
  - Rename `LICENSE` → `LICENSE.md` (if present and extensionless)
  - Optionally create `.asmdef`, `package.json`, and copy templates
- On completion, the Asset Database refreshes.

## What the tool generates

- Assembly definition (.asmdef)
  - Name: `UnityEssentials.<PackageName>`
  - Root namespace: `UnityEssentials`
  - Before creating, any existing `*.asmdef` files in the repo root are deleted to avoid duplicates
  - PackageName is derived from the repository folder and has "Unity" removed (e.g., `UnityFoo` → `Foo`)

- Package manifest (package.json)
  - Name: `com.unityessentials.<packagename>`
  - Display Name: `UnityEssentials <PackageName>`
  - Unity: `6000.0`
  - Version: `1.0.0`
  - Description: `This is a part of the UnityEssentials Ecosystem`
  - Author: `Unity Essentials`

- Templates (optional)
  - Recursively copies everything (except `.meta` files) from `Assets/Templates` into the cloned repo folder
  - If `Assets/Templates` does not exist, the tool logs a warning and continues

## Cloner window options (UPM scaffolding)

Inside the cloner window, you can toggle these options independently to add or regenerate UPM components for each selected repository:

- Create Assembly Definition
  - Generates a `.asmdef` named `UnityEssentials.<PackageName>` with root namespace `UnityEssentials`.
  - Note: Existing `*.asmdef` files in the repo root will be removed to avoid duplicates.

- Create Package Manifest
  - Generates a `package.json` with Unity Essentials defaults (name, displayName, unity version, description, author, dependencies).
  - Note: If a `package.json` already exists, it will be overwritten.

- Copy Template Files
  - Copies the full template tree from `Assets/Templates/` into the repo (skips `.meta` files).
  - Best results: Extract `UnityEssentials_GithubRepositoryCloner_Templates.zip` into your `Assets/` folder first, so `Assets/Templates` contains a clean UPM-style layout (standard folders, sample `package.json`, `.asmdef`, tests).
  - Note: Existing files with the same names will be overwritten.

Use these toggles selectively: if your repository already has some of these files, leave that option off; if it’s missing something, enable only the piece you want to add.

## Where things happen

- Target folder: Whatever you selected in the Project window under `Assets/`
- Token storage: Unity EditorPrefs with key `GitToken`
- Repository fetching: GitHub API `GET /user/repos?per_page=100`
- Cloning: `git clone https://<TOKEN>@github.com/<owner>/<name>.git`
- LFS pull (if used): `git lfs pull`

## Configuration and defaults

You can tweak defaults by editing `Editor/GitHubRepositoryCloner.cs`:

- DefaultOrganizationName: `UnityEssentials`
- DefaultAuthorName: `Unity Essentials`
- DefaultUnityVersion (for generated package.json): `2022.1`
- DefaultDescription: `This is a part of the UnityEssentials Ecosystem`
- DefaultDependency: `com.unityessentials.core`
- DefaultDependencyVersion: `1.0.0`
- ExcludeString: `Unity` (removed from repo folder to form PackageName)
- TemplateFolder: `Assets/Templates`

Tip: If you don’t want the repo name altered (e.g., to keep `UnityFoo` intact), set `ExcludeString` to an empty string.

## Limitations and notes

- Pagination: Only the first 100 repositories are fetched from the GitHub API.
- Existing folders: Repos are filtered out if any existing folder anywhere under `Assets/` shares the same final folder name as the repo (to avoid collisions).
- Menu availability: The Assets → GitHub Repository Cloner menu is enabled only when a valid folder (or an asset inside one) is selected.
- Token header: The tool uses the `token` auth scheme for the GitHub API request.

## Security notes

- Token storage
  - Your PAT is stored in Unity’s EditorPrefs under the key `GitToken`. Use Change Token to clear it.

- Token in clone URL and remotes
  - The clone URL embeds your token (`https://<TOKEN>@github.com/...`). Some Git versions write this into `.git/config` as the remote URL.
  - For safety, after cloning, you can remove the token from the remote:
    ```bash
    # run inside each cloned repo folder
    git remote set-url origin https://github.com/<owner>/<name>.git
    ```
