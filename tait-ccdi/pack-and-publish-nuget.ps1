dotnet pack
dotnet nuget push .\bin\Release\m0lte.tait-ccdi.0.0.4.nupkg -k $env:m0lte_nuget_key -s https://api.nuget.org/v3/index.json