namespace LibreArm.Core.Tests.Models;

using LibreArm.Core.Models;

[TestClass]
public sealed class BloodPressureClassifierTests
{
    [TestMethod]
    [DataRow(118, 78, BloodPressureCategory.Normal)]
    [DataRow(125, 78, BloodPressureCategory.Elevated)]
    [DataRow(132, 78, BloodPressureCategory.Stage1Hypertension)]
    [DataRow(118, 84, BloodPressureCategory.Stage1Hypertension)]
    [DataRow(142, 78, BloodPressureCategory.Stage2Hypertension)]
    [DataRow(118, 92, BloodPressureCategory.Stage2Hypertension)]
    [DataRow(181, 78, BloodPressureCategory.SevereHypertension)]
    [DataRow(118, 121, BloodPressureCategory.SevereHypertension)]
    public void ClassifyAdult_ReturnsExpectedCategory(double systolic, double diastolic, BloodPressureCategory expected)
    {
        var result = BloodPressureClassifier.ClassifyAdult(systolic, diastolic);

        Assert.AreEqual(expected, result.Category);
    }

    [TestMethod]
    public void Classify_ReturnsPediatricUnsupportedForUnder18()
    {
        var profile = new UserProfile(1, "Teen", DateOnly.FromDateTime(DateTime.Today).AddYears(-17), BiologicalSex.Unspecified, null, DateTimeOffset.Now, DateTimeOffset.Now);

        var result = BloodPressureClassifier.Classify(Reading(120, 80), profile);

        Assert.AreEqual(BloodPressureCategory.PediatricUnsupported, result.Category);
    }

    [TestMethod]
    public void Classify_DoesNotChangeAdultCategoryBySex()
    {
        var female = new UserProfile(1, "A", new DateOnly(1980, 1, 1), BiologicalSex.Female, null, DateTimeOffset.Now, DateTimeOffset.Now);
        var male = new UserProfile(2, "B", new DateOnly(1980, 1, 1), BiologicalSex.Male, null, DateTimeOffset.Now, DateTimeOffset.Now);

        var femaleResult = BloodPressureClassifier.Classify(Reading(132, 82), female);
        var maleResult = BloodPressureClassifier.Classify(Reading(132, 82), male);

        Assert.AreEqual(femaleResult.Category, maleResult.Category);
    }

    private static BloodPressureReading Reading(double systolic, double diastolic)
    {
        return new BloodPressureReading(
            systolic,
            diastolic,
            95,
            BloodPressureUnit.MillimetersOfMercury,
            null,
            null,
            null,
            null,
            DateTimeOffset.Now,
            []);
    }
}
