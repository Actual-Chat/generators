@echo off
call Pack.cmd
pushd ..\artifacts\nupkg
dotnet nuget push ActualLab.Generators.*.nupkg -k %ACTUALCHAT_NUGET_API_KEY% -s https://api.nuget.org/v3/index.json
popd
