using PharmacyWarehouse.Domain.Assets;

namespace PharmacyWarehouse.Domain.Tests;

// One test per row of the table in section 6 of docs\spec.md.
public class EquipmentRulesTests
{
    private static EquipmentInput Valid(EquipmentType type)
    {
        var input = new EquipmentInput
        {
            Type = type,
            AssetNumber = "TEST-0001",
            OwnerUnitId = Guid.NewGuid(),
            BuildingId = Guid.NewGuid(),
            FloorId = Guid.NewGuid(),
            LocationUnitId = Guid.NewGuid()
        };

        if (EquipmentRules.RequiresBrandAndModel(type))
        {
            input.Brand = "TestBrand";
            input.Model = "TestModel";
        }

        if (type == EquipmentType.Monitor) input.SizeText = "22";
        if (type == EquipmentType.Peripheral) input.PeripheralKind = "test-kind";

        return input;
    }

    private static string[] Fields(EquipmentInput input) =>
        EquipmentRules.Validate(input).Select(e => e.Field).ToArray();

    [Theory]
    [InlineData(EquipmentType.Case)]
    [InlineData(EquipmentType.Laptop)]
    [InlineData(EquipmentType.Monitor)]
    [InlineData(EquipmentType.Printer)]
    [InlineData(EquipmentType.Scanner)]
    [InlineData(EquipmentType.Peripheral)]
    public void MinimalValidInput_HasNoErrors(EquipmentType type)
    {
        Assert.Empty(EquipmentRules.Validate(Valid(type)));
    }

    [Theory]
    [InlineData(EquipmentType.Case)]
    [InlineData(EquipmentType.Laptop)]
    [InlineData(EquipmentType.Monitor)]
    [InlineData(EquipmentType.Printer)]
    [InlineData(EquipmentType.Scanner)]
    [InlineData(EquipmentType.Peripheral)]
    public void AssetNumber_RequiredForEveryTypeUnlessTemporary(EquipmentType type)
    {
        var input = Valid(type);
        input.AssetNumber = "  ";

        Assert.Contains(nameof(input.AssetNumber), Fields(input));

        input.UseTemporaryAssetNumber = true;
        Assert.DoesNotContain(nameof(input.AssetNumber), Fields(input));
    }

    [Theory]
    [InlineData(ComponentKind.Ram, true)]
    [InlineData(ComponentKind.Ssd, true)]
    [InlineData(ComponentKind.Hdd, true)]
    [InlineData(ComponentKind.Motherboard, false)]
    [InlineData(ComponentKind.Cpu, false)]
    [InlineData(ComponentKind.Gpu, false)]
    [InlineData(ComponentKind.PowerSupply, false)]
    [InlineData(ComponentKind.OpticalDrive, false)]
    public void Capacity_OnlyForRamSsdHdd(ComponentKind kind, bool allowed)
    {
        var input = Valid(EquipmentType.Case);
        input.Components.Add(new ComponentInput { Kind = kind, Capacity = "8GB" });

        Assert.Equal(!allowed, Fields(input).Contains(nameof(input.Components)));
    }

    [Fact]
    public void MouseAndKeyboardModel_NeedTheirTick()
    {
        var input = Valid(EquipmentType.Case);
        input.MouseModel = "M1";
        input.KeyboardModel = "K1";

        var fields = Fields(input);
        Assert.Contains(nameof(input.MouseModel), fields);
        Assert.Contains(nameof(input.KeyboardModel), fields);

        input.HasMouse = true;
        input.HasKeyboard = true;
        Assert.Empty(EquipmentRules.Validate(input));
    }

    [Fact]
    public void Links_FollowTheHubRule()
    {
        var monitor = Valid(EquipmentType.Monitor);
        monitor.LinkChildIds.Add(Guid.NewGuid());
        Assert.Contains("Link", Fields(monitor));

        var hub = Valid(EquipmentType.Case);
        hub.LinkParentId = Guid.NewGuid();
        Assert.Contains("Link", Fields(hub));
    }

    [Fact]
    public void AssetNumber_NoAssetTickSatisfiesTheRule()
    {
        var input = Valid(EquipmentType.Case);
        input.AssetNumber = null;
        input.UseTemporaryAssetNumber = true;

        Assert.Empty(EquipmentRules.Validate(input));
    }

    [Fact]
    public void AssetNumber_AndNoAssetTickTogether_IsAnError()
    {
        var input = Valid(EquipmentType.Peripheral);
        input.UseTemporaryAssetNumber = true;

        Assert.Contains(nameof(input.AssetNumber), Fields(input));
    }

    [Theory]
    [InlineData(EquipmentType.Case, false)]
    [InlineData(EquipmentType.Laptop, true)]
    [InlineData(EquipmentType.Monitor, true)]
    [InlineData(EquipmentType.Printer, true)]
    [InlineData(EquipmentType.Scanner, true)]
    [InlineData(EquipmentType.Peripheral, false)]
    public void BrandAndModel_RequiredOnlyWhereTheTableSays(EquipmentType type, bool required)
    {
        var input = Valid(type);
        input.Brand = null;
        input.Model = null;

        var fields = Fields(input);
        Assert.Equal(required, fields.Contains(nameof(input.Brand)));
        Assert.Equal(required, fields.Contains(nameof(input.Model)));
    }

