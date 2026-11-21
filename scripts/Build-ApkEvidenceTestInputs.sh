#!/usr/bin/env bash
# Rebuild the small, unsigned APK-reader test specimens. No phone or app data.
set -euo pipefail
export LC_ALL=C TZ=UTC

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
src="$root/android/apk-fixtures"
out="$root/tests/Recorder.Tests/TestData/AndroidApk"
: "${JAVA_HOME:?Use the JDK pinned by Build-CompanionApp.ps1}"
: "${ANDROID_BUILD_TOOLS:?Set this to the pinned Android Build-Tools directory}"
: "${ANDROID_PLATFORM_JAR:?Set this to the pinned Android platform android.jar}"
: "${COMPOSE_UI_AAR:?Use the official AndroidX Compose UI Android 1.7.8 AAR}"
: "${HERMESC:?Use the Linux Hermes compiler bundled with React Native 0.76.9}"
scratch=$(mktemp -d)
trap 'rm -rf -- "$scratch"' EXIT
mkdir -p "$scratch"/{generated,classes,native-dex,compose,compose-dex} "$out"

aapt2="$ANDROID_BUILD_TOOLS/aapt2"
java="$JAVA_HOME/bin/java"
javac="$JAVA_HOME/bin/javac"
d8=("$java" -cp "$ANDROID_BUILD_TOOLS/lib/d8.jar" com.android.tools.r8.D8
    --release --min-api 30 --lib "$ANDROID_PLATFORM_JAR")
snapshot="$scratch/native-web.zip"
"$aapt2" compile --dir "$src/res" -o "$scratch/resources.zip"
"$aapt2" link -I "$ANDROID_PLATFORM_JAR" --manifest "$src/AndroidManifest.xml" \
    --min-sdk-version 30 --target-sdk-version 37 --version-code 1 --version-name 1.0 \
    --java "$scratch/generated" -A "$src/assets" -o "$snapshot" "$scratch/resources.zip"
mapfile -d '' native_sources < <(find "$src/src" "$scratch/generated" -name '*.java' -print0 | sort -z)
"$javac" --release 11 -g:none -classpath "$ANDROID_PLATFORM_JAR" -d "$scratch/classes" "${native_sources[@]}"
mapfile -d '' native_classes < <(find "$scratch/classes" -name '*.class' -print0 | sort -z)
"${d8[@]}" --output "$scratch/native-dex" "${native_classes[@]}"
touch -t 200901010000 "$scratch/native-dex/classes.dex"
zip -qXj "$snapshot" "$scratch/native-dex/classes.dex"

unzip -p "$COMPOSE_UI_AAR" classes.jar > "$scratch/compose-ui.jar"
"$javac" --release 11 -g:none -classpath "$scratch/compose-ui.jar" \
    -d "$scratch/compose" "$src/matrix/ComposeFixtureCalls.java"
"${d8[@]}" --classpath "$scratch/compose-ui.jar" --output "$scratch/compose-dex" \
    "$scratch/compose/org/example/synthetic/ComposeFixtureCalls.class"
(
    cd -- "$src/matrix"
    "$HERMESC" -O -emit-binary -out="$scratch/known-props.hbc" known-props.js
)

cp -- "$snapshot" "$out/native-web.zip"
cp -- "$scratch/compose-dex/classes.dex" "$out/compose-symbols.dex"
cp -- "$scratch/known-props.hbc" "$out/known-props.hbc"
printf 'Generated test inputs in %s\n' "$out"
