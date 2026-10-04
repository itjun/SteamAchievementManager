#!/usr/bin/env python3
"""SAM 打包发布脚本（绿色便携版，Velopack + GitHub Releases）。

用法：
  python scripts/release.py release 7.2.0            # 发正式版：改版本号 -> 提交 -> tag -> 推送触发 CI
  python scripts/release.py release 7.2.0 --force    # 强制更新版：同时把 update-policy.json 提到该版本
  python scripts/release.py release 7.2.0 --prerelease  # 预演版：不推 tag，手动触发 CI（客户端不可见）
  python scripts/release.py pack                      # 仅本地打包（dotnet publish + vpk pack），不碰 git/GitHub
  通用：--dry-run 只打印计划不改文件；--yes 跳过交互确认。

正式版流程（等价手工操作）：
  1. 校验版本号为三段 SemVer 且大于当前版本，tag 未被占用，git 工作区干净
  2. 改 SAM.WinUI/SAM.WinUI.csproj 的 Version/AssemblyVersion/FileVersion
  3. --force 时改 update-policy.json 的 minimumRequired（不加则沿用，= 可选更新）
  4. 提交 "Release vX.Y.Z"，打 annotated tag，push 触发 .github/workflows/release.yml
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CSPROJ = REPO / "SAM.WinUI" / "SAM.WinUI.csproj"
POLICY = REPO / "update-policy.json"
RELEASES_DIR = REPO / "Releases"

PACK_ID = "SteamAchievementManager"
PACK_TITLE = "Steam Achievement Manager"
MAIN_EXE = "SAM.WinUI.exe"
REPO_URL = "https://github.com/itjun/SteamAchievementManager"

SEMVER = re.compile(r"^(\d+)\.(\d+)\.(\d+)$")


def info(msg: str) -> None:
    print(f"[INFO] {msg}", flush=True)


def die(msg: str) -> None:
    print(f"[错误] {msg}", flush=True)
    sys.exit(1)


def run(cmd: list[str], **kwargs) -> subprocess.CompletedProcess:
    return subprocess.run(cmd, check=True, cwd=REPO, **kwargs)


def capture(cmd: list[str]) -> str:
    return subprocess.run(
        cmd, check=True, cwd=REPO, capture_output=True, text=True, encoding="utf-8", errors="replace"
    ).stdout.strip()


def parse_version(text: str) -> tuple[int, int, int]:
    m = SEMVER.match(text.strip().lower().lstrip("v"))
    if not m:
        die(f"版本号 '{text}' 非法：须为三段 SemVer（如 7.2.0）")
    return (int(m.group(1)), int(m.group(2)), int(m.group(3)))


def read_current_version() -> str:
    text = CSPROJ.read_text(encoding="utf-8")
    m = re.search(r"<Version>([^<]+)</Version>", text)
    if not m:
        die(f"未在 {CSPROJ} 找到 <Version>")
    return m.group(1).strip()


def plan_csproj_edits(new: str) -> list[tuple[str, str, str]]:
    """返回 [(说明, 旧文本, 新文本)]；不做写入。"""
    text = CSPROJ.read_text(encoding="utf-8")
    old_version = re.search(r"<Version>[^<]+</Version>", text)
    old_asm = re.search(r"<AssemblyVersion>[\d.]+</AssemblyVersion>", text)
    old_file = re.search(r"<FileVersion>[\d.]+</FileVersion>", text)
    if not (old_version and old_asm and old_file):
        die("csproj 版本字段不完整（Version/AssemblyVersion/FileVersion）")
    return [
        ("csproj Version", old_version.group(0), f"<Version>{new}</Version>"),
        ("csproj AssemblyVersion", old_asm.group(0), f"<AssemblyVersion>{new}.0</AssemblyVersion>"),
        ("csproj FileVersion", old_file.group(0), f"<FileVersion>{new}.0</FileVersion>"),
    ]


def apply_edits(edits: list[tuple[str, str, str]]) -> None:
    text = CSPROJ.read_text(encoding="utf-8")
    for _, old, new in edits:
        if old not in text:
            die(f"内部错误：待替换文本不存在（{old}）")
        text = text.replace(old, new, 1)
    CSPROJ.write_text(text, encoding="utf-8", newline="")


def read_policy_minimum() -> str:
    data = json.loads(POLICY.read_text(encoding="utf-8"))
    value = str(data.get("minimumRequired", "")).strip()
    if not SEMVER.match(value):
        die(f"update-policy.json 的 minimumRequired '{value}' 非法")
    return value


def write_policy_minimum(new: str) -> None:
    POLICY.write_text(
        json.dumps({"minimumRequired": new}, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="",
    )


def git_checks(tag: str) -> str:
    if shutil.which("git") is None:
        die("未找到 git")
    if capture(["git", "status", "--porcelain"]):
        die("git 工作区不干净：请先提交或贮藏全部改动再发版")
    branch = capture(["git", "branch", "--show-current"])
    if not branch:
        die("当前不在分支上（detached HEAD）")
    if capture(["git", "rev-parse", "-q", "--verify", f"refs/tags/{tag}"]):
        die(f"tag {tag} 已存在")
    return branch


def confirm(prompt: str) -> bool:
    try:
        answer = input(f"{prompt} [y/N] ").strip().lower()
    except EOFError:
        return False
    return answer in ("y", "yes")


def cmd_release(args) -> None:
    version = args.version
    parse_version(version)
    current = read_current_version()
    if parse_version(version) <= parse_version(current):
        die(f"新版本 {version} 必须大于当前版本 {current}")
    tag = f"v{version}"

    policy_min = read_policy_minimum()
    if not args.force and parse_version(policy_min) > parse_version(version):
        die(
            f"update-policy.json 的 minimumRequired={policy_min} 高于 {version}；"
            "发新版请加 --force 同步提升，或先调低该文件"
        )

    edits = plan_csproj_edits(version)
    policy_edit = None
    if args.force and policy_min != version:
        policy_edit = ("update-policy.json minimumRequired", policy_min, version)

    print("== 发布计划 ==")
    for what, old, new in edits + ([policy_edit] if policy_edit else []):
        print(f"  {what}: {old}  ->  {new}")
    print(f"  更新类型: {'强制更新' if (args.force or parse_version(policy_min) >= parse_version(version)) else '可选更新'}")
    print(f"  提交: 'Release {tag}'  |  tag: {tag}  |  推送后 CI: {REPO_URL}/actions")

    if args.dry_run:
        info("--dry-run：未做任何修改")
        return

    branch = git_checks(tag)
    if not args.yes and not confirm("确认执行？"):
        info("已取消")
        return

    apply_edits(edits)
    if policy_edit:
        write_policy_minimum(version)

    run(["git", "add", str(CSPROJ), str(POLICY)])
    commit_msg = f"Release {tag}" + (" (forced update)" if args.force else "")
    run(["git", "commit", "-m", commit_msg])

    if args.prerelease:
        if shutil.which("gh") is None:
            die("预演模式需要 gh CLI（GitHub Actions 手动触发）")
        run(["git", "push", "origin", branch])
        run(["gh", "workflow", "run", "Release", "--ref", branch, "-f", "prerelease=true"])
        info(f"已推送分支并触发预演构建（prerelease，客户端不可见）：{REPO_URL}/actions")
        return

    run(["git", "push", "origin", branch])
    run(["git", "push", "origin", tag])
    info(f"已推送 {tag}，CI 构建中：{REPO_URL}/actions")
    info(f"完成后 Release 页：{REPO_URL}/releases/tag/{tag}")


def vpk_exe() -> str:
    found = shutil.which("vpk")
    if found:
        return found
    fallback = Path.home() / ".dotnet" / "tools" / ("vpk.exe" if sys.platform == "win32" else "vpk")
    if fallback.exists():
        return str(fallback)
    die("未找到 vpk（先运行: dotnet tool install -g vpk）")


def build_changelog(version: str) -> str:
    try:
        tag = f"v{version}"
        prev = capture(["git", "describe", "--tags", "--abbrev=0", "--match", "v*", f"{tag}^"])
    except subprocess.CalledProcessError:
        prev = None
    try:
        cmd = ["git", "log", "--no-merges", "--pretty=format:- %s"]
        if prev:
            cmd.append(f"{prev}..HEAD")
        else:
            cmd.append("-15")
        lines = capture(cmd).splitlines()
    except subprocess.CalledProcessError:
        lines = []
    return "\n".join(lines[:40]) if lines else "- 更新说明见发布页面。"


def write_local_manifest(version: str) -> None:
    """本地 pack 也产出 update-manifest.json（与 CI 生成的一致），供 SAM_UPDATE_SOURCE 离线测试。"""
    manifest = {
        "version": version,
        "minimumRequired": read_policy_minimum(),
        "notes": build_changelog(version),
        "releaseUrl": f"{REPO_URL}/releases/tag/v{version}",
    }
    (RELEASES_DIR / "update-manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline=""
    )


def cmd_pack(args) -> None:
    if shutil.which("dotnet") is None:
        die("未找到 dotnet SDK")
    version = args.version or read_current_version()
    parse_version(version)

    publish_dir = RELEASES_DIR / "publish"
    info(f"dotnet publish（self-contained x64, 版本 {version}）...")
    run(
        [
            "dotnet", "publish", str(CSPROJ),
            "-c", "Release", "-p:Platform=x64", "-r", "win-x64",
            "--self-contained", "true", f"-p:Version={version}",
            "-o", str(publish_dir),
        ]
    )

    info("vpk pack（便携 zip + 更新包）...")
    run(
        [
            vpk_exe(), "pack",
            "-u", PACK_ID, "-v", version, "-p", str(publish_dir),
            "--packTitle", PACK_TITLE, "--mainExe", MAIN_EXE,
            "--outputDir", str(RELEASES_DIR),
        ]
    )
    write_local_manifest(version)

    print()
    print("== 产物 ==")
    for item in sorted(RELEASES_DIR.glob("*")):
        if item.is_file():
            print(f"  {item.name}")
    print(f"  便携包: {RELEASES_DIR / (PACK_ID + '-win-Portable.zip')}")
    print(f"  离线自更新测试: 设置环境变量 SAM_UPDATE_SOURCE={RELEASES_DIR} 后运行应用")


def main() -> None:
    parser = argparse.ArgumentParser(description="SAM 打包发布脚本（详见文件头注释）")
    sub = parser.add_subparsers(dest="command", required=True)

    p_release = sub.add_parser("release", help="发版：改版本 -> 提交 -> tag -> 推送触发 CI")
    p_release.add_argument("version", help="三段 SemVer，如 7.2.0")
    p_release.add_argument("--force", action="store_true", help="强制更新：minimumRequired 提到该版本")
    p_release.add_argument("--prerelease", action="store_true", help="预演：不推 tag，gh 触发 CI 且标记 prerelease")
    p_release.add_argument("--dry-run", action="store_true", help="只打印计划，不做任何修改")
    p_release.add_argument("--yes", action="store_true", help="跳过交互确认")

    p_pack = sub.add_parser("pack", help="仅本地打包（不碰 git/GitHub）")
    p_pack.add_argument("version", nargs="?", help="打包版本号（默认取 csproj 当前版本）")

    args = parser.parse_args()
    if args.command == "release":
        cmd_release(args)
    else:
        cmd_pack(args)


if __name__ == "__main__":
    main()
