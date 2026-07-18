---
baseline_commit: a1ecb33283f306f05d7d7145e87fb54cf4800de7
---

# Story 1.3: Update Copy on Upload and Download Pages

Status: review

## Story

As a **visitor**,
I want **the upload and download pages to reflect that login is now required**,
So that **I'm not misled by outdated messaging about anonymous usage**.

## Acceptance Criteria

1. **AC-1 (Upload page):** Given I view the Upload page while authenticated, then the page does not display "anonymous · no login required".

2. **AC-2 (Download page):** Given I view the Download page, then the page does not display "No login required".

3. **AC-3 (Meta description):** Given I view any page, then the meta description no longer says "anonymous".

## Tasks / Subtasks

- [x] **Task 1: Verify Upload.razor copy** (AC: 1)
  - [x] Confirm "anonymous · no login required" is NOT present — already updated to "200 MB per file · sign in to unlock higher limits" in previous commit
  - [x] No changes needed

- [x] **Task 2: Verify Download.razor copy** (AC: 2)
  - [x] Confirm "No login required" is NOT present — already removed in previous commit
  - [x] No changes needed

- [x] **Task 3: Update App.razor meta description** (AC: 3)
  - [x] Remove "anonymous" from `<meta name="description">` in `App.razor` line 6
  - [x] Update to reflect authenticated usage

## Dev Notes

### Current State

The RizzyUI refactor (commit `a1ecb33`) already updated the Upload page copy. The "anonymous · no login required" text was replaced with "200 MB per file · sign in to unlock higher limits". The Download page never had "No login required" in the current codebase.

The only remaining outdated copy is in `App.razor` line 6:
```html
<meta name="description" content="QuickShareCloud — anonymous temporary file sharing with auto-expiry." />
```

This should be updated to remove "anonymous" since uploads now require authentication.

### Previous Story Learnings

- Stories 1.1 and 1.2 are complete — auth middleware and endpoint hardening are done
- Upload page already shows "sign in to unlock higher limits" — consistent with auth requirement
- No test projects exist — verification is grep + build only

### Files to Modify

| File | Change |
|------|--------|
| `src/TeleCloud.Web/App.razor` | Update meta description to remove "anonymous" |

### Files Verified (No Changes)

| File | Why |
|------|-----|
| `src/TeleCloud.Web/Pages/Upload.razor` | Already updated in a1ecb33 — no "anonymous · no login required" |
| `src/TeleCloud.Web/Pages/Download.razor` | Never had "No login required" in current codebase |

## Dev Agent Record

### Agent Model Used

deepseek-v4-flash-free (Sisyphus via bmad-dev-story)

### Debug Log References

### Completion Notes List

- ✅ Verified Upload.razor already updated — no "anonymous · no login required" present
- ✅ Verified Download.razor already clean — no "No login required" present
- ✅ Updated `App.razor` meta description to remove "anonymous"
- ✅ TeleCloud.Web build: 0 warnings, 0 errors

### File List

- `src/TeleCloud.Web/App.razor` (MODIFIED — meta description updated)
- `_bmad-output/implementation-artifacts/1-3-update-copy-on-upload-and-download-pages.md` (this file)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (MODIFIED — story status updated)
