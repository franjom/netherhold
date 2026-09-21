// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Tools;

namespace DK2.Tools.Tests;

public class SgmapTests
{
    private const string Fixture = """
        # wholly synthetic symbol map
        struct: id=point_id,path=engine/math,name=PointPair,size=4
          field: name=firstAxis
            type: kind=int,size=2,signed=True
          field: name=secondAxis
            type: kind=int,size=2,signed=True
        struct: id=base_id,name=BaseNode,size=8,vtable=table_id
          field: name=baseValue
            type: kind=int,size=4
        struct: id=table_id,name=MethodTable,size=4
        struct: id=outer_id,name=OuterNode,size=20,vtable=table_id,super=base_id
          field: name=position
            type: kind=struct,id=point_id
          field: name=samples
            type: kind=array,count=2
              type: kind=struct,id=point_id
        global: va=00401000,name=StartRoutine,size=16,member_of=outer_id
          type: kind=function,declspec=stdcall
            ret: kind=void
        global: va=00401020,name=NodeStore,size=20
          type: kind=struct,id=outer_id
        """;

    [Fact]
    public void Parse_ComputesInheritedAndNestedOffsets()
    {
        SgmapDatabase map = Parse();
        SgmapStruct structure = Assert.Single(map.Structs, s => s.Name == "OuterNode");

        SgmapFieldLocation[] fields = map.Expand(structure).ToArray();

        Assert.Contains(fields, f => f.Path == "baseValue" && f.Offset == 4 && f.Size == 4);
        Assert.Contains(fields, f => f.Path == "position.secondAxis" && f.Offset == 10);
        Assert.Contains(fields, f => f.Path == "samples[1].firstAxis" && f.Offset == 16);
    }

    [Fact]
    public void Struct_PrintsConcreteNestedLayout()
    {
        var output = new StringWriter();

        int exitCode = SgmapCommand.RunQuery(
            Parse(), ["struct", "OuterNode"], output, new StringWriter());

        Assert.Equal(0, exitCode);
        Assert.Contains("0x000A  0x0002  position.secondAxis : int16", output.ToString());
        Assert.Contains("0x0010  0x0002  samples[1].firstAxis : int16", output.ToString());
    }

    [Fact]
    public void At_ResolvesAnArrayElementToItsLeafField()
    {
        SgmapDatabase map = Parse();
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = SgmapCommand.RunQuery(
            map, ["at", "OuterNode", "C"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("samples[0].firstAxis", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void Find_IsCaseInsensitiveAndReportsContainers()
    {
        SgmapDatabase map = Parse();
        var output = new StringWriter();

        int exitCode = SgmapCommand.RunQuery(
            map, ["find", "node"], output, new StringWriter());

        Assert.Equal(0, exitCode);
        Assert.Contains("struct  BaseNode", output.ToString());
        Assert.Contains("global  0x00401020  NodeStore", output.ToString());
    }

    [Fact]
    public void Find_ReportsPathsAndMemberContainers()
    {
        var output = new StringWriter();

        SgmapCommand.RunQuery(Parse(), ["find", "Point"], output, new StringWriter());
        SgmapCommand.RunQuery(Parse(), ["find", "Routine"], output, new StringWriter());

        Assert.Contains("path=engine/math", output.ToString());
        Assert.Contains("in OuterNode", output.ToString());
    }

    [Theory]
    [InlineData("401020", "[exact]")]
    [InlineData("0x401025", "[contains +0x5]")]
    [InlineData("401040", "[preceding, +0x20]")]
    public void Global_DistinguishesExactContainingAndPreceding(string address, string relation)
    {
        var output = new StringWriter();

        int exitCode = SgmapCommand.RunQuery(
            Parse(), ["global", address], output, new StringWriter());

        Assert.Equal(0, exitCode);
        Assert.Contains(relation, output.ToString());
    }

    [Fact]
    public void Parse_RejectsMalformedIndentation()
    {
        const string malformed = """
            struct: id=made_up,name=MadeUp,size=1
             field: name=value
            """;

        FormatException exception = Assert.Throws<FormatException>(
            () => SgmapDatabase.Parse(new StringReader(malformed)));

        Assert.Contains("indentation", exception.Message);
    }

    private static SgmapDatabase Parse() => SgmapDatabase.Parse(new StringReader(Fixture));
}
