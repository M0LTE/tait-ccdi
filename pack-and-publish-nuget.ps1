cd tait-ccdi
del .\bin\Release\m0lte.tait-ccdi.*.nupkg
dotnet pack
dotnet nuget push .\bin\Release\m0lte.tait-ccdi.*.nupkg -k $env:m0lte_nuget_key -s https://api.nuget.org/v3/index.json
cd ..