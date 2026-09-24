using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;

namespace GodrejWMS.Domain.Tests;

public class MaterialTests
{
    [Fact]
    public void RecalculateDerivedFields_MatchesWorkbookFormula_WeightEqualsGrossWeightTimesPackSize()
    {
        // The source workbook's MaterialMaster sheet computes "Weight (KG)" as
        // GrossWeight * PkSize (e.g. row for material 40034354: 0.152 * 90 = 13.68).
        var material = new Material
        {
            GrossWeightKg = 0.152m,
            PackSize = 90,
            LengthMm = 50,
            WidthMm = 29.3m,
            HeightMm = 34,
            SeasonId = SeasonIds.Rainy
        };

        material.RecalculateDerivedFields();

        Assert.Equal(13.68m, material.BoxWeightKg);
        Assert.Equal(50m * 29.3m * 34m, material.VolumeMm3);
    }
}
