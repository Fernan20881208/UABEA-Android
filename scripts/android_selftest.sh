#!/usr/bin/env bash
set -euo pipefail
mkdir -p artifacts/android-tests
adb install --no-incremental -r src/Uabea.Android/bin/Debug/net10.0-android36.0/android-x64/com.zaid.uabea-Signed.apk
adb shell run-as com.zaid.uabea rm -f files/selftest.json files/classdata-result.json
# Grant notification permission on API 36 before startup to verify a real posted status.
adb shell pm grant com.zaid.uabea android.permission.POST_NOTIFICATIONS
adb logcat -c
activity=$(adb shell cmd package resolve-activity --brief com.zaid.uabea | tr -d '\r' | tail -1)
adb shell am start -W -n "$activity" --ez run_self_test true
# Strict first-stage test: require that classdata has loaded and its SHA-256
# was verified, before starting the full engine round-trip validation.
classdata_complete=0
for stage_attempt in $(seq 1 35); do
  adb exec-out run-as com.zaid.uabea cat files/classdata-result.json > artifacts/android-tests/classdata-pending.txt 2>/dev/null || true
  if python -c 'import json; r=json.load(open("artifacts/android-tests/classdata-pending.txt")); assert isinstance(r.get("passed"), bool)' 2>/dev/null; then
    mv artifacts/android-tests/classdata-pending.txt artifacts/android-tests/classdata-result.json
    python -c 'import json; r=json.load(open("artifacts/android-tests/classdata-result.json")); print("CLASSDATA:",r); assert r["passed"]'
    classdata_complete=1
    break
  fi
  sleep 2
done
if [ "$classdata_complete" -ne 1 ]; then
  adb logcat -d > artifacts/android-tests/logcat.txt
  adb logcat -d -s UabeaSelfTest:V Uabea:V
  adb exec-out screencap -p > artifacts/android-tests/app-screen.png
  echo "Unity class database did not complete its incremental UI loop" >&2
  exit 1
fi
for attempt in $(seq 1 90); do
  # exec-out may return zero even when remote cat reports ENOENT on stdout.
  # Wait for a complete report; never mistake transport success for test success.
  adb exec-out run-as com.zaid.uabea cat files/selftest.json > artifacts/android-tests/report-pending.txt 2>/dev/null || true
  if python -c 'import json; r=json.load(open("artifacts/android-tests/report-pending.txt")); assert isinstance(r.get("passed"), bool)' 2>/dev/null; then
    mv artifacts/android-tests/report-pending.txt artifacts/android-tests/selftest.json
    adb logcat -d > artifacts/android-tests/logcat.txt
    adb exec-out screencap -p > artifacts/android-tests/app-screen.png
    python -c 'import json; r=json.load(open("artifacts/android-tests/selftest.json")); print(r); assert r["passed"]'
    adb shell dumpsys notification --noredact > artifacts/android-tests/notifications.txt
    if ! grep -Fq "Base de clases Unity lista" artifacts/android-tests/notifications.txt; then
      echo "Unity database completion notification was not posted" >&2
      exit 1
    fi

    # A passing engine test does not prove that the user can press the buttons.
    # Verify cold-start interaction by opening Tools and loading the sample bundle.
    adb shell am force-stop com.zaid.uabea
    adb shell am start -W -n "$activity"
    sleep 2
    capture_ui() {
      adb shell uiautomator dump /sdcard/uabea-ui.xml >/dev/null
      adb pull /sdcard/uabea-ui.xml artifacts/android-tests/ui.xml >/dev/null
    }
    tap_node() {
      local point
      point="$(python scripts/android_ui.py artifacts/android-tests/ui.xml point "$1")"
      adb shell input tap $point
    }
    capture_ui
    python scripts/android_ui.py artifacts/android-tests/ui.xml assert "Herramientas"
    tap_node "Herramientas"
    sleep 1
    capture_ui
    python scripts/android_ui.py artifacts/android-tests/ui.xml assert "Abrir demostración"
    tap_node "Abrir demostración"
    loaded=0
    for ui_attempt in $(seq 1 25); do
      sleep 2
      capture_ui
      if python scripts/android_ui.py artifacts/android-tests/ui.xml assert "fixture.bundle · 6 objetos"; then
        loaded=1
        break
      fi
    done
    adb exec-out screencap -p > artifacts/android-tests/app-screen.png
    if [ "$loaded" -ne 1 ]; then
      adb logcat -d > artifacts/android-tests/logcat.txt
      echo "App UI failed to open demonstration bundle" >&2
      exit 1
    fi
    echo 'PASS: tapped Tools, launched demonstration, showed 6 Unity objects.'
    exit 0
  fi
  sleep 2
done
adb logcat -d > artifacts/android-tests/logcat.txt
adb logcat -d -s Uabea:V UabeaSelfTest:V
adb exec-out screencap -p > artifacts/android-tests/app-screen.png
adb exec-out run-as com.zaid.uabea cat files/selftest-progress.txt > artifacts/android-tests/progress.txt || true
adb shell uiautomator dump /sdcard/uabea-ui.xml || true
adb pull /sdcard/uabea-ui.xml artifacts/android-tests/ui.xml || true
adb shell run-as com.zaid.uabea ls -l files > artifacts/android-tests/private-files.txt || true
echo 'Android self-test did not produce a result' >&2
exit 1
