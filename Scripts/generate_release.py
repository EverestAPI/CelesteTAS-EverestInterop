import os
import sys
import re
import json
import requests

# Map entry in commit message to GameBanana update category / heading in release page
categories = {
    "feature": ("Feature", "Features"),
    "fix": ("Bugfix", "Bug Fixes"),
    "tweak": ("Tweak", "Tweaks"),
    "refactor": ("Refactor", "Refactors"),
    "remove": ("Removal", "Removals"),
    "optimize": ("Optimization", "Optimizations"),
}

conventional_commit_types = {
    "feat": 'Features',
    "fix": 'Bug Fixes',
    "docs": 'Documentation',
    "style": 'Styles',
    "refactor": 'Code Refactoring',
    "tweak": "Tweaks",
    "perf": 'Performance Improvements',
    "test": 'Tests',
    "build": 'Builds',
    "ci": 'Continuous Integration',
    "chore": 'Chores',
    "revert": 'Reverts',
    "commit": "Commits",
}

def main():
    commit_message = sys.argv[1]
    version_info_file = sys.argv[2]
    gb_changelog_file = sys.argv[3]
    gh_changelog_file = sys.argv[4]
    studio_changelog_file = sys.argv[5]

    # Find CelesteTAS / Studio version
    celestetas_version = re.search(r"CelesteTAS\s+(v[\d.]+)", commit_message).group(1)
    studio_version = re.search(r"Studio\s+(v[\d.]+)", commit_message).group(1)
    with open(version_info_file, "w") as f:
        f.write(f"{celestetas_version.strip()}\n")
        f.write(f"{studio_version.strip()}\n")

    # Find changelog entries
    changes = []
    for change_type, change_message in re.findall(r"-\s+([a-zA-Z]+)\s*:\s*(.+)", commit_message):
        if change_type.lower() not in categories:
            print(f"Invalid change type '{change_type.lower()}' with message '{change_message}'", flush=True)
            continue
        changes.append((change_type.lower(), change_message))

    # Generate GameBanana changelog
    gb_changelog = []
    for change_type, change_message in changes:
        # Entries have to be at least 10 characters, so lets cheat a bit with a ZWNBS
        if len(change_message) < 10:
            change_message = change_message.ljust(9) + "\ufeff"

        gb_changelog.append({ "cat": categories[change_type][0], "text": change_message.strip() })
    with open(gb_changelog_file, "w") as f:
        f.write(json.dumps(gb_changelog))

    # Generate GitHub release
    gh_changelog = {}
    for category in categories:
        gh_changelog[category] = []

    for change_type, change_message in changes:
        gh_changelog[change_type].append(change_message)

    # Convert to MarkDown
    gh_markdown = []
    for category in gh_changelog:
        changes = gh_changelog[category]
        if len(changes) == 0:
            continue

        if len(gh_markdown) != 0:
            gh_markdown.append("")

        gh_markdown.append(f"## {categories[category][1]}")
        for change in changes:
            gh_markdown.append(f"- {change}")

    # Generate commit overview from the current to previous tag
    gh_repo = os.getenv("GITHUB_REPO")
    gh_token = os.getenv("GITHUB_TOKEN")

    # Get previous and current tag
    res = requests.request(
        method="GET",
        url=f"https://api.github.com/repos/{gh_repo}/tags",
        headers={
            "Authorization": f"Bearer {gh_token}"
        },
    )
    res_json = res.json()

    current_tag = res_json[0]
    previous_tag = res_json[1]

    print(f"Generating changelog for releases {previous_tag["name"]} to {current_tag["name"]} ...", flush=True)

    # Get commits between tags
    parsed_commits = {}
    for commit_type in conventional_commit_types:
        parsed_commits[commit_type] = []

    next_url = None
    while True:
        res = requests.request(
            method="GET",
            url=f"https://api.github.com/repos/{gh_repo}/compare/{previous_tag["commit"]["sha"]}...{current_tag["commit"]["sha"]}"
                if next_url is None else
                    next_url,
            headers={
                "Authorization": f"Bearer {gh_token}"
            },
            params = {
                # The docs say the maximum is 100, however this doesnt appear to be the case
                "per_page": 1000
            }
        )
        res_json = res.json()

        for commit_entry in res_json["commits"]:
            parsed_commit = parse_commit(commit_entry)
            if parsed_commit is not None:
                parsed_commits[parsed_commit["commit_type"]].append(parsed_commit)

        if "link" in res.headers and 'rel="next"' in res.headers["link"]:
            next_url = re.search(r'(?<=<)([\S]*)(?=>; rel="Next")', res.headers["link"], re.IGNORECASE).group(0)
        else:
            break # No more pages left
        break

    # Only include release notes (and not commit details) in Studio changelog
    with open(studio_changelog_file, "w") as f:
        f.write("\n".join(gh_markdown))

    # Generate commit details
    gh_markdown.append("<details>")
    gh_markdown.append("<summary><h3>Commit Details</h3></summary>")

    for commit_type in parsed_commits:
        commits = parsed_commits[commit_type]
        if len(commits) == 0:
            continue

        if len(gh_markdown) != 0:
            gh_markdown.append("")

        gh_markdown.append(f"### {conventional_commit_types[commit_type]}")
        for commit in commits:
            prs = [f"[#{pull_request["id"]}]({pull_request["url"]})" for pull_request in commit["pull_requests"]]
            scope = f"**{commit["commit_scope"]}**: " if commit["commit_scope"] is not None else ""
            gh_markdown.append(f"- {commit["sha"][0:7]} {scope}{commit["commit_message"]} (@{commit["author"]}) {", ".join(prs)}")

    gh_markdown.append("</details>")

    with open(gh_changelog_file, "w") as f:
        f.write("\n".join(gh_markdown))


def parse_commit(commit_entry):
    print(f"Parsing commit '{commit_entry["commit"]["message"].splitlines()[0]}'...", flush=True)

    commit = commit_entry["commit"]
    author = commit_entry["author"]

    # Skip merge commits
    if len(commit_entry["parents"]) != 1:
        return None

    # Parse commit message
    commit_message_raw = commit["message"].splitlines()[0]
    commit_match = re.match(r'^([a-zA-Z]+)\s*(?:\(([a-zA-z]+)\))?\s*:\s*(.+)', commit_message_raw)

    is_conventional_commit = commit_match is not None and commit_match.group(1) in conventional_commit_types

    commit_type = commit_match.group(1) if is_conventional_commit else "commit"
    commit_scope = commit_match.group(2)  if is_conventional_commit else None
    commit_message = commit_match.group(3)  if is_conventional_commit else commit_message_raw

    # Link associated pull requests
    gh_repo = os.getenv("GITHUB_REPO")
    gh_token = os.getenv("GITHUB_TOKEN")

    res = requests.request(
        method="GET",
        url=f"https://api.github.com/repos/{gh_repo}/commits/{commit_entry["sha"]}/pulls",
        headers={
            "Authorization": f"Bearer {gh_token}"
        },
        params = {
            # There aren never going to be more than 100 PRs per commit.
            "per_page": 100
        }
    )

    pull_requests = []
    if res.status_code == 200:
        for pull_request_entry in res.json():
            pull_requests.append({
                "url": pull_request_entry["url"],
                "id": pull_request_entry["number"],
            })

    return {
        "sha": commit_entry["sha"],
        "commit_type": commit_type,
        "commit_scope": commit_scope,
        "commit_message": commit_message,
        "author": author["login"],
        "pull_requests": pull_requests,
    }

if __name__ == "__main__":
    main()
