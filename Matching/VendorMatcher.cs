using System.Text;
using JetSolutions.BillVendorSync.BillDotCom;
using JetSolutions.BillVendorSync.Excel;

namespace JetSolutions.BillVendorSync.Matching;

/// <summary>
/// Matches M2M Excel rows to Bill.com vendors in two passes:
///   1. Exact: normalized name (case/whitespace/punctuation + legal-suffix stripping).
///   2. Relaxed: significant-token sets (articles, web TLDs, and generic noise words
///      removed) with equal-set or subset matching.
/// City and Zip are used as a tiebreaker whenever more than one candidate remains.
/// </summary>
public sealed class VendorMatcher
{
    private static readonly string[] CompanySuffixes =
    {
        "incorporated", "inc", "corporation", "corp", "company", "co",
        "limited", "ltd", "llc", "llp", "lp", "plc", "gmbh",
    };

    // Tokens dropped during relaxed normalization (in addition to the legal suffixes above).
    private static readonly HashSet<string> NoiseWords = BuildNoiseWords();

    public IReadOnlyList<MatchResult> Match(
        IReadOnlyList<M2MVendor> m2mVendors,
        IReadOnlyList<VendorResponseDto> billVendors)
    {
        var results = new List<MatchResult>();

        // Only consider active vendors as update candidates.
        var activeBill = billVendors.Where(v => !v.Archived).ToList();
        var consumedBillIds = new HashSet<string>(StringComparer.Ordinal);

        // M2M rows that could not be matched exactly; carried into the relaxed pass.
        var pending = new List<M2MVendor>();

        // ---- Pass 1: exact name match ----
        var billByName = activeBill
            .GroupBy(v => NormalizeName(v.Name))
            .Where(g => g.Key.Length > 0)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var nameGroup in m2mVendors.GroupBy(m => NormalizeName(m.VendorName)))
        {
            var normName = nameGroup.Key;
            var m2mRows = nameGroup.ToList();

            if (normName.Length == 0)
            {
                foreach (var m2m in m2mRows)
                {
                    results.Add(new MatchResult
                    {
                        Status = MatchStatus.Unmatched,
                        M2M = m2m,
                        Note = "Empty/blank vendor name in Excel row.",
                    });
                }

                continue;
            }

            if (!billByName.TryGetValue(normName, out var candidates) || candidates.Count == 0)
            {
                pending.AddRange(m2mRows);
                continue;
            }

            var available = new List<VendorResponseDto>(candidates);

            foreach (var m2m in m2mRows)
            {
                var (picked, _) = PickCandidate(m2m, available, m2mRows.Count);

                if (picked is null)
                {
                    // Could not resolve by exact name alone; defer to the relaxed pass.
                    pending.Add(m2m);
                    continue;
                }

                available.Remove(picked);
                consumedBillIds.Add(picked.Id);
                results.Add(BuildMatch(m2m, picked, VendorMatchType.Exact));
            }
        }

        // ---- Pass 2: relaxed (significant-token) match on leftovers ----
        var leftoverBill = activeBill.Where(v => !consumedBillIds.Contains(v.Id)).ToList();
        var billTokenSets = leftoverBill.ToDictionary(v => v, v => SignificantTokens(v.Name));

