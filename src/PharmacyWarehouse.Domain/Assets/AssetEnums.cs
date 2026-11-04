namespace PharmacyWarehouse.Domain.Assets;

public enum EquipmentType
{
    Case,
    Laptop,
    Monitor,
    Printer,
    Scanner,
    Peripheral
}

public enum EquipmentStatus
{
    Active,
    InStorage,
    InRepair,
    Retired
}

public enum ComponentKind
{
    Motherboard,
    Cpu,
    Ram,
    Gpu,
    PowerSupply,
    OpticalDrive,
    Ssd,
    Hdd
}
