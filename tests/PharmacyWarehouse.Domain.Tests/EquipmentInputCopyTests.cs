using PharmacyWarehouse.Domain.Assets;

namespace PharmacyWarehouse.Domain.Tests;

public class EquipmentInputCopyTests
{
    [Fact]
    public void Copy_IsIndependentOfTheOriginal()
    {
        var original = new EquipmentInput { Brand = "A" };
        original.Components.Add(new ComponentInput { Kind = ComponentKind.Ram, Capacity = "8GB" });
        original.LinkChildIds.Add(Guid.NewGuid());

        var copy = original.Copy();
        copy.Brand = "B";
        copy.Components[0].Capacity = "16GB";

        Assert.Equal("A", original.Brand);
        Assert.Equal("8GB", original.Components[0].Capacity);
        Assert.Empty(copy.LinkChildIds);
    }
}
