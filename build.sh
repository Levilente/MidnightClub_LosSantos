#!/bin/sh
# Builds build/MidnightClub.dll (needs mono mcs and ScriptHookVDotNet3.dll path in $SHVDN)
SHVDN=${SHVDN:-/home/claude/tools/ScriptHookVDotNet3.dll}
cd "$(dirname "$0")"
mcs -target:library -sdk:4.8 -unsafe -optimize+ -r:$SHVDN -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Runtime.Serialization.dll -r:System.Core.dll \
  -resource:res/roads.bin.gz,roads.bin.gz -resource:res/skills.json,skills.json -out:build/MidnightClub.dll -recurse:'src/*.cs'
