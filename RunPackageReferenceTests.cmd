@echo off

:: .NET CORE TEST CASES
echo Building .NET Core Tests
call :pauseOnError msbuild -t:Clean

:: ************** IMPORTANT NOTE BEFORE PROCEEDING WITH "PACKAGE" REFERENCE TYPE ***************
:: THESE ARE NOT STAND ALONE TEST COMMANDS AND NEED A DEVELOPER'S SPECIAL ATTENTION TO WORK. ATTEMPTING TO RUN THE ENTIRE SET OF COMMANDS AS-IS IS LIKELY TO FAIL!

:: CREATE A NUGET PACKAGE WITH BELOW COMMAND AND ADD TO LOCAL FOLDER + UPDATE NUGET CONFIG FILE TO READ FROM THAT LOCATION
:: > MSBuild -p:Configuration=Release

:: Based on `dotnet test` documentation, the `Platform` property has no effect on choosing the underlying architecture for the test execution environment.
:: You need to install and run the `dotnet` command for a specific architecture (x64, x86, Arm64).

:: Build and run tests for .NET 8.0 on AnyCPU
call :pauseOnError msbuild -t:BuildTests -p:TargetFramework=net8.0 -p:Configuration="Release" -p:ReferenceType=Package -p:Platform=AnyCPU
call :pauseOnError dotnet test "src\Microsoft.Data.SqlClient\tests\FunctionalTests\Microsoft.Data.SqlClient.Tests.csproj" -p:Configuration="Release" -f net8.0 --no-build -v n --filter "category!=nonnetcoreapptests&category!=failing&category!=nonwindowstests" -p:Platform="AnyCPU" -p:ReferenceType=Package -l:trx;LogFileName=..\..\..\..\..\artifacts\Results\package-net8.0-functional-anycpu.xml
call :pauseOnError dotnet test "src\Microsoft.Data.SqlClient\tests\ManualTests\Microsoft.Data.SqlClient.ManualTesting.Tests.csproj" -p:Configuration="Release" -f net8.0 --no-build -v n --filter "category!=nonnetcoreapptests&category!=failing&category!=nonwindowstests"  -p:Platform="AnyCPU" -p:ReferenceType=Package -l:trx;LogFileName=..\..\..\..\..\artifacts\Results\package-net8.0-manual-anycpu.xml

:: Build and run tests for .NET 8.0 on x64
call :pauseOnError msbuild -t:BuildTests -p:TargetFramework=net8.0 -p:Configuration="Release" -p:ReferenceType=Package -p:Platform=x64
call :pauseOnError dotnet test "src\Microsoft.Data.SqlClient\tests\FunctionalTests\Microsoft.Data.SqlClient.Tests.csproj" -p:Configuration="Release" -f net8.0 --no-build -v n --filter "category!=nonnetcoreapptests&category!=failing&category!=nonwindowstests" -p:Platform="x64" -p:ReferenceType=Package -l:trx;LogFileName=..\..\..\..\..\artifacts\Results\package-net8.0-functional-x64.xml
call :pauseOnError dotnet test "src\Microsoft.Data.SqlClient\tests\ManualTests\Microsoft.Data.SqlClient.ManualTesting.Tests.csproj" -p:Configuration="Release" -f net8.0 --no-build -v n --filter "category!=nonnetcoreapptests&category!=failing&category!=nonwindowstests"  -p:Platform="x64" -p:ReferenceType=Package -l:trx;LogFileName=..\..\..\..\..\artifacts\Results\package-net8.0-manual-x64.xml

:: Build and run tests for .NET 8.0 on Win32
call :pauseOnError msbuild -t:BuildTests -p:TargetFramework=net8.0 -p:Configuration="Release" -p:ReferenceType=Package -p:Platform=Win32
call :pauseOnError dotnet test "src\Microsoft.Data.SqlClient\tests\FunctionalTests\Microsoft.Data.SqlClient.Tests.csproj" -p:Configuration="Release" -f net8.0 --no-build -v n --filter "category!=nonnetcoreapptests&category!=failing&category!=nonwindowstests" -p:Platform="Win32" -p:ReferenceType=Package -l:trx;LogFileName=..\..\..\..\..\artifacts\Results\package-net8.0-functional-win32.xml
call :pauseOnError dotnet test "src\Microsoft.Data.SqlClient\tests\ManualTests\Microsoft.Data.SqlClient.ManualTesting.Tests.csproj" -p:Configuration="Release" -f net8.0 --no-build -v n --filter "category!=nonnetcoreapptests&category!=failing&category!=nonwindowstests"  -p:Platform="Win32" -p:ReferenceType=Package -l:trx;LogFileName=..\..\..\..\..\artifacts\Results\package-net8.0-manual-win32.xml

:: Build and run tests for .NET Framework 4.6.2 on AnyCPU
call :pauseOnError msbuild -t:BuildTests -p:TargetFramework=net462 -p:Configuration="Release" -p:ReferenceType=Package -p:Platform=AnyCPU
call :pauseOnError dotnet test "src\Microsoft.Data.SqlClient\tests\FunctionalTests\Microsoft.Data.SqlClient.Tests.csproj" -p:Platform="AnyCPU" -p:Configuration="Release" -f net462 --no-build -v n --filter "category!=nonnetfxtests&category!=failing&category!=nonwindowstests"  -p:ReferenceType=Package -l:trx;LogFileName=..\..\..\..\..\artifacts\Results\package-net462-functional-anycpu.xml
call :pauseOnError dotnet test "src\Microsoft.Data.SqlClient\tests\ManualTests\Microsoft.Data.SqlClient.ManualTesting.Tests.csproj" -p:Platform="AnyCPU" -p:Configuration="Release" -f net4623 --no-build -v n --filter "category!=nonnetfxtests&category!=failing&category!=nonwindowstests"  -p:ReferenceType=Package -l:trx;LogFileName=..\..\..\..\..\artifacts\Results\package-net462-manual-anycpu.xml

