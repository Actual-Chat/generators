@echo off
pushd ..
set PUBLIC_BUILD=1
dotnet pack -c:Release
popd
