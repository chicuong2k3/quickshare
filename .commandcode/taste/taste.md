# Taste (Continuously Learned by [CommandCode][cmd])

[cmd]: https://commandcode.ai/

# api-design
- Design folder management with dedicated CRUD endpoints; reference folders by ID (not path strings) when assigning files to folders. Confidence: 0.60
- Default sort direction for list/query endpoints should be ascending (asc), following Stripe API conventions. Use repeated `?sort=field direction` params for multi-column sort support. Confidence: 0.65

# api-docs
- Use Swagger UI (Swashbuckle.AspNetCore) instead of Scalar for OpenAPI — Swagger UI auto-fills default/example values in "Try it" forms, which Scalar has a known unfixed bug preventing. Confidence: 0.50

# blazor
- All web UI pages must use RizzyUI Razor components (RzButton, RzCard, RzBadge, RzTable, RzAvatar, RzAlert, RzProgress, RzSpinner, RzSeparator, RzDialog, etc.) instead of raw HTML elements. Plain div, span, p, header, footer, main, nav wrappers and svg icons are allowed for layout only. Tailwind CSS remains for spacing, alignment, and overrides. Confidence: 0.85
- RizzyUI components (RzButton, etc.) support Alpine.js directives like x-on:click — they pass through additional attributes to the underlying HTML element. Use RizzyUI components with Alpine.js instead of falling back to raw <button> elements. Confidence: 0.70
- When switching from native HTML elements to RizzyUI components breaks JavaScript DOM selectors (e.g., getElementById), update the JavaScript selectors to match the new component's rendered output instead of reverting the component change. Confidence: 0.65

# architecture
See [architecture/taste.md](architecture/taste.md)
# csharp
See [csharp/taste.md](csharp/taste.md)

# code-scope
- Do not reference or implement Telegram (WTelegram) functionality — the user does not want Telegram integration in this project. Confidence: 0.85
