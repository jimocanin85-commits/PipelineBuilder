namespace PipelineBuilder.Core.Yaml;

/// <summary>
/// Bash shared by the steps that run on Linux servers. Command substitution is written with
/// backticks, never <c>$(...)</c>, because Azure DevOps reads <c>$(name)</c> as a pipeline variable.
/// </summary>
public static class BashSnippets
{
    /// <summary>Stop at the first failing command, unset variable or failing pipe.</summary>
    public const string Strict = "set -euo pipefail";

    /// <summary>
    /// Defines <c>sync_folder SOURCE DESTINATION</c>: makes the destination an exact copy of the source.
    /// Uses rsync when it is installed and plain <c>cp</c> otherwise.
    /// </summary>
    public const string SyncFolderFunction = """
sync_folder() {
  # Stop if the folder is not set, so nothing is copied to or deleted from the wrong place.
  case "$2" in
    ''|/|\$\(*) echo "##vso[task.logissue type=error]The folder to copy to is not set: '$2'. Check the pipeline variables."; exit 1 ;;
  esac
  mkdir -p "$2"
  if command -v rsync >/dev/null 2>&1; then
    rsync -a --delete "$1"/ "$2"/
  else
    find "$2" -mindepth 1 -delete
    cp -a "$1"/. "$2"/
  fi
}
""";

    /// <summary>
    /// Sets <c>source</c> to a folder containing the package: the package itself when it is a
    /// folder, or an extracted copy when it is a zip file. Expects <c>package</c> to be set.
    /// </summary>
    public const string ResolvePackageSource = """
if [[ "$package" == *.zip ]]; then
  source="$AGENT_TEMPDIRECTORY/package"
  rm -rf "$source"
  mkdir -p "$source"
  unzip -q "$package" -d "$source"
else
  source="$package"
fi
""";

    /// <summary>
    /// Defines <c>as_root COMMAND...</c>: runs the command directly when the agent is root, and through
    /// <c>sudo</c> (without a password prompt) otherwise. Used for <c>systemctl</c>.
    /// </summary>
    public const string AsRootFunction = """
as_root() {
  if [ "`id -u`" -eq 0 ]; then "$@"; else sudo -n "$@"; fi
}
""";
}
