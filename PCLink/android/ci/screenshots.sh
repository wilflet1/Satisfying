#!/usr/bin/env bash
# Installs the debug APK on the emulator, walks through the screens and
# saves screenshots to shots/. Fails if the app crashes.
set -euo pipefail
cd "$(dirname "$0")/.."
PKG=dev.pclink
mkdir -p shots
adb install -r -g app/build/outputs/apk/debug/app-debug.apk

shot() { sleep 2; adb exec-out screencap -p > "shots/$1.png"; echo "shot $1"; }
ui() { adb shell uiautomator dump /sdcard/ui.xml > /dev/null; adb pull /sdcard/ui.xml ui.xml > /dev/null; }
tap() { ui; python3 ci/tap.py "$1"; }
start() { adb shell am start -W -n "$PKG/.MainActivity" > /dev/null; sleep 3; }
scroll_down() { adb shell input swipe 540 1900 540 500 400; }

# 1. First launch: the pairing screen.
start
shot 01-pairing

# 2. Pretend to be paired (debug builds allow run-as) to show the main screen.
adb shell am force-stop $PKG
cat > prefs.xml <<'XML'
<?xml version='1.0' encoding='utf-8' standalone='yes' ?>
<map>
    <string name="device_id">screenshot-phone</string>
    <string name="pc_id">screenshot-pc</string>
    <string name="pc_name">Studio PC</string>
    <string name="hosts">10.0.2.2</string>
    <string name="fp">0000000000000000000000000000000000000000000000000000000000000000</string>
    <string name="token">screenshot</string>
    <string name="mac">00:11:22:33:44:55</string>
    <boolean name="asked_notifications" value="true" />
</map>
XML
adb push prefs.xml /data/local/tmp/pclink.xml > /dev/null
adb shell run-as $PKG mkdir -p shared_prefs
adb shell run-as $PKG cp /data/local/tmp/pclink.xml shared_prefs/pclink.xml
start
shot 02-home-top
scroll_down
shot 03-home-middle
scroll_down
shot 04-home-bottom

# 3. Touchpad & keys, then with the keyboard open.
start
tap "Touchpad"
shot 05-remote
tap "Type or dictate"
shot 06-remote-keyboard
adb shell input keyevent KEYCODE_BACK
adb shell input keyevent KEYCODE_BACK

# 4. PC files while not connected.
start
tap "PC files"
shot 07-pc-files-offline
adb shell input keyevent KEYCODE_BACK

# 5. Large text and dark mode.
adb shell settings put system font_scale 1.3
adb shell am force-stop $PKG
start
shot 08-home-large-text
tap "Touchpad"
shot 09-remote-large-text
adb shell settings put system font_scale 1.0
adb shell cmd uimode night yes
adb shell am force-stop $PKG
start
shot 10-home-dark
scroll_down
shot 11-home-dark-middle

if adb logcat -d | grep -q "FATAL EXCEPTION"; then
  adb logcat -d | grep -A 30 "FATAL EXCEPTION"
  echo "The app crashed during the screenshot run" >&2
  exit 1
fi
