import os
import sys
import re
import json
import requests
import dataclasses
from dataclasses import dataclass, field
from typing import Optional

# from rich import print as print

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
    "remove": "Removals",
    "perf": 'Performance Improvements',
    "test": 'Tests',
    "build": 'Builds',
    "ci": 'Continuous Integration',
    "chore": 'Chores',
    "revert": 'Reverts',
    "commit": "Commits",
}

@dataclass
class Image:
    src: str
    align: str
    width: int
    height: int

    def as_dict(self):
        return {
            "source": self.src,
            "align": self.align,
            "width": self.width,
            "height": self.height,
        }

@dataclass
class Page:
    text: str
    image: Optional[Image] = None

    def as_dict(self):
        return {
            "text": self.text.strip(),
            "image": self.image.as_dict() if self.image else None,
        }


@dataclass
class Version:
    celestetas_version: str
    studio_version: str

    pages: list[Page] = field(default_factory=list)

    change_list: list[tuple[str, str]] = field(default_factory=list)
    change_category: dict[str, list[str]] = field(default_factory=lambda: Version._init_change_category())

    markdown_text: str = ""

    def as_dict(self):
        return {
            "celesteTasVersion": self.celestetas_version,
            "studioVersion": self.studio_version,
            "pages": [page.as_dict() for page in self.pages],
            "changes": self.change_category,
        }

    def _init_change_category():
        changes = {}
        for cat in categories:
            changes[cat] = []
        return changes

@dataclass
class PullRequest:
    url: str
    id: int

@dataclass
class Commit:
    sha: str
    type: str
    scope: str
    message: str
    author: str
    pull_requests: list[PullRequest]


def main():
    commit_message = sys.argv[1]
    changelog_file = sys.argv[2]
    version_info_file = sys.argv[3]
    gb_changelog_file = sys.argv[4]
    gh_changelog_file = sys.argv[5]
    studio_changelog_file = sys.argv[6]

    # Find CelesteTAS / Studio version
    celestetas_version = re.search(r"CelesteTAS\s+v([\d.]+)", commit_message).group(1)
    studio_version = re.search(r"Studio\s+v([\d.]+)", commit_message).group(1)
    with open(version_info_file, "w") as f:
        f.write(f"{celestetas_version.strip()}\n")
        f.write(f"{studio_version.strip()}\n")

    # Parse CHANGELOG file
    versions = []
    with open(changelog_file, "r") as f:
        current_version = None
        current_page = None
        while line := f.readline():
            celestetas_match = re.search(r"CelesteTAS\s+v([\d.]+)", line)
            studio_match = re.search(r"Studio\s+v([\d.]+)", line)
            if celestetas_match and studio_match:
                if current_version:
                    versions.append(current_version)
                current_version = Version(celestetas_match.group(1), studio_match.group(1))
                print(current_version)
                continue
            elif not current_version:
                continue

            change_match = re.search(r"-\s+([a-zA-Z]+)\s*:\s*(.+)", line)
            image_match = re.search(r"<!-- IMAGE (right|left) (\d+) (\d+) ([\w/.]+) -->", line)
            if change_match:
                change_type, change_message = change_match.group(1).lower(), change_match.group(2).strip()
                if change_type not in current_version.change_category:
                    print(f"Invalid change type '{change_type}' with message '{change_message}'", flush=True)
                    continue
                current_version.change_list.append((change_type, change_message))
                current_version.change_category[change_type].append(change_message)
            elif image_match:
                if not current_page:
                    current_page = Page(text="")
                current_page.image = Image(image_match.group(4), image_match.group(1), int(image_match.group(2)), int(image_match.group(3)))
            elif current_page:
                if line.startswith("---"):
                    current_version.pages.append(current_page)
                    current_page = None
                else:
                    current_page.text += line
            elif line and line.strip():
                current_page = Page(text=line)

        if current_page:
            current_version.pages.append(current_page)
        if current_version:
            versions.append(current_version)

    for version in versions:
        if version.celestetas_version != celestetas_version or version.studio_version != studio_version:
            continue

        # Generate GameBanana changelog
        gb_changelog = []
        for change_type, change_message in version.change_list:
            # Entries have to be at least 10 characters, so lets cheat a bit with a ZWNBS
            if len(change_message) < 10:
                change_message = change_message.ljust(9) + "\ufeff"

            # Replace ` with ' since GB doesn't support code blocks
            gb_changelog.append({ "cat": categories[change_type][0], "text": change_message.replace('`', '\'') })

        with open(gb_changelog_file, "w") as f:
            f.write(json.dumps(gb_changelog))

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
        if res.status_code != 200:
            print(f"Failed to fetch repository tags: {res.text}", flush=True)
            continue

        res_json = res.json()

        current_tag = res_json[0]
        previous_tag = res_json[1]

        print(f"Generating changelog for releases {previous_tag["name"]} to {current_tag["name"]} ...", flush=True)

        # Get commits between tags
        parsed_commits: dict[str, Commit] = {}
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
                if parsed_commit:
                    parsed_commits[parsed_commit.type].append(parsed_commit)

            if "link" in res.headers and 'rel="next"' in res.headers["link"]:
                next_url = re.search(r'(?<=<)([\S]*)(?=>; rel="Next")', res.headers["link"], re.IGNORECASE).group(0)
            else:
                break # No more pages left
            break

        # Convert to GitHub MarkDown
        gh_markdown = ""
        for page in version.pages:
            if page.image:
                gh_markdown += f"<img src=\"https://raw.githubusercontent.com/{gh_repo}/{current_tag["commit"]["sha"]}/{page.image.src}\" width=\"{page.image.width}\" height=\"{page.image.width}\" align=\"{page.image.align}\">\n"
                gh_markdown += f"{page.text.strip()}\n<br clear=\"{page.image.align}\"/> <hr/>\n\n"
            else:
                gh_markdown += f"{page.text.strip()}\n\n---\n\n"

        for category in version.change_category:
            changes = version.change_category[category]
            if len(changes) == 0:
                continue

            gh_markdown += f"## {categories[category][1]}\n"
            for change in changes:
                gh_markdown += f"- {change}\n"
            gh_markdown += "\n"


        # Generate commit details
        gh_markdown += "<details>\n"
        gh_markdown += "<summary><h3>Commit Details</h3></summary>\n"

        for commit_type in parsed_commits:
            commits = parsed_commits[commit_type]
            if len(commits) == 0:
                continue

            if len(gh_markdown) != 0:
                gh_markdown += "\n"

            gh_markdown += f"### {conventional_commit_types[commit_type]}\n"
            for commit in commits:
                prs = [f"[#{pull_request.id}]({pull_request.url})" for pull_request in commit.pull_requests]
                scope = f"**{commit.scope}**: " if commit.scope else ""
                gh_markdown += f"- {commit.sha[0:7]} {scope}{commit.message} (@{commit.author}) {", ".join(prs)}\n"

        gh_markdown += "</details>\n"

        with open(gh_changelog_file, "w") as f:
            f.write(gh_markdown)

    with open(studio_changelog_file, "w") as f:
        json.dump({
            "categoryNames": {cat: categories[cat][1] for cat in categories},
            "versions": [version.as_dict() for version in versions],
        }, f)

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
            pull_requests.append(PullRequest(url=pull_request_entry["url"], id=pull_request_entry["number"]))

    return Commit(
        sha=commit_entry["sha"],
        type=commit_type,
        scope=commit_scope,
        message=commit_message,
        author=author["login"],
        pull_requests=pull_requests,
    )



if __name__ == "__main__":
    main()
