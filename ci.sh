dotnet pack Koala/Koala.csproj -c Release -o $PWD/nupkgs
versionedNupkg=$( find nupkgs/Koala.*.nupkg )
dotnet nuget push $versionedNupkg --source https://api.nuget.org/v3/index.json -k $NUGET_API_KEY