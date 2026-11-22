#! /bin/bash
PLATFORM=$1
TYPE=$2
COVERAGE=$3
WHERE="Category!=ManualTest"
FILES=( "Bibliophilarr.Api.Test.dll" "Bibliophilarr.Common.Test.dll" "Bibliophilarr.Core.Test.dll" "Bibliophilarr.Host.Test.dll" "Bibliophilarr.Integration.Test.dll" "Bibliophilarr.Libraries.Test.dll" "Bibliophilarr.Mono.Test.dll" "Bibliophilarr.Update.Test.dll" "Bibliophilarr.Windows.Test.dll" )
ASSEMBLIES=()
TEST_LOG_FILE="TestLog.txt"

echo "test dir: $TEST_DIR"
if [ -z "$TEST_DIR" ]; then
    TEST_DIR="."
fi

if [ -d "$TEST_DIR/_tests" ]; then
  TEST_DIR="$TEST_DIR/_tests"
fi

rm -f "$TEST_LOG_FILE"

# Uncomment to log test output to a file instead of the console
export BIBLIOPHILARR_TESTS_LOG_OUTPUT="File"

VSTEST_PARAMS="--logger:nunit;LogFilePath=TestResult.xml"

if [ "$PLATFORM" = "Mac" ]; then

  export DYLD_FALLBACK_LIBRARY_PATH="$TEST_DIR:$MONOPREFIX/lib:/usr/local/lib:/lib:/usr/lib"
  echo $DYLD_FALLBACK_LIBRARY_PATH
  mono --version

  # To debug which libraries are being loaded:
  # export DYLD_PRINT_LIBRARIES=YES
fi

if [ "$PLATFORM" = "Windows" ]; then
  mkdir -p "$ProgramData/Bibliophilarr"
  WHERE="$WHERE&Category!=LINUX"
elif [ "$PLATFORM" = "Linux" ] || [ "$PLATFORM" = "Mac" ] ; then
  mkdir -p ~/.config/Bibliophilarr
  WHERE="$WHERE&Category!=WINDOWS"
else
  echo "Platform must be provided as first arguement: Windows, Linux or Mac"
  exit 1
fi

if [ "$TYPE" = "Unit" ]; then
  WHERE="$WHERE&Category!=IntegrationTest"
elif [ "$TYPE" = "Integration" ] || [ "$TYPE" = "int" ] ; then
  WHERE="$WHERE&Category=IntegrationTest"
elif [ "$TYPE" = "Automation" ] ; then
  echo 'Browser automation now uses tests/browser/smoke.py. See tests/browser/CATALOGUE.md.'
  exit 2
else
  echo "Type must be provided as second argument: Unit or Integration"
  exit 2
fi

for assembly in "${FILES[@]}"; do
  if [ ! -f "$TEST_DIR/$assembly" ]; then
    echo "Missing $TEST_DIR/$assembly. Set TEST_DIR to the built test directory."
    exit 1
  fi
  ASSEMBLIES+=("$TEST_DIR/$assembly")
done

DOTNET_PARAMS=("${ASSEMBLIES[@]}" "--filter:$WHERE" "$VSTEST_PARAMS")

if [ "$COVERAGE" = "Coverage" ]; then
  script_dir="$(cd "$(dirname "$0")" && pwd)"
  settings="$script_dir/coverlet.runsettings"
  if [ ! -f "$settings" ]; then
    settings="$script_dir/src/coverlet.runsettings"
  fi
  if [ ! -f "$settings" ]; then
    echo "Missing coverlet.runsettings beside the runner or in its source directory."
    exit 1
  fi
  EXIT_CODE=0
  for assembly in "${ASSEMBLIES[@]}"; do
    # Coverlet temporarily instruments shared DLLs. Complete one assembly before starting the next.
    assembly_name="$(basename "$assembly" .dll)"
    results_dir="$PWD/CoverageResults/$assembly_name"
    dotnet test "$assembly" "--filter:$WHERE" "--logger:nunit;LogFilePath=$results_dir/TestResult.xml" \
      --settings:"$settings" --results-directory:"$results_dir" || EXIT_CODE=$?
  done
elif [ "$COVERAGE" = "Test" ] ; then
  dotnet test "${DOTNET_PARAMS[@]}"
  EXIT_CODE=$?
else
  echo "Run Type must be provided as third argument: Coverage or Test"
  exit 3
fi

if [ "$EXIT_CODE" -ne 0 ]; then
  echo "Failed tests: EXIT_CODE=$EXIT_CODE"
  exit $EXIT_CODE
fi