        foreach (var m2m in pending)
        {
            var m2mTokens = SignificantTokens(m2m.VendorName);
            if (m2mTokens.Count == 0)
            {
                results.Add(new MatchResult
                {
                    Status = MatchStatus.Unmatched,
                    M2M = m2m,
                    Note = "No Bill.com vendor with a matching name.",
                });

                continue;
            }

            var candidates = leftoverBill
                .Where(v => !consumedBillIds.Contains(v.Id))
                .Select(v => (Vendor: v, Tokens: billTokenSets[v]))
                .Where(x => x.Tokens.Count > 0 && HasContainment(m2mTokens, x.Tokens))
                .ToList();

            if (candidates.Count == 0)
            {
                results.Add(new MatchResult
                {
                    Status = MatchStatus.Unmatched,
                    M2M = m2m,
                    Note = "No Bill.com vendor name matched (exact or relaxed).",
                });

                continue;
            }

            // Prefer exact set equality; otherwise fall back to subset candidates.
            var equal = candidates.Where(x => x.Tokens.SetEquals(m2mTokens)).ToList();
            var pool = equal.Count > 0 ? equal : candidates;

            var picked = ResolveRelaxed(m2m, pool);
            if (picked is null)
            {
                results.Add(new MatchResult
                {
                    Status = MatchStatus.Ambiguous,
                    MatchType = VendorMatchType.Relaxed,
                    M2M = m2m,
                    Candidates = pool.Select(x => x.Vendor).ToList(),
                    Note = $"{pool.Count} Bill.com vendors plausibly match by relaxed name; City/Zip did not resolve a single match.",
                });

                continue;
            }

            consumedBillIds.Add(picked.Id);
            results.Add(BuildMatch(m2m, picked, VendorMatchType.Relaxed));
        }

        // Report Bill.com vendors that matched nothing (report only).
        foreach (var bill in activeBill)
        {
            if (!consumedBillIds.Contains(bill.Id))
            {
                results.Add(new MatchResult
                {
                    Status = MatchStatus.Unmatched,
                    BillVendor = bill,
                    Note = "Bill.com vendor with no matching M2M row.",
                });
            }
        }

