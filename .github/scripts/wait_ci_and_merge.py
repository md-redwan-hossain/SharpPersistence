import json
import os
import time

from gh_cli import gh, gh_run


def find_ci_run_for_commit(branch: str, commit_sha: str):
    raw = gh(
        f'gh run list --workflow dotnet.yaml --branch "{branch}" '
        f"--limit 20 --json headSha,status,conclusion"
    )
    runs = json.loads(raw)
    for run in runs:
        if run["headSha"] == commit_sha:
            return run
    return None


def main() -> None:
    pr_url = os.environ["PR_URL"]
    branch = os.environ["BRANCH"]
    commit_sha = gh("git rev-parse HEAD").strip()

    ci_passed = False
    for _ in range(60):
        run = find_ci_run_for_commit(branch, commit_sha)

        if run is None:
            print(f"waiting for CI on {commit_sha[:7]}")
            time.sleep(10)
            continue

        status = run["status"]
        result = run["conclusion"]
        print(f"CI status={status} result={result}")

        if status != "completed":
            time.sleep(10)
            continue

        if result == "success":
            print("CI passed")
            ci_passed = True
            break

        raise SystemExit(f"CI failed: {result}")

    if not ci_passed:
        raise SystemExit("CI timed out")

    gh_run(f'gh pr merge "{pr_url}" --squash --delete-branch')


if __name__ == "__main__":
    main()
