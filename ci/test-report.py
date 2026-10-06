"""Parse TRX, report real failures and fail if mandatory tests did not pass."""
import argparse
import os
from pathlib import Path
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument("--verify", action="store_true")
args = parser.parse_args()
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
results = []
parse_errors = []
for file in Path("artifacts").rglob("*.trx"):
    try:
        results.extend(ET.parse(file).findall(".//t:UnitTestResult", ns))
    except (ET.ParseError, OSError) as error:
        parse_errors.append(f"{file.name}: {error}")

required = [line.strip().split("|") for line in Path("ci/required-tests.txt").read_text().splitlines()
            if line.strip() and not line.startswith("#")]
missing = []
for specification in required:
    name = specification[0]
    minimum = int(specification[1]) if len(specification) > 1 else 1
    actual = sum((r.attrib.get("testName", "").split("(")[0].endswith("." + name)
                  or r.attrib.get("testName", "").split("(")[0] == name)
                 and r.attrib.get("outcome") == "Passed" for r in results)
    if actual < minimum:
        missing.append(f"{name} ({actual}/{minimum})")
passed = sum(r.attrib.get("outcome") == "Passed" for r in results)
failed = sum(r.attrib.get("outcome") == "Failed" for r in results)
skipped = sum(r.attrib.get("outcome") in ("NotExecuted", "Skipped") for r in results)
bad = bool(parse_errors or missing or failed or skipped or not results
           or any(r.attrib.get("outcome") != "Passed" for r in results))

def safe(value):
    # Defense in depth: generated test DB credentials are masked by the runner too.
    for key in ("IDENTITY_TEST_CONNECTION", "TEST_SQL_PASSWORD", "Jwt__Key"):
        secret = os.environ.get(key)
        if secret:
            value = value.replace(secret, "[REDACTED]")
    return value.replace("```", "'''")

is_ci = os.environ.get("GITHUB_ACTIONS") == "true"
lines = ["## Identity Service CI" if is_ci else "## Identity Service — local tests"]
stages = ["setup", "database", "restore", "build", "test"]
statuses = {stage: os.environ.get(stage.upper() + "_OUTCOME", "not-run") for stage in stages}
overall = (all(value == "success" for value in statuses.values()) if is_ci else True) and not bad
lines.append("**PASS**" if overall else "**FAIL / chưa xác minh đầy đủ**")
for stage, outcome in (statuses.items() if is_ci else []):
    lines.append(f"- {stage}: {outcome}")
    if outcome == "failure":
        log = Path("artifacts") / (stage + ".log")
        if log.exists():
            excerpt = "\n".join(log.read_text(errors="replace").splitlines()[-60:])
            lines.extend(["```text", safe(excerpt), "```"])
        else:
            lines.append("Không có log artifact; xem log của bước tương ứng để lấy lỗi thực tế.")
if not results:
    lines.append("**Tests chưa chạy hoặc chưa có kết quả TRX. Không báo test PASS.**")
else:
    lines.append(f"Total: {len(results)}; pass: {passed}; fail: {failed}; skipped: {skipped}.")
for error in parse_errors:
    lines.append(safe(error))
for result in results:
    if result.attrib.get("outcome") != "Passed":
        lines.append(f"### {result.attrib.get('testName')} — {result.attrib.get('outcome')}")
        message = result.find("t:Output/t:ErrorInfo/t:Message", ns)
        stack = result.find("t:Output/t:ErrorInfo/t:StackTrace", ns)
        lines.extend(["```text", safe(message.text or "") if message is not None else "",
                      safe("\n".join((stack.text or "").splitlines()[:15])) if stack is not None else "", "```"])
if missing:
    lines.append("Các test bắt buộc chưa có kết quả pass: " + ", ".join(missing))
output = "\n".join(lines) + "\n"
Path("artifacts").mkdir(exist_ok=True)
Path("artifacts/summary.md").write_text(output, encoding="utf-8")
if os.environ.get("GITHUB_STEP_SUMMARY"):
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
        summary.write(output)
print(f"Tests: total={len(results)}, pass={passed}, fail={failed}, skipped={skipped}.")
if missing:
    print("Missing mandatory passing tests: " + ", ".join(missing))
if args.verify and bad:
    raise SystemExit(1)
