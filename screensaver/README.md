# Znake screensaver

Znake lives over the unlocked Windows desktop. A green snake feeds on matching light, changes color, grows, sheds, and leaves faint traces. The scene resumes after dismissal. In the normal view the desktop stays visible around the snake and through the vines and fruit.

## Build and install

Use Windows 10 version 2004 or later with .NET Framework. From the repository root, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\screensaver\build.ps1
```

Right-click `screensaver\out\Znake.scr` and select **Install**. In **Screen Saver Settings**, use **Settings** to set the debug view, daylight coordinates, or first-birth genes. Without coordinates, the saver uses daytime gene expression.

Genes left blank get small random values. An override applies to the first birth in a new profile. To set the same overrides from a script, write JSON to `%LOCALAPPDATA%\Znake\settings.json` before the first full-screen run:

```json
{
  "debug": true,
  "genes": {
    "Length5": 1,
    "ColorDrift": 0.12,
    "LearningOffset": 0.25
  }
}
```

Znake keeps the versioned world and one previous checkpoint in that same local folder. Desktop images stay in memory. The simulation pauses when Windows switches away from the unlocked desktop.

## Verify

```powershell
.\screensaver\build.ps1 -Test
.\screensaver\build.ps1 -Smoke
.\screensaver\tests\WindowsSmoke.ps1 -Mode Modes
.\screensaver\tests\WindowsSmoke.ps1 -Mode InvalidState
.\screensaver\tests\WindowsSmoke.ps1 -Mode Corner
.\screensaver\tests\WindowsSmoke.ps1 -Mode Direct
.\screensaver\tests\WindowsSmoke.ps1 -Mode Idle
```

The corner test checks live colors and checkpoint resume in a small corner without taking focus.
The idle test uses a temporary saver selection and timeout, restores both at exit, and keeps its saves under the ignored build directory. Leave the pointer and keyboard idle while the test runs. It uses the current Windows protection setting.
