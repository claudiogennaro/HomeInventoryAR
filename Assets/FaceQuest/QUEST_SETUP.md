# FaceQuest — Quest 3 setup and troubleshooting

For a Meta Quest 3 / 3S that runs FaceQuest (package `it.cnr.isti.facequest`).
Two parts: (1) fixing the **"cam waiting"** message, including for builds made
*before* the app learned to ask for the camera permission itself; (2) removing the
headset's *physical* restrictions (proximity sensor, boundary, auto-sleep, system
dialogs) while testing.

---

## 1. "cam waiting" — the camera permission

**What it means.** The app cannot read the passthrough camera, almost always because
the permission *Headset cameras* (`horizonos.permission.HEADSET_CAMERA`) was never
granted on this headset. No camera means no faces, so nothing is recognised.

**Requirements**

* Meta Quest 3 or 3S, Horizon OS **v74 or later** (the Passthrough Camera API does not
  exist before).
* **Passthrough enabled** in the headset (the API needs it).
* The camera permission granted to FaceQuest.

**Newer builds** ask for the permission at start-up (up to four times, 8 s apart). If
the prompt appears, accept it. If the status line at the bottom of the People panel
reads `cam: PERMISSION MISSING - Settings > Privacy > App permissions > Headset cameras`,
the permission is still not granted: use A or B below.

**Older builds** (made before this change) never show the prompt, so the permission
has to be granted by hand.

### A. In the headset

1. Open the Quest **Settings** and go to the app's permissions page. The exact menu
   differs between Horizon OS versions; look under *Privacy and safety → App permissions*
   or *Apps → FaceQuest → Permissions*.
2. Enable **Headset cameras** (some versions call it *Cameras* or *Passthrough camera*)
   for **FaceQuest**.
3. Close FaceQuest completely and start it again.

### B. From a computer, with adb (works for old builds)

Requires developer mode on the headset and USB debugging allowed (see part 2).

```
adb devices
adb shell pm grant it.cnr.isti.facequest horizonos.permission.HEADSET_CAMERA
adb shell pm grant it.cnr.isti.facequest com.oculus.permission.USE_SCENE
adb shell am force-stop it.cnr.isti.facequest
```

Check that it took effect:

```
adb shell dumpsys package it.cnr.isti.facequest | findstr /i "HEADSET_CAMERA"
```

(on macOS/Linux use `grep -i` instead of `findstr /i`). The line must end with
`granted=true`. Then start the app again.

### If it still says "cam waiting"

* Horizon OS older than v74: update the headset.
* Passthrough turned off: turn it on in the headset's settings.
* A fresh install resets permissions: grant them again after every reinstall.
* Send the status lines of the People panel (the first two rows) — the first item that
  reads `KO` or `MISSING` is the cause.

---

## 2. Removing the physical restrictions while testing

These are the things that get in the way when you run the app without a person
wearing the headset all the time, or while moving around a room. They are meant for
**testing only**.

| Restriction | What it does | How to remove it |
|---|---|---|
| **Proximity sensor** | The headset sleeps when it is not worn, pausing the app | Simulate "worn": `adb shell am broadcast -a com.oculus.vrpowermanager.prox_close` |
| **Boundary (Guardian)** | Stops the app and shows the boundary when you move out of the play area | Disable it (see below) |
| **Auto-sleep** | The headset dims and sleeps after a period of inactivity | Disable it (see below) |
| **System dialogs** | Blocking pop-ups such as *Allow USB debugging* | Disable them (see below) |

### Prerequisites

1. Developer mode on, and the headset authorised for USB debugging (accept the
   *Allow USB debugging* prompt, tick *Always allow*).
2. Android platform-tools (`adb`) installed on the computer.
3. `adb devices` lists the headset as `device`.

### Proximity sensor

Keep the headset awake without wearing it:

```
adb shell am broadcast -a com.oculus.vrpowermanager.prox_close
```

Go back to normal behaviour (headset "taken off"):

```
adb shell am broadcast -a com.oculus.vrpowermanager.prox_far
```

### Boundary, auto-sleep and dialogs

Meta's *Scriptable Testing Services* expose these as properties. They need the
**Store PIN** of the account signed in on the headset (the PIN used for purchases):

```
adb shell content call --uri content://com.oculus.rc --method SET_PROPERTY --extra "disable_guardian:b:true" --extra "PIN:s:<YOUR_PIN>"
adb shell content call --uri content://com.oculus.rc --method SET_PROPERTY --extra "disable_autosleep:b:true" --extra "PIN:s:<YOUR_PIN>"
adb shell content call --uri content://com.oculus.rc --method SET_PROPERTY --extra "disable_dialogs:b:true" --extra "PIN:s:<YOUR_PIN>"
```

See the current values:

```
adb shell content call --uri content://com.oculus.rc --method GET_PROPERTY
```

**Put everything back when you are done** (replace `true` with `false`):

```
adb shell content call --uri content://com.oculus.rc --method SET_PROPERTY --extra "disable_guardian:b:false" --extra "PIN:s:<YOUR_PIN>"
adb shell content call --uri content://com.oculus.rc --method SET_PROPERTY --extra "disable_autosleep:b:false" --extra "PIN:s:<YOUR_PIN>"
adb shell content call --uri content://com.oculus.rc --method SET_PROPERTY --extra "disable_dialogs:b:false" --extra "PIN:s:<YOUR_PIN>"
```

On Windows PowerShell, keep the double quotes as written; in a Linux/macOS shell
single quotes also work.

### Notes and cautions

* These properties **survive a reboot**; only a factory reset restores the defaults. Do
  not forget to switch them back.
* Meta warns that leaving them off can cause **screen burn-in** and **fast battery
  drain**. Do not leave a headset running unattended with the sleep protections off.
* With the boundary disabled the headset no longer warns you about walls and
  furniture. The passthrough view stays on, but move slowly and keep the area clear.
* Meta documents these commands for test accounts and test devices. If one fails on
  your headset (it depends on the Horizon OS version and the account), the headset's own
  settings are the fallback: *Settings → Physical space → Boundary*, and *Settings →
  System → Power* for sleep.
* The proximity-sensor commands only last until you wear and remove the headset or
  reboot; run them again if the headset goes to sleep.

## 3. Quick checklist

1. Horizon OS ≥ v74, passthrough on.
2. Permission *Headset cameras* granted to FaceQuest (A or B above).
3. Start FaceQuest; the panel should show `cam 1280x960` (or similar) instead of
   `cam waiting`.
4. Press **grip** to switch Search / Train; in Train, a new face brings up the
   Add / Discard pop-up.
5. For unattended testing only: proximity sensor, boundary, auto-sleep and dialogs as
   above — and restore them afterwards.

Sources: Meta Horizon documentation, *Passthrough Camera API* and *Scriptable Testing
Services* (developers.meta.com/horizon).