:: Run tests for .NET Framework 4.8 on AnyCPU
call :pauseOnError dotnet test "src\Microsoft.Data.SqlClient\tests\FunctionalTests\Microsoft.Data.SqlClient.Tests.csproj" -p:Platform="AnyCPU" -p:Configuration="Release" -f net48 --no-build -v n --filter "category!=nonnetfxtests&category!=failing&category!=nonwindowstests" -p:ReferenceType=Package -l:trx;LogFileName=..\..\..\..\..\artifacts\Results\package-net48-functional-anycpu.xml
call :pauseOnError dotnet test "src\Microsoft.Data.SqlClient\tests\ManualTests\Microsoft.Data.SqlClient.ManualTesting.Tests.csproj" -p:Platform="AnyCPU" -p:Configuration="Release" -f net48 --no-build -v n --filter "category!=nonnetfxtests&category!=failing&category!=nonwindowstests" -p:ReferenceType=Package -l:trx;LogFileName=..\..\..\..\..\artifacts\Results\package-net48-manual-anycpu.xml

:: Build and run tests for .NET Framework 4.6.2 on x64
call :pauseOnError msbuild -t:BuildTests -p:TargetFramework=net462 -p:Configuration="Release" -p:ReferenceType=Package -p:Platform=x64
call :pauseOnError dotnet test "src\Microsoft.Data.SqlClient\tests\FunctionalTests\Microsoft.Data.SqlClient.Tests.csproj" -p:Platform="x64" -p:Configuration="Release" -f net462 --no-build -v n --filter "category!=nonnetfxtests&category!=failing&category!=nonwindowstests" -p:ReferenceType=Package -l:trx;LogFileName=..\..\..\..\..\artifacts\Results\package-net462-functional-x64.xml
call :pauseOnError dotnet test "src\Microsoft.Data.SqlClient\tests\ManualTests\Microsoft.Data.SqlClient.ManualTesting.Tests.csproj" -p:Platform="x64" -p:Configuration="Release" -f net462 --no-build -v n --filter "category!=nonnetfxtests&category!=failing&category!=nonwindowstests" -p:ReferenceType=Package -l:trx;LogFileName=..\..\..\..\..\artifacts\Results\package-net462-manual-x64.xml

:: Run tests for .NET Framework 4.8 on x64
call :pauseOnError dotnet test "src\Microsoft.Data.SqlClient\tests\FunctionalTests\Microsoft.Data.SqlClient.Tests.csproj" -p:Platform="x64" -p:Configuration="Release" -f net48 --no-build -v n --filter "category!=nonnetfxtests&category!=failing&category!=nonwindowstests" -p:ReferenceType=Package -l:trx;LogFileName=..\..\..\..\..\artifacts\Results\package-net48-functional-x64.xml
call :pauseOnError dotnet test "src\Microsoft.Data.SqlClient\tests\ManualTests\Microsoft.Data.SqlClient.ManualTesting.Tests.csproj" -p:Platform="x64" -p:Configuration="Release" -f net48 --no-build -v n --filter "category!=nonnetfxtests&category!=failing&category!=nonwindowstests" -p:ReferenceType=Package -l:trx;LogFileName=..\..\..\..\..\artifacts\Results\package-net48-manual-x64.xml

:: Build and run tests for .NET Framework 4.6.2 on Win32
call :pauseOnError msbuild -t:BuildTests -p:TargetFramework=net462 -p:Configuration="Release" -p:ReferenceType=Package -p:Platform=Win32
call :pauseOnError dotnet test "src\Microsoft.Data.SqlClient\tests\FunctionalTests\Microsoft.Data.SqlClient.Tests.csproj" -p:Platform="Win32" -p:Configuration="Release" -f net462 --no-build -v n --filter "category!=nonnetfxtests&category!=failing&category!=nonwindowstests" -p:ReferenceType=Package -l:trx;LogFileName=..\..\..\..\..\artifacts\Results\package-net462-functional-win32.xml
call :pauseOnError dotnet test "src\Microsoft.Data.SqlClient\tests\ManualTests\Microsoft.Data.SqlClient.ManualTesting.Tests.csproj" -p:Platform="Win32" -p:Configuration="Release" -f net462 --no-build -v n --filter "category!=nonnetfxtests&category!=failing&category!=nonwindowstests" -p:ReferenceType=Package -l:trx;LogFileName=..\..\..\..\..\artifacts\Results\package-net462-manual-win32.xml

:: Run tests for .NET Framework 4.8 on Win32
call :pauseOnError dotnet test "src\Microsoft.Data.SqlClient\tests\FunctionalTests\Microsoft.Data.SqlClient.Tests.csproj" -p:Platform="Win32" -p:Configuration="Release" -f net48 --no-build -v n --filter "category!=nonnetfxtests&category!=failing&category!=nonwindowstests" -p:ReferenceType=Package -l:trx;LogFileName=..\..\..\..\..\artifacts\Results\package-net48-functional-Win32.xml
call :pauseOnError dotnet test "src\Microsoft.Data.SqlClient\tests\ManualTests\Microsoft.Data.SqlClient.ManualTesting.Tests.csproj" -p:Platform="Win32" -p:Configuration="Release" -f net48 --no-build -v n --filter "category!=nonnetfxtests&category!=failing&category!=nonwindowstests" -p:ReferenceType=Package -l:trx;LogFileName=..\..\..\..\..\artifacts\Results\package-net48-manual-Win32.xml

goto :eof

:pauseOnError
%*
if ERRORLEVEL 1 pause
goto :eof
