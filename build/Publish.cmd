@echo off
pushd ..
set PUBLIC_BUILD=1
rmdir /S /Q artifacts 2>nul
dotnet pack -c:Release

pushd artifacts\nupkg
call :publish ActualLab.Generators.nupkg
call :publish ActualLab.Generators.Abstractions.nupkg
popd
popd
goto :eof

:publish
dotnet nuget push %1 -k %ACTUALCHAT_NUGET_API_KEY% -s https://api.nuget.org/v3/index.json
goto :eof
