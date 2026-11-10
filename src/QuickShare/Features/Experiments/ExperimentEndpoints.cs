using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.FeatureFlags;
using System.Security.Cryptography;
using System.Text;
using QuickShare.Features.Analytics;

namespace QuickShare.Features.Experiments;

public sealed class CreateExperimentRequest
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Surface { get; set; } = "landing";
    public string VariantACopy { get; set; } = string.Empty;
    public string VariantBCopy { get; set; } = string.Empty;
    public int TrafficSplitPercent { get; set; } = 50;
}

public sealed class PromoteExperimentRequest
{
    public string WinnerVariant { get; set; } = Experiment.VariantA;
}

public sealed record ExperimentAssignmentDto(
    string Key,
    string Variant,
    string Copy,
    string Status);

public sealed record ExperimentReportDto(
    Guid Id,
    string Key,
    string Name,
    string Surface,
    string Status,
    string? WinnerVariant,
    string VariantACopy,
    string VariantBCopy,
    int TrafficSplitPercent,
    long VariantAExposures,
    long VariantAConversions,
    double VariantAConversionRate,
    long VariantBExposures,
    long VariantBConversions,
    double VariantBConversionRate,
    double ZScore,
    double ConfidencePercent,
    string RecommendedWinner);

/// <summary>
/// Deterministic visitor bucketing and statistical significance calculator for A/B experiments (issue #92).
/// </summary>
public static class ExperimentEvaluator
{
    public static string AssignVariant(Guid visitorId, string experimentKey, int splitPercent = 50, string? promotedWinner = null)
    {
        if (!string.IsNullOrEmpty(promotedWinner))
        {
            return string.Equals(promotedWinner, Experiment.VariantB, StringComparison.OrdinalIgnoreCase)
                ? Experiment.VariantB
                : Experiment.VariantA;
        }

        var clamped = Math.Clamp(splitPercent, 0, 100);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{visitorId:N}:{experimentKey.Trim().ToLowerInvariant()}"));
        var bucket = BitConverter.ToUInt32(bytes, 0) % 100;
        return bucket < clamped ? Experiment.VariantB : Experiment.VariantA;
    }

    public static (double ZScore, double ConfidencePercent, string RecommendedWinner) ComputeSignificance(
        long exposuresA, long conversionsA, long exposuresB, long conversionsB)
    {
        if (exposuresA <= 0 || exposuresB <= 0)
            return (0, 0, "insufficient_data");

        var pA = Math.Clamp((double)conversionsA / exposuresA, 0.0, 1.0);
        var pB = Math.Clamp((double)conversionsB / exposuresB, 0.0, 1.0);
        var pooled = (double)(conversionsA + conversionsB) / (exposuresA + exposuresB);
        var stdErr = Math.Sqrt(pooled * (1.0 - pooled) * ((1.0 / exposuresA) + (1.0 / exposuresB)));

        if (stdErr <= 1e-9)
            return (0, 0, "tie");

        var z = (pB - pA) / stdErr;
        var absZ = Math.Abs(z);

        // Two-tailed normal confidence approximation
        var confidence = absZ switch
        {
            >= 2.576 => 99.0,
            >= 1.96 => 95.0,
            >= 1.645 => 90.0,
            >= 1.282 => 80.0,
            _ => Math.Round(Math.Min(79.0, absZ / 1.282 * 80.0), 1),
        };

        var winner = absZ < 1.645
            ? "inconclusive"
            : z > 0 ? Experiment.VariantB : Experiment.VariantA;

        return (Math.Round(z, 3), confidence, winner);
    }
}

