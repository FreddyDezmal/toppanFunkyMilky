# M14 UAT defect log

Defects found by stakeholder testers during UAT. Developer-found defects stay in `docs/testing/m14-defect-log.md`.
Severity scale: `m14-uat-plan.md` §6. The tester describes the problem and agrees the severity; the facilitator
records it in the tester's words and never closes a defect without a tester's retest.

## How to fill in a row

| Column | What to write |
| --- | --- |
| ID | `UAT-DEF-01`, `UAT-DEF-02`, … in the order found |
| Script | The script being run (e.g. `P-13`), or "exploratory" |
| Found by / date | The tester's name and the date |
| What happened | What the tester saw, and what they expected instead |
| Evidence | Screenshot file name, order reference, email subject |
| Severity | Critical / High / Medium / Low |
| Status | Open → Fixed (awaiting retest) → Closed (retest passed), or Accepted (agreed not to fix) |
| Agreed plan | For Medium/Low left open at sign-off: fix by a date, or accept with the reason. Agreed by whom |
| Fix | Commit and deployed version that fixed it |
| Retest | Tester and date of the passing retest |

## Defects

| ID | Script | Found by / date | What happened | Evidence | Severity | Status | Agreed plan | Fix | Retest |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| | | | | | | | | | |

## Summary at sign-off

| Severity | Found | Closed | Accepted | Open (with agreed plan) |
| --- | --- | --- | --- | --- |
| Critical | | | | must be 0 |
| High | | | | must be 0 |
| Medium | | | | |
| Low | | | | |
