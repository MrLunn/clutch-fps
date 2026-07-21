# Building & sharing Clutch FPS

Friends always download the newest build from one permanent link:

**https://github.com/MrLunn/clutch-fps/releases/latest**

They download `ClutchFPS-Windows.zip`, unzip the whole folder, and run
`ClutchFPS.exe`. (Windows may warn about an unsigned app → *More info → Run
anyway* — normal for indie playtests.)

## Updating the build (the day-to-day way)

Builds locally with your already-licensed editor, then republishes the
`latest` release — no cloud license needed:

1. **Close the Unity editor** (it locks the project while open).
2. In PowerShell, from the project folder: `./publish.ps1`

That builds the Windows player, zips it, and uploads it over the `latest`
release. The download link above serves the new build immediately.

Requires the GitHub CLI (`gh`) authenticated once (`gh auth login`) — already
done on this machine.

---

## Optional: fully-automatic cloud builds (CI)

`.github/workflows/build.yml` can build in the cloud on every push, but it's
**manual-only right now** because GitHub's runners need a Unity license, and
Unity 6 Personal activation in CI is fiddly. The local `publish.ps1` avoids all
of that. If you later want auto-on-push, set the license secrets below and
switch the workflow trigger back to `push`.

---

## One-time setup (only you can do this — it needs your Unity login)

The CI build has to activate a free **Unity Personal** license, which needs a
license file and your Unity credentials stored as GitHub secrets. Do this once:

1. **Get the activation file (`.alf`)** locally — GameCI's old activation action
   is deprecated, so generate it from your installed editor:
   ```
   "C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe" ^
     -quit -batchmode -nographics -logFile - -createManualActivationFile
   ```
   It writes `Unity_v6000.3.20f1.alf` to the current folder. (Already generated
   once and placed on the Desktop.)

2. **Convert it to a license:** go to <https://license.unity3d.com/manual>,
   upload the `.alf`, choose **Unity Personal / not using professionally**, and
   download the resulting **`.ulf`** file.

3. **Add three repo secrets:** repo → **Settings** → *Secrets and variables* →
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