/// <summary>
/// GET /api/experiments/assign?key=... — assigns the caller's anonymous visitor id to Variant A or B
/// and records an anonymous exposure event (issue #92).
/// </summary>
public sealed class AssignExperimentEndpoint(AppDbContext db, AnalyticsService analytics)
    : EndpointWithoutRequest<ExperimentAssignmentDto>
{
    public override void Configure()
    {
        Get(RoutePaths.ExperimentsAssignApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Analytics));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var key = (Query<string>("key", isRequired: false) ?? "landing_hero").Trim().ToLowerInvariant();
        var exp = await db.Experiments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Key == key, ct);

        var ctx = AnalyticsService.CaptureContext(HttpContext);
        var visitorId = ctx.VisitorId ?? Guid.NewGuid();

        if (exp is null)
        {
            var fallbackVariant = ExperimentEvaluator.AssignVariant(visitorId, key, 50, null);
            await Send.OkAsync(new ExperimentAssignmentDto(key, fallbackVariant, string.Empty, Experiment.StatusRunning), ct);
            return;
        }

        var variant = ExperimentEvaluator.AssignVariant(
            visitorId,
            exp.Key,
            exp.TrafficSplitPercent,
            exp.Status == Experiment.StatusPromoted ? exp.WinnerVariant : null);

        var copy = variant == Experiment.VariantB ? exp.VariantBCopy : exp.VariantACopy;

        await analytics.RecordAsync(
            AnalyticsEvents.ExperimentExposure,
            new AnalyticsEventData(FileName: $"{exp.Key}:{variant}"),
            ctx,
            ct);

        await Send.OkAsync(new ExperimentAssignmentDto(exp.Key, variant, copy, exp.Status), ct);
    }
}

