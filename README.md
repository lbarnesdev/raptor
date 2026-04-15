# RAPTOR

Sci-fi horizontal shoot-em-up. Godot 4.6 / C# (.NET 8).

---

## Git LFS Setup

This repository uses [Git LFS](https://git-lfs.com/) to store binary assets
(sprites, audio, fonts). You must install and initialise LFS **once per machine**
before cloning or pushing, otherwise asset files will appear as 1-line pointer
stubs instead of actual data.

**1 — Install Git LFS**

```
# Windows (winget)
winget install GitHub.GitLFS

# macOS (Homebrew)
brew install git-lfs

# Ubuntu / Debian
sudo apt install git-lfs
```

**2 — Initialise LFS for your user account (one-time)**

```
git lfs install
```

This adds the LFS filter hooks to your global `~/.gitconfig`. You only
ever need to run this once per machine.

**3 — Clone as normal**

```
git clone https://github.com/your-org/raptor.git
```

LFS pointers are resolved automatically on clone and checkout after
`git lfs install` has been run.

**4 — Verify tracked files**

```
# List every LFS-tracked file in the current working tree
git lfs ls-files

# Show which patterns are being tracked
git lfs track
```

If `git lfs ls-files` returns nothing after cloning and the asset files are
suspiciously tiny (< 200 bytes), LFS was not installed before the clone.
Fix it with:

```
git lfs install
git lfs pull
```

**Adding new binary asset types**

```
git lfs track "*.psd"
git add .gitattributes
git commit -m "chore: track .psd files via Git LFS"
```

The `.gitattributes` file at the repo root defines all tracked patterns.
Commit `.gitattributes` changes before committing the binary files.
