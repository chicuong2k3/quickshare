# QuickShareCloud Style Gallery Preview

## Goal

Create a standalone HTML preview that lets the product owner evaluate one authored, broad-audience file-transfer workbench before changing the real Blazor UI.

## Chosen direction

The preview uses a **file-transfer workbench**: the sender sees the file list as a working table on the left and recipient/delivery details as a short numbered form on the right. It avoids a marketing hero, theme picker, decorative gradients, pill-heavy navigation, and nested cards. The visual language is cool blue-gray paper, dark ink, thin rules, one signal color, and measured typography.

## Variants

The workbench is one committed direction rather than a color-skin gallery. Its signature is the split workspace, file table, route label `/send`, and explicit three-step delivery form.

## Preview behavior

- A single HTML file opens without the application backend.
- The file picker updates a visible file table and count.
- The form exposes recipient, delivery mode, optional message, and a primary send action.
- Demo controls update only local preview state: file rows and preview-only submission status.
- A comparison note explains that the screen is a visual prototype and does not send files.
- Semantic HTML, native controls, visible text states, keyboard focus, reduced-motion support, and responsive behavior are required.

## Scope and non-goals

The preview does not modify `Home.razor`, `MainLayout.razor`, production tokens, localization resources, upload JavaScript, or routing. It does not claim real limits, encryption behavior, delivery status, or pricing. The real app's current Miura design system remains untouched until a direction is selected.

## Acceptance criteria

- Opening the file locally shows all six choices and a working selector.
- Desktop and narrow mobile layouts remain usable without horizontal page scrolling.
- Upload, recipient, and send controls have clear labels and visible focus states.
- The workbench is understandable within one viewport without technical knowledge.
- No fake upload completion or real delivery claim is presented as production behavior.
