# NexJob — packaging helpers
# Usage:
#   make pack                   → pack all projects (version from Directory.Build.props)
#   make pack VERSION=0.1.0     → pack with explicit version
#   make publish                → push ./nupkg/*.nupkg to NuGet.org (requires NUGET_API_KEY env var)
#   make clean-pack             → delete ./nupkg output directory

VERSION_PREFIX ?=
VERSION_SUFFIX ?=

PACK_ARGS = --configuration Release --output ./nupkg
ifdef VERSION_PREFIX
  PACK_ARGS += -p:VersionPrefix=$(VERSION_PREFIX) -p:VersionSuffix=
endif

PROJECTS = \
  src/NexJob/NexJob.csproj \
  src/NexJob.Postgres/NexJob.Postgres.csproj \
  src/NexJob.MongoDB/NexJob.MongoDB.csproj \
  src/NexJob.Dashboard/NexJob.Dashboard.csproj

.PHONY: pack publish clean-pack

pack:
	dotnet build --configuration Release
	$(foreach proj,$(PROJECTS),dotnet pack $(proj) --no-build $(PACK_ARGS);)

publish:
	@test -n "$(NUGET_API_KEY)" || (echo "Error: NUGET_API_KEY is not set" && exit 1)
	dotnet nuget push "./nupkg/*.nupkg" \
		--api-key $(NUGET_API_KEY) \
		--source https://api.nuget.org/v3/index.json \
		--skip-duplicate

clean-pack:
	rm -rf ./nupkg
