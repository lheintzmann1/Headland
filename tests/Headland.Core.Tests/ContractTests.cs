using Headland.Core.Content;

namespace Headland.Core.Tests;

public class ContractTests
{
    [Fact]
    public void ContractTypesComeFromTheData()
    {
        var types = TestContent.Content.ContractTypes;
        Assert.Equal(["cultivate", "sow", "harvest", "deliver"], types.Keys.ToArray());
        var harvest = types["harvest"];
        Assert.Equal(("harvester", 0.9f), (harvest.Work, harvest.Deliver!.Share));
        Assert.Equal(["harvestable"], harvest.Offer.Crop);
        Assert.Equal("", types["deliver"].Work);
        Assert.Equal([4000f, 12000f], types["deliver"].Deliver!.Amount);
    }

    [Fact]
    public void ContractTypesAreValidated()
    {
        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        db.ContractTypes["plow"] = new ContractTypeDef
        {
            Id = "plow", Name = "Plow", Work = "plow", Months = [13], Days = [4, 2], Weight = 0,
            Offer = new FieldStateDef { Ground = ["road"], Crop = ["ripe"] },
            Deliver = new ContractDeliveryDef { Share = 0.5f, Amount = [1000f, 2000f] },
        };
        db.ContractTypes["haul"] = new ContractTypeDef
        {
            Id = "haul", Name = "Haul", Done = new FieldStateDef { Ground = ["cultivated"] },
            Deliver = new ContractDeliveryDef { Share = 0.5f, FillTypes = ["gravel"], PriceFactor = 0 },
        };
        var errors = db.Validate();
        Assert.Contains("contract type 'plow': months must be 1..12", errors);
        Assert.Contains("contract type 'plow': days needs [min, max] >= 1", errors);
        Assert.Contains("contract type 'plow': weight must be > 0 and rewardPerHa >= 0", errors);
        Assert.Contains("contract type 'plow': offer ground 'road' is not a field's (grass, cultivated, seeded, stubble, plowed)", errors);
        Assert.Contains("contract type 'plow': offer crop state 'ripe' is unknown (none, dead, sown, growing, harvestable)", errors);
        Assert.Contains("contract type 'plow': unknown work 'plow'", errors);
        Assert.Contains("contract type 'plow': a field job needs offer and done states", errors);
        Assert.Contains("contract type 'plow': deliver.amount is for delivery jobs (no work)", errors);
        Assert.Contains("contract type 'plow': deliver.share must be in (0, 1], on harvester jobs", errors);
        Assert.Contains("contract type 'haul': a delivery job (no work) needs deliver.amount [min, max] > 0", errors);
        Assert.Contains("contract type 'haul': offer and done are for field jobs", errors);
        Assert.Contains("contract type 'haul': deliver.share is for harvest jobs", errors);
        Assert.Contains("contract type 'haul': deliver.priceFactor must be > 0", errors);
        Assert.Contains("contract type 'haul': unknown fill type 'gravel'", errors);
        Assert.Equal(14, errors.Count);
    }
}
