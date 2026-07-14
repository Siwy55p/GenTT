using TikTokGenerator.Models;
using TikTokGenerator.Services;

namespace TikTokGenerator.Tests;

public sealed class ShortGeneratorRepairDecisionTests
{
    [Fact]
    public void ShouldRepairAfterReview_WhenReviewWasSanitizedAndApproved_DoesNotRepair()
    {
        var review = new ContentReview
        {
            Approved = true,
            UsefulnessScore = 7,
            Issues =
            [
                new ContentReviewIssue
                {
                    Severity = "warning",
                    Segment = "hook",
                    Code = "hookDoesNotMatchPayoff",
                    Message = "Hook nie spelnia payoffu. [Zdegradowano: hook i payoff sa powiazane ze zrodlem oraz krokami scenariusza.]",
                    SuggestedFix = "Nie przebudowuj scenariusza."
                },
                new ContentReviewIssue
                {
                    Severity = "info",
                    Segment = "scene_01",
                    Code = "noNewInformation",
                    Message = "Scena nie wnosi nowej informacji. [Zdegradowano: scena moze wnosic osobny krok ze zrodla jako newInformation.]",
                    SuggestedFix = "Zostaw lokalnie zatwierdzony krok."
                }
            ]
        };

        Assert.False(ShortGenerator.ShouldRepairAfterReview(review));
    }

    [Fact]
    public void ShouldRepairAfterReview_WhenActiveErrorRemains_Repairs()
    {
        var review = new ContentReview
        {
            Approved = false,
            Issues =
            [
                new ContentReviewIssue
                {
                    Severity = "error",
                    Segment = "scene_01",
                    Code = "sourceMismatch",
                    Message = "Scena nie wynika ze zrodla.",
                    SuggestedFix = "Odtworz scenariusz ze zrodla."
                }
            ]
        };

        Assert.True(ShortGenerator.ShouldRepairAfterReview(review));
    }
}
