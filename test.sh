#! /bin/bash
PLATFORM=$1
TYPE=$2
COVERAGE=$3
WHERE="Category!=ManualTest"
TEST_PATTERN="*Test.dll"
FILES=( "Bibliophilarr.Api.Test.dll" "Bibliophilarr.Automation.Test.dll" "Bibliophilarr.Common.Test.dll" "Bibliophilarr.Core.Test.dll" "Bibliophilarr.Host.Test.dll" "Bibliophilarr.Integration.Test.dll" "Bibliophilarr.Libraries.Test.dll" "Bibliophilarr.Mono.Test.dll" "Bibliophilarr.Update.Test.dll" "Bibliophilarr.Windows.Test.dll" )
ASSMEBLIES=""
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
  WHERE="$WHERE&Category!=IntegrationTest&Category!=AutomationTest"
elif [ "$TYPE" = "Integration" ] || [ "$TYPE" = "int" ] ; then
  WHERE="$WHERE&Category=IntegrationTest"
elif [ "$TYPE" = "Automation" ] ; then
  WHERE="$WHERE&Category=AutomationTest"
else
  echo "Type must be provided as second argument: Unit, Integration or Automation"
  exit 2
fi

for i in "${FILES[@]}";
  do ASSEMBLIES="$ASSEMBLIES $TEST_DIR/$i"
done

DOTNET_PARAMS="$ASSEMBLIES --filter:$WHERE $VSTEST_PARAMS"

if [ "$COVERAGE" = "Coverage" ]; then
  dotnet test $DOTNET_PARAMS --settings:"src/coverlet.runsettings" --results-directory:./CoverageResults
  EXIT_CODE=$?
elif [ "$COVERAGE" = "Test" ] ; then
  dotnet test $DOTNET_PARAMS
  EXIT_CODE=$?
else
  echo "Run Type must be provided as third argument: Coverage or Test"
  exit 3
fi

if [ "$EXIT_CODE" -ne 0 ]; then
  echo "Failed tests: EXIT_CODE=$EXIT_CODE"
  exit $EXIT_CODE
fi
