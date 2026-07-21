# Building & sharing Clutch FPS

Builds are automated. Every push to `main` builds a Windows client via GitHub
Actions and publishes it to a permanent **Latest** release, so friends always
grab the newest version from one link:

**https://github.com/MrLunn/clutch-fps/releases/latest**

They download `ClutchFPS-Windows.zip`, unzip the whole folder, and run
`ClutchFPS.exe`. (Windows may warn about an unsigned app → *More info → Run
anyway* — normal for indie playtests.)

---

## One-time setup (only you can do this — it needs your Unity login)

The CI build has to activate a free **Unity Personal** license, which needs a
license file and your Unity credentials stored as GitHub secrets. Do this once:

1. **Push these workflows** to GitHub (already done if you see them under the
   repo's *Actions* tab).

2. **Get the activation file:** repo → **Actions** → *Acquire Unity Activation
   File* → **Run workflow**. When it finishes, open the run and download the
   **Manual Activation File** artifact (a `.alf`).

3. **Convert it to a license:** go to <https://license.unity3d.com/manual>,
   upload the `.alf`, choose **Unity Personal / not using professionally**, and
   download the resulting **`.ulf`** file.

4. **Add three repo secrets:** repo → **Settings** → *Secrets and variables* →
   **Actions** → *New repository secret*:
   - `UNITY_LICENSE` — paste the **entire contents** of the `.ulf` file
   - `UNITY_EMAIL` — your Unity account email
   - `UNITY_PASSWORD` — your Unity account password

   (These stay encrypted in GitHub. Never commit them to the repo.)

5. **Trigger a build:** push any commit to `main`, or run the *Build & Release*
   workflow manually. First build is slow (~20–40 min while it imports all
   assets); later builds are cached and much faster.

Once green, the **Latest** release updates automatically on every push.

---

## Notes

- **Public vs private repo:** if the repo is public, the release download is
  public too (anyone with the link). Fine for a friends playtest. If you make
  the repo private, friends need GitHub access to download release assets.
- **Windows only for now.** The pipeline builds `StandaloneWindows64`. A Mac
  friend needs a `StandaloneOSX` target — ask and we'll add it.
- **Scripting backend:** the Linux CI runner builds with the **Mono** backend
  (the Standalone default). If you switch to IL2CPP, the build needs a Windows
  runner instead — tell me and I'll adjust.
- **If the build errors on the Unity image**, GameCI may not have `6000.3.20f1`
  yet; bump `unityVersion` in both workflow files to the closest available.
