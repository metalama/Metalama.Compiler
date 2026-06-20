# Per-repo customization of the container environment, dot-sourced by DockerBuild.ps1 after the
# standard environment-variable set is assembled and before eng-Metalama/.g/Init.g.ps1 is written.
# Mutate $ContainerEnvironmentVariables in place (add / change / remove keys).
# See "Customizing the container environment (product repos)" in PostSharp.Engineering's
# doc/dockerbuild.md.

param(
    [hashtable] $ContainerEnvironmentVariables,  # full normal var set; mutate in place
    [string]    $DockerfileName,                 # leaf Dockerfile, e.g. 'build.Dockerfile' / 'claude.Dockerfile'
    [switch]    $Claude                          # true when running the Claude leaf
)

# Work around https://github.com/dotnet/arcade/issues/15970: building via the Arcade toolset
# (microsoft.dotnet.arcade.sdk/<ver>/tools/Build.proj) fails to resolve packages from the GLOBAL
# NuGet cache -- e.g. "NETSDK1064: Package Microsoft.CodeAnalysis.Analyzers ... was not found"
# (also BannedApiAnalyzers, MessagePackAnalyzer) -- even though the packages are physically on disk.
# Restore into a repo-local '.packages' folder instead, exactly as azure-pipelines.yml and
# eng/make-bootstrap.ps1 do. This OVERRIDES DockerBuild.ps1's default of $USERPROFILE\.nuget\packages
# (which, on the machine-account TeamCity agent, is the global C:\WINDOWS\system32\config\systemprofile
# cache that triggers the bug).
#
# Applies to every DockerBuild leaf (build and Claude). The container bind-mounts the repo at the
# same path as the host, so an absolute host path resolves identically inside the container.
$repoRoot = Split-Path -Parent $PSScriptRoot
$ContainerEnvironmentVariables['NUGET_PACKAGES'] = Join-Path $repoRoot '.packages'
$ContainerEnvironmentVariables['RESTORENOCACHE'] = 'true'
