call :pauseOnError msbuild -p:configuration=Release -t:clean
call :pauseOnError msbuild -p:configuration=Release -t:BuildAllConfigurations
call :pauseOnError msbuild -p:configuration=Release -t:BuildAKV
call :pauseOnError msbuild -p:configuration=Release -t:BuildAKVAllOS
call :pauseOnError msbuild -p:configuration=Release -t:GenerateAKVProviderNugetPackage

goto :eof

:pauseOnError
%*
if ERRORLEVEL 1 pause
goto :eof
