#!/usr/bin/env bash
# Local gate: build, test, format check. Exits non-zero on the first failure.
#
#   build/gate.sh [--allow-narrowed-tests]
#
# The tests run as four processes at once: the Core tests, and the App tests in the lanes of build/app-test-lanes.txt (the UI tests of
# one process share one UI thread, so they run one at a time). Their test methods must add up to the full listing's, so no test is dropped.
# When less than 10 GB of memory is free (or SAVEPOINT_GATE_APP_LANES=1), the App tests run as one process instead, and the gate says so.
#
# SAVEPOINT_TEST_ARGS takes only a report (--report-*). Anything else, such as a filter or a retry of failed tests (the retry
# extension is no longer part of the test projects), is refused unless --allow-narrowed-tests says it is on purpose, because a run
# that tests part of the code or lets a failure pass would read as a passed gate.
set -euo pipefail

allow_narrowed=0
for argument in "$@"; do
  case "$argument" in
    --allow-narrowed-tests) allow_narrowed=1 ;;
    *) echo "gate: unknown argument $argument" >&2; exit 2 ;;
  esac
done

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

cd "$(dirname "${BASH_SOURCE[0]}")/.."

# Keep the machine responsive: lower priority, and caps one gate cannot exceed (override per run).
renice -n 10 $$ >/dev/null 2>&1 || true
build_nodes="${SAVEPOINT_GATE_BUILD_NODES:-4}"
# Core is the longest process (git work that needs a thread each), so it gets a third of the machine's logical processors: 4 on a
# small machine, up to 8 on a big one. SAVEPOINT_GATE_TEST_THREADS sets it by hand.
logical_processors="$(nproc 2>/dev/null || sysctl -n hw.logicalcpu 2>/dev/null || echo 4)"
default_threads=$(( logical_processors / 3 ))
[ "$default_threads" -lt 4 ] && default_threads=4
[ "$default_threads" -gt 8 ] && default_threads=8
test_threads="${SAVEPOINT_GATE_TEST_THREADS:-$default_threads}"
# The App lanes are mostly UI tests that wait for one UI thread each, so two threads serve a lane. At most core + 3 x 2 test threads.
app_threads=$(( test_threads < 2 ? test_threads : 2 ))
# One App process holds the UI tests and the rest, so it gets up to four.
app_single_threads=$(( test_threads < 4 ? test_threads : 4 ))

# Four test processes at once need about 10 GB free; below that the App tests run as one process, as before the lanes (the
# 7 GB Linux VM ran out of memory with four). SAVEPOINT_GATE_APP_LANES=1 forces one process and 3 forces the lanes (any number
# above 1 does); SAVEPOINT_GATE_AVAILABLE_MEMORY_MB replaces the reading of free memory. On Linux the reading is the smaller of
# MemAvailable and what is left of the container's cgroup limit; SAVEPOINT_GATE_MEMINFO_FILE and SAVEPOINT_GATE_CGROUP_DIR point it at other files
# (the tests use them). When the memory cannot be read at all, the single App process runs.
lanes_memory_mb=10240

# Prints what is left of the container's memory limit in MB (the limit less what the container already uses), or nothing when there
# is no limit (cgroup v2 says "max", v1 a number near 2^63). SAVEPOINT_GATE_CGROUP_DIR replaces /sys/fs/cgroup.
cgroup_limit_mb() {
  local dir="${SAVEPOINT_GATE_CGROUP_DIR:-/sys/fs/cgroup}" limit_file usage_file bytes used
  for limit_file in "$dir/memory.max" "$dir/memory/memory.limit_in_bytes"; do
    [ -r "$limit_file" ] || continue
    bytes="$(tr -d '[:space:]' <"$limit_file")"
    if [[ "$bytes" =~ ^[0-9]{1,15}$ ]]; then
      case "$limit_file" in
        */memory.max) usage_file="$dir/memory.current" ;;
        *) usage_file="$dir/memory/memory.usage_in_bytes" ;;
      esac
      if [ -r "$usage_file" ]; then
        used="$(tr -d '[:space:]' <"$usage_file")"
        if [[ "$used" =~ ^[0-9]{1,15}$ ]]; then
          bytes=$(( bytes > used ? bytes - used : 0 ))
        fi
      fi
      printf '%s\n' $(( bytes / 1048576 ))
      return 0
    fi
  done
  return 0
}