/// <summary>GET /api/admin/experiments — lists all A/B experiments with conversion rates &amp; Z-score (issue #92).</summary>
public sealed class ListExperimentsEndpoint(AppDbContext db, IOptions<AdminOptions> adminOptions)
    : EndpointWithoutRequest<IReadOnlyList<ExperimentReportDto>>
{
    public override void Configure()
    {
        Get(RoutePaths.AdminExperimentsApi);
        Description(b => b.WithTags(ApiTags.Analytics));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!User.IsAdmin(adminOptions))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        var experiments = await db.Experiments
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

        var exposures = await db.AnalyticsEvents
            .AsNoTracking()
            .Where(e => e.EventName == AnalyticsEvents.ExperimentExposure && e.VisitorId != null && e.FileName != null)
            .Select(e => new { VisitorId = e.VisitorId!.Value, Tag = e.FileName! })
            .ToListAsync(ct);

        var conversions = await db.AnalyticsEvents
            .AsNoTracking()
            .Where(e => (e.EventName == AnalyticsEvents.UploadCompleted || e.EventName == AnalyticsEvents.Signup) && e.VisitorId != null)
            .Select(e => e.VisitorId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var convertedSet = conversions.ToHashSet();

        var reports = new List<ExperimentReportDto>(experiments.Count);
        foreach (var exp in experiments)
        {
            var prefixA = $"{exp.Key}:{Experiment.VariantA}";
            var prefixB = $"{exp.Key}:{Experiment.VariantB}";

            var visitorsA = exposures
                .Where(x => string.Equals(x.Tag, prefixA, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.VisitorId)
                .Distinct()
                .ToList();

            var visitorsB = exposures
                .Where(x => string.Equals(x.Tag, prefixB, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.VisitorId)
                .Distinct()
                .ToList();

            var expA = (long)visitorsA.Count;
            var convA = (long)visitorsA.Count(v => convertedSet.Contains(v));
            var expB = (long)visitorsB.Count;
            var convB = (long)visitorsB.Count(v => convertedSet.Contains(v));

            var rateA = expA == 0 ? 0 : Math.Round((double)convA / expA * 100.0, 2);
            var rateB = expB == 0 ? 0 : Math.Round((double)convB / expB * 100.0, 2);
            var (z, conf, winner) = ExperimentEvaluator.ComputeSignificance(expA, convA, expB, convB);

            reports.Add(new ExperimentReportDto(
                exp.Id,
                exp.Key,
                exp.Name,
                exp.Surface,
                exp.Status,
                exp.WinnerVariant,
                exp.VariantACopy,
                exp.VariantBCopy,
                exp.TrafficSplitPercent,
                expA,
                convA,
                rateA,
                expB,
                convB,
                rateB,
                z,
                conf,
                winner));
        }

        await Send.OkAsync(reports, ct);
    }
}

/// <summary>POST /api/admin/experiments — create or update an A/B experiment (issue #92).</summary>
public sealed class UpsertExperimentEndpoint(AppDbContext db, IOptions<AdminOptions> adminOptions)
    : Endpoint<CreateExperimentRequest, ExperimentReportDto>
{
    public override void Configure()
    {
        Post(RoutePaths.AdminExperimentsApi);
        Description(b => b.WithTags(ApiTags.Analytics));
    }

    public override async Task HandleAsync(CreateExperimentRequest req, CancellationToken ct)
    {
        if (!User.IsAdmin(adminOptions))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        var key = (req.Key ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(key) || key.Length > Experiment.KeyMaxLength)
        {
            ThrowError(ErrorCodes.ExperimentKeyInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var existing = await db.Experiments.FirstOrDefaultAsync(e => e.Key == key, ct);
        if (existing is null)
        {
            existing = new Experiment
            {
                Id = Guid.CreateVersion7(),
                Key = key,
                Name = string.IsNullOrWhiteSpace(req.Name) ? key : req.Name.Trim(),
                Surface = string.IsNullOrWhiteSpace(req.Surface) ? "landing" : req.Surface.Trim().ToLowerInvariant(),
                VariantACopy = (req.VariantACopy ?? string.Empty).Trim(),
                VariantBCopy = (req.VariantBCopy ?? string.Empty).Trim(),
                TrafficSplitPercent = Math.Clamp(req.TrafficSplitPercent, 0, 100),
                Status = Experiment.StatusRunning,
            };
            db.Experiments.Add(existing);
        }
        else
        {
            existing.Name = string.IsNullOrWhiteSpace(req.Name) ? existing.Name : req.Name.Trim();
            existing.VariantACopy = (req.VariantACopy ?? string.Empty).Trim();
            existing.VariantBCopy = (req.VariantBCopy ?? string.Empty).Trim();
            existing.TrafficSplitPercent = Math.Clamp(req.TrafficSplitPercent, 0, 100);
        }

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new ExperimentReportDto(
            existing.Id,
            existing.Key,
            existing.Name,
            existing.Surface,
            existing.Status,
            existing.WinnerVariant,
            existing.VariantACopy,
            existing.VariantBCopy,
            existing.TrafficSplitPercent,
            0, 0, 0, 0, 0, 0, 0, 0, "insufficient_data"), ct);
    }
}

/// <summary>POST /api/admin/experiments/{Key}/promote — promote winning variant to 100% traffic (issue #92).</summary>
public sealed class PromoteExperimentEndpoint(AppDbContext db, IOptions<AdminOptions> adminOptions)
    : Endpoint<PromoteExperimentRequest, ExperimentAssignmentDto>
{
    public override void Configure()
    {
        Post(RoutePaths.AdminExperimentsApi + "/{Key}/promote");
        Description(b => b.WithTags(ApiTags.Analytics));
    }

    public override async Task HandleAsync(PromoteExperimentRequest req, CancellationToken ct)
    {
        if (!User.IsAdmin(adminOptions))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        var key = (Route<string>("Key") ?? string.Empty).Trim().ToLowerInvariant();
        var exp = await db.Experiments.FirstOrDefaultAsync(e => e.Key == key, ct);
        if (exp is null)
        {
            ThrowError(ErrorCodes.ExperimentNotFound, StatusCodes.Status404NotFound);
            return;
        }

        var winner = string.Equals(req.WinnerVariant, Experiment.VariantB, StringComparison.OrdinalIgnoreCase)
            ? Experiment.VariantB
            : Experiment.VariantA;

        exp.Status = Experiment.StatusPromoted;
        exp.WinnerVariant = winner;
        await db.SaveChangesAsync(ct);

        var copy = winner == Experiment.VariantB ? exp.VariantBCopy : exp.VariantACopy;
        await Send.OkAsync(new ExperimentAssignmentDto(exp.Key, winner, copy, exp.Status), ct);
    }
}