    [Theory]
    [InlineData("22", true)]
    [InlineData("۲۲", true)]
    [InlineData("23.8", true)]
    [InlineData("10", true)]
    [InlineData("100", true)]
    [InlineData("9", false)]
    [InlineData("101", false)]
    [InlineData("abc", false)]
    [InlineData(null, false)]
    public void MonitorSize_IsNumberBetween10And100(string? size, bool valid)
    {
        var input = Valid(EquipmentType.Monitor);
        input.SizeText = size;

        Assert.Equal(!valid, Fields(input).Contains(nameof(input.SizeText)));
    }

    [Theory]
    [InlineData(EquipmentType.Printer)]
    [InlineData(EquipmentType.Scanner)]
    public void Size_OptionalFreeTextForPrinterAndScanner(EquipmentType type)
    {
        var input = Valid(type);

        input.SizeText = null;
        Assert.Empty(EquipmentRules.Validate(input));

        input.SizeText = "A4";
        Assert.Empty(EquipmentRules.Validate(input));
    }

    [Theory]
    [InlineData(EquipmentType.Case)]
    [InlineData(EquipmentType.Laptop)]
    [InlineData(EquipmentType.Peripheral)]
    public void Size_ForbiddenWhereTheTableSays(EquipmentType type)
    {
        var input = Valid(type);
        input.SizeText = "A4";

        Assert.Contains(nameof(input.SizeText), Fields(input));
    }

    [Theory]
    [InlineData(EquipmentType.Case, true)]
    [InlineData(EquipmentType.Laptop, true)]
    [InlineData(EquipmentType.Monitor, false)]
    [InlineData(EquipmentType.Printer, false)]
    [InlineData(EquipmentType.Scanner, false)]
    [InlineData(EquipmentType.Peripheral, false)]
    public void ComputerName_ComponentsAndAccessories_OnlyOnHubs(EquipmentType type, bool allowed)
    {
        var name = Valid(type);
        name.ComputerName = "TEST-PC";
        Assert.Equal(!allowed, Fields(name).Contains(nameof(name.ComputerName)));

        var parts = Valid(type);
        parts.Components.Add(new ComponentInput { Kind = ComponentKind.Ram });
        Assert.Equal(!allowed, Fields(parts).Contains(nameof(parts.Components)));

        var accessories = Valid(type);
        accessories.HasMouse = true;
        Assert.Equal(!allowed, Fields(accessories).Contains(nameof(accessories.OtherAccessories)));
    }

    [Fact]
    public void PeripheralKind_RequiredForPeripheral_ForbiddenElsewhere()
    {
        var peripheral = Valid(EquipmentType.Peripheral);
        peripheral.PeripheralKind = null;
        Assert.Contains(nameof(peripheral.PeripheralKind), Fields(peripheral));

        var monitor = Valid(EquipmentType.Monitor);
        monitor.PeripheralKind = "test-kind";
        Assert.Contains(nameof(monitor.PeripheralKind), Fields(monitor));
    }

    [Theory]
    [InlineData(ComponentKind.Ram, true)]
    [InlineData(ComponentKind.Ssd, true)]
    [InlineData(ComponentKind.Hdd, true)]
    [InlineData(ComponentKind.Cpu, false)]
    [InlineData(ComponentKind.Motherboard, false)]
    [InlineData(ComponentKind.Gpu, false)]
    [InlineData(ComponentKind.PowerSupply, false)]
    [InlineData(ComponentKind.OpticalDrive, false)]
    public void Components_OnlyRamSsdHddMayRepeat(ComponentKind kind, bool mayRepeat)
    {
        var input = Valid(EquipmentType.Case);
        input.Components.Add(new ComponentInput { Kind = kind });
        input.Components.Add(new ComponentInput { Kind = kind });

        Assert.Equal(!mayRepeat, Fields(input).Contains(nameof(input.Components)));
    }

    [Fact]
    public void OwnerAndLocation_AreRequired()
    {
        var input = Valid(EquipmentType.Case);
        input.OwnerUnitId = Guid.Empty;
        input.BuildingId = Guid.Empty;
        input.FloorId = Guid.Empty;
        input.LocationUnitId = Guid.Empty;

        var fields = Fields(input);
        Assert.Contains(nameof(input.OwnerUnitId), fields);
        Assert.Contains(nameof(input.BuildingId), fields);
        Assert.Contains(nameof(input.FloorId), fields);
        Assert.Contains(nameof(input.LocationUnitId), fields);
    }

    [Fact]
    public void TooLongField_IsAnError()
    {
        var input = Valid(EquipmentType.Case);
        input.AssetNumber = new string('1', EquipmentRules.MaxAssetNumber + 1);

        Assert.Contains(nameof(input.AssetNumber), Fields(input));
    }
}
