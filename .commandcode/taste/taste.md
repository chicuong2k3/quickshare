# Taste (Continuously Learned by [CommandCode][cmd])

[cmd]: https://commandcode.ai/

# api-design
- Design folder management with dedicated CRUD endpoints; reference folders by ID (not path strings) when assigning files to folders. Confidence: 0.60
- Default sort direction for list/query endpoints should be ascending (asc), following Stripe API conventions. Use repeated `?sort=field direction` params for multi-column sort support. Confidence: 0.65

# api-docs
- Use Swagger UI (Swashbuckle.AspNetCore) instead of Scalar for OpenAPI — Swagger UI auto-fills default/example values in "Try it" forms, which Scalar has a known unfixed bug preventing. Confidence: 0.50

# architecture
See [architecture/taste.md](architecture/taste.md)
# csharp
See [csharp/taste.md](csharp/taste.md)
