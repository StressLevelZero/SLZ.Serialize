dotnet restore
dotnet build --configuration Release --no-restore --no-incremental /p:ContinuousIntegrationBuild=true
dotnet test --configuration Release --no-build /p:ContinuousIntegrationBuild=true
dotnet pack --configuration Release --no-build /p:ContinuousIntegrationBuild=true