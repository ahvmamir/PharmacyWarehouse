using PharmacyWarehouse.Domain.Storage;

namespace PharmacyWarehouse.Domain.Tests;

public class LocationTreeTests
{
    private static LocationNode N(string name, LocationNode? parent = null, int order = 0) =>
        new(Guid.NewGuid(), parent?.Id, name, order);

    [Fact]
    public void Paths_JoinAncestors()
    {
        var a = N("ضلع جنوبی");
        var b = N("قفسه ۲", a);
        var c = N("طبقه ۳", b);
        var d = N("قسمت B", c);

        var paths = LocationTree.Paths([d, c, b, a]);

        Assert.Equal("ضلع جنوبی › قفسه ۲ › طبقه ۳ › قسمت B", paths[d.Id]);
        Assert.Equal("ضلع جنوبی", paths[a.Id]);
    }

    [Fact]
    public void Cycle_SelfParent_IsDetected()
    {
        var a = N("A");
        Assert.True(LocationTree.WouldCreateCycle([a], a.Id, a.Id));
    }

    [Fact]
    public void Cycle_MoveUnderDescendant_IsDetected()
    {
        var a = N("A");
        var b = N("B", a);
        var c = N("C", b);

        Assert.True(LocationTree.WouldCreateCycle([a, b, c], a.Id, c.Id));
        Assert.True(LocationTree.WouldCreateCycle([a, b, c], a.Id, b.Id));
    }

    [Fact]
    public void MoveToRootOrSibling_IsNotCycle()
    {
        var a = N("A");
        var b = N("B", a);
        var c = N("C", b);
        var x = N("X");

        Assert.False(LocationTree.WouldCreateCycle([a, b, c, x], c.Id, null));
        Assert.False(LocationTree.WouldCreateCycle([a, b, c, x], c.Id, a.Id));
        Assert.False(LocationTree.WouldCreateCycle([a, b, c, x], a.Id, x.Id));
    }

    [Fact]
    public void Paths_BrokenParent_DoesNotLoop()
    {
        var orphan = new LocationNode(Guid.NewGuid(), Guid.NewGuid(), "یتیم", 0);
        var p = new LocationNode(Guid.NewGuid(), null, "P", 0);
        var q = new LocationNode(Guid.NewGuid(), p.Id, "Q", 0);
        var looped = p with { ParentId = q.Id };

        var paths = LocationTree.Paths([orphan, looped, q]);

        Assert.Equal("یتیم", paths[orphan.Id]);
        Assert.Contains("Q", paths[q.Id]);
    }
}
