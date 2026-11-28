#!/bin/sh
# Builds build/MidnightClub.dll (needs mono mcs and ScriptHookVDotNet3.dll path in $SHVDN)
SHVDN=${SHVDN:-/home/claude/tools/ScriptHookVDotNet3.dll}
cd "$(dirname "$0")"
mcs -target:library -sdk:4.8 -unsafe -optimize+ -r:$SHVDN -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Runtime.Serialization.dll -r:System.Core.dll -r:System.Management.dll \
  -resource:res/roads.bin.gz,roads.bin.gz -resource:res/skills.json,skills.json -resource:res/camera/CameraPhysics.xml,camera.CameraPhysics.xml -resource:res/camera/CameraSettings.ini,camera.CameraSettings.ini $(for f in res/ui/*.png; do printf -- '-resource:%s,ui.%s ' "$f" "$(basename "$f")"; done) $(for f in res/specs/*.json; do case "$(basename "$f")" in _*) continue;; esac; printf -- '-resource:%s,specs.%s ' "$f" "$(basename "$f")"; done) -out:build/MidnightClub.dll -recurse:'src/*.cs'
# Launcher
mcs -target:winexe -sdk:4.8 -optimize+ -win32icon:launcher/icon.ico -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Core.dll \
  $(for f in launcher/icons/*.png; do printf -- '-resource:%s,icon.%s ' "$f" "$(basename "$f")"; done) \
  $(for f in launcher/art/*.jpg launcher/art/*.png; do printf -- '-resource:%s,art.%s ' "$f" "$(basename "$f")"; done) \
  $(for f in launcher/fonts/*.ttf; do printf -- '-resource:%s,font.%s ' "$f" "$(basename "$f")"; done) \
  -out:build/MidnightClubLauncher.exe launcher/Launcher.cs
