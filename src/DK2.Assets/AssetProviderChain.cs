// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;

namespace DK2.Assets;

/// <summary>
/// Asks each provider in turn and takes the first answer.
///
/// <para>The order <i>is</i> the policy the plan describes (§7.2):
/// <c>replacement → converted → placeholder</c>. A replacement pack overrides
/// original art; original art overrides a box; and because
/// <see cref="PlaceholderProvider"/> always answers, the chain is total and the
/// game always runs.</para>
///
/// <para><b>Why "first answer wins" rather than "best answer".</b> A ranking
/// rule would need to know which of two files is better, which is a judgement no
/// code here can make, and it would make the result depend on what happens to be
/// installed. First-answer-wins makes the outcome a function of the declared
/// order alone — the same reasoning that puts system order in one declared list
/// (ADR-0013).</para>
///
/// <para>The chain counts what it hands out, which is how the harness reports
/// "this run drew N of M assets as boxes". Missing art is then a visible fact
/// about a run rather than something a viewer has to notice.</para>
/// </summary>
public sealed class AssetProviderChain : IAssetProvider
{
    private readonly IAssetProvider[] _providers;
    private readonly Dictionary<string, int> _resolvedByProvider = new(StringComparer.Ordinal);

    public AssetProviderChain(params IAssetProvider[] providers)
    {
        if (providers is null || providers.Length == 0)
            throw new ArgumentException("A chain needs at least one provider.", nameof(providers));
        if (providers.Any(p => p is null))
            throw new ArgumentException("A chain cannot contain a null provider.", nameof(providers));

        _providers = providers;
    }

    /// <summary>
    /// The standard chain: an optional replacement pack, an optional converted
    /// directory, then the placeholder. Passing null for both is the "no assets
    /// at all" configuration, and it is a supported one — see
    /// <see cref="PlaceholderProvider"/>.
    /// </summary>
    public static AssetProviderChain Standard(string? replacementRoot = null, string? convertedRoot = null)
    {
        var providers = new List<IAssetProvider>(3);
        if (!string.IsNullOrWhiteSpace(replacementRoot))
            providers.Add(FileAssetProvider.Replacement(replacementRoot!));
        if (!string.IsNullOrWhiteSpace(convertedRoot))
            providers.Add(FileAssetProvider.Converted(convertedRoot!));
        providers.Add(PlaceholderProvider.Instance);
        return new AssetProviderChain([.. providers]);
    }

    public string Name => "chain(" + string.Join(" → ", _providers.Select(p => p.Name)) + ")";

    /// <summary>The links, in order. Diagnostics and tests.</summary>
    public IReadOnlyList<IAssetProvider> Providers => _providers;

    public bool TryResolve(AssetRef reference, out ResolvedAsset asset)
    {
        foreach (IAssetProvider provider in _providers)
        {
            if (!provider.TryResolve(reference, out asset)) continue;

            _resolvedByProvider.TryGetValue(provider.Name, out int count);
            _resolvedByProvider[provider.Name] = count + 1;
            return true;
        }

        asset = default;
        return false;
    }

    /// <summary>
    /// Resolves, or throws. For the callers whose next line cannot cope with a
    /// miss — which, with the placeholder in the chain, means a genuine bug
    /// rather than a missing file.
    /// </summary>
    public ResolvedAsset Resolve(AssetRef reference) =>
        TryResolve(reference, out ResolvedAsset asset)
            ? asset
            : throw new InvalidOperationException(
                $"No provider in {Name} answered for '{reference}'. With PlaceholderProvider " +
                "in the chain this cannot happen for a valid key, so the key is empty or the " +
                "chain was built without a placeholder.");

    /// <summary>How many keys each provider has answered, for the run report.</summary>
    public IReadOnlyDictionary<string, int> ResolvedByProvider => _resolvedByProvider;

    /// <summary>Forgets the counts, for a fresh report.</summary>
    public void ResetCounts() => _resolvedByProvider.Clear();

    /// <summary>
    /// One line saying what this run's art actually came from. Written for the
    /// harness's "what this run could not do" report, where an honest
    /// "everything was a box" belongs.
    /// </summary>
    public string Summarise()
    {
        if (_resolvedByProvider.Count == 0) return "no assets were requested";

        int total = _resolvedByProvider.Values.Sum();
        IEnumerable<string> parts = _providers
            .Select(p => p.Name)
            .Where(_resolvedByProvider.ContainsKey)
            .Select(n => $"{_resolvedByProvider[n]} from {n}");

        return $"{total} asset(s) resolved: {string.Join(", ", parts)}";
    }
}