        return results;
    }

    private static MatchResult BuildMatch(M2MVendor m2m, VendorResponseDto vendor, VendorMatchType type)
    {
        var existing = vendor.AdditionalInfo?.CompanyName;
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return new MatchResult
            {
                Status = MatchStatus.AlreadySet,
                MatchType = type,
                M2M = m2m,
                BillVendor = vendor,
                Note = $"Existing companyName '{existing}' kept (would have set '{m2m.M2MVendorId}').",
            };
        }

        return new MatchResult
        {
            Status = MatchStatus.Matched,
            MatchType = type,
            M2M = m2m,
            BillVendor = vendor,
        };
    }

    /// <summary>
    /// Picks the single best candidate for an M2M row from the available list.
    /// Returns (null, candidates) when the choice is ambiguous.
    /// </summary>
    private static (VendorResponseDto? Picked, IReadOnlyList<VendorResponseDto> Ambiguous) PickCandidate(
        M2MVendor m2m,
        List<VendorResponseDto> available,
        int siblingCount)
    {
        if (available.Count == 0)
        {
            return (null, Array.Empty<VendorResponseDto>());
        }

        // Unique name match and only one M2M row for this name: trivially the match.
        if (available.Count == 1 && siblingCount == 1)
        {
            return (available[0], available);
        }

        // Score each candidate by City/Zip agreement and keep the best-scoring set.
        int bestScore = int.MinValue;
        var best = new List<VendorResponseDto>();

        foreach (var candidate in available)
        {
            int score = ScoreTiebreaker(m2m, candidate);
            if (score > bestScore)
            {
                bestScore = score;
                best.Clear();
                best.Add(candidate);
            }
            else if (score == bestScore)
            {
                best.Add(candidate);
            }
        }

        // A positive score means at least City or Zip agreed. Require a unique winner.
        if (best.Count == 1 && bestScore > 0)
        {
            return (best[0], best);
        }

        // Single remaining candidate with no conflicting City/Zip signal is acceptable.
        if (available.Count == 1 && bestScore >= 0)
        {
            return (available[0], available);
        }

        return (null, available);
    }

    /// <summary>Resolves a relaxed-match candidate pool to a single vendor, or null if ambiguous.</summary>
    private static VendorResponseDto? ResolveRelaxed(
        M2MVendor m2m,
        List<(VendorResponseDto Vendor, HashSet<string> Tokens)> pool)
    {
        if (pool.Count == 1)
        {
            return pool[0].Vendor;
        }

        int bestScore = int.MinValue;
        var best = new List<VendorResponseDto>();

        foreach (var (vendor, _) in pool)
        {
            int score = ScoreTiebreaker(m2m, vendor);
            if (score > bestScore)
            {
                bestScore = score;
                best.Clear();
                best.Add(vendor);
            }
            else if (score == bestScore)
            {
                best.Add(vendor);
            }
        }

        // Only accept a relaxed match when City/Zip positively singled out one vendor.
        return best.Count == 1 && bestScore > 0 ? best[0] : null;
    }

    private static int ScoreTiebreaker(M2MVendor m2m, VendorResponseDto candidate)
    {
        int score = 0;

        bool zipComparable = !string.IsNullOrWhiteSpace(m2m.ZipCode)
                             && !string.IsNullOrWhiteSpace(candidate.Address?.ZipOrPostalCode);
        if (zipComparable)
        {
            score += NormalizeZip(m2m.ZipCode) == NormalizeZip(candidate.Address!.ZipOrPostalCode)
                ? 2
                : -2;
        }

        bool cityComparable = !string.IsNullOrWhiteSpace(m2m.City)
                              && !string.IsNullOrWhiteSpace(candidate.Address?.City);
        if (cityComparable)
        {
            score += NormalizeText(m2m.City) == NormalizeText(candidate.Address!.City)
                ? 1
                : -1;
        }

        return score;
    }

    private static bool HasContainment(HashSet<string> a, HashSet<string> b)
        => a.SetEquals(b) || a.IsSubsetOf(b) || b.IsSubsetOf(a);

    public static string NormalizeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var collapsed = NormalizeText(raw);
        if (collapsed.Length == 0)
        {
            return string.Empty;
        }

        var tokens = collapsed.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Drop trailing company-suffix tokens (inc, llc, corp, ...).
        while (tokens.Count > 1 && CompanySuffixes.Contains(tokens[^1]))
        {
            tokens.RemoveAt(tokens.Count - 1);
        }

        return string.Join(' ', tokens);
    }

    /// <summary>
    /// Reduces a name to its distinctive tokens: lowercased, punctuation-stripped, with a
    /// leading article, web TLDs, and generic noise words removed. Falls back to the full
    /// token set if removing noise words would leave nothing.
    /// </summary>
    public static HashSet<string> SignificantTokens(string? raw)
    {
        var collapsed = NormalizeText(raw);
        if (collapsed.Length == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var tokens = collapsed.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Drop a single leading article (e.g. "The SolidExperts").
        if (tokens.Count > 1 && tokens[0] == "the")
        {
            tokens.RemoveAt(0);
        }

        var significant = tokens
            .Where(t => !NoiseWords.Contains(t))
            .ToHashSet(StringComparer.Ordinal);

        return significant.Count > 0
            ? significant
            : tokens.ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> BuildNoiseWords()
    {
        var words = new HashSet<string>(StringComparer.Ordinal)
        {
            // Articles / connectors.
            "the", "of", "and",
            // Generic descriptors.
            "group", "supply", "supplies", "vendor", "sales", "service", "services",
            "usa", "holdings", "enterprises", "company",
            // Web TLDs (from names like "etrailer.com").
            "com", "net", "org", "io",
        };

        foreach (var suffix in CompanySuffixes)
        {
            words.Add(suffix);
        }

        return words;
    }

    private static string NormalizeText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(raw.Length);
        bool lastWasSpace = false;

        foreach (char c in raw.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                lastWasSpace = false;
            }
            else
            {
                // Treat any punctuation/whitespace as a single separating space.
                if (!lastWasSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
        }

        return sb.ToString().Trim();
    }

    private static string NormalizeZip(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        // Compare on the first 5 digits to tolerate ZIP+4 vs ZIP5 differences.
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length > 5 ? digits[..5] : digits;
    }
}
