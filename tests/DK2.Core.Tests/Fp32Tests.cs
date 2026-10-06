// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Core.Tests;

public class Fp32Tests
{
    [Fact]
    public void AdditionAndSubtraction_WorkOnTheRawValues()
    {
        Assert.Equal(new Fp32(0x1800), new Fp32(Fp32.One) + new Fp32(0x800));
        Assert.Equal(new Fp32(0x800), new Fp32(Fp32.One) - new Fp32(0x800));
        Assert.Equal(new Fp32(-Fp32.One), -new Fp32(Fp32.One));
    }

    [Fact]
    public void Addition_WrapsAtThirtyTwoBitsLikeTheOriginalsIntegerMath()
    {
        Assert.Equal(new Fp32(int.MinValue), new Fp32(int.MaxValue) + new Fp32(1));
        Assert.Equal(new Fp32(int.MaxValue), new Fp32(int.MinValue) - new Fp32(1));
    }

    [Fact]
    public void Addition_AgreesWithTheRawArithmeticItReplaced()
    {
        var range = new Fp32(0xC00);
        const int reach = 0x400;

        Assert.Equal(range.Raw + reach, (range + new Fp32(reach)).Raw);
    }
}
