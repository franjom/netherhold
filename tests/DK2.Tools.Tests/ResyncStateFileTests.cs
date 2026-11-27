// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text.Json;
using System.Text.Json.Nodes;
using DK2.Simulation;

namespace DK2.Tools.Tests;

public class ResyncStateFileTests
{
    private static ResyncState Load(JsonNode? terminal, JsonNode? heart)
    {
        var player = new JsonObject { ["id"] = 3, ["money"] = 0, ["mana"] = 0 };
        if (terminal is not null)
        {
            player["terminal"] = terminal;
        }

        if (heart is not null)
        {
            player["heart"] = heart;
        }

        var state = new JsonObject
        {
            ["tick"] = 5000, ["width"] = 1, ["height"] = 1,
            ["tiles"] = new JsonArray(0, 0, 0), ["players"] = new JsonArray(player), ["creatures"] = new JsonArray(),
        };
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, state.ToJsonString());
            return ResyncStateFile.Load(path);
        }
        finally { File.Delete(path); }
    }

    private static JsonNode Terminal()
    {
        return JsonNode.Parse("""
        {"status":0,"killedByPlayer":0,"heartDiedTick":0,"keeperFrameAfterDefeat":false}
        """)!;
    }

    private static JsonNode Heart()
    {
        return JsonNode.Parse("""
        {"health":0,"lastAttacker":0,"roomRuined":false,"regenCountdown":0,"roomTag":77}
        """)!;
    }

    [Theory]
    [InlineData("status")]
    [InlineData("killedByPlayer")]
    [InlineData("heartDiedTick")]
    [InlineData("keeperFrameAfterDefeat")]
    public void PresentTerminalSectionsRequireEveryFieldInsteadOfInventingPlaying(string missing)
    {
        JsonNode terminal = Terminal();
        terminal.AsObject().Remove(missing);
        Assert.IsType<JsonException>(Assert.Throws<InvalidDataException>(() => Load(terminal, null)).InnerException);
    }

    [Theory]
    [InlineData("health")]
    [InlineData("lastAttacker")]
    [InlineData("roomRuined")]
    [InlineData("regenCountdown")]
    [InlineData("roomTag")]
    public void PresentHeartSectionsRequireEveryFieldInsteadOfInventingADeadHeart(string missing)
    {
        JsonNode heart = Heart();
        heart.AsObject().Remove(missing);
        Assert.IsType<JsonException>(Assert.Throws<InvalidDataException>(() => Load(null, heart)).InnerException);
    }

    [Fact]
    public void AbsentLegacySectionsStayAbsentAndExplicitZeroValuesAreValid()
    {
        ResyncState legacy = Load(null, null);
        Assert.Null(legacy.Players[0].Terminal);
        Assert.Null(legacy.Players[0].Heart);
        ResyncState captured = Load(Terminal(), Heart());
        Assert.Equal(0, captured.Players[0].Terminal!.Status);
        Assert.Equal(0, captured.Players[0].Heart!.Health);
    }
}
