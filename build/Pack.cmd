@echo off
pushd ..
set PUBLIC_BUILD=1
rmdir /S /Q artifacts\nupkg 2>nul
dotnet pack -c:Release
popd
