using Microsoft.EntityFrameworkCore;
using Store.Domain.Catalog;

namespace Store.Data.Seeding;

/// <summary>Sample catalog data for development. Does nothing if categories already exist.</summary>
public static class CatalogSeeder
{
  public static async Task SeedAsync(StoreDbContext db, CancellationToken ct = default)
  {
    if (await db.Categories.AnyAsync(ct)) return;

    var categories = new Dictionary<string, Category>
    {
      ["cpus"] = new() { Slug = "cpus", Name = "Processors" },
      ["motherboards"] = new() { Slug = "motherboards", Name = "Motherboards" },
      ["memory"] = new() { Slug = "memory", Name = "Memory" },
      ["storage"] = new() { Slug = "storage", Name = "Storage" },
      ["gpus"] = new() { Slug = "gpus", Name = "Graphics Cards" },
      ["power-supplies"] = new() { Slug = "power-supplies", Name = "Power Supplies" },
      ["cases"] = new() { Slug = "cases", Name = "Cases" },
      ["cooling"] = new() { Slug = "cooling", Name = "Cooling" },
    };
    db.Categories.AddRange(categories.Values);

    // specs are "name=value" pairs
    void Add(string category, string sku, string brand, string name, decimal price, int stock, params string[] specs)
    {
      var pairs = specs.Select(s => s.Split('=', 2)).ToList();
      db.Products.Add(new Product
      {
        Sku = sku,
        Brand = brand,
        Name = name,
        Price = price,
        Category = categories[category],
        Description = $"{brand} {name}. " + string.Join(", ", pairs.Select(p => $"{p[0]}: {p[1]}")),
        Specs = pairs.Select(p => new ProductSpec { Name = p[0], Value = p[1] }).ToList(),
        Inventory = new Inventory { QuantityOnHand = stock },
      });
    }

    // Processors
    Add("cpus", "CPU-AMD-9950X", "AMD", "Ryzen 9 9950X", 549.00m, 25, "socket=AM5", "cores=16", "tdp=170W");
    Add("cpus", "CPU-AMD-9800X3D", "AMD", "Ryzen 7 9800X3D", 479.00m, 12, "socket=AM5", "cores=8", "tdp=120W");
    Add("cpus", "CPU-AMD-9600X", "AMD", "Ryzen 5 9600X", 279.00m, 40, "socket=AM5", "cores=6", "tdp=65W");
    Add("cpus", "CPU-AMD-7700X", "AMD", "Ryzen 7 7700X", 299.00m, 30, "socket=AM5", "cores=8", "tdp=105W");
    Add("cpus", "CPU-INT-285K", "Intel", "Core Ultra 9 285K", 589.00m, 18, "socket=LGA1851", "cores=24", "tdp=125W");
    Add("cpus", "CPU-INT-265K", "Intel", "Core Ultra 7 265K", 394.00m, 22, "socket=LGA1851", "cores=20", "tdp=125W");
    Add("cpus", "CPU-INT-14600K", "Intel", "Core i5-14600K", 319.00m, 0, "socket=LGA1700", "cores=14", "tdp=125W");

    // Motherboards
    Add("motherboards", "MB-ASUS-X870E", "ASUS", "ROG Strix X870E-E", 499.00m, 10, "socket=AM5", "formFactor=ATX", "memoryType=DDR5");
    Add("motherboards", "MB-MSI-B650", "MSI", "MAG B650 Tomahawk", 219.00m, 20, "socket=AM5", "formFactor=ATX", "memoryType=DDR5");
    Add("motherboards", "MB-GIG-B650M", "Gigabyte", "B650M Aorus Elite", 169.00m, 28, "socket=AM5", "formFactor=mATX", "memoryType=DDR5");
    Add("motherboards", "MB-ASUS-Z890", "ASUS", "ROG Strix Z890-F", 479.00m, 8, "socket=LGA1851", "formFactor=ATX", "memoryType=DDR5");
    Add("motherboards", "MB-MSI-Z790", "MSI", "MPG Z790 Carbon", 369.00m, 6, "socket=LGA1700", "formFactor=ATX", "memoryType=DDR5");
    Add("motherboards", "MB-GIG-H610", "Gigabyte", "H610M S2H", 99.00m, 35, "socket=LGA1700", "formFactor=mATX", "memoryType=DDR4");

    // Memory
    Add("memory", "RAM-COR-32-6000", "Corsair", "Vengeance 32GB (2x16) DDR5-6000", 109.00m, 50, "type=DDR5", "capacity=32GB", "speed=6000MT/s");
    Add("memory", "RAM-COR-64-6000", "Corsair", "Vengeance 64GB (2x32) DDR5-6000", 209.00m, 24, "type=DDR5", "capacity=64GB", "speed=6000MT/s");
    Add("memory", "RAM-GSK-32-6400", "G.Skill", "Trident Z5 32GB (2x16) DDR5-6400", 139.00m, 31, "type=DDR5", "capacity=32GB", "speed=6400MT/s");
    Add("memory", "RAM-KNG-16-5600", "Kingston", "Fury Beast 16GB (2x8) DDR5-5600", 59.00m, 60, "type=DDR5", "capacity=16GB", "speed=5600MT/s");
    Add("memory", "RAM-CRU-32-3200", "Crucial", "32GB (2x16) DDR4-3200", 74.00m, 45, "type=DDR4", "capacity=32GB", "speed=3200MT/s");
    Add("memory", "RAM-KNG-16-3200", "Kingston", "Fury Beast 16GB (2x8) DDR4-3200", 42.00m, 70, "type=DDR4", "capacity=16GB", "speed=3200MT/s");

    // Storage
    Add("storage", "SSD-SAM-990-2T", "Samsung", "990 Pro 2TB", 189.00m, 33, "type=NVMe", "capacity=2TB", "interface=PCIe 4.0");
    Add("storage", "SSD-SAM-990-1T", "Samsung", "990 Pro 1TB", 109.00m, 41, "type=NVMe", "capacity=1TB", "interface=PCIe 4.0");
    Add("storage", "SSD-WD-SN850X", "Western Digital", "WD_BLACK SN850X 2TB", 159.00m, 29, "type=NVMe", "capacity=2TB", "interface=PCIe 4.0");
    Add("storage", "SSD-CRU-T705", "Crucial", "T705 2TB", 299.00m, 9, "type=NVMe", "capacity=2TB", "interface=PCIe 5.0");
    Add("storage", "SSD-CRU-MX500", "Crucial", "MX500 1TB", 79.00m, 52, "type=SATA SSD", "capacity=1TB", "interface=SATA III");
    Add("storage", "HDD-SEA-4T", "Seagate", "Barracuda 4TB", 84.00m, 38, "type=HDD", "capacity=4TB", "interface=SATA III");

    // Graphics cards
    Add("gpus", "GPU-NV-5090", "NVIDIA", "GeForce RTX 5090", 1999.00m, 3, "chipset=RTX 5090", "memory=32GB", "tdp=575W");
    Add("gpus", "GPU-NV-5080", "NVIDIA", "GeForce RTX 5080", 999.00m, 7, "chipset=RTX 5080", "memory=16GB", "tdp=360W");
    Add("gpus", "GPU-NV-5070", "NVIDIA", "GeForce RTX 5070", 549.00m, 15, "chipset=RTX 5070", "memory=12GB", "tdp=250W");
    Add("gpus", "GPU-AMD-9070XT", "AMD", "Radeon RX 9070 XT", 599.00m, 14, "chipset=RX 9070 XT", "memory=16GB", "tdp=304W");
    Add("gpus", "GPU-AMD-9060XT", "AMD", "Radeon RX 9060 XT", 349.00m, 20, "chipset=RX 9060 XT", "memory=16GB", "tdp=182W");
    Add("gpus", "GPU-INT-B580", "Intel", "Arc B580", 249.00m, 26, "chipset=Arc B580", "memory=12GB", "tdp=190W");
    Add("gpus", "GPU-NV-5060", "NVIDIA", "GeForce RTX 5060", 299.00m, 0, "chipset=RTX 5060", "memory=8GB", "tdp=145W");

    // Power supplies
    Add("power-supplies", "PSU-COR-RM1000X", "Corsair", "RM1000x", 189.00m, 17, "wattage=1000W", "efficiency=80+ Gold", "modular=Full");
    Add("power-supplies", "PSU-COR-RM850X", "Corsair", "RM850x", 149.00m, 25, "wattage=850W", "efficiency=80+ Gold", "modular=Full");
    Add("power-supplies", "PSU-SEA-VERT750", "Seasonic", "Focus GX-750", 129.00m, 21, "wattage=750W", "efficiency=80+ Gold", "modular=Full");
    Add("power-supplies", "PSU-EVGA-650", "EVGA", "650 BR", 59.00m, 44, "wattage=650W", "efficiency=80+ Bronze", "modular=None");
    Add("power-supplies", "PSU-MSI-1200", "MSI", "MPG A1250G", 219.00m, 5, "wattage=1250W", "efficiency=80+ Gold", "modular=Full");
    Add("power-supplies", "PSU-BEQ-550", "be quiet!", "Pure Power 12 M 550W", 89.00m, 19, "wattage=550W", "efficiency=80+ Gold", "modular=Semi");

    // Cases
    Add("cases", "CASE-NZXT-H7", "NZXT", "H7 Flow", 129.00m, 16, "formFactor=ATX", "color=Black");
    Add("cases", "CASE-NZXT-H7W", "NZXT", "H7 Flow (White)", 129.00m, 11, "formFactor=ATX", "color=White");
    Add("cases", "CASE-LIAN-O11", "Lian Li", "O11 Dynamic EVO", 169.00m, 13, "formFactor=ATX", "color=Black");
    Add("cases", "CASE-FRAC-NORTH", "Fractal Design", "North", 149.00m, 9, "formFactor=ATX", "color=Charcoal");
    Add("cases", "CASE-COR-4000D", "Corsair", "4000D Airflow", 104.00m, 27, "formFactor=ATX", "color=Black");
    Add("cases", "CASE-COOL-NR200", "Cooler Master", "NR200P", 99.00m, 14, "formFactor=Mini-ITX", "color=Black");

    // Cooling
    Add("cooling", "COOL-NOC-D15", "Noctua", "NH-D15 G2", 149.00m, 23, "type=Air", "size=Dual tower", "sockets=AM5/LGA1700/LGA1851");
    Add("cooling", "COOL-TH-AK620", "Thermalright", "Peerless Assassin 120", 38.00m, 55, "type=Air", "size=Dual tower", "sockets=AM5/LGA1700");
    Add("cooling", "COOL-ARC-LF3-360", "Arctic", "Liquid Freezer III 360", 119.00m, 18, "type=AIO", "size=360mm", "sockets=AM5/LGA1700/LGA1851");
    Add("cooling", "COOL-COR-H150", "Corsair", "iCUE H150i Elite", 189.00m, 12, "type=AIO", "size=360mm", "sockets=AM5/LGA1700/LGA1851");
    Add("cooling", "COOL-NZXT-K240", "NZXT", "Kraken 240", 139.00m, 15, "type=AIO", "size=240mm", "sockets=AM5/LGA1700");
    Add("cooling", "COOL-DEEP-AG400", "DeepCool", "AG400", 29.00m, 61, "type=Air", "size=Single tower", "sockets=AM5/LGA1700");

    await db.SaveChangesAsync(ct);
  }
}
