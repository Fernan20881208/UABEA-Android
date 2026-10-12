#!/usr/bin/env bash
set -euo pipefail
mkdir -p artifacts/android-tests
adb install -r src/Uabea.Android/bin/Debug/net10.0-android36.0/android-x64/com.zaid.uabea-Signed.apk
activity=$(adb shell cmd package resolve-activity --brief com.zaid.uabea | tr -d '\r' | tail -1)
adb shell am start -W -n "$activity" --ez run_self_test true
for attempt in $(seq 1 90); do
  if adb exec-out run-as com.zaid.uabea cat files/selftest.json > artifacts/android-tests/selftest.json 2>/dev/null; then
    adb logcat -d > artifacts/android-tests/logcat.txt
    python -c 'import json; r=json.load(open("artifacts/android-tests/selftest.json")); print(r); assert r["passed"]'
    exit 0
  fi
  sleep 2
done
adb logcat -d > artifacts/android-tests/logcat.txt
echo 'Android self-test did not produce a result' >&2
exit 1