# Prints the memory, in MB, that programs can use without the machine swapping or killing something, or nothing when it is unknown.
available_memory_mb() {
  if [ -n "${SAVEPOINT_GATE_AVAILABLE_MEMORY_MB:-}" ]; then
    printf '%s\n' "$SAVEPOINT_GATE_AVAILABLE_MEMORY_MB"
  elif [ -n "${SAVEPOINT_GATE_MEMINFO_FILE:-}" ] || [ -r /proc/meminfo ]; then
    # Inside a container /proc/meminfo shows the host's memory, so the container's own limit caps it.
    local meminfo="${SAVEPOINT_GATE_MEMINFO_FILE:-/proc/meminfo}" available limit
    [ -r "$meminfo" ] || return 0
    available="$(awk '/^MemAvailable:/ { print int($2 / 1024) }' "$meminfo")"
    [ -n "$available" ] || return 0
    limit="$(cgroup_limit_mb)"
    if [ -n "$limit" ] && [ "$limit" -lt "$available" ]; then available="$limit"; fi
    printf '%s\n' "$available"
  elif command -v vm_stat >/dev/null 2>&1; then
    # macOS: free, inactive and speculative pages can be handed out again.
    local page_size pages
    page_size="$(vm_stat | sed -n 's/.*page size of \([0-9][0-9]*\) bytes.*/\1/p')"
    pages="$(vm_stat | awk '/^Pages (free|inactive|speculative):/ { gsub(/\./, "", $NF); sum += $NF } END { print sum + 0 }')"
    [ -n "$page_size" ] && printf '%s\n' $(( pages * page_size / 1048576 ))
  fi
}

# Sets app_mode (lanes or one) and app_mode_reason, or refuses the run for a setting it does not know.
choose_app_mode() {
  local setting="${SAVEPOINT_GATE_APP_LANES:-}" memory
  if [ -n "$setting" ]; then
    case "$setting" in
      1) app_mode=one; app_mode_reason="SAVEPOINT_GATE_APP_LANES=1" ;;
      [2-9] | [1-9][0-9]*) app_mode=lanes; app_mode_reason="SAVEPOINT_GATE_APP_LANES=$setting" ;;
      *)
        echo "gate refused: SAVEPOINT_GATE_APP_LANES is '$setting'; use 1 for one App test process or 3 for the lanes." >&2
        exit 1
        ;;
    esac
    return
  fi
  memory="$(available_memory_mb)"
  if [ -z "$memory" ]; then
    # Unknown memory takes the layout that needs less of it.
    app_mode=one
    app_mode_reason="free memory could not be read; SAVEPOINT_GATE_APP_LANES=3 runs the lanes anyway"
  elif [ "$memory" -lt "$lanes_memory_mb" ]; then
    app_mode=one
    app_mode_reason="${memory} MB free is below ${lanes_memory_mb} MB; SAVEPOINT_GATE_APP_LANES=3 runs the lanes anyway"
  else
    app_mode=lanes
    app_mode_reason="${memory} MB free"
  fi
}

# SAVEPOINT_TEST_ARGS is for a report (--report-*). Any other argument, a filter in whatever spelling, a response file (@file) or
# an option that lets failures pass (a retry of failed tests among them), could make "gate passed" mean less than a full run, so
# it counts as narrowing. A word without dashes is allowed only as the value of an allowed option before it.
# Prints each argument that is not allowed, one per line.
narrowing_arguments() {
  local argument after_allowed=0
  for argument in "$@"; do
    case "$argument" in
      --report-*)
        case "$argument" in
          *=*) after_allowed=0 ;;
          *) after_allowed=1 ;;
        esac
        ;;
      -* | @*)
        after_allowed=0
        printf '%s\n' "$argument"
        ;;
      *)
        if [ "$after_allowed" -eq 1 ]; then
          after_allowed=0
        else
          printf '%s\n' "$argument"
        fi
        ;;
    esac
  done
}

