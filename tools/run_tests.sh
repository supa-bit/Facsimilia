#!/usr/bin/env bash
# Runs the Godot tests (CLAUDE.md, "Building and testing").
#   tools/run_tests.sh          quick: every test except the slow ones
#   tools/run_tests.sh full     every test in src/Tests
#   tools/run_tests.sh Name...  just these (e.g. VoiceTest OpeningTest)
# The slow ones each play years of the game or the whole map's people;
# together they are most of the 8 or so minutes a full run takes.
SLOW="AiTest DisasterTest PopulationBalanceTest NatureTest ChronicleEndTest TechTest DynastyLifeTest"
cd "$(dirname "$0")/.."
# Build first, as strictly as GitHub's Tests workflow does (warnings count as
# errors there), so tests never run an old build and a warning is caught here.
if ! build=$(dotnet build -nologo -warnaserror 2>&1); then
  echo "$build" | grep -E "error" | sort -u | head -20
  echo "BUILD FAILED (warnings count as errors, as on GitHub)"
  exit 1
fi
if [ "$1" = "full" ]; then
  names=$(ls src/Tests/*Test.cs | xargs -n1 basename | sed 's/\.cs$//')
elif [ $# -gt 0 ]; then
  names="$*"
else
  names=$(ls src/Tests/*Test.cs | xargs -n1 basename | sed 's/\.cs$//' | grep -vxF -f <(tr ' ' '\n' <<< "$SLOW"))
fi
pass=0; fail=0
for n in $names; do
  if out=$(timeout 1500 godot --headless --path . --script "res://src/Tests/$n.cs" 2>&1); then
    pass=$((pass + 1))
  else
    code=$?
    fail=$((fail + 1)); echo "FAILED $n (exit code $code)"
    # A failed check prints FAIL; a crash or a timeout (exit code 124) may not, so show the end of its output then.
    if echo "$out" | grep -qE "FAIL|rror"; then echo "$out" | grep -E "FAIL|rror" | head -5; else echo "$out" | tail -15; fi
  fi
done
echo "passed $pass failed $fail"
[ "$fail" -eq 0 ]
