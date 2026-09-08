// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace DK2.Assets;

/// <summary>Asks each provider in turn and takes the first answer.</summary>
/// <remarks>
/// <para>Order is the policy (ADR-0034, plan §7.2):
/// <c>replacement → converted → placeholder</c>. Because
/// <see cref="PlaceholderProvider"/> always answers, the chain is total.</para>
/// <para>First answer wins, not best answer: ranking two files needs a judgement no
/// code here can make, and would make the result depend on what happens to be
/// installed. The declared order alone decides (ADR-0013's reasoning).</para>
/// <para>The chain counts what it hands out, so the harness can report how much of a
/// run was drawn as boxes.</para>
/// </remarks>
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
    /// One line saying where this run's art came from, for the harness's "what this
    /// run could not do" report.
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