# Said first, so a leftover value in the shell is seen before anything runs.
narrowing=""
if [ -n "${SAVEPOINT_TEST_ARGS:-}" ]; then
  echo "==> extra test arguments (SAVEPOINT_TEST_ARGS): ${SAVEPOINT_TEST_ARGS}"
  read -r -a test_args <<<"${SAVEPOINT_TEST_ARGS}"
  while read -r argument; do
    narrowing="$narrowing $argument"
  done < <(narrowing_arguments "${test_args[@]}")
  if [ -n "$narrowing" ] && [ "$allow_narrowed" -eq 0 ]; then
    echo "gate refused: SAVEPOINT_TEST_ARGS holds${narrowing}, which could run only some of the tests or let a failure pass. Only a report (--report-*) is allowed there; test retries are not available. Unset it, or pass --allow-narrowed-tests if that is on purpose." >&2
    exit 1
  fi
  if [ -n "$narrowing" ]; then
    echo "==> NARROWED on purpose: the extra arguments can leave tests out or let a failure pass, so a pass is not a full gate."
  fi
fi

app_mode=lanes
app_mode_reason=""
choose_app_mode

echo "==> build"
dotnet build Savepoint.slnx -c Release -warnaserror "-m:${build_nodes}" -nodeReuse:false

# The test processes make their temp files in a folder on the disk the gate runs on. Some systems (Ubuntu 26.04) mount /tmp as
# RAM, and four processes at once filled it. It is made fresh and removed at the end, and only the test steps use it: a Unix socket
# path is short, and the build's server pipes live under the temp folder too.
gate_tmp="$PWD/artifacts/tmp"
rm -rf "$gate_tmp"
mkdir -p "$gate_tmp"
trap 'rm -rf "$gate_tmp"' EXIT
temp_env=(TMPDIR="$gate_tmp" TMP="$gate_tmp" TEMP="$gate_tmp")

# Starts one test process in the background. Each line it prints appears at once with its name in front (and is kept in
# <results>/<name>.log), so a process that hangs shows where it stopped; the exit code is the process's own. Arguments after the
# thread count are its filter.
test_pids=()
test_names=()
start_test_process() {
  local name="$1" project="$2" threads="$3"
  shift 3
  (
    set +e
    started=$SECONDS
    # SAVEPOINT_TEST_ARGS is empty unless the caller sets it (a report); it goes to every process.
    # shellcheck disable=SC2086
    env "${temp_env[@]}" dotnet test --project "$project" -c Release --no-build --max-threads "$threads" --report-xunit --report-xunit-filename "$name.xunit" --results-directory "$results_dir" "$@" ${SAVEPOINT_TEST_ARGS:-} 2>&1 \
      | tee "$results_dir/$name.log" \
      | while IFS= read -r line || [ -n "$line" ]; do printf '[%s] %s\n' "$name" "$line"; done
    code=${PIPESTATUS[0]}
    echo "==> test process '$name' ended with exit code $code after $((SECONDS - started)) s"
    exit "$code"
  ) &
  test_pids+=($!)
  test_names+=("$name")
}

# Returns when every test process has ended, saying every SAVEPOINT_GATE_HEARTBEAT_SECONDS (300) which are still going.
wait_for_test_processes() {
  local interval="${SAVEPOINT_GATE_HEARTBEAT_SECONDS:-300}" started=$SECONDS next running i
  next=$interval
  while true; do
    running=""
    for i in "${!test_pids[@]}"; do
      if kill -0 "${test_pids[$i]}" 2>/dev/null; then running="${running:+$running, }${test_names[$i]}"; fi
    done
    [ -z "$running" ] && break
    if [ $((SECONDS - started)) -ge "$next" ]; then
      echo "==> still running after $((SECONDS - started)) s: $running"
      next=$((next + interval))
    fi
    sleep 1
  done
}

# The gate always asks for the xunit report itself (the skip check reads it), so SAVEPOINT_TEST_ARGS must not.
results_dir="$PWD/artifacts/test-results"
rm -rf "$results_dir"
mkdir -p "$results_dir"
if [ -n "$narrowing" ]; then
  echo "==> test"
  # A filter of the caller's cannot be combined with the lanes' filters, so a narrowed run keeps the modules whole.
  # shellcheck disable=SC2086
  env "${temp_env[@]}" dotnet test Savepoint.slnx -c Release --no-build --max-parallel-test-modules 2 --max-threads "${test_threads}" --report-xunit --results-directory "$results_dir" ${SAVEPOINT_TEST_ARGS:-}
