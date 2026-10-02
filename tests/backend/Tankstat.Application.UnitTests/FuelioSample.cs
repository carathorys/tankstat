namespace Tankstat.Application.UnitTests;

/// <summary>A small Fuelio "sync" export (synthetic data, same structure and quirks as real exports).</summary>
internal static class FuelioSample
{
    public const string Csv = """
        "## Vehicle"
        "Name","Description","DistUnit","FuelUnit","ConsumptionUnit","ImportCSVDateFormat","VIN","Insurance","Plate","Make","Model","Year","TankCount","Tank1Type","Tank2Type","Active","Tank1Capacity","Tank2Capacity","FuelUnitTank2","FuelConsumptionTank2","guid","lastupdated"
        "Polo","","1","2","0","yyyy-MM-dd","","","abc-123","","",,"1","200","0","1","0.0","0.0","0","0","f4992f58-93da-4324-b224-b7360d7cca78","1775101020000"
        "## Log"
        "Data","Odo (km)","Fuel (litres)","Full","Price (optional)","l/100km (optional)","latitude (optional)","longitude (optional)","City (optional)","Notes (optional)","Missed","TankNumber","FuelType","VolumePrice","StationID (optional)","ExcludeDistance","UniqueId","TankCalc","Weather","guid","lastupdated"
        "2026-09-17 18:15","1300.0","38.5","1","24669.0",,"47.7","19.0","Town, Station","Holiday, ""big"" trip","0","1","0","640.0","1","0.0","3","0.0",,"g3","1"
        "2026-07-11 18:37","1000.0","40.0","0","22329.0","5.97","47.7","19.0","Town","","0","1","0","558.0","1","0.0","2","0.0",,"g2","1"
        "2026-06-01","700","35.25","1","20000","5.97","47.7","19.0","Town","","0","1","0","567.0","1","0.0","1","0.0",,"g1","1"
        "## CostCategories"
        "CostTypeID","Name","priority","color","guid","lastupdated"
        "1","Service","0","","c1","1"
        "5","Parking","0","","c5","1"
        "## Costs"
        "CostTitle","Date","Odo","CostTypeID","Notes","Cost","flag","idR","read","RemindOdo","RemindDate","isTemplate","RepeatOdo","RepeatMonths","isIncome","UniqueId","guid","lastupdated"
        "Oil change","2026-07-20 09:00","1100","1","with filters","35000.0","0","0","0","0","2011-01-01","0","0","0","0","1","e1","1"
        "Garage","2026-08-02","0","5","","1500.0","0","0","0","0","2011-01-01","0","0","0","0","2","e2","1"
        "Brake pads","2028-04-10","0","1","","0.0","0","0","0","0","2011-01-01","1","0","0","0","3","e3","1"
        "Refund","2026-08-03","0","1","","500.0","0","0","0","0","2011-01-01","0","0","0","1","4","e4","1"
        "","2026-08-04","0","5","","900.0","0","0","0","0","2011-01-01","0","0","0","0","5","e5","1"
        "Broken","not-a-date","0","1","","10.0","0","0","0","0","2011-01-01","0","0","0","0","6","e6","1"
        "Insurance","2026-01-10","1000","5","yearly","80000.0","0","0","0","0","2011-01-01","1","0","12","0","7","e7","1"
        "Tyres","2026-02-01","900","1","","0.0","0","0","0","21000","2027-02-01","1","15000","24","0","8","e8","1"
        "## FavStations"
        "NameBrand","Latitude","Longitude","StationID","Description","CountryCode","guid","lastupdated"
        "MOL","47.7","19.0","1","Town","HUN","s1","1"
        "## Category"
        "IdCategory","Name","guid","lastupdated"
        "1","Private","k1","1"
        """;
}
