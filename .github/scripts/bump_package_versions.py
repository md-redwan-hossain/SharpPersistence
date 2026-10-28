import json
import os
import re
from pathlib import Path

from gh_cli import gh, gh_run


def set_projects(value: list[str]) -> None:
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as out:
        out.write(f"projects={json.dumps(value)}\n")


def package_versions_from_diff(diff: str, prefix: str) -> set[tuple[str, str]]:
    pkg_re = re.compile(
        r'<PackageVersion\s+(?:Include|Update)="([^"]+)"\s+Version="([^"]+)"',
        re.IGNORECASE,
    )
    found: set[tuple[str, str]] = set()
    for line in diff.splitlines():
        if not line.startswith(prefix) or line.startswith(prefix * 3):
            continue
        match = pkg_re.search(line[1:])
        if match:
            found.add((match.group(1), match.group(2)))
    return found


def project_consumes_package(csproj_content: str, package_name: str) -> bool:
    pattern = rf'<PackageReference\s+Include="{re.escape(package_name)}"'
    return re.search(pattern, csproj_content, re.IGNORECASE) is not None


def main() -> None:
    set_projects([])

    last_commit = gh("git log -1 --pretty=%B").strip()
    if "chore: auto-bump nuget package versions" in last_commit:
        print("Already bumped. Skipping.")
        return

    gh_run("git fetch origin main")
    diff = gh("git diff origin/main...HEAD -- Directory.Packages.props")

    added = package_versions_from_diff(diff, "+")
    removed = package_versions_from_diff(diff, "-")
    changed = added - removed

    updated_pkgs: set[str] = set()
    for name, _version in changed:
        updated_pkgs.add(name)

    print(f"Updated packages: {updated_pkgs}")
    if not updated_pkgs:
        print("No PackageVersion Version changes detected.")
        return

    projects = [
        "SharpPersistence",
        "SharpPersistence.Abstractions",
        "SharpPersistence.EfCore",
    ]
    bumped_projects: list[str] = []
    version_pattern = re.compile(r"(<Version>\d+\.\d+\.)(\d+)(</Version>)")

    for proj in projects:
        csproj_path = Path(f"src/{proj}/{proj}.csproj")
        if not csproj_path.exists():
            continue

        content = csproj_path.read_text(encoding="utf-8")

        consumes = False
        for pkg in updated_pkgs:
            if project_consumes_package(content, pkg):
                consumes = True
                break
        if not consumes:
            continue

        match = version_pattern.search(content)
        if match is None:
            print(f"No <Version> tag found in {csproj_path}")
            continue

        major_minor = match.group(1)
        patch = int(match.group(2))
        suffix = match.group(3)
        new_version_tag = f"{major_minor}{patch + 1}{suffix}"
        new_content = version_pattern.sub(new_version_tag, content, count=1)
        csproj_path.write_text(new_content, encoding="utf-8")
        bumped_projects.append(proj)
        print(f"Bumped version for: {proj}")

    if not bumped_projects:
        print("No consuming projects matched the updated dependencies.")
        return

    gh_run("git config user.name 'github-actions[bot]'")
    gh_run(
        "git config user.email '41898282+github-actions[bot]@users.noreply.github.com'"
    )
    gh_run("git add src/")
    gh_run('git commit -m "chore: auto-bump nuget package versions"')
    gh_run("git push")

    set_projects(bumped_projects)


if __name__ == "__main__":
    main()