else
  app_project="tests/Savepoint.App.Tests"
  listing="$(dotnet test --project "$app_project" -c Release --no-build --list-tests 2>&1)" || {
    echo "listing the App tests failed:" >&2
    echo "$listing" >&2
    exit 1
  }
  # A listed test is "  <class>.<method>(<data>)". A theory whose data cannot be listed shows once in the listing and once per row in a
  # report, so the check counts methods, not cases.
  # LC_ALL=C: names that differ only in case are different methods, as they are in the Windows gate.
  app_test_count="$({ printf '%s\n' "$listing" | grep -E '^  [A-Za-z_][A-Za-z0-9_.+]*\.[A-Za-z_][A-Za-z0-9_]*(\(|$)' || true; } | cut -d'(' -f1 | LC_ALL=C sort -u | wc -l | tr -d ' ')"
  if [ "$app_test_count" -eq 0 ]; then
    echo "listing the App tests failed: no tests in:" >&2
    echo "$listing" >&2
    exit 1
  fi

  # The App test processes: one per line of build/app-test-lanes.txt, then "rest", which runs everything the lines do not name.
  lane_names=()
  lane_namespaces=()
  while read -r lane namespace extra || [ -n "$lane" ]; do
    case "$lane" in '' | '#'*) continue ;; esac
    if [ -z "$namespace" ] || [ -n "$extra" ] || [ "$lane" = rest ]; then
      echo "build/app-test-lanes.txt: expected '<name> <namespace>' (the name 'rest' is taken), got '$lane $namespace $extra'" >&2
      exit 1
    fi
    lane_names+=("$lane")
    lane_namespaces+=("$namespace")
  done <build/app-test-lanes.txt

  app_reports=()
  if [ "$app_mode" = one ]; then
    echo "==> test (Core, and ${app_test_count} App test methods): App tests in one process (${app_mode_reason})"
    start_test_process core tests/Savepoint.Core.Tests "$test_threads"
    start_test_process app "$app_project" "$app_single_threads"
    app_reports+=("$results_dir/app.xunit")
  else
    echo "==> test (Core, and ${app_test_count} App test methods): App tests in lanes (${app_mode_reason})"
    start_test_process core tests/Savepoint.Core.Tests "$test_threads"
    rest_filter=()
    for i in "${!lane_names[@]}"; do
      start_test_process "app-${lane_names[$i]}" "$app_project" "$app_threads" --filter-namespace "${lane_namespaces[$i]}"
      rest_filter+=(--filter-not-namespace "${lane_namespaces[$i]}")
      app_reports+=("$results_dir/app-${lane_names[$i]}.xunit")
    done
    start_test_process app-rest "$app_project" "$app_threads" ${rest_filter[@]+"${rest_filter[@]}"}
    app_reports+=("$results_dir/app-rest.xunit")
  fi

  wait_for_test_processes
  failed=""
  for i in "${!test_pids[@]}"; do
    if ! wait "${test_pids[$i]}"; then
      failed="$failed ${test_names[$i]}"
    fi
  done
  if [ -n "$failed" ]; then
    echo "test failed in:${failed}" >&2
    exit 1
  fi

  echo "==> test count"
  bash build/check-test-counts.sh "$app_test_count" "${app_reports[@]}"
fi

# A skip probe that breaks turns real tests into skips and the run still passes; this fails on any skip the list does not name.
# SAVEPOINT_EXPECTED_SKIPS names the list in build/expected-skips; Linux defaults to linux-ci, and no macOS list exists yet (macOS only checks that the script parse test ran).
expected_skips="${SAVEPOINT_EXPECTED_SKIPS:-}"
if [ -z "$expected_skips" ]; then
  case "$(uname -s)" in
    Linux) expected_skips="linux-ci" ;;
  esac
fi
if [ -n "$expected_skips" ]; then
  echo "==> skips (expected: build/expected-skips/${expected_skips}.txt)"
  dotnet run --project tools/SkipCheck -c Release --no-build -- --reports "$results_dir" --expected "build/expected-skips/${expected_skips}.txt"
else
  # No list for this system yet, but the script parse test needs PowerShell 7 everywhere, so its skip must not pass silently.
  echo "==> skips: no list for $(uname -s); checking that the script parse test ran"
  dotnet run --project tools/SkipCheck -c Release --no-build -- --reports "$results_dir" --never-skip Savepoint.Core.Tests.Build.BuildScriptSyntaxTests.Every_powershell_script_under_build_parses
fi

echo "==> format"
dotnet format Savepoint.slnx --verify-no-changes

if [ -n "$narrowing" ]; then
  echo "gate passed, NARROWED: not a full gate"
else
  echo "gate passed"
fi
