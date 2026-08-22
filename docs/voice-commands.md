# Voice commands (#277)

Design and invariants for the spoken input layer.

Related: the command palette (#153) in `Components/Layout/MainLayout.razor`, which owns the
actions this drives.

## It is an input, not a menu

`ts/voice.ts` holds no command registry. It matches a transcript against the palette's own list
— label text plus `data-keywords`, the same fields the typed filter reads — and follows the link
it finds. Two consequences fall out for free: commands are localised because the palette's labels
are, and a page added to the palette is speakable the same day.

Longest label wins a tie, because the more specific phrase is the one the speaker meant.

## Nothing runs before it is shown

Speech recognition mishears. The flow is therefore: hear → say what was heard → say what is about
to run → pause → run. Escape, or the word *cancel* (in either language), stops it during the
pause. There is no undo afterwards because there is nothing to undo — the pause **is** the undo.

The status line is `role="status"` with `aria-live="polite"`. The user this feature exists for
may not be looking at the screen, and an announcement they cannot hear is not a confirmation.

## Capability, not pretence

The mic button hides itself when neither `SpeechRecognition` nor `webkitSpeechRecognition`
exists, and the unsupported message names the fallback (Ctrl+K). A visible control that cannot
work is worse than no control.

Note for CSS: `hidden` alone was not enough — the button class sets `display`, which beats the
attribute. `.layout-voice-trigger[hidden]` says it explicitly. The same trap applies to any
`db-btn` hidden by attribute.

## What is actually private

The matching is local: transcripts are compared against DOM text and never sent anywhere.
**Recognition itself may not be.** Chrome's Web Speech implementation streams audio to a vendor
service; Safari's is on-device. The product does not claim "audio never leaves your device",
because for the most common browser that is false.

Nothing is recorded, stored or logged by us either way — there is no server call in this feature
at all.

## Not shipped

- A wake phrase. Always-on listening is a microphone nobody asked to leave open, and the button
  is one click.
- Dictation into fields — a different feature with different failure modes (a mis-dictated
  password is worse than a mis-heard command).
- Actions beyond navigation. "Delete that file" needs a target the palette does not model; when
  the palette grows per-item actions, this follows for free.
