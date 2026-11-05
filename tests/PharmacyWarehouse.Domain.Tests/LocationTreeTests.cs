using PharmacyWarehouse.Domain.Storage;

namespace PharmacyWarehouse.Domain.Tests;

public class LocationTreeTests
{
    private static LocationNode N(string name, LocationNode? parent = null, int order = 0) =>
        new(Guid.NewGuid(), parent?.Id, name, order);

    [Fact]
    public void Subtree_ContainsSelfAndAllDescendants_NotSiblingsOrParent()
    {
        var root = N("R");
        var a = N("A", root);
        var a1 = N("A1", a);
        var b = N("B", root);

        Assert.Equal(new HashSet<Guid> { a.Id, a1.Id }, LocationTree.Subtree([root, a, a1, b], a.Id));
        Assert.Equal(new HashSet<Guid> { root.Id, a.Id, a1.Id, b.Id }, LocationTree.Subtree([root, a, a1, b], root.Id));
        Assert.Equal(new HashSet<Guid> { a1.Id }, LocationTree.Subtree([root, a, a1, b], a1.Id));
    }

    [Fact]
    public void Subtree_WithCycle_Terminates()
    {
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();

        Assert.Equal(new HashSet<Guid> { x, y }, LocationTree.Subtree([new LocationNode(x, y, "X", 0), new LocationNode(y, x, "Y", 0)], x));
    }

    [Fact]
    public void RootOf_WalksToTopLevel()
    {
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var grandchild = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?> { [root] = null, [child] = root, [grandchild] = child };

        Assert.Equal(root, LocationTree.RootOf(parents, grandchild));
        Assert.Equal(root, LocationTree.RootOf(parents, root));
    }

    [Fact]
    public void RootOf_MissingParentOrCycle_Terminates()
    {
        var orphan = Guid.NewGuid();
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?> { [orphan] = Guid.NewGuid(), [x] = y, [y] = x };

        Assert.Equal(orphan, LocationTree.RootOf(parents, orphan));
        Assert.Contains(LocationTree.RootOf(parents, x), new[] { x, y });
    }

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

    [Fact]
    public void Ordered_SiblingNameExtendingParentName_StaysOutsideParentsChildren()
    {
        var shelf = N("قفسه");
        var fridge = N("قفسه یخچال");
        var tier = N("طبقه 1", shelf);

        var result = LocationTree.Ordered([fridge, tier, shelf]);

        Assert.Equal([shelf.Id, tier.Id, fridge.Id], result.Select(r => r.Node.Id));
        Assert.Equal([0, 1, 0], result.Select(r => r.Depth));
    }

    [Fact]
    public void Ordered_UsesSortOrderBeforeName()
    {
        var a = N("الف", order: 2);
        var b = N("ب", order: 1);

        var result = LocationTree.Ordered([a, b]);

        Assert.Equal([b.Id, a.Id], result.Select(r => r.Node.Id));
    }

    [Fact]
    public void Ordered_CycleNodes_AreStillListed()
    {
        var root = N("ریشه");
        var a = new LocationNode(Guid.NewGuid(), null, "A", 0);
        var b = new LocationNode(Guid.NewGuid(), a.Id, "B", 0);
        a = a with { ParentId = b.Id };

        var result = LocationTree.Ordered([a, b, root]);

        Assert.Equal(3, result.Count);
        Assert.Equal(root.Id, result[0].Node.Id);
        Assert.Contains(result, r => r.Node.Id == a.Id);
        Assert.Contains(result, r => r.Node.Id == b.Id);
    }

    [Fact]
    public void EffectivelyActive_InactiveAncestor_DeactivatesBranch()
    {
        var a = N("A");
        var b = N("B", a);
        var c = N("C", b);
        var d = N("D");

        var active = LocationTree.EffectivelyActive([(a, true), (b, false), (c, true), (d, true)]);

        Assert.Contains(a.Id, active);
        Assert.DoesNotContain(b.Id, active);
        Assert.DoesNotContain(c.Id, active);
        Assert.Contains(d.Id, active);
    }
}
