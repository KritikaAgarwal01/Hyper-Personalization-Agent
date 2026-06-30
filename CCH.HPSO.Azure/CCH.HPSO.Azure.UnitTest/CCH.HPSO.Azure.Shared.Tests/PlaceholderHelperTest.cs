using CCH.HPSO.Azure.Shared.DataModel;
using CCH.HPSO.Azure.Shared.Enum;
using CCH.HPSO.Azure.Shared.Helpers;
using CCH.HPSO.Azure.Shared.Contracts;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Moq;
using Xunit;
using System.Collections.Generic;

namespace CCH.HPSO.Azure.Shared.Tests
{
    public class PlaceholderHelperTest
    {
        [Fact]
        public void ExtractPlaceholders_ReturnsCorrectPlaceholders()
        {
            // Arrange
            var text = "Hello {FirstName}, your account {AccountId} is active.";

            // Act
            var result = PlaceholderHelper.ExtractPlaceholders(text);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Contains(result, p => p.Placeholder == "{FirstName}");
            Assert.Contains(result, p => p.Placeholder == "{AccountId}");
        }

        [Fact]
        public void ExtractPlaceholders_ReturnsEmptyList_WhenNoPlaceholders()
        {
            // Arrange
            var text = "Hello World!";

            // Act
            var result = PlaceholderHelper.ExtractPlaceholders(text);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void ExtractPlaceholders_ReturnsEmptyList_WhenInputIsNullOrEmpty()
        {
            Assert.Empty(PlaceholderHelper.ExtractPlaceholders(null));
            Assert.Empty(PlaceholderHelper.ExtractPlaceholders(""));
        }

        [Fact]
        public void ReplacePlaceholders_ReplacesWithActualValues()
        {
            // Arrange
            var text = "Hello {FirstName}, your account {AccountId} is active.";
            var placeholders = new List<PlaceHolderInformation>
            {
                new PlaceHolderInformation { Placeholder = "{FirstName}", ActualValue = "John" },
                new PlaceHolderInformation { Placeholder = "{AccountId}", ActualValue = "12345" }
            };
            var inputMessage = new InputMessage();

            // Act
            var result = PlaceholderHelper.ReplacePlaceholders(text, placeholders, inputMessage);

            // Assert
            Assert.Equal("Hello John, your account 12345 is active.", result);
        }

        [Fact]
        public void ReplacePlaceholders_ReturnsOriginal_WhenNoActualValues()
        {
            var text = "Hello {FirstName}";
            var placeholders = new List<PlaceHolderInformation>
            {
                new PlaceHolderInformation { Placeholder = "{FirstName}", ActualValue = "" }
            };
            var inputMessage = new InputMessage();

            var result = PlaceholderHelper.ReplacePlaceholders(text, placeholders, inputMessage);

            Assert.Equal(text, result);
        }

        [Fact]
        public void ReplacePlaceholders_ReturnsOriginal_WhenNoPlaceholders()
        {
            var text = "Hello World!";
            var inputMessage = new InputMessage();

            var result = PlaceholderHelper.ReplacePlaceholders(text, null, inputMessage);
            Assert.Equal(text, result);

            result = PlaceholderHelper.ReplacePlaceholders(text, new List<PlaceHolderInformation>(), inputMessage);
            Assert.Equal(text, result);
        }
    }
}